using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UrabaConecta.Application;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Identity;

namespace UrabaConecta.Infrastructure.Persistence;

public sealed class PlatformAdministrationStore(AppDbContext db) : IPlatformAdministrationStore
{
    public async Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
        => new EfApplicationTransaction(await db.Database.BeginTransactionAsync(cancellationToken));

    public async Task<IReadOnlyList<PlatformBusinessRecord>> ListAsync(string? search, string? municipality,
        string? status, string? module, CancellationToken cancellationToken)
    {
        var query = db.Businesses.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => EF.Functions.ILike(x.Name, $"%{search.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(municipality))
            query = query.Where(x => db.Municipalities.Any(m => m.Id == x.MunicipalityId && m.Slug == municipality));
        if (Enum.TryParse<BusinessStatus>(status, true, out var parsedStatus)) query = query.Where(x => x.Status == parsedStatus);
        if (Enum.TryParse<BusinessModuleKind>(module, true, out var parsedModule))
            query = query.Where(x => db.BusinessModules.Any(m => m.BusinessId == x.Id && m.Module == parsedModule && m.IsEnabled));
        var ids = await query.OrderBy(x => x.Name).Select(x => x.Id).Take(200).ToListAsync(cancellationToken);
        var records = new List<PlatformBusinessRecord>();
        foreach (var id in ids) if (await GetAsync(id, cancellationToken) is { } item) records.Add(item);
        return records;
    }

    public async Task<PlatformBusinessRecord?> GetAsync(Guid businessId, CancellationToken cancellationToken)
    {
        var business = await db.Businesses.SingleOrDefaultAsync(x => x.Id == businessId, cancellationToken);
        if (business is null) return null;
        var municipality = await db.Municipalities.Where(x => x.Id == business.MunicipalityId)
            .Select(x => x.Name).SingleAsync(cancellationToken);
        var category = await db.Categories.Where(x => x.Id == business.CategoryId)
            .Select(x => x.Name).SingleAsync(cancellationToken);
        var modules = await db.BusinessModules.Where(x => x.BusinessId == businessId).ToListAsync(cancellationToken);
        var owner = await (from membership in db.BusinessMemberships
                           join user in db.Users on membership.UserId equals user.Id
                           where membership.BusinessId == businessId && membership.IsActive &&
                                 membership.Role == MembershipRole.Owner
                           orderby membership.CreatedAtUtc
                           select new IdentityAccount(user.Id, user.Email ?? "", user.DisplayName,
                               user.MustChangePassword)).FirstOrDefaultAsync(cancellationToken);
        var operations = await db.Appointments.CountAsync(x => x.BusinessId == businessId, cancellationToken) +
                         await db.QueueSessions.CountAsync(x => x.BusinessId == businessId, cancellationToken) +
                         await db.QueueTickets.CountAsync(x => x.BusinessId == businessId, cancellationToken) +
                         await db.PickupOrders.CountAsync(x => x.BusinessId == businessId, cancellationToken);
        return new(business, municipality, category, modules, owner,
            await db.BusinessHours.AnyAsync(x => x.BusinessId == businessId, cancellationToken),
            await db.Services.AnyAsync(x => x.BusinessId == businessId && x.IsActive, cancellationToken),
            await db.QueueDefinitions.AnyAsync(x => x.BusinessId == businessId && x.IsActive && x.IsEnabled, cancellationToken),
            await db.PickupOrderSettings.AnyAsync(x => x.BusinessId == businessId && x.IsEnabled, cancellationToken),
            await db.ProductCategories.AnyAsync(x => x.BusinessId == businessId && x.IsActive, cancellationToken),
            await db.Products.AnyAsync(x => x.BusinessId == businessId && x.IsActive, cancellationToken), operations);
    }

    public Task<Business?> LockBusinessAsync(Guid businessId, CancellationToken cancellationToken)
        => db.Businesses.FromSqlInterpolated($"SELECT * FROM businesses WHERE \"Id\"={businessId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
    public Task<bool> SlugExistsAsync(string slug, Guid? excludingId, CancellationToken cancellationToken)
        => db.Businesses.AnyAsync(x => x.Slug == slug && (!excludingId.HasValue || x.Id != excludingId), cancellationToken);
    public Task<bool> MunicipalityExistsAsync(Guid id, CancellationToken cancellationToken)
        => db.Municipalities.AnyAsync(x => x.Id == id && x.IsActive, cancellationToken);
    public Task<bool> CategoryExistsAsync(Guid id, CancellationToken cancellationToken)
        => db.Categories.AnyAsync(x => x.Id == id && x.IsActive, cancellationToken);
    public async Task<IReadOnlyList<PlatformOptionDto>> ListMunicipalitiesAsync(CancellationToken cancellationToken)
        => await db.Municipalities.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new PlatformOptionDto(x.Id, x.Slug, x.Name)).ToListAsync(cancellationToken);
    public async Task<IReadOnlyList<PlatformOptionDto>> ListCategoriesAsync(CancellationToken cancellationToken)
        => await db.Categories.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new PlatformOptionDto(x.Id, x.Slug, x.Name)).ToListAsync(cancellationToken);
    public Task<BusinessMembership?> GetOwnerAsync(Guid businessId, CancellationToken cancellationToken)
        => db.BusinessMemberships.FirstOrDefaultAsync(x => x.BusinessId == businessId && x.IsActive &&
            x.Role == MembershipRole.Owner, cancellationToken);
    public Task<BusinessMembership?> GetMembershipByUserAsync(Guid businessId, Guid userId,
        CancellationToken cancellationToken)
        => db.BusinessMemberships.SingleOrDefaultAsync(x => x.BusinessId == businessId && x.UserId == userId,
            cancellationToken);
    public void AddBusiness(Business business) => db.Add(business);
    public void AddModule(BusinessModule module) => db.Add(module);
    public void AddMembership(BusinessMembership membership) => db.Add(membership);
    public void AddHour(BusinessHour hour) => db.Add(hour);
    public void AddService(Service service) => db.Add(service);
    public void AddStaff(StaffMember staff) => db.Add(staff);
    public void AddStaffService(StaffService link) => db.Add(link);
    public void AddQueueDefinition(QueueDefinition definition) => db.Add(definition);
    public void AddPickupSettings(PickupOrderSettings settings) => db.Add(settings);
    public void AddProductCategory(ProductCategory category) => db.Add(category);
    public void AddProduct(Product product) => db.Add(product);
    public void AddAudit(PlatformAuditEntry audit) => db.Add(audit);
    public void RemoveBusiness(Business business)
    {
        var businessId = business.Id;
        var memberships = db.BusinessMemberships.Where(x => x.BusinessId == businessId).ToList();
        var membershipIds = memberships.Select(x => x.Id).ToArray();
        db.MembershipAuditEntries.RemoveRange(db.MembershipAuditEntries.Where(x =>
            x.BusinessId == businessId || membershipIds.Contains(x.MembershipId)));
        db.PlatformAuditEntries.RemoveRange(db.PlatformAuditEntries.Where(x => x.BusinessId == businessId));
        db.StaffServices.RemoveRange(db.StaffServices.Where(x => x.BusinessId == businessId));
        db.AvailabilityExceptions.RemoveRange(db.AvailabilityExceptions.Where(x => x.BusinessId == businessId));
        db.StaffMembers.RemoveRange(db.StaffMembers.Where(x => x.BusinessId == businessId));
        db.Services.RemoveRange(db.Services.Where(x => x.BusinessId == businessId));
        db.BusinessHours.RemoveRange(db.BusinessHours.Where(x => x.BusinessId == businessId));
        db.QueueDefinitions.RemoveRange(db.QueueDefinitions.Where(x => x.BusinessId == businessId));
        db.Products.RemoveRange(db.Products.Where(x => x.BusinessId == businessId));
        db.ProductCategories.RemoveRange(db.ProductCategories.Where(x => x.BusinessId == businessId));
        db.PickupOrderSettings.RemoveRange(db.PickupOrderSettings.Where(x => x.BusinessId == businessId));
        db.BusinessMemberships.RemoveRange(memberships);
        db.BusinessModules.RemoveRange(db.BusinessModules.Where(x => x.BusinessId == businessId));
        db.Businesses.Remove(business);
    }
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        { throw new ApiException("CONCURRENCY_CONFLICT", "La información cambió. Recargue e intente de nuevo.", 409); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        { throw new ApiException("SLUG_EXISTS", "Ese identificador ya está en uso.", 409); }
    }
    private sealed class EfApplicationTransaction(IDbContextTransaction transaction) : IApplicationTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
