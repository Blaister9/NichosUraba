namespace UrabaConecta.Domain;

public enum PushAudience { Owner, Appointment, QueueTicket, PickupOrder, ProductRestock, BusinessFollower }

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

/// <summary>
/// Publicación comercial breve y con vigencia. Guardarla no concede permiso para enviar Push:
/// ese envío sólo puede alcanzar dispositivos que siguieron explícitamente al negocio.
/// </summary>
public sealed class BusinessPromotion : IBusinessOwned
{
    private BusinessPromotion() { }

    public BusinessPromotion(Guid id, Guid businessId, string headline, string? body, string ctaLabel,
        string deepLink, DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc, bool isActive,
        DateTimeOffset now)
    {
        Validate(businessId, headline, body, ctaLabel, deepLink, startsAtUtc, endsAtUtc);
        Id = id; BusinessId = businessId; Headline = headline.Trim(); Body = body?.Trim() ?? "";
        CtaLabel = ctaLabel.Trim(); DeepLink = deepLink.Trim(); StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc; IsActive = isActive; CreatedAtUtc = UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public string Headline { get; private set; } = "";
    public string Body { get; private set; } = "";
    public string CtaLabel { get; private set; } = "";
    public string DeepLink { get; private set; } = "";
    public DateTimeOffset StartsAtUtc { get; private set; }
    public DateTimeOffset EndsAtUtc { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset? PushSentAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public long Version { get; private set; }

    public bool IsCurrent(DateTimeOffset now) => IsActive && StartsAtUtc <= now && EndsAtUtc > now;

    public void Update(string headline, string? body, string ctaLabel, string deepLink,
        DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc, bool isActive, long expectedVersion,
        DateTimeOffset now)
    {
        if (Version != expectedVersion)
            throw new DomainException("CONCURRENCY_CONFLICT", "La promoción cambió. Recarga la información.");
        Validate(BusinessId, headline, body, ctaLabel, deepLink, startsAtUtc, endsAtUtc);
        Headline = headline.Trim(); Body = body?.Trim() ?? ""; CtaLabel = ctaLabel.Trim();
        DeepLink = deepLink.Trim(); StartsAtUtc = startsAtUtc; EndsAtUtc = endsAtUtc;
        IsActive = isActive; UpdatedAtUtc = now; Version++;
    }

    public void MarkPushSent(DateTimeOffset now)
    {
        PushSentAtUtc = now; UpdatedAtUtc = now; Version++;
    }

    private static void Validate(Guid businessId, string headline, string? body, string ctaLabel,
        string deepLink, DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc)
    {
        if (businessId == Guid.Empty || string.IsNullOrWhiteSpace(headline) || headline.Trim().Length > 90 ||
            (body?.Trim().Length ?? 0) > 220 || string.IsNullOrWhiteSpace(ctaLabel) ||
            ctaLabel.Trim().Length > 32 || string.IsNullOrWhiteSpace(deepLink) || deepLink.Length > 500 ||
            !deepLink.StartsWith('/') || deepLink.StartsWith("//", StringComparison.Ordinal) ||
            deepLink.Contains('\r') || deepLink.Contains('\n') || endsAtUtc <= startsAtUtc ||
            endsAtUtc - startsAtUtc > TimeSpan.FromDays(31))
            throw new DomainException("INVALID_PROMOTION", "Los datos de la promoción no son válidos.");
    }
}
