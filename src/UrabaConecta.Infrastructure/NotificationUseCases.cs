using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UrabaConecta.Application;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.Infrastructure;

/// <summary>
/// La bandeja. Es la respuesta a "¿y si el aviso no llegó?": aquí está, guardado, con su hora y su
/// enlace a la operación, sin depender de que ningún servicio externo haya funcionado.
/// </summary>
public sealed class NotificationUseCases(AppDbContext db, IPublicCodeService codes,
    IOptions<WebPushOptions> pushOptions, TimeProvider clock) : INotificationUseCases
{
    /// <summary>Tope de la bandeja en una lectura. Más que esto no cabe en una pantalla de móvil.</summary>
    private const int MaximumPageSize = 100;

    private static readonly NotificationKind[] AppointmentKinds =
    [
        NotificationKind.AppointmentRequested, NotificationKind.AppointmentConfirmed,
        NotificationKind.AppointmentRejected, NotificationKind.AppointmentCancelled,
        NotificationKind.AppointmentCompleted, NotificationKind.AppointmentNoShow
    ];
    private static readonly NotificationKind[] QueueKinds =
    [
        NotificationKind.QueueTicketJoined, NotificationKind.QueueTicketAlmost,
        NotificationKind.QueueTicketCalled, NotificationKind.QueueTicketServed,
        NotificationKind.QueueTicketCancelled
    ];
    private static readonly NotificationKind[] OrderKinds =
    [
        NotificationKind.OrderPlaced, NotificationKind.OrderAccepted, NotificationKind.OrderRejected,
        NotificationKind.OrderPreparing, NotificationKind.OrderReady, NotificationKind.OrderDelivered,
        NotificationKind.OrderCancelled
    ];
    /// <summary>Avisos que no pertenecen a ninguna de las tres operaciones y ve cualquier miembro.</summary>
    private static readonly NotificationKind[] NeutralKinds =
    [
        NotificationKind.ProductRestocked, NotificationKind.PromotionPublished
    ];

    public async Task<NotificationPageDto> GetBusinessInboxAsync(Guid userId, Guid businessId,
        bool unreadOnly, int take, CancellationToken cancellationToken = default)
    {
        var access = await DemandMemberAsync(userId, businessId, cancellationToken);
        var allowed = AllowedKinds(access);
        var business = await db.Businesses.AsNoTracking()
            .Where(x => x.Id == businessId).Select(x => new { x.Name, x.TimeZoneId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos el establecimiento.", 404);

        var visible = db.Notifications.AsNoTracking().Where(x => x.BusinessId == businessId &&
            x.Audience == NotificationAudience.Business && allowed.Contains(x.Kind));
        var unread = await visible.CountAsync(x => x.ReadAtUtc == null, cancellationToken);
        var page = unreadOnly ? visible.Where(x => x.ReadAtUtc == null) : visible;
        var items = await page.OrderByDescending(x => x.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, MaximumPageSize))
            .Select(x => new NotificationDto(x.Id, x.Kind.ToString(), x.Title, x.Body, x.DeepLink,
                x.EntityType, x.EntityId, x.CreatedAtUtc, x.ReadAtUtc))
            .ToListAsync(cancellationToken);
        return new(businessId, business.Name, business.TimeZoneId, items, unread);
    }

    public async Task<IReadOnlyList<NotificationCountDto>> GetUnreadCountsAsync(Guid userId,
        CancellationToken cancellationToken = default)
    {
        // El alcance sale de las membresías de quien pregunta; no se acepta ninguna lista de
        // identificadores del cliente, que sería una vía para tantear qué negocios existen.
        var memberships = await db.BusinessMemberships.AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive)
            .Join(db.Businesses.AsNoTracking().Where(b => b.Status != BusinessStatus.Archived),
                m => m.BusinessId, b => b.Id,
                (m, b) => new MemberAccess(b.Id, b.Name, m.Role == MembershipRole.Owner,
                    m.Role == MembershipRole.Owner || m.CanManageAppointments,
                    m.Role == MembershipRole.Owner || m.CanManageQueues,
                    m.Role == MembershipRole.Owner || m.CanManageOrders))
            .ToListAsync(cancellationToken);
        if (memberships.Count == 0) return [];

        var businessIds = memberships.Select(x => x.BusinessId).ToArray();
        // Una lectura para todos los negocios: el contador vive en la barra y se pide en cada
        // pantalla, así que no puede costar una consulta por negocio.
        var counts = await db.Notifications.AsNoTracking()
            .Where(x => businessIds.Contains(x.BusinessId) &&
                        x.Audience == NotificationAudience.Business && x.ReadAtUtc == null)
            .Select(x => new { x.BusinessId, x.Kind })
            .ToListAsync(cancellationToken);
        return memberships.Select(access =>
        {
            var allowed = AllowedKinds(access);
            return new NotificationCountDto(access.BusinessId, access.BusinessName,
                counts.Count(c => c.BusinessId == access.BusinessId && allowed.Contains(c.Kind)));
        }).OrderBy(x => x.BusinessName).ToList();
    }

    public async Task<NotificationPageDto> MarkReadAsync(Guid userId, Guid businessId,
        MarkNotificationsReadRequest request, CancellationToken cancellationToken = default)
    {
        var access = await DemandMemberAsync(userId, businessId, cancellationToken);
        var allowed = AllowedKinds(access);
        var query = db.Notifications.Where(x => x.BusinessId == businessId &&
            x.Audience == NotificationAudience.Business && x.ReadAtUtc == null &&
            allowed.Contains(x.Kind));
        if (request.Ids.Count > 0)
        {
            var ids = request.Ids.Take(MaximumPageSize).ToArray();
            query = query.Where(x => ids.Contains(x.Id));
        }
        var pending = await query.ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        foreach (var notification in pending) notification.MarkRead(userId, now);
        if (pending.Count > 0) await db.SaveChangesAsync(cancellationToken);
        return await GetBusinessInboxAsync(userId, businessId, false, 30, cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationDto>> GetCustomerInboxAsync(PushAudience audience,
        string publicCode, CancellationToken cancellationToken = default)
    {
        // Se responde exactamente igual ante un código con formato inválido y ante uno que no existe:
        // una diferencia aquí convertiría la ruta en un detector de códigos válidos.
        if (string.IsNullOrWhiteSpace(publicCode) || publicCode.Length is < 20 or > 128) return [];
        var hash = codes.Hash(publicCode);
        var entityId = audience switch
        {
            PushAudience.Appointment => await db.Appointments.AsNoTracking()
                .Where(x => x.PublicCodeHash == hash).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken),
            PushAudience.QueueTicket => await db.QueueTickets.AsNoTracking()
                .Where(x => x.PublicCodeHash == hash).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken),
            PushAudience.PickupOrder => await db.PickupOrders.AsNoTracking()
                .Where(x => x.PublicCodeHash == hash).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken),
            _ => null
        };
        if (entityId is null) return [];
        return await db.Notifications.AsNoTracking()
            .Where(x => x.Audience == NotificationAudience.Customer && x.EntityId == entityId)
            .OrderByDescending(x => x.CreatedAtUtc).Take(30)
            .Select(x => new NotificationDto(x.Id, x.Kind.ToString(), x.Title, x.Body, null,
                x.EntityType, x.EntityId, x.CreatedAtUtc, x.ReadAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<NotificationDiagnosticsDto> GetDiagnosticsAsync(Guid userId, Guid businessId,
        CancellationToken cancellationToken = default)
    {
        var access = await DemandMemberAsync(userId, businessId, cancellationToken);
        if (!access.IsOwner)
            throw new ApiException("MEMBERSHIP_FORBIDDEN",
                "Sólo la persona propietaria consulta el diagnóstico de avisos.", 403);
        var now = clock.GetUtcNow();
        var since = now.AddHours(-24);
        var devices = await db.WebPushSubscriptions.AsNoTracking()
            .Where(x => x.BusinessId == businessId && x.Audience == PushAudience.Owner)
            .Select(x => new { x.IsActive, x.LastSuccessfulAtUtc })
            .ToListAsync(cancellationToken);
        var deliveries = await db.NotificationDeliveries.AsNoTracking()
            .Where(x => x.BusinessId == businessId &&
                        (x.Status == NotificationDeliveryStatus.Pending || x.CreatedAtUtc >= since))
            .Select(x => new { x.Status, x.LastError, x.LastAttemptAtUtc })
            .ToListAsync(cancellationToken);
        var lastFailure = deliveries
            .Where(x => x.Status is NotificationDeliveryStatus.Abandoned or NotificationDeliveryStatus.Expired)
            .OrderByDescending(x => x.LastAttemptAtUtc).FirstOrDefault();
        return new(businessId, pushOptions.Value.IsConfigured,
            devices.Count(x => x.IsActive), devices.Count(x => !x.IsActive),
            deliveries.Count(x => x.Status == NotificationDeliveryStatus.Pending),
            deliveries.Count(x => x.Status == NotificationDeliveryStatus.Sent),
            // "Fallidas" son las que siguen intentándose: son las que explican un aviso que tarda.
            deliveries.Count(x => x.Status == NotificationDeliveryStatus.Pending && x.LastAttemptAtUtc != null),
            deliveries.Count(x => x.Status == NotificationDeliveryStatus.Abandoned),
            deliveries.Count(x => x.Status == NotificationDeliveryStatus.Expired),
            devices.Max(x => x.LastSuccessfulAtUtc), lastFailure?.LastError);
    }

    public async Task<NotificationHealthDto> GetPlatformHealthAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var since = now.AddHours(-24);
        var pendingNotifications = await db.Notifications.CountAsync(x => x.FannedOutAtUtc == null, cancellationToken);
        var deliveries = await db.NotificationDeliveries.AsNoTracking()
            .Where(x => x.Status == NotificationDeliveryStatus.Pending || x.CreatedAtUtc >= since)
            .Select(x => new { x.BusinessId, x.Status, x.NextAttemptAtUtc, x.CreatedAtUtc })
            .ToListAsync(cancellationToken);
        var subscriptions = await db.WebPushSubscriptions.AsNoTracking()
            .Select(x => new { x.BusinessId, x.IsActive }).ToListAsync(cancellationToken);
        var unread = await db.Notifications.AsNoTracking()
            .Where(x => x.Audience == NotificationAudience.Business && x.ReadAtUtc == null)
            .Select(x => x.BusinessId).ToListAsync(cancellationToken);
        var businessIds = deliveries.Select(x => x.BusinessId)
            .Concat(subscriptions.Select(x => x.BusinessId)).Concat(unread).Distinct().ToArray();
        var names = await db.Businesses.AsNoTracking().Where(x => businessIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name }).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var pending = deliveries.Where(x => x.Status == NotificationDeliveryStatus.Pending).ToList();
        return new(pushOptions.Value.IsConfigured, pendingNotifications, pending.Count,
            pending.Count(x => x.NextAttemptAtUtc <= now.AddMinutes(-5)),
            deliveries.Count(x => x.Status == NotificationDeliveryStatus.Abandoned),
            deliveries.Count(x => x.Status == NotificationDeliveryStatus.Expired),
            deliveries.Count(x => x.Status == NotificationDeliveryStatus.Sent),
            subscriptions.Count(x => x.IsActive), subscriptions.Count(x => !x.IsActive),
            pending.Count == 0 ? null : pending.Min(x => x.CreatedAtUtc),
            names.Select(entry => new NotificationHealthBusinessDto(entry.Key, entry.Value,
                    DemoFixtures.IsFixture(entry.Key),
                    subscriptions.Count(s => s.BusinessId == entry.Key && s.IsActive),
                    pending.Count(d => d.BusinessId == entry.Key),
                    deliveries.Count(d => d.BusinessId == entry.Key &&
                        d.Status == NotificationDeliveryStatus.Abandoned),
                    unread.Count(b => b == entry.Key)))
                .Where(x => x.ActiveSubscriptions > 0 || x.PendingDeliveries > 0 ||
                            x.AbandonedLast24Hours > 0 || x.UnreadNotifications > 0)
                .OrderByDescending(x => x.AbandonedLast24Hours).ThenBy(x => x.BusinessName).ToList());
    }

    private sealed record MemberAccess(Guid BusinessId, string BusinessName, bool IsOwner,
        bool Appointments, bool Queues, bool Orders);

    private async Task<MemberAccess> DemandMemberAsync(Guid userId, Guid businessId,
        CancellationToken cancellationToken)
        => await db.BusinessMemberships.AsNoTracking()
               .Where(x => x.UserId == userId && x.BusinessId == businessId && x.IsActive)
               .Join(db.Businesses.AsNoTracking(), m => m.BusinessId, b => b.Id,
                   (m, b) => new MemberAccess(b.Id, b.Name, m.Role == MembershipRole.Owner,
                       m.Role == MembershipRole.Owner || m.CanManageAppointments,
                       m.Role == MembershipRole.Owner || m.CanManageQueues,
                       m.Role == MembershipRole.Owner || m.CanManageOrders))
               .SingleOrDefaultAsync(cancellationToken)
           ?? throw new ApiException("MEMBERSHIP_FORBIDDEN", "No tienes acceso a este establecimiento.", 403);

    /// <summary>
    /// La bandeja respeta los mismos permisos que la operación. Un colaborador que sólo atiende la
    /// fila no debe leer en los avisos el resumen de los pedidos que no puede abrir.
    /// </summary>
    private static NotificationKind[] AllowedKinds(MemberAccess access)
    {
        var kinds = new List<NotificationKind>(NeutralKinds);
        if (access.Appointments) kinds.AddRange(AppointmentKinds);
        if (access.Queues) kinds.AddRange(QueueKinds);
        if (access.Orders) kinds.AddRange(OrderKinds);
        return [.. kinds];
    }
}
