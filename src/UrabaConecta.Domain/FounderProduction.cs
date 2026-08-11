namespace UrabaConecta.Domain;

public enum BusinessImageKind { Logo, Cover, Gallery }

/// <summary>Tipo de acceso que concede una invitación aceptada.</summary>
public enum AccessGrantKind { PartnerOperator, BusinessOwner, BusinessStaff }

/// <summary>Motivo por el que se emitió el enlace de un solo uso.</summary>
public enum AccessInvitationPurpose { Invitation, PasswordReset }

public enum PlatformAccessAction
{
    InvitationCreated, InvitationAccepted, InvitationRevoked, InvitationResent, InvitationExpired,
    PartnerOperatorCreated, PartnerOperatorRevoked, AdministrativeAccessReset,
    PasswordChanged, SessionsRevoked, DemoAdministratorBootstrap, ProductionAdministratorBootstrap
}

/// <summary>
/// Imagen pública de un negocio. El binario vive en <see cref="IObjectStorageMarker"/> (object storage);
/// aquí sólo se guarda la referencia y los metadatos necesarios para renderizarla sin saltos visuales.
/// </summary>
public sealed class BusinessImage : IBusinessOwned
{
    public const int MaximumGalleryImages = 8;

    private BusinessImage() { }

    public BusinessImage(Guid id, Guid businessId, BusinessImageKind kind, string storageKey,
        string contentType, int width, int height, long byteSize, string? altText, int displayOrder,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Length > 400)
            throw new DomainException("INVALID_IMAGE", "La referencia de almacenamiento no es válida.");
        if (width is < 1 or > 10000 || height is < 1 or > 10000 || byteSize <= 0)
            throw new DomainException("INVALID_IMAGE", "Las dimensiones de la imagen no son válidas.");
        if (displayOrder < 0) throw new DomainException("INVALID_IMAGE", "El orden no puede ser negativo.");
        (Id, BusinessId, Kind, StorageKey, ContentType) = (id, businessId, kind, storageKey.Trim(), contentType);
        (Width, Height, ByteSize, DisplayOrder) = (width, height, byteSize, displayOrder);
        AltText = NormalizeAltText(altText);
        CreatedAtUtc = UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public BusinessImageKind Kind { get; private set; }
    public string StorageKey { get; private set; } = "";
    public string ContentType { get; private set; } = "";
    public int Width { get; private set; }
    public int Height { get; private set; }
    public long ByteSize { get; private set; }
    public string? AltText { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? DeletedAtUtc { get; private set; }
    public long Version { get; private set; }

    public void Describe(string? altText, int displayOrder, DateTimeOffset now, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        EnsureNotDeleted();
        if (displayOrder < 0) throw new DomainException("INVALID_IMAGE", "El orden no puede ser negativo.");
        AltText = NormalizeAltText(altText); DisplayOrder = displayOrder; Touch(now);
    }

    /// <summary>Eliminación lógica. El borrado físico del objeto es un paso administrativo posterior.</summary>
    public void SoftDelete(DateTimeOffset now, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        EnsureNotDeleted();
        IsDeleted = true; DeletedAtUtc = now; Touch(now);
    }

    private void EnsureNotDeleted()
    {
        if (IsDeleted) throw new DomainException("IMAGE_DELETED", "La imagen ya fue eliminada.");
    }
    private static string? NormalizeAltText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > 160)
            throw new DomainException("INVALID_ALT_TEXT", "El texto alternativo admite máximo 160 caracteres.");
        if (trimmed.Contains('<') || trimmed.Contains('>'))
            throw new DomainException("INVALID_ALT_TEXT", "No se admite HTML en el texto alternativo.");
        return trimmed;
    }
    private void Touch(DateTimeOffset now) { UpdatedAtUtc = now; Version++; }
    private void EnsureVersion(long expected)
    {
        if (Version != expected)
            throw new DomainException("CONCURRENCY_CONFLICT", "La imagen cambió. Recargue la información.");
    }
}

/// <summary>Marcador documental: el binario no se guarda en PostgreSQL sino en almacenamiento de objetos.</summary>
public interface IObjectStorageMarker;

/// <summary>
/// Enlace de un solo uso para que una persona defina su propia contraseña.
/// Nunca se persiste el token en claro: sólo su hash.
/// </summary>
public sealed class AccessInvitation
{
    public const int MaximumFailedAttempts = 5;

    private AccessInvitation() { }

