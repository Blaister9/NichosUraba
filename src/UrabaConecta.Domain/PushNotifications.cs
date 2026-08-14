namespace UrabaConecta.Domain;

public enum PushAudience { Owner, Appointment, QueueTicket, PickupOrder }

/// <summary>
/// Un destino Web Push. El endpoint nunca se usa como identidad de negocio: el alcance autorizado
/// queda congelado en Audience + ScopeKey y se vuelve a comprobar antes de cada envío Owner.
/// </summary>
public sealed class WebPushSubscription : IBusinessOwned
{
    private WebPushSubscription() { }

    public WebPushSubscription(Guid id, Guid businessId, PushAudience audience, string scopeKey,
        string endpointHash, string endpoint, string p256dh, string auth, Guid? userId,
        Guid? entityId, string? protectedDeepLink, DateTimeOffset now)
    {
        if (businessId == Guid.Empty || string.IsNullOrWhiteSpace(scopeKey))
            throw new DomainException("INVALID_PUSH_SCOPE", "El alcance de la suscripción no es válido.");
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Length > 2048 ||
            string.IsNullOrWhiteSpace(p256dh) || p256dh.Length > 256 ||
            string.IsNullOrWhiteSpace(auth) || auth.Length > 256)
            throw new DomainException("INVALID_PUSH_SUBSCRIPTION", "La suscripción del navegador no es válida.");
        if (audience == PushAudience.Owner && userId is null)
            throw new DomainException("INVALID_PUSH_SCOPE", "La suscripción Owner requiere una cuenta.");
        if (audience != PushAudience.Owner && entityId is null)
            throw new DomainException("INVALID_PUSH_SCOPE", "La suscripción de cliente requiere una operación.");

        Id = id; BusinessId = businessId; Audience = audience; ScopeKey = scopeKey;
        EndpointHash = endpointHash; Endpoint = endpoint; P256dh = p256dh; Auth = auth;
        UserId = userId; EntityId = entityId; ProtectedDeepLink = protectedDeepLink;
        CreatedAtUtc = UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public PushAudience Audience { get; private set; }
    public string ScopeKey { get; private set; } = "";
    public string EndpointHash { get; private set; } = "";
    public string Endpoint { get; private set; } = "";
    public string P256dh { get; private set; } = "";
    public string Auth { get; private set; } = "";
    public Guid? UserId { get; private set; }
    public Guid? EntityId { get; private set; }
    public string? ProtectedDeepLink { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int FailureCount { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? LastSuccessfulAtUtc { get; private set; }
    public long Version { get; private set; }

    public void Refresh(string endpoint, string p256dh, string auth, Guid? userId,
        string? protectedDeepLink, DateTimeOffset now)
    {
        Endpoint = endpoint; P256dh = p256dh; Auth = auth; UserId = userId;
        ProtectedDeepLink = protectedDeepLink; IsActive = true; FailureCount = 0; Touch(now);
    }

    public void MarkDelivered(DateTimeOffset now)
    {
        LastSuccessfulAtUtc = now; FailureCount = 0; Touch(now);
    }

    public void MarkFailed(DateTimeOffset now, bool expired)
    {
        FailureCount++;
        if (expired || FailureCount >= 3) IsActive = false;
        Touch(now);
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false; Touch(now);
    }

    private void Touch(DateTimeOffset now) { UpdatedAtUtc = now; Version++; }
}
