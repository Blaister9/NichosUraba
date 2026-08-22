using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Application;

/// <summary>
/// Lo que un caso de uso quiere contar. No dice por qué canal sale: eso lo decide el buzón según
/// la audiencia y las suscripciones que existan en ese momento.
/// </summary>
public sealed record NotificationRequest(Guid BusinessId, NotificationAudience Audience,
    NotificationKind Kind, string Title, string Body, string? DeepLink, string EntityType,
    Guid? EntityId, string DedupKey, PushAudience? PushAudience = null, bool Renotify = false,
    bool DeactivateTargetAfterDelivery = false);

/// <summary>
/// La única puerta por la que un hecho de negocio se convierte en aviso.
///
/// El contrato importante es lo que NO hace: no habla con el servicio Web Push, no espera a nadie
/// y no puede tumbar la operación que lo llamó. Guarda el hecho, avisa en vivo a quien esté
/// mirando, y deja la entrega externa en manos del trabajador de fondo. Si el proveedor Push está
/// caído, el pedido se crea igual y el aviso sigue estando en la bandeja.
/// </summary>
public interface INotificationPublisher
{
    Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default);
    /// <summary>
    /// Nombre distinto y no una sobrecarga: con `new(...)` en la llamada, el compilador no puede
    /// decidir entre un aviso y una lista de avisos, y el error sale en el sitio de la llamada.
    /// </summary>
    Task PublishManyAsync(IReadOnlyList<NotificationRequest> requests, CancellationToken cancellationToken = default);
}

/// <summary>Bandeja y diagnóstico. La autorización la impone el caso de uso, no la ruta.</summary>
public interface INotificationUseCases
{
    Task<NotificationPageDto> GetBusinessInboxAsync(Guid userId, Guid businessId, bool unreadOnly,
        int take, CancellationToken cancellationToken = default);
    /// <summary>No leídos por negocio de esta persona. El alcance sale de sus membresías.</summary>
    Task<IReadOnlyList<NotificationCountDto>> GetUnreadCountsAsync(Guid userId,
        CancellationToken cancellationToken = default);
    Task<NotificationPageDto> MarkReadAsync(Guid userId, Guid businessId,
        MarkNotificationsReadRequest request, CancellationToken cancellationToken = default);
    /// <summary>
    /// Los avisos de una operación de cliente. La credencial es el mismo código de seguimiento con
    /// el que ya consulta su estado: no se crea cuenta ni se abre una vía nueva de enumeración.
    /// </summary>
    Task<IReadOnlyList<NotificationDto>> GetCustomerInboxAsync(PushAudience audience, string publicCode,
        CancellationToken cancellationToken = default);
    Task<NotificationDiagnosticsDto> GetDiagnosticsAsync(Guid userId, Guid businessId,
        CancellationToken cancellationToken = default);
    Task<NotificationHealthDto> GetPlatformHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Actualización en vivo del panel. Es un acelerador: quien pierda el mensaje recupera el estado
/// correcto volviendo a preguntar a la API, que sigue siendo la fuente de verdad.
/// </summary>
public interface IRealtimeNotifier
{
    /// <summary>Algo cambió en un negocio. <paramref name="channel"/> sale de <see cref="RealtimeChannels"/>.</summary>
    Task BusinessChangedAsync(Guid businessId, string channel, CancellationToken cancellationToken = default);
    /// <summary>Cambió una operación que un cliente sigue con su código.</summary>
    Task TrackingChangedAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Quién puede escuchar qué. Un grupo en vivo es una autorización tanto como un endpoint: si
/// cualquiera pudiera unirse al grupo de un negocio, sabría cuándo entra un pedido allí.
/// </summary>
public interface IRealtimeAccessGuard
{
    Task<bool> CanSubscribeBusinessAsync(Guid userId, Guid businessId, string channel,
        CancellationToken cancellationToken);
    /// <summary>
    /// Traduce el código de seguimiento del cliente a la operación que sigue. Nulo cuando no
    /// corresponde a nada: la respuesta es idéntica para un código mal formado y para uno que no
    /// existe, de modo que suscribirse no sirve para descubrir códigos válidos.
    /// </summary>
    Task<Guid?> ResolveTrackedEntityAsync(string entityType, string publicCode,
        CancellationToken cancellationToken);
}

public static class RealtimeChannels
{
    public const string Appointments = "appointments";
    public const string Orders = "orders";
    public const string Notifications = "notifications";
}

/// <summary>Tipos de operación que un cliente sigue. Se usan como parte del grupo en vivo.</summary>
public static class TrackedEntities
{
    public const string Appointment = "Appointment";
    public const string PickupOrder = "PickupOrder";
    public const string QueueTicket = "QueueTicket";
    public const string Product = "Product";
    public const string Business = "Business";
}

public sealed record NotificationDispatchReport(int FannedOut, int Attempted, int Sent, int Retried,
    int Expired, int Abandoned, int Skipped)
{
    public static NotificationDispatchReport Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);
    public int Total => FannedOut + Attempted;
}

/// <summary>
/// Una pasada del buzón: materializa entregas pendientes y las intenta. Se expone como método
/// porque las pruebas necesitan ejecutarla de forma determinista, sin competir con el servicio de
/// fondo que en producción la llama en bucle.
/// </summary>
public interface INotificationDispatcher
{
    Task<NotificationDispatchReport> RunOnceAsync(CancellationToken cancellationToken = default);
    /// <summary>Retira lo terminado y ya leído. Un aviso sin leer nunca se poda.</summary>
    Task<int> PruneAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Le da un golpecito al trabajador para que no espere al siguiente sondeo. Perder un golpecito no
/// pierde el aviso: el sondeo periódico encuentra igual lo que quedó pendiente en la base.
/// </summary>
public interface INotificationSignal
{
    void Pulse();
    Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
