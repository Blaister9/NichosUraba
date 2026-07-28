using System.Text.Json;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Application;

public sealed class PlatformAdministrationUseCases(
    IPlatformAdministrationStore store,
    IIdentityAccountManager identity,
    IUrabaStore directory,
    IObjectStorage storage,
    TimeProvider timeProvider) : IPlatformAdministrationUseCases
{
    public async Task<PlatformBusinessListDto> ListAsync(PlatformActor actor, string? search, string? municipality,
        string? status, string? module, CancellationToken cancellationToken = default)
    {
        EnsureOperator(actor);
        // Una socia sólo ve los negocios que ella dio de alta.
        var scope = actor.IsPlatformAdmin ? (Guid?)null : actor.UserId;
        return new((await store.ListAsync(search, municipality, status, module, scope, cancellationToken))
                .Select(ToDto).ToList(),
            await store.ListMunicipalitiesAsync(cancellationToken),
            await store.ListCategoriesAsync(cancellationToken),
            identity.DevelopmentAccountCreationEnabled);
    }

    public async Task<PlatformBusinessDto> GetAsync(PlatformActor actor, Guid businessId,
        CancellationToken cancellationToken = default)
        => ToDto(await RequireScopedAsync(actor, businessId, cancellationToken));

    public async Task<PlatformBusinessCreatedDto> CreateAsync(PlatformActor actor,
        CreatePlatformBusinessRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOperator(actor);
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
        business.AssignCreator(actor.UserId);
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
            Audit(business.Id, actor, PlatformAuditAction.PilotAccountCreated, "{}", new { owner.UserId }, now);
        }
        if (owner is not null)
        {
            if (await store.GetMembershipByUserAsync(business.Id, owner.UserId, cancellationToken) is not null)
                throw new ApiException("MEMBERSHIP_EXISTS", "La cuenta ya pertenece al negocio.", 409);
            store.AddMembership(new BusinessMembership(Guid.NewGuid(), business.Id, owner.UserId,
                MembershipRole.Owner, true, true, true, now,
                modules.Contains(BusinessModuleKind.VirtualQueues), modules.Contains(BusinessModuleKind.PickupOrders)));
            Audit(business.Id, actor, PlatformAuditAction.OwnerAssigned, "{}", new { owner.UserId }, now);
            if (!request.SaveAsDraft) business.MarkPending(now, business.Version);
        }
        Audit(business.Id, actor, PlatformAuditAction.BusinessCreated, "{}", Snapshot(business), now);
        store.AddStatusChange(new BusinessStatusChange(Guid.NewGuid(), business.Id, BusinessStatus.Draft,
            business.Status, actor.UserId, "Alta del negocio.", now));
        await store.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new(ToDto((await store.GetAsync(business.Id, cancellationToken))!), temporaryPassword);
    }

    public async Task<PlatformBusinessDto> UpdateAsync(PlatformActor actor, Guid businessId,
        UpdatePlatformBusinessRequest request, CancellationToken cancellationToken = default)
    {
        var current = await RequireScopedAsync(actor, businessId, cancellationToken);
        return await SaveProfileAsync(actor, businessId, new SaveBusinessProfileRequest
        {
            Name = request.Name, Slug = request.Slug, MunicipalityId = request.MunicipalityId,
            CategoryId = request.CategoryId, Description = request.Description, Address = request.Address,
            PublicPhone = request.PublicPhone, WhatsAppUrl = request.WhatsAppUrl,
            LocationUrl = request.LocationUrl, Version = request.Version,
            // El formulario heredado no envía los campos nuevos: se conservan los ya guardados.
            ShortDescription = string.IsNullOrWhiteSpace(current.Business.ShortDescription)
                ? Fallback(request.Description, request.Name)
                : current.Business.ShortDescription,
            ReferencePoint = current.Business.ReferencePoint,
            PublicEmail = current.Business.PublicEmail,
            InstagramUrl = current.Business.InstagramUrl,
            FacebookUrl = current.Business.FacebookUrl,
            CustomerInstructions = current.Business.CustomerInstructions
        }, cancellationToken);
    }

    /// <summary>Descripción breve derivada cuando aún no existe una explícita.</summary>
    private static string Fallback(string description, string name)
    {
        var source = string.IsNullOrWhiteSpace(description) ? name : description;
        return source.Length <= 160 ? source : source[..160];
    }

    public async Task<PlatformBusinessDto> SaveProfileAsync(PlatformActor actor, Guid businessId,
        SaveBusinessProfileRequest request, CancellationToken cancellationToken = default)
    {
        await RequireScopedAsync(actor, businessId, cancellationToken);
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
            business.UpdateCommercialProfile(new BusinessProfileEdit(request.Slug, request.Name,
                request.MunicipalityId, request.CategoryId, request.ShortDescription, request.Description,
                request.Address, request.ReferencePoint, request.PublicPhone, request.WhatsAppUrl,
                request.PublicEmail, request.InstagramUrl, request.FacebookUrl, request.LocationUrl,
                request.CustomerInstructions), now, request.Version);
            return true;
        });
        Audit(businessId, actor, PlatformAuditAction.BusinessUpdated, before, Snapshot(business), now);
        await store.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
        return ToDto((await store.GetAsync(businessId, cancellationToken))!);
    }

    public async Task<PlatformBusinessDto> ChangeStateAsync(PlatformActor actor, Guid businessId, string action,
        PlatformBusinessStateRequest request, CancellationToken cancellationToken = default)
    {
        // Publicar, suspender, archivar y eliminar son actos de revisión: sólo la administración de plataforma.
        if (!actor.IsPlatformAdmin)
            throw new ApiException("FORBIDDEN", "Solo la administración de plataforma cambia el estado.", 403);
        await using var tx = await store.BeginTransactionAsync(cancellationToken);
        var business = await store.LockBusinessAsync(businessId, cancellationToken)
            ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos el negocio.", 404);
        if (business.Version != request.Version)
            throw new ApiException("CONCURRENCY_CONFLICT", "El negocio cambió. Recargue e intente de nuevo.", 409);
        var before = Snapshot(business); var previousStatus = business.Status;
        var now = timeProvider.GetUtcNow();
        var record = await store.GetAsync(businessId, cancellationToken)
            ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos el negocio.", 404);
        var audit = TryDomain(() => action.ToLowerInvariant() switch
        {
            "activate" or "reactivate" or "publish" => Activate(business, Readiness(record).IsReady, now),
            "suspend" => Suspend(business, request.Reason, now),
            "archive" => Archive(business, now),
            "delete" => Delete(business, record.OperationCount),
            _ => throw new ApiException("INVALID_ACTION", "La acción administrativa no es válida.")
        });
        if (action.Equals("delete", StringComparison.OrdinalIgnoreCase))
        {
            store.RemoveBusiness(business);
        }
        else
        {
            Audit(business.Id, actor, audit, before, Snapshot(business), now);
            store.AddStatusChange(new BusinessStatusChange(Guid.NewGuid(), businessId, previousStatus,
                business.Status, actor.UserId, request.Reason, now));
        }
        await store.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
        if (action.Equals("delete", StringComparison.OrdinalIgnoreCase)) return ToDto(record);
        return ToDto((await store.GetAsync(businessId, cancellationToken))!);
    }

    public async Task<PlatformBusinessDto> SubmitForReviewAsync(PlatformActor actor, Guid businessId,
        SubmitForReviewRequest request, CancellationToken cancellationToken = default)
    {
        await RequireScopedAsync(actor, businessId, cancellationToken);
        await using var tx = await store.BeginTransactionAsync(cancellationToken);
        var business = await store.LockBusinessAsync(businessId, cancellationToken)
            ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos el negocio.", 404);
        if (business.Version != request.Version)
            throw new ApiException("CONCURRENCY_CONFLICT", "El negocio cambió. Recargue.", 409);
        var record = await store.GetAsync(businessId, cancellationToken)
            ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos el negocio.", 404);
        var readiness = Readiness(record);
        if (!readiness.IsReady)
            throw new ApiException("BUSINESS_NOT_READY",
                "Falta completar: " + string.Join(" ", readiness.MissingLabels), 409);
        var previous = business.Status; var now = timeProvider.GetUtcNow();
        TryDomain(() => { business.SubmitForReview(true, now, request.Version); return true; });
        Audit(businessId, actor, PlatformAuditAction.BusinessSubmittedForReview, Snapshot(business),
            Snapshot(business), now);
        store.AddStatusChange(new BusinessStatusChange(Guid.NewGuid(), businessId, previous, business.Status,
            actor.UserId, "Enviado a revisión.", now));
        await store.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
        return ToDto((await store.GetAsync(businessId, cancellationToken))!);
    }

    public async Task<PlatformBusinessDto> RejectReviewAsync(PlatformActor actor, Guid businessId,
        RejectReviewRequest request, CancellationToken cancellationToken = default)
    {
        if (!actor.IsPlatformAdmin)
            throw new ApiException("FORBIDDEN", "Solo la administración de plataforma revisa negocios.", 403);
        await using var tx = await store.BeginTransactionAsync(cancellationToken);
        var business = await store.LockBusinessAsync(businessId, cancellationToken)
            ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos el negocio.", 404);
        if (business.Version != request.Version)
            throw new ApiException("CONCURRENCY_CONFLICT", "El negocio cambió. Recargue.", 409);
        var previous = business.Status; var now = timeProvider.GetUtcNow();
        TryDomain(() => { business.RejectReview(request.Notes, now, request.Version); return true; });
        Audit(businessId, actor, PlatformAuditAction.BusinessReviewRejected, "{}",
            new { Notes = request.Notes }, now);
        store.AddStatusChange(new BusinessStatusChange(Guid.NewGuid(), businessId, previous, business.Status,
            actor.UserId, request.Notes, now));
        await store.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
        return ToDto((await store.GetAsync(businessId, cancellationToken))!);
    }

    public async Task<BusinessProfileDto> PreviewAsync(PlatformActor actor, Guid businessId,
        CancellationToken cancellationToken = default)
    {
        var record = await RequireScopedAsync(actor, businessId, cancellationToken);
        var profile = await directory.GetBusinessProfileAsync(record.Business.Slug, requirePublished: false,
                cancellationToken)
            ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos el negocio.", 404);
        return profile with { IsPreview = true };
    }

    public async Task<IReadOnlyList<BusinessStatusChangeDto>> ListStatusHistoryAsync(PlatformActor actor,
        Guid businessId, CancellationToken cancellationToken = default)
    {
        await RequireScopedAsync(actor, businessId, cancellationToken);
        return await store.ListStatusHistoryAsync(businessId, cancellationToken);
    }

    public async Task<IReadOnlyList<PlatformAuditEntryDto>> ListAuditAsync(PlatformActor actor, Guid businessId,
        CancellationToken cancellationToken = default)
    {
        if (!actor.IsPlatformAdmin)
            throw new ApiException("FORBIDDEN", "Solo la administración de plataforma consulta la auditoría.", 403);
        return await store.ListBusinessAuditAsync(businessId, 200, cancellationToken);
    }

    public async Task<PlatformBusinessDto> UpdateModulesAsync(PlatformActor actor, Guid businessId,
        UpdatePlatformModulesRequest request, CancellationToken cancellationToken = default)
    {
        await RequireScopedAsync(actor, businessId, cancellationToken);
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
        Audit(businessId, actor, PlatformAuditAction.ModulesChanged, "{}",
            JsonSerializer.Serialize(selected.Select(x => x.ToString())), now);
        await store.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
        return ToDto((await store.GetAsync(businessId, cancellationToken))!);
    }

    // -----------------------------------------------------------------------

    private async Task<PlatformBusinessRecord> RequireScopedAsync(PlatformActor actor, Guid businessId,
        CancellationToken cancellationToken)
    {
        EnsureOperator(actor);
        var record = await store.GetAsync(businessId, cancellationToken)
            ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos el negocio.", 404);
        if (!actor.IsPlatformAdmin && record.Business.CreatedByUserId != actor.UserId)
            throw new ApiException("FORBIDDEN", "El negocio no está a su cargo.", 403);
        return record;
    }

    private static void EnsureOperator(PlatformActor actor)
    {
        if (!actor.CanOperate)
            throw new ApiException("FORBIDDEN", "No tiene permiso para administrar negocios.", 403);
    }

    private void CreateInitialConfiguration(Guid businessId, CreatePlatformBusinessRequest request,
        IReadOnlyCollection<BusinessModuleKind> modules, DateTimeOffset now)
    {
        if (modules.Contains(BusinessModuleKind.Appointments))
        {
            foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                         DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday })
                // TimeOnly(8) invoca el constructor de ticks, no el de horas: el día quedaba vacío.
                store.AddHour(new BusinessHour(Guid.NewGuid(), businessId, day,
                    new TimeOnly(8, 0), new TimeOnly(18, 0)));
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
                request.PickupSlotMinutes, request.PickupCapacity, new TimeOnly(8, 0), new TimeOnly(18, 0)));
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
    {
        var b = r.Business;
        var signals = new BusinessCompletionSignals(
            HasContact: !string.IsNullOrWhiteSpace(b.PublicPhone) || !string.IsNullOrWhiteSpace(b.WhatsAppUrl)
                        || !string.IsNullOrWhiteSpace(b.PublicEmail),
            HasLocation: !string.IsNullOrWhiteSpace(b.Address),
            HasLogo: r.HasLogo, HasCover: r.HasCover);
        return BusinessReadinessCalculator.Calculate(
            !string.IsNullOrWhiteSpace(b.Name) && !string.IsNullOrWhiteSpace(b.ShortDescription)
                && !string.IsNullOrWhiteSpace(b.Description),
            r.Owner is not null, r.Modules.Where(x => x.IsEnabled).Select(x => x.Module).ToList(),
            r.HasHours, r.HasService, r.HasQueueDefinition, r.HasPickupSettings, r.HasProductCategory,
            r.HasProduct, signals);
    }

    private PlatformBusinessDto ToDto(PlatformBusinessRecord r)
    {
        var readiness = Readiness(r);
        var b = r.Business;
        return new(b.Id, b.Name, b.Slug, r.Municipality, r.Category, b.Status.ToString(), b.IsPublished,
            r.Modules.Where(x => x.IsEnabled).Select(x => x.Module.ToString()).ToList(),
            r.Owner?.DisplayName, r.Owner?.Email,
            readiness.Requirements
                .Select(x => new ReadinessItemDto(x.Key, x.Label, x.IsApplicable, x.IsComplete, x.MissingHint))
                .ToList(),
            readiness.IsReady, b.SuspensionReason, b.Version,
            b.MunicipalityId, b.CategoryId, b.Description, b.Address, b.PublicPhone, b.WhatsAppUrl, b.LocationUrl,
            b.ShortDescription, b.ReferencePoint, b.PublicEmail, b.InstagramUrl, b.FacebookUrl,
            b.CustomerInstructions, readiness.CompletionPercentage, readiness.MissingLabels, b.ReviewNotes,
            r.LiveImages.OrderBy(x => x.Kind).ThenBy(x => x.DisplayOrder)
                .Select(x => new BusinessImageDto(x.Id, x.Kind.ToString(), storage.PublicUrl(x.StorageKey),
                    x.AltText, x.Width, x.Height, x.DisplayOrder, x.Version)).ToList());
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
    private void Audit(Guid businessId, PlatformActor actor, PlatformAuditAction action, object before, object after,
        DateTimeOffset now) => store.AddAudit(new PlatformAuditEntry(Guid.NewGuid(), businessId, actor.UserId, action,
            before is string s ? s : JsonSerializer.Serialize(before),
            after is string t ? t : JsonSerializer.Serialize(after), now, actor.CorrelationId));
    private static object Snapshot(Business b) => new { b.Status, b.IsPublished, b.Version };
    private static T TryDomain<T>(Func<T> action)
    {
        try { return action(); } catch (DomainException ex) { throw new ApiException(ex.Code, ex.Message, 400); }
    }
}
