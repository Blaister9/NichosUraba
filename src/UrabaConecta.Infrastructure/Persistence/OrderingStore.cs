using Microsoft.EntityFrameworkCore;
using Npgsql;
using UrabaConecta.Application;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Infrastructure.Persistence;

public sealed class OrderingStore(AppDbContext db) : IOrderingStore
{
    public async Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken ct)
        => new EfTransaction(await db.Database.BeginTransactionAsync(ct));
    public async Task<(Business Business, PickupOrderSettings Settings)?> GetPublicContextAsync(string slug, CancellationToken ct)
    {
        var row = await (from b in db.Businesses join s in db.PickupOrderSettings on b.Id equals s.BusinessId
            where b.Slug == slug && b.IsPublished && b.Status == BusinessStatus.Active && s.IsEnabled
                && db.BusinessModules.Any(m => m.BusinessId == b.Id && m.Module == BusinessModuleKind.PickupOrders && m.IsEnabled)
            select new { b, s }).SingleOrDefaultAsync(ct);
        return row is null ? null : (row.b, row.s);
    }
    public Task<PickupOrderSettings?> GetSettingsAsync(Guid businessId, CancellationToken ct)
        => db.PickupOrderSettings.SingleOrDefaultAsync(x => x.BusinessId == businessId, ct);
    public Task<PickupOrderSettings?> LockSettingsAsync(Guid businessId, CancellationToken ct)
    {
        // Public context was read before opening the transaction. Discard its tracked settings so
        // the row-lock query refreshes NextOrderNumber and Version after any preceding contender.
        db.ChangeTracker.Clear();
        return db.PickupOrderSettings.FromSqlInterpolated($"""
                SELECT * FROM ordering_pickup_settings WHERE "BusinessId" = {businessId} FOR UPDATE
                """).SingleOrDefaultAsync(ct);
    }
    public Task LockSlotAsync(Guid businessId, DateTimeOffset start, CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT pg_advisory_xact_lock(hashtextextended({businessId.ToString() + "|" + start.UtcTicks}, 0))
            """, ct);
    public async Task<IReadOnlyList<ProductCategory>> GetCategoriesAsync(Guid businessId, bool activeOnly, CancellationToken ct)
        => await db.ProductCategories.Where(x => x.BusinessId == businessId && (!activeOnly || x.IsActive))
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).ToListAsync(ct);
    public async Task<IReadOnlyList<Product>> GetProductsAsync(Guid businessId, bool activeOnly, CancellationToken ct)
        => await db.Products.Where(x => x.BusinessId == businessId && (!activeOnly || x.IsActive))
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).ToListAsync(ct);
    public async Task<IReadOnlyDictionary<Guid, CatalogPhoto>> GetProductPhotosAsync(Guid businessId,
        CancellationToken ct)
        => await db.BusinessImages
            .Where(x => x.BusinessId == businessId && !x.IsDeleted && x.ProductId != null)
            .Select(x => new { Product = x.ProductId!.Value, x.StorageKey, x.AltText })
            .ToDictionaryAsync(x => x.Product, x => new CatalogPhoto(x.StorageKey, x.AltText), ct);
    public Task<ProductCategory?> GetCategoryAsync(Guid businessId, Guid id, CancellationToken ct)
        => db.ProductCategories.SingleOrDefaultAsync(x => x.BusinessId == businessId && x.Id == id, ct);
    public Task<Product?> GetProductAsync(Guid businessId, Guid id, CancellationToken ct)
        => db.Products.SingleOrDefaultAsync(x => x.BusinessId == businessId && x.Id == id, ct);
    public Task<Business?> GetBusinessAsync(Guid businessId, CancellationToken ct)
        => db.Businesses.SingleOrDefaultAsync(x => x.Id == businessId, ct);
    public async Task<IReadOnlyList<BusinessHour>> GetHoursAsync(Guid businessId, CancellationToken ct)
        => await db.BusinessHours.Where(x => x.BusinessId == businessId).ToListAsync(ct);
    /// <summary>
    /// El mismo criterio de "activo" que <see cref="CountActiveInSlotAsync"/>, agrupado por franja
    /// y resuelto en una sentencia para todo el rango. Los dos filtros tienen que decir lo mismo:
    /// si un día cambia qué estado ocupa cupo, cambian los dos o la disponibilidad que se enseña
    /// deja de coincidir con la que se autoriza.
    /// </summary>
    public async Task<IReadOnlyDictionary<DateTimeOffset, int>> GetActiveSlotCountsAsync(Guid businessId,
        DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken ct)
        => await db.PickupOrders.AsNoTracking()
            .Where(x => x.BusinessId == businessId &&
                x.PickupStartUtc >= rangeStart && x.PickupStartUtc <= rangeEnd &&
                x.Status != PickupOrderStatus.Rejected && x.Status != PickupOrderStatus.Cancelled &&
                x.Status != PickupOrderStatus.Delivered)
            .GroupBy(x => x.PickupStartUtc)
            .Select(g => new { Start = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Start, x => x.Count, ct);
    public Task<int> CountActiveInSlotAsync(Guid businessId, DateTimeOffset start, CancellationToken ct)
        => db.PickupOrders.CountAsync(x => x.BusinessId == businessId && x.PickupStartUtc == start &&
            x.Status != PickupOrderStatus.Rejected && x.Status != PickupOrderStatus.Cancelled &&
            x.Status != PickupOrderStatus.Delivered, ct);
    public Task<PickupOrder?> FindByCodeAsync(string hash, CancellationToken ct)
        => db.PickupOrders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.PublicCodeHash == hash, ct);
    public Task<PickupOrder?> ReadByCodeAsync(string hash, CancellationToken ct)
        => db.PickupOrders.AsNoTracking().Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.PublicCodeHash == hash, ct);
    public Task<PickupOrder?> GetOrderAsync(Guid businessId, Guid orderId, CancellationToken ct)
        => db.PickupOrders.Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.BusinessId == businessId && x.Id == orderId, ct);
    public async Task<IReadOnlyList<PickupOrder>> ListOrdersAsync(Guid businessId, string? status,
        DateOnly? date, CancellationToken ct)
    {
        var query = db.PickupOrders.Include(x => x.Lines).Where(x => x.BusinessId == businessId);
        if (Enum.TryParse<PickupOrderStatus>(status, true, out var parsed)) query = query.Where(x => x.Status == parsed);
        if (date.HasValue)
        {
            var from = new DateTimeOffset(date.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var until = from.AddDays(1);
            query = query.Where(x => x.PickupStartUtc >= from && x.PickupStartUtc < until);
        }
        return await query.OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync(ct);
    }
    public Task<bool> CanManageOrdersAsync(Guid userId, Guid businessId, CancellationToken ct)
        => db.BusinessMemberships.AnyAsync(x => x.UserId == userId && x.BusinessId == businessId &&
            x.IsActive && (x.Role == MembershipRole.Owner || x.CanManageOrders), ct);
    public Task<bool> IsModuleEnabledAsync(Guid businessId, BusinessModuleKind module, CancellationToken ct)
        => db.BusinessModules.AsNoTracking()
            .AnyAsync(x => x.BusinessId == businessId && x.Module == module && x.IsEnabled, ct);
    public Task<bool> CanManageConfigurationAsync(Guid userId, Guid businessId, CancellationToken ct)
        => db.BusinessMemberships.AnyAsync(x => x.UserId == userId && x.BusinessId == businessId &&
            x.IsActive && (x.Role == MembershipRole.Owner || x.CanManageConfiguration), ct);
    public void AddCategory(ProductCategory x) => db.Add(x);
    public void AddProduct(Product x) => db.Add(x);
    public void AddSettings(PickupOrderSettings x) => db.Add(x);
    public void AddOrder(PickupOrder x) => db.Add(x);
    public void AddConsent(ConsentReceipt x) => db.Add(x);
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new ApiException("CONCURRENCY_CONFLICT", "La fila cambió. Recargue la información.", 409);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
            pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ApiException("ORDER_CONCURRENCY_CONFLICT", "El pedido cambió al mismo tiempo. Intente de nuevo.", 409);
        }
    }
    private sealed class EfTransaction(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
        : IApplicationTransaction
    {
        public Task CommitAsync(CancellationToken ct) => transaction.CommitAsync(ct);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
