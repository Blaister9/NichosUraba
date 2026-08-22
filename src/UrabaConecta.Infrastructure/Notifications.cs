using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using UrabaConecta.Application;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.Infrastructure;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";
    /// <summary>Espera entre sondeos cuando no había nada que hacer.</summary>
    public int IdlePollSeconds { get; set; } = 15;
    /// <summary>Espera entre pasadas cuando la anterior sí encontró trabajo.</summary>
    public int BusyPollSeconds { get; set; } = 1;
    public int FanOutBatchSize { get; set; } = 100;
    public int DispatchBatchSize { get; set; } = 50;
    /// <summary>Cuánto tiempo se reserva una entrega antes de que otro trabajador pueda retomarla.</summary>
    public int LeaseSeconds { get; set; } = 120;
    /// <summary>Días que se conservan los avisos ya leídos y las entregas terminadas.</summary>
    public int RetentionDays { get; set; } = 45;
    /// <summary>
    /// Apagar el bucle de fondo. Sólo lo usan las pruebas que necesitan ejecutar el buzón paso a
    /// paso; en cualquier ambiente real el trabajador tiene que estar encendido.
    /// </summary>
    public bool BackgroundWorkerEnabled { get; set; } = true;
}

/// <summary>
/// Golpecito en memoria para que el trabajador no espere al siguiente sondeo. No es durable a
/// propósito: lo durable es la fila en la base, y el sondeo la encuentra igual si el golpecito se
/// pierde por un reinicio.
/// </summary>
public sealed class NotificationSignal : INotificationSignal
{
    private readonly SemaphoreSlim gate = new(0, 1);

    public void Pulse()
    {
        try { gate.Release(); }
        catch (SemaphoreFullException) { /* ya había un golpecito sin atender */ }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        => gate.WaitAsync(timeout, cancellationToken);
}

/// <summary>
/// Guarda el hecho y avisa en vivo. Nada de lo que hace aquí depende de un servicio externo, y
/// por eso ninguna operación de negocio puede caerse porque Web Push esté caído.
/// </summary>
public sealed class NotificationPublisher(AppDbContext db, IRealtimeNotifier realtime,
    INotificationSignal signal, TimeProvider clock, ILogger<NotificationPublisher> logger)
    : INotificationPublisher
{
    public Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        => PublishManyAsync([request], cancellationToken);

    public async Task PublishManyAsync(IReadOnlyList<NotificationRequest> requests,
        CancellationToken cancellationToken = default)
    {
        if (requests.Count == 0) return;
        var now = clock.GetUtcNow();
        var saved = new List<Notification>(requests.Count);
        foreach (var request in requests)
        {
            var notification = await PersistAsync(request, now, cancellationToken);
            if (notification is not null) saved.Add(notification);
        }
        if (saved.Count == 0) return;
        signal.Pulse();
        await AnnounceAsync(saved, cancellationToken);
    }

    private async Task<Notification?> PersistAsync(NotificationRequest request, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Comprobar antes evita el camino de excepción en el caso normal; la restricción única de la
        // base sigue siendo la que decide cuando dos peticiones llegan a la vez.
        if (await db.Notifications.AsNoTracking()
                .AnyAsync(x => x.DedupKey == request.DedupKey, cancellationToken))
            return null;
        Notification notification;
        try
        {
            notification = new Notification(Guid.NewGuid(), request.BusinessId, request.Audience,
                request.Kind, request.Title, request.Body, request.DeepLink, request.EntityType,
                request.EntityId, request.DedupKey, now, request.PushAudience, request.Renotify,
                request.DeactivateTargetAfterDelivery);
        }
        catch (DomainException ex)
        {
            // Un aviso mal formado no puede tumbar la operación que ya se completó. Se registra y
            // se sigue: el negocio ya tiene su pedido, su cita o su turno.
            logger.LogError(ex, "Aviso descartado por datos inválidos: {Kind} de {BusinessId}.",
                request.Kind, request.BusinessId);
            return null;
        }
        db.Notifications.Add(notification);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return notification;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Otro camino guardó el mismo hecho entre la comprobación y el guardado.
            db.Entry(notification).State = EntityState.Detached;
            return null;
        }
    }

    /// <summary>
    /// El aviso en vivo. Va detrás del guardado y no dentro: si el circuito falla, el hecho ya está
    /// en la base y quien recargue lo verá igual.
    /// </summary>
    private async Task AnnounceAsync(IReadOnlyList<Notification> saved, CancellationToken cancellationToken)
    {
        foreach (var notification in saved)
        {
            try
            {
                if (notification.Audience == NotificationAudience.Business)
                {
                    await realtime.BusinessChangedAsync(notification.BusinessId,
                        RealtimeChannels.Notifications, cancellationToken);
                    if (ChannelFor(notification.Kind) is { } channel)
                        await realtime.BusinessChangedAsync(notification.BusinessId, channel, cancellationToken);
                }
                else if (notification.EntityId is { } entityId)
                    await realtime.TrackingChangedAsync(notification.EntityType, entityId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "No se pudo anunciar en vivo el aviso {NotificationId}.", notification.Id);
            }
        }
    }