    public AccessInvitation(Guid id, string email, string displayName, AccessGrantKind grant, Guid? businessId,
        string tokenHash, Guid createdByUserId, DateTimeOffset now, TimeSpan lifetime,
        AccessInvitationPurpose purpose = AccessInvitationPurpose.Invitation)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > 160)
            throw new DomainException("INVALID_INVITATION_EMAIL", "Ingrese un correo válido.");
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 100)
            throw new DomainException("INVALID_INVITATION_NAME", "Ingrese el nombre visible de la persona.");
        if (string.IsNullOrWhiteSpace(tokenHash) || tokenHash.Length > 128)
            throw new DomainException("INVALID_INVITATION_TOKEN", "El token de invitación no es válido.");
        // Un reinicio de contraseña no concede accesos nuevos, por eso no exige negocio.
        if (purpose == AccessInvitationPurpose.Invitation &&
            grant != AccessGrantKind.PartnerOperator && businessId is null)
            throw new DomainException("BUSINESS_REQUIRED", "Indique el negocio al que pertenece el acceso.");
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromDays(30))
            throw new DomainException("INVALID_INVITATION_LIFETIME", "La vigencia debe estar entre 1 minuto y 30 días.");
        (Id, Email, DisplayName, Grant, BusinessId, Purpose) =
            (id, email.Trim().ToLowerInvariant(), displayName.Trim(), grant, businessId, purpose);
        TokenHash = tokenHash; CreatedByUserId = createdByUserId;
        CreatedAtUtc = now; ExpiresAtUtc = now + lifetime;
    }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public AccessGrantKind Grant { get; private set; }
    public AccessInvitationPurpose Purpose { get; private set; }
    public Guid? BusinessId { get; private set; }
    public string TokenHash { get; private set; } = "";
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? AcceptedAtUtc { get; private set; }
    public Guid? AcceptedUserId { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public Guid? RevokedByUserId { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTimeOffset? LockedUntilUtc { get; private set; }
    public long Version { get; private set; }

    public bool IsPending(DateTimeOffset now)
        => AcceptedAtUtc is null && RevokedAtUtc is null && ExpiresAtUtc > now;

    public string StatusFor(DateTimeOffset now)
        => AcceptedAtUtc is not null ? "Accepted"
            : RevokedAtUtc is not null ? "Revoked"
            : ExpiresAtUtc <= now ? "Expired"
            : "Pending";

    /// <summary>Consume la invitación. Falla si ya se usó, se revocó, expiró o está bloqueada.</summary>
    public void Accept(Guid userId, DateTimeOffset now)
    {
        EnsureUsable(now);
        AcceptedAtUtc = now; AcceptedUserId = userId; Version++;
    }

    public void Revoke(Guid actorUserId, DateTimeOffset now)
    {
        if (AcceptedAtUtc is not null)
            throw new DomainException("INVITATION_ALREADY_ACCEPTED", "La invitación ya fue aceptada.");
        if (RevokedAtUtc is not null)
            throw new DomainException("INVITATION_ALREADY_REVOKED", "La invitación ya estaba revocada.");
        RevokedAtUtc = now; RevokedByUserId = actorUserId; Version++;
    }

    /// <summary>Registra un intento fallido y bloquea temporalmente al llegar al límite.</summary>
    public void RegisterFailedAttempt(DateTimeOffset now)
    {
        FailedAttempts++;
        if (FailedAttempts >= MaximumFailedAttempts) LockedUntilUtc = now.AddMinutes(15);
        Version++;
    }

    private void EnsureUsable(DateTimeOffset now)
    {
        if (AcceptedAtUtc is not null)
            throw new DomainException("INVITATION_ALREADY_USED", "Este enlace ya fue utilizado.");
        if (RevokedAtUtc is not null)
            throw new DomainException("INVITATION_REVOKED", "Este enlace fue revocado.");
        if (ExpiresAtUtc <= now)
            throw new DomainException("INVITATION_EXPIRED", "Este enlace expiró. Solicite uno nuevo.");
        if (LockedUntilUtc is { } locked && locked > now)
            throw new DomainException("INVITATION_LOCKED", "Demasiados intentos. Espere unos minutos.");
    }
}

/// <summary>Historial explícito de cambios de estado de un negocio, visible para administración.</summary>
public sealed class BusinessStatusChange : IBusinessOwned
{
    private BusinessStatusChange() { }

    public BusinessStatusChange(Guid id, Guid businessId, BusinessStatus fromStatus, BusinessStatus toStatus,
        Guid actorUserId, string? notes, DateTimeOffset occurredAtUtc)
    {
        if (notes?.Length > 400)
            throw new DomainException("INVALID_STATE_NOTES", "Las observaciones admiten máximo 400 caracteres.");
        (Id, BusinessId, FromStatus, ToStatus, ActorUserId, OccurredAtUtc) =
            (id, businessId, fromStatus, toStatus, actorUserId, occurredAtUtc);
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public BusinessStatus FromStatus { get; private set; }
    public BusinessStatus ToStatus { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
}

/// <summary>
/// Auditoría de accesos y cuentas. Se separa de <see cref="PlatformAuditEntry"/> porque una invitación
/// de socia no pertenece a ningún negocio. Nunca almacena tokens, contraseñas ni secretos.
/// </summary>
public sealed class PlatformAccessAudit
{
    private PlatformAccessAudit() { }

    public PlatformAccessAudit(Guid id, Guid? actorUserId, PlatformAccessAction action, string entity,
        string entityId, Guid? businessId, string summary, string? ipAddress, DateTimeOffset occurredAtUtc,
        string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(entity) || entity.Length > 80)
            throw new DomainException("INVALID_AUDIT_ENTITY", "La entidad auditada no es válida.");
        if (summary.Length > 400)
            throw new DomainException("INVALID_AUDIT_SUMMARY", "El resumen admite máximo 400 caracteres.");
        (Id, ActorUserId, Action, Entity, EntityId) = (id, actorUserId, action, entity, entityId);
        (BusinessId, Summary, OccurredAtUtc, CorrelationId) = (businessId, summary, occurredAtUtc, correlationId);
        IpAddress = Truncate(ipAddress, 45);
    }

    public Guid Id { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public PlatformAccessAction Action { get; private set; }
    public string Entity { get; private set; } = "";
    public string EntityId { get; private set; } = "";
    public Guid? BusinessId { get; private set; }
    public string Summary { get; private set; } = "";
    public string? IpAddress { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

    private static string? Truncate(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null
            : value.Length <= max ? value : value[..max];
}
