namespace UrabaConecta.Domain;

/// <summary>Para quién es el aviso. Decide cómo se autoriza su lectura, no cómo se entrega.</summary>
public enum NotificationAudience { Business, Customer }

/// <summary>
/// Qué ocurrió. Es del dominio, no del canal: el mismo hecho puede terminar en la bandeja, en
/// SignalR y en Web Push, y ninguno de los tres puede inventar hechos nuevos.
/// </summary>
public enum NotificationKind
{
    AppointmentRequested, AppointmentConfirmed, AppointmentRejected, AppointmentCancelled,
    AppointmentCompleted, AppointmentNoShow,
    OrderPlaced, OrderAccepted, OrderRejected, OrderPreparing, OrderReady, OrderDelivered, OrderCancelled,
    QueueTicketJoined, QueueTicketAlmost, QueueTicketCalled, QueueTicketServed, QueueTicketCancelled,
    ProductRestocked, PromotionPublished, TrackingSubscribed
}

/// <summary>
/// Estado de un intento de entrega externa. Expired y Abandoned son finales por razones distintas:
/// el primero porque el destino ya no existe, el segundo porque se agotaron los reintentos.
/// Distinguirlos es lo que permite saber si el problema es del dispositivo o del proveedor.
/// </summary>
public enum NotificationDeliveryStatus { Pending, Sent, Expired, Abandoned, Skipped }

/// <summary>
/// El hecho, guardado antes de depender de nadie. Web Push, SignalR y el navegador pueden fallar
/// los tres a la vez: la persona que atiende el negocio tiene que poder entrar y encontrar el aviso
/// igual. Por eso esta fila se escribe con la operación y no después de que algo externo responda.
/// </summary>
public sealed class Notification : IBusinessOwned
{
    private Notification() { }

    public Notification(Guid id, Guid businessId, NotificationAudience audience, NotificationKind kind,
        string title, string body, string? deepLink, string entityType, Guid? entityId,
        string dedupKey, DateTimeOffset now, PushAudience? pushAudience = null,
        bool renotify = false, bool deactivateTargetAfterDelivery = false)
    {
        if (businessId == Guid.Empty) throw new DomainException("INVALID_NOTIFICATION", "El aviso necesita un negocio.");
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 120)
            throw new DomainException("INVALID_NOTIFICATION", "El título del aviso no es válido.");
        if (string.IsNullOrWhiteSpace(body) || body.Trim().Length > 240)
            throw new DomainException("INVALID_NOTIFICATION", "El cuerpo del aviso no es válido.");
        if (string.IsNullOrWhiteSpace(dedupKey) || dedupKey.Length > 160)
            throw new DomainException("INVALID_NOTIFICATION", "La clave de deduplicación no es válida.");
        // Un enlace de la bandeja del negocio es siempre interno. Aceptar uno absoluto convertiría
        // la bandeja en un redirector hacia fuera.
        if (deepLink is { Length: > 0 } &&
            (deepLink.Length > 500 || deepLink[0] != '/' ||
             deepLink.StartsWith("//", StringComparison.Ordinal) ||
             deepLink.Contains('\r') || deepLink.Contains('\n')))
            throw new DomainException("INVALID_NOTIFICATION", "El enlace del aviso no es válido.");
        // Cuando el destino es una operación que el cliente sigue con su código, el enlace vive
        // cifrado en su suscripción y no aquí: guardarlo en claro sería guardar la credencial. Un
        // aviso de promoción o de reposición sí puede llevar una ruta pública, que no es secreta.
        if (pushAudience is Domain.PushAudience.Appointment or Domain.PushAudience.QueueTicket
                or Domain.PushAudience.PickupOrder && !string.IsNullOrEmpty(deepLink))
            throw new DomainException("INVALID_NOTIFICATION",
                "El aviso de seguimiento no guarda su enlace: lo lleva la suscripción, cifrado.");
        if (audience == NotificationAudience.Customer && entityId is null)
            throw new DomainException("INVALID_NOTIFICATION", "El aviso del cliente pertenece a una operación.");