    internal static string? ChannelFor(NotificationKind kind) => kind switch
    {
        NotificationKind.AppointmentRequested or NotificationKind.AppointmentConfirmed
            or NotificationKind.AppointmentRejected or NotificationKind.AppointmentCancelled
            or NotificationKind.AppointmentCompleted or NotificationKind.AppointmentNoShow
            => RealtimeChannels.Appointments,
        NotificationKind.OrderPlaced or NotificationKind.OrderAccepted or NotificationKind.OrderRejected
            or NotificationKind.OrderPreparing or NotificationKind.OrderReady
            or NotificationKind.OrderDelivered or NotificationKind.OrderCancelled
            => RealtimeChannels.Orders,
        _ => null
    };

    internal static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

/// <summary>
/// El trabajador del buzón. Dos fases por pasada: repartir lo que todavía no tiene destinos y
/// enviar lo que ya los tiene. Las dos son idempotentes, así que un reinicio a mitad no duplica ni
/// pierde nada: lo que quedó en Pending sigue en Pending.
/// </summary>
public sealed class NotificationDispatcher(AppDbContext db, IWebPushTransport transport,
    IPersonalDataProtector protector, IOptions<WebPushOptions> pushOptions,
    IOptions<NotificationOptions> options, TimeProvider clock, ILogger<NotificationDispatcher> logger)
    : INotificationDispatcher
{
    private static readonly Guid Worker = Guid.NewGuid();
    private readonly NotificationOptions settings = options.Value;
    private readonly WebPushOptions push = pushOptions.Value;

    public async Task<NotificationDispatchReport> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var fannedOut = await FanOutAsync(cancellationToken);
        var dispatched = await DispatchAsync(cancellationToken);
        return dispatched with { FannedOut = fannedOut };
    }

    /// <summary>
    /// Convierte cada aviso pendiente en una entrega por dispositivo elegible. Un aviso sin
    /// dispositivos queda repartido igual, con cero entregas: el hecho ya está en la bandeja y no
    /// hay nada más que intentar.
    /// </summary>
    private async Task<int> FanOutAsync(CancellationToken cancellationToken)
    {
        var pending = await db.Notifications
            .Where(x => x.FannedOutAtUtc == null)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(settings.FanOutBatchSize)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0) return 0;

