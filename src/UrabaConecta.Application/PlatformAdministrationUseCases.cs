using System.Text.Json;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Application;

public sealed class PlatformAdministrationUseCases(
    IPlatformAdministrationStore store,
    IIdentityAccountManager identity,
    TimeProvider timeProvider) : IPlatformAdministrationUseCases
{
    public async Task<PlatformBusinessListDto> ListAsync(string? search, string? municipality, string? status,
        string? module, CancellationToken cancellationToken = default)
        => new((await store.ListAsync(search, municipality, status, module, cancellationToken)).Select(ToDto).ToList(),
            await store.ListMunicipalitiesAsync(cancellationToken),
            await store.ListCategoriesAsync(cancellationToken),
            identity.DevelopmentAccountCreationEnabled);

    public async Task<PlatformBusinessDto> GetAsync(Guid businessId, CancellationToken cancellationToken = default)
        => ToDto(await store.GetAsync(businessId, cancellationToken)
            ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos el negocio.", 404));

    public async Task<PlatformBusinessCreatedDto> CreateAsync(Guid actorUserId, CreatePlatformBusinessRequest request,
        CancellationToken cancellationToken = default)
    {
        var modules = Selected(request);
        if (modules.Count == 0) throw new ApiException("MODULE_REQUIRED", "Seleccione al menos una función.");
        var slug = Business.NormalizeSlug(request.Slug);
        await using var tx = await store.BeginTransactionAsync(cancellationToken);
        if (await store.SlugExistsAsync(slug, null, cancellationToken))
            throw new ApiException("SLUG_EXISTS", "Ese identificador ya está en uso.", 409);
        if (!await store.MunicipalityExistsAsync(request.MunicipalityId, cancellationToken) ||
            !await store.CategoryExistsAsync(request.CategoryId, cancellationToken))
            throw new ApiException("INVALID_CATALOG", "Seleccione municipio y categoría válidos.");

        var now = timeProvider.GetUtcNow();
        var business = TryDomain(() => Business.CreateDraft(Guid.NewGuid(), slug, request.Name,
            request.MunicipalityId, request.CategoryId, request.Description, request.Address, request.PublicPhone,
            request.WhatsAppUrl, request.LocationUrl, now));
        store.AddBusiness(business);
        foreach (var module in modules) store.AddModule(new BusinessModule(business.Id, module, true, now));
        CreateInitialConfiguration(business.Id, request, modules, now);

        string? temporaryPassword = null;
        IdentityAccount? owner = null;
        if (!string.IsNullOrWhiteSpace(request.ExistingOwnerEmail))
            owner = await identity.FindByExactEmailAsync(request.ExistingOwnerEmail, cancellationToken)
                ?? throw new ApiException("ACCOUNT_NOT_FOUND", "No encontramos la cuenta propietaria.", 404);
        else if (!string.IsNullOrWhiteSpace(request.PilotEmail))
        {
            if (string.IsNullOrWhiteSpace(request.PilotDisplayName))
                throw new ApiException("OWNER_NAME_REQUIRED", "Ingrese el nombre visible de la persona propietaria.");
            var created = await identity.CreatePilotAsync(request.PilotDisplayName, request.PilotEmail, cancellationToken);
            owner = created.Account; temporaryPassword = created.TemporaryPassword;
            Audit(business.Id, actorUserId, PlatformAuditAction.PilotAccountCreated, "{}", new { owner.UserId }, now);
        }
        if (owner is not null)
        {
            if (await store.GetMembershipByUserAsync(business.Id, owner.UserId, cancellationToken) is not null)
                throw new ApiException("MEMBERSHIP_EXISTS", "La cuenta ya pertenece al negocio.", 409);
            store.AddMembership(new BusinessMembership(Guid.NewGuid(), business.Id, owner.UserId,
                MembershipRole.Owner, true, true, true, now,
                modules.Contains(BusinessModuleKind.VirtualQueues), modules.Contains(BusinessModuleKind.PickupOrders)));
            Audit(business.Id, actorUserId, PlatformAuditAction.OwnerAssigned, "{}", new { owner.UserId }, now);
            if (!request.SaveAsDraft) business.MarkPending(now, business.Version);
        }
        Audit(business.Id, actorUserId, PlatformAuditAction.BusinessCreated, "{}", Snapshot(business), now);
        await store.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var createdRecord = await store.GetAsync(business.Id, cancellationToken)
            ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos el negocio.", 404);
        if (!request.SaveAsDraft && Readiness(createdRecord).IsReady)
        {
            await using var activateTx = await store.BeginTransactionAsync(cancellationToken);
            var locked = await store.LockBusinessAsync(business.Id, cancellationToken)
                ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos el negocio.", 404);
            locked.Activate(true, now, locked.Version);
            Audit(locked.Id, actorUserId, PlatformAuditAction.BusinessActivated, "{}", Snapshot(locked), now);
            await store.SaveChangesAsync(cancellationToken); await activateTx.CommitAsync(cancellationToken);
        }
        return new(ToDto((await store.GetAsync(business.Id, cancellationToken))!), temporaryPassword);
    }

    public async Task<PlatformBusinessDto> ChangeStateAsync(Guid actorUserId, Guid businessId, string action,
        PlatformBusinessStateRequest request, CancellationToken cancellationToken = default)
    {
        await using var tx = await store.BeginTransactionAsync(cancellationToken);
        var business = await store.LockBusinessAsync(businessId, cancellationToken)
            ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos el negocio.", 404);
        if (business.Version != request.Version)
            throw new ApiException("CONCURRENCY_CONFLICT", "El negocio cambió. Recargue e intente de nuevo.", 409);
        var before = Snapshot(business); var now = timeProvider.GetUtcNow();
        var record = await store.GetAsync(businessId, cancellationToken)
            ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos el negocio.", 404);
        var audit = TryDomain(() => action.ToLowerInvariant() switch
        {
            "activate" or "reactivate" => Activate(business, Readiness(record).IsReady, now),
            "suspend" => Suspend(business, request.Reason, now),
            "archive" => Archive(business, now),
            "delete" => Delete(business, record.OperationCount),
            _ => throw new ApiException("INVALID_ACTION", "La acción administrativa no es válida.")
        });
        if (action.Equals("delete", StringComparison.OrdinalIgnoreCase))
        {
            store.RemoveBusiness(business);
        }
        else Audit(business.Id, actorUserId, audit, before, Snapshot(business), now);
        await store.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
        if (action.Equals("delete", StringComparison.OrdinalIgnoreCase)) return ToDto(record);
        return ToDto((await store.GetAsync(businessId, cancellationToken))!);
    }

    public async Task<PlatformBusinessDto> UpdateAsync(Guid actorUserId, Guid businessId,
        UpdatePlatformBusinessRequest request, CancellationToken cancellationToken = default)
    {
        await using var tx = await store.BeginTransactionAsync(cancellationToken);
        var business = await store.LockBusinessAsync(businessId, cancellationToken)
            ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos el negocio.", 404);
        if (business.Version != request.Version)
            throw new ApiException("CONCURRENCY_CONFLICT", "El negocio cambió. Recargue.", 409);
        var slug = Business.NormalizeSlug(request.Slug);
        if (await store.SlugExistsAsync(slug, businessId, cancellationToken))
            throw new ApiException("SLUG_EXISTS", "Ese identificador ya está en uso.", 409);
        if (!await store.MunicipalityExistsAsync(request.MunicipalityId, cancellationToken) ||
            !await store.CategoryExistsAsync(request.CategoryId, cancellationToken))
            throw new ApiException("INVALID_CATALOG", "Seleccione municipio y categoría válidos.");
        var before = Snapshot(business); var now = timeProvider.GetUtcNow();
        TryDomain(() =>
        {
            business.UpdatePlatformProfile(request.Slug, request.Name, request.MunicipalityId, request.CategoryId,
                request.Description, request.Address, request.PublicPhone, request.WhatsAppUrl, request.LocationUrl,
                now, request.Version);
            return true;
        });
        Audit(businessId, actorUserId, PlatformAuditAction.BusinessUpdated, before, Snapshot(business), now);
        await store.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
        return ToDto((await store.GetAsync(businessId, cancellationToken))!);
    }

    public async Task<PlatformBusinessDto> UpdateModulesAsync(Guid actorUserId, Guid businessId,
        UpdatePlatformModulesRequest request, CancellationToken cancellationToken = default)
    {
        var selected = Selected(request);
        if (selected.Count == 0) throw new ApiException("MODULE_REQUIRED", "Seleccione al menos una función.");
        await using var tx = await store.BeginTransactionAsync(cancellationToken);
        var business = await store.LockBusinessAsync(businessId, cancellationToken)
            ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos el negocio.", 404);
        if (business.Version != request.Version)
            throw new ApiException("CONCURRENCY_CONFLICT", "El negocio cambió. Recargue.", 409);
        var record = await store.GetAsync(businessId, cancellationToken)
            ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos el negocio.", 404);
        var now = timeProvider.GetUtcNow();
        foreach (var kind in Enum.GetValues<BusinessModuleKind>())
        {
            var module = record.Modules.SingleOrDefault(x => x.Module == kind);
            if (module is null) store.AddModule(new BusinessModule(businessId, kind, selected.Contains(kind), now));
            else module.SetEnabled(selected.Contains(kind), now, module.Version);
        }
        TryDomain(() =>
        {
            business.ConfigurationChanged(now, business.Version);
            return true;
        });
        Audit(businessId, actorUserId, PlatformAuditAction.ModulesChanged, "{}",
            JsonSerializer.Serialize(selected.Select(x => x.ToString())), now);
        await store.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
        return ToDto((await store.GetAsync(businessId, cancellationToken))!);
    }

    private void CreateInitialConfiguration(Guid businessId, CreatePlatformBusinessRequest request,
        IReadOnlyCollection<BusinessModuleKind> modules, DateTimeOffset now)
    {
        if (modules.Contains(BusinessModuleKind.Appointments))
        {
            foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                         DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday })
                store.AddHour(new BusinessHour(Guid.NewGuid(), businessId, day, new TimeOnly(8), new TimeOnly(18)));
            if (!string.IsNullOrWhiteSpace(request.InitialServiceName))
            {
                var service = new Service(Guid.NewGuid(), businessId, request.InitialServiceName,
                    request.InitialServiceDurationMinutes, 0);
                var staff = new StaffMember(Guid.NewGuid(), businessId, "Persona por asignar");
                store.AddService(service); store.AddStaff(staff);
                store.AddStaffService(new StaffService(businessId, staff.Id, service.Id));
            }
        }
        if (modules.Contains(BusinessModuleKind.VirtualQueues))
            store.AddQueueDefinition(new QueueDefinition(Guid.NewGuid(), businessId, "Atención general",
                request.QueueAverageMinutes, request.QueueMaximumWaiting, request.QueueMessage, true));
        if (modules.Contains(BusinessModuleKind.PickupOrders))
        {
            store.AddPickupSettings(new PickupOrderSettings(Guid.NewGuid(), businessId, true,
                "Haz tu pedido y recógelo en el establecimiento.", request.PickupPreparationMinutes,
                request.PickupSlotMinutes, request.PickupCapacity, new TimeOnly(8), new TimeOnly(18)));
            if (!string.IsNullOrWhiteSpace(request.InitialProductCategory) &&
                !string.IsNullOrWhiteSpace(request.InitialProductName))
            {
                var category = new ProductCategory(Guid.NewGuid(), businessId, request.InitialProductCategory, 1);
                store.AddProductCategory(category);
                store.AddProduct(new Product(Guid.NewGuid(), businessId, category.Id, request.InitialProductName,
                    "", request.InitialProductPrice, 1));
            }
        }
    }

    private static BusinessReadiness Readiness(PlatformBusinessRecord r)
        => BusinessReadinessCalculator.Calculate(!string.IsNullOrWhiteSpace(r.Business.Name) &&
            !string.IsNullOrWhiteSpace(r.Business.Description), r.Owner is not null,
            r.Modules.Where(x => x.IsEnabled).Select(x => x.Module).ToList(), r.HasHours, r.HasService,
            r.HasQueueDefinition, r.HasPickupSettings, r.HasProductCategory, r.HasProduct);
    private static PlatformBusinessDto ToDto(PlatformBusinessRecord r)
    {
        var readiness = Readiness(r);
        return new(r.Business.Id, r.Business.Name, r.Business.Slug, r.Municipality, r.Category,
            r.Business.Status.ToString(), r.Business.IsPublished,
            r.Modules.Where(x => x.IsEnabled).Select(x => x.Module.ToString()).ToList(),
            r.Owner?.DisplayName, r.Owner?.Email,
            readiness.Requirements.Select(x => new ReadinessItemDto(x.Key, x.Label, x.IsApplicable, x.IsComplete)).ToList(),
            readiness.IsReady, r.Business.SuspensionReason, r.Business.Version,
            r.Business.MunicipalityId, r.Business.CategoryId, r.Business.Description, r.Business.Address,
            r.Business.PublicPhone, r.Business.WhatsAppUrl, r.Business.LocationUrl);
    }
    private static List<BusinessModuleKind> Selected(CreatePlatformBusinessRequest r)
        => Selected(r.Appointments, r.VirtualQueues, r.PickupOrders);
    private static List<BusinessModuleKind> Selected(UpdatePlatformModulesRequest r)
        => Selected(r.Appointments, r.VirtualQueues, r.PickupOrders);
    private static List<BusinessModuleKind> Selected(bool a, bool q, bool o)
        => new[] { (a, BusinessModuleKind.Appointments), (q, BusinessModuleKind.VirtualQueues),
            (o, BusinessModuleKind.PickupOrders) }.Where(x => x.Item1).Select(x => x.Item2).ToList();
    private static PlatformAuditAction Activate(Business b, bool ready, DateTimeOffset now)
    { var action = b.Status == BusinessStatus.Suspended ? PlatformAuditAction.BusinessReactivated : PlatformAuditAction.BusinessActivated;
      b.Activate(ready, now, b.Version); return action; }
    private static PlatformAuditAction Suspend(Business b, string? reason, DateTimeOffset now)
    { b.Suspend(reason ?? "", now, b.Version); return PlatformAuditAction.BusinessSuspended; }
    private static PlatformAuditAction Archive(Business b, DateTimeOffset now)
    { b.Archive(now, b.Version); return PlatformAuditAction.BusinessArchived; }
    private static PlatformAuditAction Delete(Business b, int operations)
    {
        if (b.Status is not (BusinessStatus.Draft or BusinessStatus.PendingConfiguration) || operations > 0)
            throw new ApiException("BUSINESS_DELETE_FORBIDDEN", "El negocio tiene historia o un estado que exige archivarlo.", 409);
        return PlatformAuditAction.BusinessDeleted;
    }
    private void Audit(Guid businessId, Guid actor, PlatformAuditAction action, object before, object after,
        DateTimeOffset now) => store.AddAudit(new PlatformAuditEntry(Guid.NewGuid(), businessId, actor, action,
            before is string s ? s : JsonSerializer.Serialize(before),
            after is string t ? t : JsonSerializer.Serialize(after), now));
    private static object Snapshot(Business b) => new { b.Status, b.IsPublished, b.Version };
    private static T TryDomain<T>(Func<T> action)
    {
        try { return action(); } catch (DomainException ex) { throw new ApiException(ex.Code, ex.Message, 400); }
    }
}
