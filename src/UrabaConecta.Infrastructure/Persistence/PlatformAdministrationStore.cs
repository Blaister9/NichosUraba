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
        string? status, string? module, Guid? createdByUserId, CancellationToken cancellationToken)
    {
        var query = db.Businesses.AsQueryable();
        if (createdByUserId is { } creator) query = query.Where(x => x.CreatedByUserId == creator);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => EF.Functions.ILike(x.Name, $"%{search.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(municipality))
            query = query.Where(x => db.Municipalities.Any(m => m.Id == x.MunicipalityId && m.Slug == municipality));
        if (Enum.TryParse<BusinessStatus>(status, true, out var parsedStatus)) query = query.Where(x => x.Status == parsedStatus);
        if (Enum.TryParse<BusinessModuleKind>(module, true, out var parsedModule))
            query = query.Where(x => db.BusinessModules.Any(m => m.BusinessId == x.Id && m.Module == parsedModule && m.IsEnabled));
        // El listado es de sólo lectura y se resuelve en una única consulta: recorrer los
        // negocios llamando a GetAsync costaba dieciséis idas y vueltas por negocio, y la
        // base está en otra región, así que cada ida y vuelta se paga en decenas de ms.
        var rows = await Summaries(query.AsNoTracking()).OrderBy(x => x.Business.Name)
            .Take(200).ToListAsync(cancellationToken);
        return rows.Select(Compose).ToList();
    }

    public async Task<PlatformBusinessRecord?> GetAsync(Guid businessId, CancellationToken cancellationToken)
    {
        // Se mantiene el rastreo: UpdateModulesAsync muta los módulos de este registro.
        var row = await Summaries(db.Businesses.Where(x => x.Id == businessId))
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : Compose(row);
    }

    /// <summary>
    /// Proyección común del resumen administrativo de un negocio. Reúne catálogos, propietario,
    /// banderas de preparación y conteos de operación en una sola sentencia.
    /// </summary>
    private IQueryable<BusinessSummaryRow> Summaries(IQueryable<Business> source)
        => source.Select(b => new BusinessSummaryRow
        {
            Business = b,
            Municipality = db.Municipalities.Where(x => x.Id == b.MunicipalityId).Select(x => x.Name).First(),
            Category = db.Categories.Where(x => x.Id == b.CategoryId).Select(x => x.Name).First(),
            Modules = db.BusinessModules.Where(x => x.BusinessId == b.Id).ToList(),
            // Sólo las del establecimiento. Las del catálogo cuelgan de un servicio o de un producto
            // y se administran junto a ellos: aquí aparecerían como fotografías sueltas de la
            // galería, sin decir de qué son, y contarían para la lista de imágenes del negocio.
            Images = db.BusinessImages.Where(x => x.BusinessId == b.Id && !x.IsDeleted &&
                (x.Kind == BusinessImageKind.Logo || x.Kind == BusinessImageKind.Cover ||
                 x.Kind == BusinessImageKind.Gallery)).ToList(),
            Owner = (from membership in db.BusinessMemberships
                     join user in db.Users on membership.UserId equals user.Id
                     where membership.BusinessId == b.Id && membership.IsActive &&
                           membership.Role == MembershipRole.Owner
                     orderby membership.CreatedAtUtc
                     select new IdentityAccount(user.Id, user.Email ?? "", user.DisplayName,
                         user.MustChangePassword)).FirstOrDefault(),
            HasHours = db.BusinessHours.Any(x => x.BusinessId == b.Id),
            HasService = db.Services.Any(x => x.BusinessId == b.Id && x.IsActive),
            HasEligibleStaff = db.StaffServices.Any(link => link.BusinessId == b.Id &&
                db.Services.Any(service => service.BusinessId == b.Id && service.Id == link.ServiceId &&
                    service.IsActive) &&
                db.StaffMembers.Any(staff => staff.BusinessId == b.Id && staff.Id == link.StaffMemberId &&
                    staff.IsActive && staff.ParticipatesInAvailability)),
            HasBookableAppointmentConfiguration = db.StaffServices.Any(link => link.BusinessId == b.Id &&
                db.StaffMembers.Any(staff => staff.BusinessId == b.Id && staff.Id == link.StaffMemberId &&
                    staff.IsActive && staff.ParticipatesInAvailability) &&
                db.Services.Any(service => service.BusinessId == b.Id && service.Id == link.ServiceId &&
                    service.IsActive && db.BusinessHours.Any(hour => hour.BusinessId == b.Id &&
                        (hour.ClosesAt - hour.OpensAt).TotalMinutes >= service.DurationMinutes))),
            HasQueueDefinition = db.QueueDefinitions.Any(x => x.BusinessId == b.Id && x.IsActive && x.IsEnabled),
            HasPickupSettings = db.PickupOrderSettings.Any(x => x.BusinessId == b.Id && x.IsEnabled),
            HasProductCategory = db.ProductCategories.Any(x => x.BusinessId == b.Id && x.IsActive),
            HasProduct = db.Products.Any(x => x.BusinessId == b.Id && x.IsActive && x.IsAvailable &&
                db.ProductCategories.Any(category => category.BusinessId == b.Id &&
                    category.Id == x.ProductCategoryId && category.IsActive)),
            HasCompatiblePickupWindow = db.PickupOrderSettings.Any(settings => settings.BusinessId == b.Id &&
                settings.IsEnabled && db.BusinessHours.Any(hour => hour.BusinessId == b.Id &&
                    hour.OpensAt < settings.ReceivesUntil && hour.ClosesAt > settings.ReceivesFrom &&
                    ((hour.ClosesAt < settings.ReceivesUntil ? hour.ClosesAt : settings.ReceivesUntil) -
                     (hour.OpensAt > settings.ReceivesFrom ? hour.OpensAt : settings.ReceivesFrom)).TotalMinutes >=
                        settings.SlotIntervalMinutes)),
            Appointments = db.Appointments.Count(x => x.BusinessId == b.Id),
            QueueSessions = db.QueueSessions.Count(x => x.BusinessId == b.Id),
            QueueTickets = db.QueueTickets.Count(x => x.BusinessId == b.Id),
            PickupOrders = db.PickupOrders.Count(x => x.BusinessId == b.Id),
        });

    private static PlatformBusinessRecord Compose(BusinessSummaryRow row)
    {
        var b = row.Business;
        var capabilities = BusinessCapabilities.Resolve(row.Modules);
        var facts = new BusinessOperationalFacts(!string.IsNullOrWhiteSpace(b.Name),
            !string.IsNullOrWhiteSpace(b.ShortDescription), !string.IsNullOrWhiteSpace(b.Description),
            !string.IsNullOrWhiteSpace(b.PublicPhone) || !string.IsNullOrWhiteSpace(b.WhatsAppUrl) ||
                !string.IsNullOrWhiteSpace(b.PublicEmail),
            b.LocationMode, b.OrderFulfillmentMode, !string.IsNullOrWhiteSpace(b.Address),
            row.Images.Any(x => !x.IsDeleted && x.Kind == BusinessImageKind.Logo),
            row.Images.Any(x => !x.IsDeleted && x.Kind == BusinessImageKind.Cover), row.Owner is not null,
            capabilities, row.HasHours, row.HasService, row.HasEligibleStaff,
            row.HasBookableAppointmentConfiguration, row.HasQueueDefinition, row.HasPickupSettings,
            row.HasProductCategory, row.HasProduct, row.HasCompatiblePickupWindow);
        return new(row.Business, row.Municipality, row.Category, row.Modules, row.Owner,
            row.HasHours, row.HasService, row.HasQueueDefinition, row.HasPickupSettings,
            row.HasProductCategory, row.HasProduct,
            row.Appointments + row.QueueSessions + row.QueueTickets + row.PickupOrders,
            row.Images, facts);
    }

    private sealed class BusinessSummaryRow
    {
        public Business Business { get; init; } = null!;
        public string Municipality { get; init; } = "";
        public string Category { get; init; } = "";
        public List<BusinessModule> Modules { get; init; } = [];
        public List<BusinessImage> Images { get; init; } = [];
        public IdentityAccount? Owner { get; init; }
        public bool HasHours { get; init; }
        public bool HasService { get; init; }
        public bool HasEligibleStaff { get; init; }
        public bool HasBookableAppointmentConfiguration { get; init; }
        public bool HasQueueDefinition { get; init; }
        public bool HasPickupSettings { get; init; }
        public bool HasProductCategory { get; init; }
        public bool HasProduct { get; init; }
        public bool HasCompatiblePickupWindow { get; init; }
        public int Appointments { get; init; }
        public int QueueSessions { get; init; }
        public int QueueTickets { get; init; }
        public int PickupOrders { get; init; }
    }

    public void AddStatusChange(BusinessStatusChange change) => db.Add(change);

    public async Task<IReadOnlyList<BusinessStatusChangeDto>> ListStatusHistoryAsync(Guid businessId,
        CancellationToken cancellationToken)
        => await (from change in db.BusinessStatusChanges.AsNoTracking()
                  join user in db.Users on change.ActorUserId equals user.Id into actors
                  from actor in actors.DefaultIfEmpty()
                  where change.BusinessId == businessId
                  orderby change.OccurredAtUtc descending
                  select new BusinessStatusChangeDto(change.FromStatus.ToString(), change.ToStatus.ToString(),
                      actor.Email, change.Notes, change.OccurredAtUtc)).Take(100).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PlatformAuditEntryDto>> ListBusinessAuditAsync(Guid businessId, int take,
        CancellationToken cancellationToken)
        => await (from entry in db.PlatformAuditEntries.AsNoTracking()
                  join user in db.Users on entry.ActorUserId equals user.Id into actors
                  from actor in actors.DefaultIfEmpty()
                  where entry.BusinessId == businessId
                  orderby entry.OccurredAtUtc descending
                  select new PlatformAuditEntryDto(entry.Id, actor.Email, entry.Action.ToString(),
                      entry.PreviousState, entry.NewState, entry.OccurredAtUtc)).Take(take)
            .ToListAsync(cancellationToken);

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
    /// <summary>
    /// Cada categoría viaja con la combinación de funciones que se propone marcada al dar de alta
    /// un negocio de esa clase. La sugerencia sale del dominio, no de la pantalla: si mañana una
    /// veterinaria arranca distinto, cambia en un sitio y no en cada formulario.
    /// </summary>
    public async Task<IReadOnlyList<PlatformOptionDto>> ListCategoriesAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Categories.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Slug, x.Name }).ToListAsync(cancellationToken);
        return rows.Select(x =>
        {
            var preset = CategoryCapabilityPresets.For(x.Slug);
            return new PlatformOptionDto(x.Id, x.Slug, x.Name, preset.Count == 0 ? null
                : new BusinessCapabilitiesDto(
                    preset.Contains(BusinessModuleKind.Appointments),
                    preset.Contains(BusinessModuleKind.VirtualQueues),
                    preset.Contains(BusinessModuleKind.PickupOrders),
                    BusinessCapabilities.DerivedDefault(BusinessModuleKind.Services, preset),
                    BusinessCapabilities.DerivedDefault(BusinessModuleKind.Products, preset),
                    BusinessCapabilities.DerivedDefault(BusinessModuleKind.Staff, preset)));
        }).ToList();
    }
    public Task<BusinessMembership?> GetOwnerAsync(Guid businessId, CancellationToken cancellationToken)
        => db.BusinessMemberships.FirstOrDefaultAsync(x => x.BusinessId == businessId && x.IsActive &&
            x.Role == MembershipRole.Owner, cancellationToken);
    public Task<BusinessMembership?> GetMembershipByUserAsync(Guid businessId, Guid userId,
        CancellationToken cancellationToken)
        => db.BusinessMemberships.SingleOrDefaultAsync(x => x.BusinessId == businessId && x.UserId == userId,
            cancellationToken);
    public Task<QueueDefinition?> GetQueueDefinitionAsync(Guid businessId, CancellationToken cancellationToken)
        => db.QueueDefinitions.SingleOrDefaultAsync(x => x.BusinessId == businessId, cancellationToken);
    public Task<PickupOrderSettings?> GetPickupSettingsAsync(Guid businessId, CancellationToken cancellationToken)
        => db.PickupOrderSettings.SingleOrDefaultAsync(x => x.BusinessId == businessId, cancellationToken);
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
        db.BusinessImages.RemoveRange(db.BusinessImages.Where(x => x.BusinessId == businessId));
        db.BusinessStatusChanges.RemoveRange(db.BusinessStatusChanges.Where(x => x.BusinessId == businessId));
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