        var now = clock.GetUtcNow();
        foreach (var notification in pending)
        {
            // Sin VAPID no hay a dónde enviar. El aviso queda repartido con cero entregas en vez de
            // acumularse: la bandeja sigue siendo correcta y el buzón no crece sin sentido.
            var targets = notification.PushAudience is null || !push.IsConfigured
                ? []
                : await ResolveTargetsAsync(notification, cancellationToken);
            foreach (var subscriptionId in targets)
                db.NotificationDeliveries.Add(new NotificationDelivery(Guid.NewGuid(), notification.Id,
                    notification.BusinessId, subscriptionId, now));
            notification.MarkFannedOut(now);
        }
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (NotificationPublisher.IsUniqueViolation(ex))
        {
            // Otro trabajador repartió el mismo aviso. Se descarta la pasada; la siguiente lee el
            // estado ya escrito por el otro y no vuelve a intentarlo.
            logger.LogDebug(ex, "Reparto simultáneo del buzón; se cede la pasada.");
            foreach (var entry in db.ChangeTracker.Entries<NotificationDelivery>().ToList())
                entry.State = EntityState.Detached;
            return 0;
        }
        return pending.Count;
    }

    /// <summary>
    /// A qué suscripciones alcanza el aviso. Para el negocio se vuelve a comprobar la membresía y el
    /// permiso concreto: quien perdió el acceso a pedidos deja de recibir avisos de pedidos aunque
    /// su dispositivo siga registrado.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveTargetsAsync(Notification notification,
        CancellationToken cancellationToken)
    {
        var audience = notification.PushAudience!.Value;
        if (audience == PushAudience.Owner)
        {
            var query = db.WebPushSubscriptions.AsNoTracking().Where(x =>
                x.BusinessId == notification.BusinessId && x.Audience == PushAudience.Owner &&
                x.IsActive && x.UserId != null);
            var permission = PermissionFor(notification.Kind);
            query = permission switch
            {
                OperationalPermission.Appointments => query.Where(x => db.BusinessMemberships.Any(m =>
                    m.BusinessId == notification.BusinessId && m.UserId == x.UserId && m.IsActive &&
                    (m.Role == MembershipRole.Owner || m.CanManageAppointments))),
                OperationalPermission.Queues => query.Where(x => db.BusinessMemberships.Any(m =>
                    m.BusinessId == notification.BusinessId && m.UserId == x.UserId && m.IsActive &&
                    (m.Role == MembershipRole.Owner || m.CanManageQueues))),
                OperationalPermission.Orders => query.Where(x => db.BusinessMemberships.Any(m =>
                    m.BusinessId == notification.BusinessId && m.UserId == x.UserId && m.IsActive &&
                    (m.Role == MembershipRole.Owner || m.CanManageOrders))),
                _ => query.Where(x => db.BusinessMemberships.Any(m =>
                    m.BusinessId == notification.BusinessId && m.UserId == x.UserId && m.IsActive))
            };
            return await query.Select(x => x.Id).ToListAsync(cancellationToken);
        }
        if (notification.EntityId is not { } entityId) return [];
        return await db.WebPushSubscriptions.AsNoTracking()
            .Where(x => x.Audience == audience && x.EntityId == entityId && x.IsActive)
            .Select(x => x.Id).ToListAsync(cancellationToken);
    }

    private enum OperationalPermission { None, Appointments, Queues, Orders }

    private static OperationalPermission PermissionFor(NotificationKind kind) => kind switch
    {
        NotificationKind.AppointmentRequested or NotificationKind.AppointmentConfirmed
            or NotificationKind.AppointmentRejected or NotificationKind.AppointmentCancelled
            or NotificationKind.AppointmentCompleted or NotificationKind.AppointmentNoShow
            => OperationalPermission.Appointments,
        NotificationKind.QueueTicketJoined or NotificationKind.QueueTicketAlmost
            or NotificationKind.QueueTicketCalled or NotificationKind.QueueTicketServed
            or NotificationKind.QueueTicketCancelled => OperationalPermission.Queues,
        NotificationKind.OrderPlaced or NotificationKind.OrderAccepted or NotificationKind.OrderRejected
            or NotificationKind.OrderPreparing or NotificationKind.OrderReady
            or NotificationKind.OrderDelivered or NotificationKind.OrderCancelled
            => OperationalPermission.Orders,
        _ => OperationalPermission.None
    };

    private async Task<NotificationDispatchReport> DispatchAsync(CancellationToken cancellationToken)
    {
        if (!push.IsConfigured) return NotificationDispatchReport.Empty;
        var now = clock.GetUtcNow();
        var until = now.AddSeconds(settings.LeaseSeconds);
        var claimed = await ClaimAsync(now, until, cancellationToken);
        if (claimed.Count == 0) return NotificationDispatchReport.Empty;

        // La reserva se escribió con SQL directo. Si la entrega nació en el reparto de esta misma
        // pasada, EF devolvería la instancia que ya tiene en seguimiento —sin reserva— y al
        // soltarla no vería ningún cambio: la reserva se quedaría escrita en la base y el
        // reintento no volvería a salir hasta que caducara. Vaciar el seguimiento obliga a releer
        // el estado real, que es el único que puede compararse con lo que se va a escribir.
        db.ChangeTracker.Clear();
        var deliveries = await db.NotificationDeliveries
            .Where(x => claimed.Contains(x.Id)).ToListAsync(cancellationToken);
        var notificationIds = deliveries.Select(x => x.NotificationId).Distinct().ToList();
        var subscriptionIds = deliveries.Select(x => x.SubscriptionId).Distinct().ToList();
        var notifications = await db.Notifications.Where(x => notificationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var subscriptions = await db.WebPushSubscriptions.Where(x => subscriptionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        int attempted = 0, sent = 0, retried = 0, expired = 0, abandoned = 0, skipped = 0;
        foreach (var delivery in deliveries)
        {
            if (delivery.IsFinal) continue;
            if (!notifications.TryGetValue(delivery.NotificationId, out var notification) ||
                !subscriptions.TryGetValue(delivery.SubscriptionId, out var subscription))
            {
                delivery.MarkSkipped(clock.GetUtcNow(), "TARGET_MISSING"); skipped++; continue;
            }
            if (!subscription.IsActive)
            {
                delivery.MarkSkipped(clock.GetUtcNow(), "SUBSCRIPTION_INACTIVE"); skipped++; continue;
            }

            attempted++;
            var message = BuildMessage(notification, subscription);
            try
            {
                await transport.SendAsync(subscription, message, cancellationToken);
                var moment = clock.GetUtcNow();
                delivery.MarkSent(moment);
                subscription.MarkDelivered(moment);
                // El aviso de reposición consume la suscripción: ya cumplió lo que se le pidió.
                if (notification.DeactivateTargetAfterDelivery) subscription.Deactivate(moment);
                sent++;
            }
            catch (PushDeliveryException ex)
            {
                var moment = clock.GetUtcNow();
                if (ex.StatusCode is (int)HttpStatusCode.NotFound or (int)HttpStatusCode.Gone)
                {
                    delivery.MarkExpired(moment, ex.StatusCode, Reason(ex));
                    subscription.MarkFailed(moment, expired: true);
                    expired++;
                }
                else
                {
                    delivery.MarkTransientFailure(moment, ex.StatusCode, Reason(ex));
                    subscription.MarkFailed(moment, expired: false);
                    if (delivery.Status == NotificationDeliveryStatus.Abandoned) abandoned++; else retried++;
                }
                logger.LogWarning("Web Push respondió {StatusCode} para la entrega {DeliveryId}.",
                    ex.StatusCode, delivery.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                var moment = clock.GetUtcNow();
                delivery.MarkTransientFailure(moment, null, ex.GetType().Name);
                subscription.MarkFailed(moment, expired: false);
                if (delivery.Status == NotificationDeliveryStatus.Abandoned) abandoned++; else retried++;
                logger.LogWarning(ex, "Falló la entrega {DeliveryId}.", delivery.Id);
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return new(0, attempted, sent, retried, expired, abandoned, skipped);
    }

    /// <summary>
    /// El motivo que se guarda: tipo de excepción y código, nunca el mensaje del proveedor. Ese
    /// mensaje puede llevar dentro el endpoint del dispositivo, que identifica al navegador de una
    /// persona, y esta fila la lee después la propietaria en su diagnóstico. Es el mismo criterio
    /// que ya sigue la salud de la instalación con las excepciones de base de datos.
    /// </summary>
    private static string Reason(PushDeliveryException exception)
        => exception.InnerException is { } inner
            ? $"{inner.GetType().Name} ({exception.StatusCode})"
            : $"PushDeliveryException ({exception.StatusCode})";

    private PushMessage BuildMessage(Notification notification, WebPushSubscription subscription)
    {
        // El aviso manda su propio enlace cuando lo tiene —la ruta del panel, la de una promoción—.
        // Si no, se usa el que quedó cifrado en la suscripción, que es donde vive el enlace de
        // seguimiento porque lleva el código del cliente.
        var url = notification.DeepLink
            ?? (subscription.ProtectedDeepLink is { Length: > 0 } link ? protector.Unprotect(link) : "");
        var tag = notification.EntityId is { } entityId
            ? $"{notification.EntityType.ToLowerInvariant()}-{entityId:N}"
            : $"business-{notification.BusinessId:N}";
        return new(notification.Title, notification.Body, url, tag, notification.Renotify);
    }

    /// <summary>
    /// Reserva un lote con FOR UPDATE SKIP LOCKED. Es lo que impide que dos instancias envíen la
    /// misma entrega: la que no consigue el bloqueo se salta la fila en vez de esperarla.
    /// </summary>
    private async Task<List<Guid>> ClaimAsync(DateTimeOffset now, DateTimeOffset until,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE notification_deliveries AS d
            SET "LeaseOwner" = @owner, "LeasedUntilUtc" = @until
            FROM (
                SELECT "Id" FROM notification_deliveries
                WHERE "Status" = 'Pending' AND "NextAttemptAtUtc" <= @now
                  AND ("LeasedUntilUtc" IS NULL OR "LeasedUntilUtc" <= @now)
                ORDER BY "NextAttemptAtUtc"
                LIMIT @batch
                FOR UPDATE SKIP LOCKED
            ) AS c
            WHERE d."Id" = c."Id"
            RETURNING d."Id" AS "Value"
            """;
        return await db.Database.SqlQueryRaw<Guid>(sql,
                new NpgsqlParameter("owner", Worker),
                new NpgsqlParameter("until", until),
                new NpgsqlParameter("now", now),
                new NpgsqlParameter("batch", settings.DispatchBatchSize))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Poda. Sólo borra lo terminado y ya leído; un aviso sin leer se queda por antiguo que sea,
    /// porque su razón de existir es que alguien todavía no se enteró.
    /// </summary>
    public async Task<int> PruneAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = clock.GetUtcNow().AddDays(-settings.RetentionDays);
        var deliveries = await db.NotificationDeliveries
            .Where(x => x.Status != NotificationDeliveryStatus.Pending && x.CompletedAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        var notifications = await db.Notifications
            .Where(x => x.ReadAtUtc != null && x.CreatedAtUtc < cutoff &&
                        !db.NotificationDeliveries.Any(d => d.NotificationId == x.Id &&
                            d.Status == NotificationDeliveryStatus.Pending))
            .ExecuteDeleteAsync(cancellationToken);
        return deliveries + notifications;
    }
}
