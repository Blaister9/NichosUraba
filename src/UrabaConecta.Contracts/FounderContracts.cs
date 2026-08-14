using System.ComponentModel.DataAnnotations;

namespace UrabaConecta.Contracts;

// ---------------------------------------------------------------------------
// Imágenes
// ---------------------------------------------------------------------------

public sealed record BusinessImageDto(Guid Id, string Kind, string Url, string? AltText,
    int Width, int Height, int DisplayOrder, long Version,
    Guid? ServiceId = null, Guid? ProductId = null);

public sealed class UpdateBusinessImageRequest
{
    [StringLength(160)] public string? AltText { get; set; }
    [Range(0, 100)] public int DisplayOrder { get; set; }
    public long Version { get; set; }
}

// ---------------------------------------------------------------------------
// Invitaciones y accesos
// ---------------------------------------------------------------------------

public sealed class CreateInvitationRequest
{
    [Required, EmailAddress, StringLength(160)] public string Email { get; set; } = "";
    [Required, StringLength(100, MinimumLength = 2)] public string DisplayName { get; set; } = "";
    /// <summary>PartnerOperator, BusinessOwner o BusinessStaff.</summary>
    [Required] public string Grant { get; set; } = "";
    public Guid? BusinessId { get; set; }
    [Range(1, 720)] public int LifetimeHours { get; set; } = 72;
}

public sealed class ResetAccessRequest
{
    [Required, EmailAddress, StringLength(160)] public string Email { get; set; } = "";
    [Range(1, 72)] public int LifetimeHours { get; set; } = 4;
}

/// <summary>
/// Resultado de emitir una invitación. <see cref="AcceptPath"/> es la ruta relativa que el
/// administrador copia y entrega por el canal que prefiera. El token no se persiste en claro.
/// </summary>
public sealed record InvitationIssuedDto(Guid Id, string Email, string Grant, string AcceptPath,
    DateTimeOffset ExpiresAtUtc);

public sealed record InvitationDto(Guid Id, string Email, string DisplayName, string Grant, string Purpose,
    Guid? BusinessId, string? BusinessName, string Status, DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc, DateTimeOffset? AcceptedAtUtc);

public sealed record InvitationPreviewDto(string Email, string DisplayName, string Grant, string Purpose,
    string? BusinessName, DateTimeOffset ExpiresAtUtc);

public sealed record PlatformAccountDto(Guid UserId, string Email, string DisplayName, bool IsActive,
    DateTimeOffset? LockoutEndUtc);

public sealed record PlatformAccessAuditDto(Guid Id, string? ActorEmail, string Action, string Entity,
    string EntityId, Guid? BusinessId, string Summary, string? IpAddress, DateTimeOffset OccurredAtUtc);

// ---------------------------------------------------------------------------
// Onboarding y revisión
// ---------------------------------------------------------------------------

public sealed record BusinessStatusChangeDto(string FromStatus, string ToStatus, string? ActorEmail,
    string? Notes, DateTimeOffset OccurredAtUtc);

public sealed record PlatformAuditEntryDto(Guid Id, string? ActorEmail, string Action, string PreviousState,
    string NewState, DateTimeOffset OccurredAtUtc);

public sealed class SubmitForReviewRequest
{
    public long Version { get; set; }
}

public sealed class RejectReviewRequest
{
    public long Version { get; set; }
    [Required, StringLength(400, MinimumLength = 5)] public string Notes { get; set; } = "";
}

/// <summary>Perfil comercial completo tal como lo administra una socia.</summary>
/// <summary>
/// Lo que un propietario puede cambiar de su propio perfil comercial. Deliberadamente no incluye
/// nombre, dirección web, municipio ni categoría: son la identidad del negocio en el directorio
/// público —el enlace ya repartido, la ficha ya indexada— y cambiarlos sigue siendo decisión de la
/// administración de plataforma. El resto del perfil es suyo y lo edita sin pedir permiso a nadie.
/// </summary>
public sealed class SaveOwnerProfileRequest
{
    [Required(ErrorMessage = "La descripción breve es obligatoria."),
     StringLength(160, MinimumLength = 10,
         ErrorMessage = "La descripción breve admite de 10 a 160 caracteres.")]
    public string ShortDescription { get; set; } = "";
    [StringLength(600)] public string Description { get; set; } = "";
    [StringLength(240)] public string? Address { get; set; }
    [StringLength(160)] public string? ReferencePoint { get; set; }
    [StringLength(30)] public string? PublicPhone { get; set; }
    [Url, StringLength(500)] public string? WhatsAppUrl { get; set; }
    [EmailAddress, StringLength(160)] public string? PublicEmail { get; set; }
    [Url, StringLength(500)] public string? InstagramUrl { get; set; }
    [Url, StringLength(500)] public string? FacebookUrl { get; set; }
    [Url, StringLength(500)] public string? LocationUrl { get; set; }
    [StringLength(600)] public string? CustomerInstructions { get; set; }
    public long Version { get; set; }
}

public sealed class SaveBusinessProfileRequest
{
    [Required, StringLength(160, MinimumLength = 2)] public string Name { get; set; } = "";
    [Required, StringLength(120, MinimumLength = 3)] public string Slug { get; set; } = "";
    [Required] public Guid MunicipalityId { get; set; }
    [Required] public Guid CategoryId { get; set; }
    [Required, StringLength(160, MinimumLength = 10)] public string ShortDescription { get; set; } = "";
    [StringLength(600)] public string Description { get; set; } = "";
    [StringLength(240)] public string? Address { get; set; }
    [StringLength(160)] public string? ReferencePoint { get; set; }
    [StringLength(30)] public string? PublicPhone { get; set; }
    [Url, StringLength(500)] public string? WhatsAppUrl { get; set; }
    [EmailAddress, StringLength(160)] public string? PublicEmail { get; set; }
    [Url, StringLength(500)] public string? InstagramUrl { get; set; }
    [Url, StringLength(500)] public string? FacebookUrl { get; set; }
    [Url, StringLength(500)] public string? LocationUrl { get; set; }
    [StringLength(600)] public string? CustomerInstructions { get; set; }
    public long Version { get; set; }
}

// ---------------------------------------------------------------------------
// Textos legales y consentimiento
// ---------------------------------------------------------------------------

public sealed record LegalInfoDto(string ResponsibleName, string Identification, string Address,
    string PrivacyEmail, string SupportEmail, string PolicyVersion, string PolicyEffectiveDate);

// ---------------------------------------------------------------------------
// Salud administrativa
// ---------------------------------------------------------------------------

public sealed record PlatformHealthDto(string Environment, string Version, string Commit,
    DateTimeOffset? DeployedAtUtc, string DatabaseStatus, string ObjectStorageStatus,
    string ObjectStorageProvider, string DataProtectionStatus, bool DemoSeedEnabled,
    TimeSpan Uptime, string MigrationStatus);