        Id = id; BusinessId = businessId; Audience = audience; Kind = kind;
        Title = title.Trim(); Body = body.Trim(); DeepLink = string.IsNullOrWhiteSpace(deepLink) ? null : deepLink;
        EntityType = entityType; EntityId = entityId; DedupKey = dedupKey;
        PushAudience = pushAudience; Renotify = renotify;
        DeactivateTargetAfterDelivery = deactivateTargetAfterDelivery;
        CreatedAtUtc = now;
    }

    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public NotificationAudience Audience { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string Title { get; private set; } = "";
    public string Body { get; private set; } = "";
    /// <summary>Ruta interna. Nula cuando el enlace lo lleva la suscripción, cifrado.</summary>
    public string? DeepLink { get; private set; }
    public string EntityType { get; private set; } = "";
    public Guid? EntityId { get; private set; }
    /// <summary>
    /// Identidad del hecho. Es única en la base: dos clics sobre "Aceptar" producen un solo aviso,
    /// aunque las dos peticiones lleguen a la vez.
    /// </summary>
    public string DedupKey { get; private set; } = "";
    /// <summary>A qué suscripciones alcanza este aviso. Nulo significa que no sale a Web Push.</summary>
    public PushAudience? PushAudience { get; private set; }
    public bool Renotify { get; private set; }
    /// <summary>
    /// El aviso de "volvió el producto" consume la suscripción: quien lo pidió ya recibió lo que
    /// esperaba y no debe seguir recibiendo avisos de ese artículo.
    /// </summary>
    public bool DeactivateTargetAfterDelivery { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    /// <summary>Cuándo el trabajador materializó las entregas. Nulo = todavía no salió del buzón.</summary>
    public DateTimeOffset? FannedOutAtUtc { get; private set; }
    public DateTimeOffset? ReadAtUtc { get; private set; }
    public Guid? ReadByUserId { get; private set; }
    public long Version { get; private set; }

    public bool IsRead => ReadAtUtc is not null;

    /// <summary>Idempotente: marcar dos veces no reescribe quién lo leyó primero.</summary>
    public void MarkRead(Guid userId, DateTimeOffset now)
    {
        if (ReadAtUtc is not null) return;
        ReadAtUtc = now; ReadByUserId = userId; Version++;
    }

    public void MarkUnread()
    {
        if (ReadAtUtc is null) return;
        ReadAtUtc = null; ReadByUserId = null; Version++;
    }

    public void MarkFannedOut(DateTimeOffset now)
    {
        FannedOutAtUtc = now; Version++;
    }

    /// <summary>
    /// Clave estable del hecho. El estado entra en la clave a propósito: "pedido aceptado" y
    /// "pedido listo" son hechos distintos del mismo pedido y los dos deben quedar guardados.
    /// </summary>
    public static string Key(NotificationAudience audience, NotificationKind kind, Guid entityId,
        string? discriminator = null)
        => discriminator is { Length: > 0 }
            ? $"{audience}:{kind}:{entityId:N}:{discriminator}"
            : $"{audience}:{kind}:{entityId:N}";
}

/// <summary>
/// Un intento durable de sacar el aviso hacia un dispositivo concreto. Sobrevive al reinicio del
/// proceso: mientras la fila siga en Pending, el trabajo sigue pendiente, lo mire quien lo mire.
/// </summary>
public sealed class NotificationDelivery
{
    /// <summary>
    /// Espera antes de cada reintento. Seis intentos cubren algo más de dos horas y media, que es
    /// bastante más que cualquier caída pasajera de un servicio Push, sin convertir un destino roto
    /// en trabajo perpetuo.
    /// </summary>
    public static readonly TimeSpan[] Backoff =
    [
        TimeSpan.Zero, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2)
    ];

    public static int MaximumAttempts => Backoff.Length;

    private NotificationDelivery() { }

    public NotificationDelivery(Guid id, Guid notificationId, Guid businessId, Guid subscriptionId,
        DateTimeOffset now)
    {
        Id = id; NotificationId = notificationId; BusinessId = businessId; SubscriptionId = subscriptionId;
        CreatedAtUtc = now; NextAttemptAtUtc = now;
    }

    public Guid Id { get; private set; }
    public Guid NotificationId { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public NotificationDeliveryStatus Status { get; private set; } = NotificationDeliveryStatus.Pending;
    public int AttemptCount { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset NextAttemptAtUtc { get; private set; }
    public DateTimeOffset? LastAttemptAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public int? LastStatusCode { get; private set; }
    public string? LastError { get; private set; }
    /// <summary>Quién tiene tomada la fila ahora mismo. Evita que dos trabajadores la envíen dos veces.</summary>
    public Guid? LeaseOwner { get; private set; }
    public DateTimeOffset? LeasedUntilUtc { get; private set; }
    public long Version { get; private set; }

    public bool IsFinal => Status is not NotificationDeliveryStatus.Pending;

    public void Lease(Guid owner, DateTimeOffset until)
    {
        LeaseOwner = owner; LeasedUntilUtc = until;
    }

    public void MarkSent(DateTimeOffset now)
    {
        Status = NotificationDeliveryStatus.Sent; AttemptCount++; LastAttemptAtUtc = now;
        CompletedAtUtc = now; LastStatusCode = 201; LastError = null; Release(); Version++;
    }

    /// <summary>El destino ya no existe (404/410). No se reintenta y la suscripción se desactiva.</summary>
    public void MarkExpired(DateTimeOffset now, int? statusCode, string? error)
    {
        Status = NotificationDeliveryStatus.Expired; AttemptCount++; LastAttemptAtUtc = now;
        CompletedAtUtc = now; LastStatusCode = statusCode; LastError = Trim(error); Release(); Version++;
    }

    /// <summary>
    /// Fallo pasajero. Reprograma con espera creciente y sólo se rinde al agotar los intentos.
    /// Nunca desactiva la suscripción: un 500 del proveedor no dice nada del dispositivo.
    /// </summary>
    public void MarkTransientFailure(DateTimeOffset now, int? statusCode, string? error)
    {
        AttemptCount++; LastAttemptAtUtc = now; LastStatusCode = statusCode; LastError = Trim(error);
        if (AttemptCount >= MaximumAttempts)
        {
            Status = NotificationDeliveryStatus.Abandoned; CompletedAtUtc = now;
        }
        else NextAttemptAtUtc = now + Backoff[AttemptCount];
        Release(); Version++;
    }

    /// <summary>La suscripción dejó de ser elegible antes de intentarlo. No es un fallo de entrega.</summary>
    public void MarkSkipped(DateTimeOffset now, string reason)
    {
        Status = NotificationDeliveryStatus.Skipped; LastAttemptAtUtc = now; CompletedAtUtc = now;
        LastError = Trim(reason); Release(); Version++;
    }

    private void Release() { LeaseOwner = null; LeasedUntilUtc = null; }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null
            : value.Trim().Length <= 300 ? value.Trim() : value.Trim()[..300];
}
