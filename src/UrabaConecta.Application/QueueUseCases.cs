using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Application;

public sealed class QueueUseCases(IQueueStore store, IPublicCodeService codes, IPersonalDataProtector protector,
    IQueueChangeNotifier notifier, IConsentPolicyProvider consentPolicy, IPushNotificationService push,
    TimeProvider clock) : IQueueUseCases
{
    public async Task<QueuePublicStatusDto?> GetPublicAsync(string slug, CancellationToken ct = default)
    {
        var context = await store.GetPublicContextAsync(slug, ct);
        if (context is null) return null;
        var found = context.Value;
        var session = await store.GetCurrentSessionAsync(found.Business.Id, ct);
        var tickets = session is null ? [] : await store.GetSessionTicketsAsync(found.Business.Id, session.Id, ct);
        return PublicDto(found.Definition, found.Business, session, tickets);
    }

    public async Task<QueueTicketCreatedDto> JoinAsync(string slug, CreateQueueTicketRequest request, CancellationToken ct = default)
    {
        // El turno público exige aceptar la versión vigente del aviso, igual que las citas y los pedidos.
        if (!request.ConsentAccepted || request.ConsentNoticeVersion != consentPolicy.CurrentVersion)
            throw new ApiException("CONSENT_REQUIRED", "Debe aceptar la versión vigente del aviso de tratamiento de datos.");
        var context = await store.GetPublicContextAsync(slug, ct)
            ?? throw new ApiException("QUEUE_NOT_FOUND", "La fila virtual no está disponible.", 404);
        return await CreateTicket(context.Definition, context.Business.Id, request, QueueTicketSource.Online, ct);
    }

    public async Task<QueueTicketTrackingDto?> TrackAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var row = await store.FindTicketAsync(codes.Hash(code), ct);
        if (row is null) return null;
        var found = row.Value;
        var tickets = await store.GetSessionTicketsAsync(found.Ticket.BusinessId, found.Ticket.QueueSessionId, ct);
        return TrackingDto(found.Ticket, found.Definition, found.Business, tickets);
    }

    public async Task CancelPublicAsync(string code, long version, CancellationToken ct = default)
    {
        var row = await store.FindTicketAsync(codes.Hash(code), ct)
            ?? throw new ApiException("QUEUE_TICKET_NOT_FOUND", "No encontramos el turno.", 404);
        if (row.Ticket.Status != QueueTicketStatus.Waiting)
            throw new ApiException("QUEUE_TICKET_CANNOT_CANCEL", "Este turno ya no puede cancelarse desde el enlace público.", 409);
        TryDomain(() => row.Ticket.Cancel(clock.GetUtcNow(), version));
        await store.SaveChangesAsync(ct);
        await Notify(row.Definition.Id, row.Ticket.Id, row.Business.Id, ct);
    }

    public async Task<QueueAdminDto> GetAdminAsync(Guid userId, Guid businessId, CancellationToken ct = default)
    {
        await Demand(userId, businessId, ct);
        return await AdminDto(businessId, ct);
    }

    public async Task<QueueDefinitionDto> SaveDefinitionAsync(Guid userId, Guid businessId,
        SaveQueueDefinitionRequest request, CancellationToken ct = default)
    {
        await Demand(userId, businessId, ct);
        var business = await store.GetBusinessNameAsync(businessId, ct)
            ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos el establecimiento.", 404);
        var definition = await store.GetDefinitionAsync(businessId, ct);
        if (definition is null)
        {
            definition = TryDomain(() => new QueueDefinition(Guid.NewGuid(), businessId, request.Name,
                request.AverageDurationMinutes, request.MaximumWaiting, request.PublicMessage,
                request.IsEnabled, clock.GetUtcNow()));
            store.AddDefinition(definition);
        }
        else
        {
            var session = await store.GetCurrentSessionAsync(businessId, ct);
            if (!request.IsEnabled && session is not null)
                throw new ApiException("QUEUE_SESSION_ACTIVE", "Cierre la jornada antes de desactivar la fila.", 409);
            TryDomain(() => definition.Update(request.Name, request.AverageDurationMinutes,
                request.MaximumWaiting, request.PublicMessage, request.IsEnabled, clock.GetUtcNow(), request.Version));
        }
        await store.SaveChangesAsync(ct);
        await notifier.PublicChangedAsync(definition.Id, ct);
        await notifier.OperationsChangedAsync(businessId, ct);
        return DefinitionDto(definition, business.BusinessName, business.BusinessSlug, business.TimeZoneId);
    }

    public async Task<QueueAdminDto> OpenAsync(Guid userId, Guid businessId, CancellationToken ct = default)
    {
        await Demand(userId, businessId, ct);
        await DemandActiveBusiness(businessId, ct);
        await using var tx = await store.BeginTransactionAsync(ct);
        var definition = await store.GetDefinitionAsync(businessId, ct)
            ?? throw new ApiException("QUEUE_NOT_CONFIGURED", "Configure la fila antes de abrir.", 409);
        if (!definition.IsEnabled) throw new ApiException("QUEUE_DISABLED", "La fila está desactivada.", 409);
        if (await store.LockCurrentSessionAsync(businessId, ct) is not null)
            throw new ApiException("QUEUE_ALREADY_ACTIVE", "Ya existe una jornada abierta o pausada.", 409);
        store.AddSession(new QueueSession(Guid.NewGuid(), businessId, definition.Id, clock.GetUtcNow()));
        await store.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        await Notify(definition.Id, null, businessId, ct);
        return await AdminDto(businessId, ct);
    }

    public Task<QueueAdminDto> PauseAsync(Guid userId, Guid businessId, long version, CancellationToken ct = default)
        => SessionCommand(userId, businessId, version, (s, now, active) => s.Pause(now, version), ct);
    public Task<QueueAdminDto> ResumeAsync(Guid userId, Guid businessId, long version, CancellationToken ct = default)
        => SessionCommand(userId, businessId, version, (s, now, active) => s.Resume(version), ct);
    public Task<QueueAdminDto> CloseAsync(Guid userId, Guid businessId, long version, CancellationToken ct = default)
        => SessionCommand(userId, businessId, version, (s, now, active) => s.Close(now, active, version), ct);

    public async Task<QueueTicketCreatedDto> WalkInAsync(Guid userId, Guid businessId,
        CreateQueueTicketRequest request, CancellationToken ct = default)
    {
        await Demand(userId, businessId, ct);
        await DemandActiveBusiness(businessId, ct);
        var definition = await store.GetDefinitionAsync(businessId, ct)
            ?? throw new ApiException("QUEUE_NOT_CONFIGURED", "La fila no está configurada.", 409);
        return await CreateTicket(definition, businessId, request, QueueTicketSource.WalkIn, ct);
    }

    public async Task<QueueAdminDto> CallNextAsync(Guid userId, Guid businessId, long sessionVersion,
        CancellationToken ct = default)
    {
        await Demand(userId, businessId, ct);
        await using var tx = await store.BeginTransactionAsync(ct);
        var session = await store.LockCurrentSessionAsync(businessId, ct)
            ?? throw new ApiException("QUEUE_NOT_ACTIVE", "No hay una jornada activa.", 409);
        if (session.Status == QueueSessionStatus.Closed)
            throw new ApiException("QUEUE_NOT_ACTIVE", "La jornada está cerrada.", 409);
        TryDomain(() => session.Touch(sessionVersion));
        var tickets = await store.GetSessionTicketsAsync(businessId, session.Id, ct);
        if (tickets.Any(x => x.Status is QueueTicketStatus.Called or QueueTicketStatus.InService))
            throw new ApiException("QUEUE_SERVICE_ACTIVE",
                "Complete, omita o cancele el turno actual antes de llamar el siguiente.", 409);
        var ticket = await store.GetNextWaitingAsync(businessId, session.Id, ct)
            ?? throw new ApiException("QUEUE_EMPTY", "No hay turnos en espera.", 409);
        TryDomain(() => ticket.Call(clock.GetUtcNow(), ticket.Version));
        await store.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        var definition = await store.GetDefinitionAsync(businessId, ct);
        await Notify(definition!.Id, ticket.Id, businessId, ct);
        await push.NotifyClientAsync(PushAudience.QueueTicket, ticket.Id,
            new("Es tu turno", $"Turno #{ticket.Number}: te están llamando.", "",
                $"queue-{ticket.Id}", true), ct);
        var nearby = tickets.Where(x => x.Status == QueueTicketStatus.Waiting).OrderBy(x => x.Number).Take(2).ToList();
        if (nearby.Count > 0)
            await push.NotifyClientAsync(PushAudience.QueueTicket, nearby[0].Id,
                new("Ya puedes venir", $"Eres la siguiente persona después del turno #{ticket.Number}.", "",
                    $"queue-{nearby[0].Id}"), ct);
        if (nearby.Count > 1)
            await push.NotifyClientAsync(PushAudience.QueueTicket, nearby[1].Id,
                new("Tu turno se aproxima", "Hay pocas personas delante de ti. Revisa tu seguimiento.", "",
                    $"queue-{nearby[1].Id}"), ct);
        return await AdminDto(businessId, ct);
    }

    public async Task<QueueAdminDto> ChangeTicketAsync(Guid userId, Guid businessId, Guid ticketId,
        string action, QueueTicketCommandRequest request, CancellationToken ct = default)
    {
        await Demand(userId, businessId, ct);
        await using var tx = await store.BeginTransactionAsync(ct);
        var session = await store.LockCurrentSessionAsync(businessId, ct)
            ?? throw new ApiException("QUEUE_NOT_ACTIVE", "No hay una jornada activa.", 409);
        TryDomain(() => session.Touch(request.SessionVersion));
        var ticket = await store.GetTicketAsync(businessId, ticketId, ct)
            ?? throw new ApiException("QUEUE_TICKET_NOT_FOUND", "No encontramos el turno en este establecimiento.", 404);
        if (ticket.QueueSessionId != session.Id)
            throw new ApiException("QUEUE_TICKET_NOT_FOUND", "El turno no pertenece a la jornada activa.", 404);
        var now = clock.GetUtcNow();
        TryDomain(() =>
        {
            switch (action.ToLowerInvariant())
            {
                case "recall": ticket.Recall(now, request.TicketVersion); break;
                case "start": ticket.Start(now, request.TicketVersion); break;
                case "complete": ticket.Complete(now, request.TicketVersion); break;
                case "skip": ticket.Skip(now, request.TicketVersion); break;
                case "cancel": ticket.Cancel(now, request.TicketVersion); break;
                case "restore": ticket.Restore(now, request.TicketVersion); break;
                default: throw new DomainException("INVALID_QUEUE_ACTION", "La acción no es válida.");
            }
        });
        await store.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        var definition = await store.GetDefinitionAsync(businessId, ct);
        await Notify(definition!.Id, ticket.Id, businessId, ct);
        return await AdminDto(businessId, ct);
    }

    private async Task<QueueTicketCreatedDto> CreateTicket(QueueDefinition definition, Guid businessId,
        CreateQueueTicketRequest request, QueueTicketSource source, CancellationToken ct, string? ipAddress = null)
    {
        var alias = request.Alias?.Trim();
        if ((alias?.Length ?? 0) > 40) throw new ApiException("INVALID_ALIAS", "El alias puede tener máximo 40 caracteres.");
        await using var tx = await store.BeginTransactionAsync(ct);
        var session = await store.LockCurrentSessionAsync(businessId, ct)
            ?? throw new ApiException("QUEUE_NOT_OPEN", "La fila no está abierta.", 409);
        if (session.Status != QueueSessionStatus.Open)
            throw new ApiException("QUEUE_NOT_OPEN", "La fila no está recibiendo turnos.", 409);
        var waiting = await store.CountWaitingAsync(businessId, session.Id, ct);
        if (waiting >= definition.MaximumWaiting)
            throw new ApiException("QUEUE_FULL", "La fila alcanzó su capacidad de espera.", 409);
        var number = TryDomain(() => session.AllocateNumber(session.Version));
        var publicCode = codes.Generate();
        var now = clock.GetUtcNow();
        var ticket = new QueueTicket(Guid.NewGuid(), businessId, session.Id, number, publicCode.Hash,
            string.IsNullOrEmpty(alias) ? null : protector.Protect(alias), source, now);
        store.AddTicket(ticket);
        if (source == QueueTicketSource.Online)
        {
            var consent = new ConsentReceipt(Guid.NewGuid(), businessId, request.ConsentNoticeVersion,
                "Gestionar el turno virtual y anunciar el llamado.", now);
            consent.LinkQueueTicket(ticket.Id);
            consent.RecordOrigin(ipAddress);
            store.AddConsent(consent);
        }
        await store.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        await Notify(definition.Id, ticket.Id, businessId, ct);
        if (source == QueueTicketSource.Online)
            await push.NotifyBusinessAsync(businessId, new("Nuevo turno en la fila",
                $"Turno #{ticket.Number} se unió desde UrabáConecta.",
                $"/panel/{businessId}/turnos?turno={ticket.Id}",
                $"business-queue-{ticket.Id}"), ct);
        return new(number, publicCode.PlainText, ticket.Status.ToString(), waiting, waiting * definition.AverageDurationMinutes);
    }

    private async Task<QueueAdminDto> SessionCommand(Guid userId, Guid businessId, long version,
        Action<QueueSession, DateTimeOffset, int> command, CancellationToken ct)
    {
        await Demand(userId, businessId, ct);
        await using var tx = await store.BeginTransactionAsync(ct);
        var session = await store.LockCurrentSessionAsync(businessId, ct)
            ?? throw new ApiException("QUEUE_NOT_ACTIVE", "No hay una jornada activa.", 409);
        var active = await store.CountActiveAsync(businessId, session.Id, ct);
        TryDomain(() => command(session, clock.GetUtcNow(), active));
        var definitionId = session.QueueDefinitionId;
        await store.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        await Notify(definitionId, null, businessId, ct);
        return await AdminDto(businessId, ct);
    }

    private async Task<QueueAdminDto> AdminDto(Guid businessId, CancellationToken ct)
    {
        var definition = await store.GetDefinitionAsync(businessId, ct)
            ?? throw new ApiException("QUEUE_NOT_CONFIGURED", "La fila no está configurada.", 404);
        var business = await store.GetBusinessNameAsync(businessId, ct)
            ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos el establecimiento.", 404);
        var session = await store.GetCurrentSessionAsync(businessId, ct);
        var tickets = session is null ? [] : await store.GetSessionTicketsAsync(businessId, session.Id, ct);
        return new(DefinitionDto(definition, business.BusinessName, business.BusinessSlug, business.TimeZoneId),
            session?.Status.ToString() ?? QueueSessionStatus.Closed.ToString(), session?.Id, session?.Version,
            tickets.Where(x => x.Status is QueueTicketStatus.Called or QueueTicketStatus.InService)
                .OrderBy(x => x.Number).Select(x => (int?)x.Number).FirstOrDefault(),
            tickets.Count(x => x.Status == QueueTicketStatus.Waiting), session?.NextNumber ?? 1,
            tickets.Select(AdminTicket).ToList());
    }

    private QueueTicketAdminDto AdminTicket(QueueTicket t) => new(t.Id, t.Number,
        t.ProtectedAlias is null ? null : protector.Unprotect(t.ProtectedAlias), t.Source.ToString(),
        t.Status.ToString(), t.CallCount, t.RestoreCount, t.CreatedAtUtc, t.UpdatedAtUtc, t.Version);
    private static QueueDefinitionDto DefinitionDto(QueueDefinition q, string name, string slug, string timeZoneId)
        => new(q.Id, q.BusinessId, name, slug, q.Name, q.AverageDurationMinutes, q.MaximumWaiting,
            q.PublicMessage, q.IsEnabled, q.Version, timeZoneId);
    private static QueuePublicStatusDto PublicDto(QueueDefinition q, Business b, QueueSession? s,
        IReadOnlyList<QueueTicket> tickets)
    {
        var waiting = tickets.Count(x => x.Status == QueueTicketStatus.Waiting);
        var current = tickets.Where(x => x.Status is QueueTicketStatus.Called or QueueTicketStatus.InService)
            .OrderBy(x => x.Number).Select(x => (int?)x.Number).FirstOrDefault();
        return new(b.Name, b.Slug, q.Name, q.PublicMessage, q.IsEnabled,
            s?.Status.ToString() ?? QueueSessionStatus.Closed.ToString(), current, waiting,
            waiting * q.AverageDurationMinutes, q.IsEnabled && s?.Status == QueueSessionStatus.Open,
            s?.Version ?? q.Version);
    }
    private static QueueTicketTrackingDto TrackingDto(QueueTicket t, QueueDefinition q, Business b,
        IReadOnlyList<QueueTicket> tickets)
    {
        var ahead = t.Status == QueueTicketStatus.Waiting
            ? tickets.Count(x => x.Number < t.Number && x.Status is QueueTicketStatus.Waiting or QueueTicketStatus.Called or QueueTicketStatus.InService)
            : 0;
        var label = t.Status switch
        {
            QueueTicketStatus.Waiting => "En espera", QueueTicketStatus.Called => "Te están llamando",
            QueueTicketStatus.InService => "En atención", QueueTicketStatus.Completed => "Atendido",
            QueueTicketStatus.Skipped => "No se presentó", _ => "Cancelado"
        };
        return new(t.Number, t.Status.ToString(), label, b.Name, q.Name, ahead,
            ahead * q.AverageDurationMinutes, t.Status == QueueTicketStatus.Waiting, t.UpdatedAtUtc, t.Version);
    }
    private async Task Demand(Guid userId, Guid businessId, CancellationToken ct)
    {
        if (!await store.CanManageQueuesAsync(userId, businessId, ct))
            throw new ApiException("MEMBERSHIP_FORBIDDEN", "No tiene permiso para administrar turnos.", 403);
        // Ocultar el botón no basta: una URL directa llegaba igual al módulo no habilitado.
        if (!await store.IsModuleEnabledAsync(businessId, BusinessModuleKind.VirtualQueues, ct))
            throw new ApiException("MODULE_DISABLED", "Este establecimiento no tiene turnos habilitados.", 403);
    }
    private async Task DemandActiveBusiness(Guid businessId, CancellationToken ct)
    {
        if (!await store.IsBusinessActiveAsync(businessId, ct))
            throw new ApiException("BUSINESS_SUSPENDED",
                "Establecimiento suspendido. No se pueden iniciar operaciones nuevas.", 409);
    }
    private Task Notify(Guid definitionId, Guid? ticketId, Guid businessId, CancellationToken ct)
        => Task.WhenAll(notifier.PublicChangedAsync(definitionId, ct),
            ticketId.HasValue ? notifier.TicketChangedAsync(ticketId.Value, ct) : Task.CompletedTask,
            notifier.OperationsChangedAsync(businessId, ct));
    private static void TryDomain(Action action) { try { action(); } catch (DomainException e) { throw Convert(e); } }
    private static T TryDomain<T>(Func<T> action) { try { return action(); } catch (DomainException e) { throw Convert(e); } }
    private static ApiException Convert(DomainException e)
        => new(e.Code, e.Message, e.Code is "CONCURRENCY_CONFLICT" or "QUEUE_HAS_ACTIVE_TICKETS" ? 409 : 400);
}

