namespace UrabaConecta.Contracts;

/// <summary>
/// Un aviso de la bandeja. Lo que llega aquí ya está guardado: se ve aunque Web Push nunca haya
/// entregado nada y aunque el circuito en vivo esté caído.
/// </summary>
public sealed record NotificationDto(Guid Id, string Kind, string Title, string Body, string? DeepLink,
    string EntityType, Guid? EntityId, DateTimeOffset CreatedAtUtc, DateTimeOffset? ReadAtUtc)
{
    public bool IsRead => ReadAtUtc is not null;
}

/// <param name="UnreadCount">
/// No leídos del negocio completo, no de la página que se está mirando: es el número que pinta el
/// contador de la barra y tiene que seguir siendo cierto al pasar de página.
/// </param>
public sealed record NotificationPageDto(Guid BusinessId, string BusinessName, string TimeZoneId,
    IReadOnlyList<NotificationDto> Items, int UnreadCount);

/// <summary>Cuántos avisos sin leer tiene cada negocio de la persona. Alimenta el punto de la barra.</summary>
public sealed record NotificationCountDto(Guid BusinessId, string BusinessName, int UnreadCount);

/// <summary>
/// Diagnóstico de entregas que puede ver el propietario de su propio negocio. No lleva endpoints
/// ni claves: sólo cuántos dispositivos hay y cómo terminó lo que se intentó.
/// </summary>
/// <param name="PushConfigured">
/// Si el ambiente tiene VAPID. Sin esto, un cero en "entregados" significa "no hay avisos
/// configurados" y no "los avisos están fallando", que son diagnósticos muy distintos.
/// </param>
public sealed record NotificationDiagnosticsDto(Guid BusinessId, bool PushConfigured,
    int ActiveOwnerDevices, int InactiveOwnerDevices, int PendingDeliveries, int SentLast24Hours,
    int FailedLast24Hours, int AbandonedLast24Hours, int ExpiredLast24Hours,
    DateTimeOffset? LastSuccessfulDeliveryUtc, string? LastFailureReason);

/// <summary>Salud del buzón en toda la plataforma. Sólo la administración técnica la consulta.</summary>
public sealed record NotificationHealthDto(bool PushConfigured, int PendingNotifications,
    int PendingDeliveries, int OverdueDeliveries, int AbandonedLast24Hours, int ExpiredLast24Hours,
    int SentLast24Hours, int ActiveSubscriptions, int InactiveSubscriptions,
    DateTimeOffset? OldestPendingDeliveryUtc, IReadOnlyList<NotificationHealthBusinessDto> Businesses);

public sealed record NotificationHealthBusinessDto(Guid BusinessId, string BusinessName, bool IsDemo,
    int ActiveSubscriptions, int PendingDeliveries, int AbandonedLast24Hours, int UnreadNotifications);

public sealed class MarkNotificationsReadRequest
{
    /// <summary>Vacío significa "todos los del negocio". Sirve al botón de marcar todo.</summary>
    public IReadOnlyList<Guid> Ids { get; set; } = [];
}

/// <summary>
/// Lo que un negocio puede hacer. El nombre de cada capacidad es el del dominio; la pantalla no
/// vuelve a decidirlo mirando la categoría.
/// </summary>
public sealed record BusinessCapabilitiesDto(bool Appointments, bool VirtualQueues, bool PickupOrders,
    bool Services, bool Products, bool Staff)
{
    public static BusinessCapabilitiesDto None { get; } = new(false, false, false, false, false, false);
    public bool HasAnyOperation => Appointments || VirtualQueues || PickupOrders;
}
