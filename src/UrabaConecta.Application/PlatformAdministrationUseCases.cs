using System.Text.Json;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Application;

public sealed class PlatformAdministrationUseCases(
    IPlatformAdministrationStore store,
    IIdentityAccountManager identity,
    IUrabaStore directory,
    IObjectStorage storage,
    IPublicDirectoryCache publicCache,
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
            request.MunicipalityId, request.CategoryId, request.ShortDescription, request.Description,
            request.Address, request.PublicPhone, request.WhatsAppUrl, request.LocationUrl, now));
        business.AssignCreator(actor.UserId);
        store.AddBusiness(business);
        foreach (var module in modules) store.AddModule(new BusinessModule(business.Id, module, true, now));
        // Se dejan escritas también las capacidades derivadas del alta. Guardarlas explícitas desde
        // el principio evita que un negocio recién creado dependa de que cada consulta repita la
        // misma deducción, y deja la fila lista para que la administración la cambie sin adivinar.
        foreach (var derived in BusinessCapabilities.Derived)
            store.AddModule(new BusinessModule(business.Id, derived,
                BusinessCapabilities.DerivedDefault(derived, modules), now));
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
        publicCache.Invalidate();
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

    /// <summary>
    /// Guardado del perfil por su propietario. Reutiliza <see cref="SaveProfileAsync"/> entero
    /// —validación de dominio, concurrencia optimista, auditoría e invalidación de caché— y sólo se
    /// encarga de una cosa: reponer desde lo guardado los cuatro campos que el propietario no
    /// gobierna, para que no pueda cambiarlos ni siquiera enviándolos a mano.
    /// </summary>
    public async Task<PlatformBusinessDto> SaveOwnerProfileAsync(PlatformActor actor, Guid businessId,
        SaveOwnerProfileRequest request, CancellationToken cancellationToken = default)
    {
        var current = (await RequireScopedAsync(actor, businessId, cancellationToken)).Business;
        return await SaveProfileAsync(actor, businessId, new SaveBusinessProfileRequest
        {
            Name = current.Name, Slug = current.Slug,
            MunicipalityId = current.MunicipalityId, CategoryId = current.CategoryId,
            ShortDescription = request.ShortDescription, Description = request.Description,
            Address = request.Address, ReferencePoint = request.ReferencePoint,
            PublicPhone = request.PublicPhone, WhatsAppUrl = request.WhatsAppUrl,
            PublicEmail = request.PublicEmail, InstagramUrl = request.InstagramUrl,
            FacebookUrl = request.FacebookUrl, LocationUrl = request.LocationUrl,
            CustomerInstructions = request.CustomerInstructions, Version = request.Version
        }, cancellationToken);
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
        publicCache.Invalidate();
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
        publicCache.Invalidate();
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
        publicCache.Invalidate();
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
        publicCache.Invalidate();
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
        // Las operaciones se guardan tal cual llegan. Las capacidades derivadas —servicios,
        // productos, personal— se guardan resueltas: lo que el formulario mandó si dijo algo, y la
        // derivación de la operación si vino en blanco. Así la fila siempre refleja la decisión
        // efectiva y ninguna pantalla tiene que volver a deducirla mirando la categoría.
        foreach (var kind in BusinessCapabilities.Operations)
            Apply(kind, selected.Contains(kind));
        foreach (var derived in BusinessCapabilities.Derived)
        {
            var stated = derived switch
            {
                BusinessModuleKind.Services => request.Services,
                BusinessModuleKind.Products => request.Products,
                _ => request.Staff
            };
            Apply(derived, stated ?? BusinessCapabilities.DerivedDefault(derived, selected));
        }

        void Apply(BusinessModuleKind kind, bool enabled)
        {
            var module = record.Modules.SingleOrDefault(x => x.Module == kind);
            if (module is null) store.AddModule(new BusinessModule(businessId, kind, enabled, now));
            else module.SetEnabled(enabled, now, module.Version);
        }
        TryDomain(() =>
        {
            business.ConfigurationChanged(now, business.Version);
            return true;
        });
        Audit(businessId, actor, PlatformAuditAction.ModulesChanged, "{}",
            JsonSerializer.Serialize(selected.Select(x => x.ToString())), now);
        await store.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
        publicCache.Invalidate();
        return ToDto((await store.GetAsync(businessId, cancellationToken))!);
    }

    public async Task<IReadOnlyList<BusinessHourAdminDto>> ListHoursAsync(PlatformActor actor, Guid businessId,
        CancellationToken cancellationToken = default)
    {
        await RequireScopedAsync(actor, businessId, cancellationToken);
        var existing = await directory.GetBusinessHoursAsync(businessId, cancellationToken);
        return Enum.GetValues<DayOfWeek>().Select(day =>
        {
            // Un día son cero, uno o varios tramos. SingleOrDefault lanzaba con jornada partida.
            var tramos = existing.Where(x => x.Day == day)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.OpensAt).ToList();
            var first = tramos.FirstOrDefault();
            return new BusinessHourAdminDto(day, tramos.Count == 0, first?.OpensAt, first?.ClosesAt,
                first?.Version ?? 0,
                tramos.Select(x => new ScheduleIntervalDto(x.OpensAt, x.ClosesAt)).ToList());
        }).ToArray();
    }

    public async Task<ConfigurationImpactDto> SetHourAsync(PlatformActor actor, Guid businessId, DayOfWeek day,
        SaveBusinessHourRequest request, CancellationToken cancellationToken = default)
    {
        await RequireScopedAsync(actor, businessId, cancellationToken);
        var conIntervalos = request.Intervals is { Count: > 0 } && !request.IsClosed;
        if (!request.IsClosed && !conIntervalos && (request.OpensAt is null || request.ClosesAt is null ||
                                  request.OpensAt >= request.ClosesAt))
            throw new ApiException("INVALID_HOURS", "La apertura debe ser anterior al cierre.");
        var current = (await directory.GetBusinessHoursAsync(businessId, cancellationToken))
            .Where(x => x.Day == day).OrderBy(x => x.SortOrder).ThenBy(x => x.OpensAt).ToList();
        var existing = current.FirstOrDefault();
        if (conIntervalos)
        {
            // La jornada del día se reemplaza entera, igual que en la configuración del negocio.
            if (existing is not null)
                EnsureVersion(existing.Version, request.Version, "El horario cambió. Recargue.");
            var normalized = TryDomain(() => BusinessSchedule.Normalize(
                request.Intervals!.Select(x => new ScheduleInterval(x.OpensAt, x.ClosesAt))));
            foreach (var stale in current) directory.RemoveBusinessHour(stale);
            var order = 0;
            foreach (var interval in normalized)
                directory.AddBusinessHour(new BusinessHour(Guid.NewGuid(), businessId, day,
                    interval.OpensAt, interval.ClosesAt, order++));
        }
        else if (request.IsClosed)
        {
            if (existing is not null)
                EnsureVersion(existing.Version, request.Version, "El horario cambió. Recargue.");
            foreach (var stale in current) directory.RemoveBusinessHour(stale);
        }
        else if (existing is null)
            directory.AddBusinessHour(new BusinessHour(Guid.NewGuid(), businessId, day,
                request.OpensAt!.Value, request.ClosesAt!.Value));
        else
        {
            EnsureVersion(existing.Version, request.Version, "El horario cambió. Recargue.");
            foreach (var stale in current.Skip(1)) directory.RemoveBusinessHour(stale);
            TryDomain(() => { existing.Update(request.OpensAt!.Value, request.ClosesAt!.Value, request.Version, 0); return true; });
        }
        await directory.SaveChangesAsync(cancellationToken);
        var conflicts = await directory.CountFutureAppointmentConflictsAsync(businessId, null, NextDate(day),
            request.IsClosed ? null : request.OpensAt, request.IsClosed ? null : request.ClosesAt, true,
            cancellationToken);
        return new(conflicts);
    }

    public async Task<IReadOnlyList<StaffMemberDto>> ListSchedulingStaffAsync(PlatformActor actor, Guid businessId,
        CancellationToken cancellationToken = default)
    {
        await RequireScopedAsync(actor, businessId, cancellationToken);
        return await directory.GetStaffAsync(businessId, cancellationToken);
    }

    public async Task<IReadOnlyList<AvailabilityExceptionDto>> ListSchedulingExceptionsAsync(PlatformActor actor,
        Guid businessId, DateOnly? from = null, CancellationToken cancellationToken = default)
    {
        await RequireScopedAsync(actor, businessId, cancellationToken);
        var items = await directory.GetAvailabilityExceptionsAsync(businessId, cancellationToken);
        var result = new List<AvailabilityExceptionDto>();
        foreach (var item in from.HasValue ? items.Where(x => x.Date >= from.Value) : items)
            result.Add(await ToSchedulingExceptionDto(item, cancellationToken));
        return result;
    }

    public async Task<AvailabilityExceptionDto> SaveSchedulingExceptionAsync(PlatformActor actor, Guid businessId,
        SaveAvailabilityExceptionRequest request, CancellationToken cancellationToken = default)
    {
        await RequireScopedAsync(actor, businessId, cancellationToken);
        if (!await directory.StaffBelongsToBusinessAsync(businessId, request.StaffMemberId, cancellationToken))
            throw new ApiException("CROSS_BUSINESS_REFERENCE", "La persona no pertenece al negocio.", 409);
        if (!Enum.TryParse<AvailabilityExceptionType>(request.Type, true, out var type))
            throw new ApiException("INVALID_EXCEPTION", "Seleccione una excepción válida.");
        if (type != AvailabilityExceptionType.ClosedAllDay &&
            (request.OpensAt is null || request.ClosesAt is null || request.OpensAt >= request.ClosesAt))
            throw new ApiException("INVALID_HOURS", "La hora inicial debe ser anterior a la hora final.");
        var item = (await directory.GetAvailabilityExceptionsAsync(businessId, cancellationToken))
            .SingleOrDefault(x => x.StaffMemberId == request.StaffMemberId && x.Date == request.Date);
        if (item is null)
        {
            item = new AvailabilityException(Guid.NewGuid(), businessId, request.StaffMemberId, request.Date,
                type, request.OpensAt, request.ClosesAt, request.Reason);
            directory.AddAvailabilityException(item);
        }
        else
        {
            EnsureVersion(item.Version, request.Version, "La excepción cambió. Recargue.");
            TryDomain(() => { item.Update(type, request.OpensAt, request.ClosesAt, request.Reason, request.Version); return true; });
        }
        await directory.SaveChangesAsync(cancellationToken);
        return await ToSchedulingExceptionDto(item, cancellationToken);
    }

    public async Task DeleteSchedulingExceptionAsync(PlatformActor actor, Guid businessId, Guid exceptionId,
        long version, CancellationToken cancellationToken = default)
    {
        await RequireScopedAsync(actor, businessId, cancellationToken);
        var item = await directory.GetAvailabilityExceptionAsync(businessId, exceptionId, cancellationToken)
            ?? throw new ApiException("EXCEPTION_NOT_FOUND", "No encontramos la excepción.", 404);
        EnsureVersion(item.Version, version, "La excepción cambió. Recargue.");
        directory.RemoveAvailabilityException(item);
        await directory.SaveChangesAsync(cancellationToken);
    }

    // -----------------------------------------------------------------------

    private async Task<PlatformBusinessRecord> RequireScopedAsync(PlatformActor actor, Guid businessId,
        CancellationToken cancellationToken)
    {
        // El permiso se resuelve antes de leer el negocio: si se leyera primero, quien no tiene nada
        // que hacer aquí distinguiría un 404 de un 403 y tendría un oráculo de existencia.
        var isOwner = await IsActiveOwnerAsync(actor, businessId, cancellationToken);
        if (!actor.CanOperate && !isOwner)
            throw new ApiException("FORBIDDEN", "No tiene permiso para administrar negocios.", 403);
        var record = await store.GetAsync(businessId, cancellationToken)
            ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos el negocio.", 404);
        if (!actor.IsPlatformAdmin && !isOwner && record.Business.CreatedByUserId != actor.UserId)
            throw new ApiException("FORBIDDEN", "El negocio no está a su cargo.", 403);
        return record;
    }

    /// <summary>
    /// Propietario activo de ESTE negocio. Es la única puerta por la que un BusinessOwner entra a los
    /// casos de uso de plataforma, y se comprueba contra su membresía, nunca contra su rol: el rol
    /// dice qué clase de persona es, la membresía dice sobre qué negocio manda.
    /// Se corta antes de consultar cuando el actor no es propietario para no cobrar una consulta
    /// extra a la administración, que llega aquí en cada petición.
    /// </summary>
    private async Task<bool> IsActiveOwnerAsync(PlatformActor actor, Guid businessId,
        CancellationToken cancellationToken)
        => actor.IsBusinessOwner
           && await store.GetMembershipByUserAsync(businessId, actor.UserId, cancellationToken)
               is { IsActive: true, Role: MembershipRole.Owner };

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
            !string.IsNullOrWhiteSpace(b.Name),
            !string.IsNullOrWhiteSpace(b.ShortDescription),
            !string.IsNullOrWhiteSpace(b.Description),
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
    private async Task<AvailabilityExceptionDto> ToSchedulingExceptionDto(AvailabilityException item,
        CancellationToken cancellationToken)
    {
        var conflicts = await directory.CountFutureAppointmentConflictsAsync(item.BusinessId, item.StaffMemberId,
            item.Date, item.Type == AvailabilityExceptionType.ClosedAllDay ? null : item.OpensAt,
            item.Type == AvailabilityExceptionType.ClosedAllDay ? null : item.ClosesAt,
            item.Type == AvailabilityExceptionType.ExtraordinaryOpening, cancellationToken);
        return new(item.Id, item.StaffMemberId, item.Date, item.Type.ToString(),
            item.OpensAt, item.ClosesAt, item.Reason, conflicts, item.Version);
    }
    private DateOnly NextDate(DayOfWeek day)
    {
        var date = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        do { date = date.AddDays(1); } while (date.DayOfWeek != day);
        return date;
    }
    private static void EnsureVersion(long actual, long expected, string message)
    {
        if (actual != expected) throw new ApiException("CONCURRENCY_CONFLICT", message, 409);
    }
    private static object Snapshot(Business b) => new { b.Status, b.IsPublished, b.Version };
    private static T TryDomain<T>(Func<T> action)
    {
        try { return action(); } catch (DomainException ex) { throw new ApiException(ex.Code, ex.Message, 400); }
    }
}
