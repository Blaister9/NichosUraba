using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Application;

/// <summary>
/// Quién ejecuta una operación administrativa y desde dónde. Se construye en el borde HTTP;
/// los casos de uso nunca deducen el rol por su cuenta.
/// </summary>
/// <summary>
/// Quién actúa sobre un negocio. <paramref name="IsBusinessOwner"/> no concede nada por sí solo: la
/// propiedad se confirma contra la membresía del negocio concreto. Existe para que un propietario
/// pueda administrar el perfil y las imágenes de SU negocio reutilizando estos mismos casos de uso,
/// en lugar de mantener un segundo sistema de perfil y de medios para él.
/// </summary>
public sealed record PlatformActor(Guid UserId, bool IsPlatformAdmin, bool IsPartnerOperator,
    string? IpAddress = null, string? CorrelationId = null, bool IsBusinessOwner = false)
{
    public bool CanReview => IsPlatformAdmin;
    public bool CanOperate => IsPlatformAdmin || IsPartnerOperator;
}

// ---------------------------------------------------------------------------
// Almacenamiento de objetos
// ---------------------------------------------------------------------------

public sealed record ObjectStorageHealth(bool IsHealthy, string Detail);

/// <summary>
/// Almacenamiento de binarios públicos. Nunca se guardan imágenes en PostgreSQL,
/// en el filesystem efímero del contenedor ni en wwwroot.
/// </summary>
public interface IObjectStorage
{
    string Provider { get; }
    Task PutAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken);
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
    /// <summary>URL pública estable para renderizar la imagen.</summary>
    string PublicUrl(string key);
    Task<ObjectStorageHealth> CheckHealthAsync(CancellationToken cancellationToken);
}

public sealed record NormalizedImage(byte[] Content, string ContentType, string Extension, int Width, int Height);

/// <summary>
/// Reescala, recomprime y elimina metadatos EXIF. Rechaza cualquier contenido cuya firma binaria
/// no corresponda a JPEG, PNG o WebP, sin confiar nunca en la extensión ni en el content type declarado.
/// </summary>
public interface IImageProcessor
{
    /// <summary>El uso decide el lado mayor: un logo no necesita el tamaño de una galería.</summary>
    NormalizedImage Normalize(ReadOnlyMemory<byte> original, BusinessImageKind kind);
}

public static class ImagePolicy
{
    public const long MaximumOriginalBytes = 5 * 1024 * 1024;
    public const int MaximumLongestSide = 1600;
    public static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];

    /// <summary>Formato de salida único. WebP pesa mucho menos que PNG y admite transparencia.</summary>
    public const string OutputContentType = "image/webp";
    public const string OutputExtension = ".webp";

    /// <summary>
    /// Lado mayor según el uso real de la imagen. Guardar todo a 1600 px hacía que una tarjeta
    /// mostrara a 64 px un archivo pensado para pantalla completa.
    /// </summary>
    public static int LongestSideFor(BusinessImageKind kind) => kind switch
    {
        // El logo se muestra a 64 px en la tarjeta y a unos 160 px en la ficha.
        BusinessImageKind.Logo => 320,
        // La portada se muestra a 640 px en la tarjeta y a ancho completo en la ficha.
        BusinessImageKind.Cover => 1280,
        // Servicio y producto viven en tarjetas de catálogo: unos 400 px en móvil, el doble en
        // pantallas densas. Guardarlos a 1600 px sólo alarga la descarga de una lista larga.
        BusinessImageKind.Service or BusinessImageKind.Product => 800,
        _ => MaximumLongestSide
    };
}

// ---------------------------------------------------------------------------
// Invitaciones y accesos
// ---------------------------------------------------------------------------

/// <summary>Genera y verifica tokens de invitación. El token en claro nunca se persiste.</summary>
public interface IInvitationTokenService
{
    (string PlainText, string Hash) Generate();
    string Hash(string plainText);
}

public sealed record InvitationRecord(AccessInvitation Invitation, string? BusinessName);

public interface IAccessInvitationStore
{
    Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
    Task<AccessInvitation?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken);
    Task<AccessInvitation?> GetAsync(Guid invitationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<InvitationRecord>> ListAsync(Guid? businessId, Guid? createdByUserId,
        CancellationToken cancellationToken);
    Task<bool> HasPendingAsync(string email, Guid? businessId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<string?> GetBusinessNameAsync(Guid businessId, CancellationToken cancellationToken);
    void Add(AccessInvitation invitation);
    void AddAudit(PlatformAccessAudit audit);
    void AddMembership(BusinessMembership membership);
    Task<BusinessMembership?> GetMembershipByUserAsync(Guid businessId, Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlatformAccessAuditDto>> ListAuditAsync(Guid? businessId, int take,
        CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Operaciones de Identity que necesitan las invitaciones, sin exponer el UserManager.</summary>
public interface IInvitationIdentityGateway
{
    Task<IdentityAccount?> FindByExactEmailAsync(string email, CancellationToken cancellationToken);
    /// <summary>Crea la cuenta sin contraseña utilizable; se define al aceptar la invitación.</summary>
    Task<IdentityAccount> CreatePendingAsync(string displayName, string email, string role,
        CancellationToken cancellationToken);
    Task EnsureRoleAsync(Guid userId, string role, CancellationToken cancellationToken);
    Task RemoveRoleAsync(Guid userId, string role, CancellationToken cancellationToken);
    /// <summary>Fija la contraseña elegida por la persona, cierra sesiones previas y activa la cuenta.</summary>
    Task SetPasswordAndActivateAsync(Guid userId, string password, CancellationToken cancellationToken);
    Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlatformAccountDto>> ListByRoleAsync(string role, CancellationToken cancellationToken);
}

public interface IAccessInvitationUseCases
{
    Task<InvitationIssuedDto> InviteAsync(PlatformActor actor, CreateInvitationRequest request,
        CancellationToken cancellationToken = default);
    Task<InvitationIssuedDto> ResendAsync(PlatformActor actor, Guid invitationId,
        CancellationToken cancellationToken = default);
    Task RevokeAsync(PlatformActor actor, Guid invitationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InvitationDto>> ListAsync(PlatformActor actor, Guid? businessId,
        CancellationToken cancellationToken = default);
    Task<InvitationPreviewDto> PreviewAsync(string token, CancellationToken cancellationToken = default);
    Task AcceptAsync(string token, string password, string? ipAddress, CancellationToken cancellationToken = default);
    Task<InvitationIssuedDto> ResetAccessAsync(PlatformActor actor, ResetAccessRequest request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlatformAccessAuditDto>> ListAuditAsync(PlatformActor actor, Guid? businessId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlatformAccountDto>> ListPartnerOperatorsAsync(PlatformActor actor,
        CancellationToken cancellationToken = default);
    Task RevokePartnerOperatorAsync(PlatformActor actor, Guid userId, CancellationToken cancellationToken = default);
}

// ---------------------------------------------------------------------------
// Imágenes de negocio
// ---------------------------------------------------------------------------

public interface IBusinessImageStore
{
    Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<BusinessImage>> ListAsync(Guid businessId, CancellationToken cancellationToken);
    Task<BusinessImage?> GetAsync(Guid businessId, Guid imageId, CancellationToken cancellationToken);
    Task<Business?> GetBusinessAsync(Guid businessId, CancellationToken cancellationToken);
    /// <summary>
    /// Confirma que la fila del catálogo existe y pertenece a ese negocio. Sin esta comprobación,
    /// un identificador ajeno colgaría una foto del servicio de otro establecimiento.
    /// </summary>
    Task<bool> CatalogTargetExistsAsync(Guid businessId, BusinessImageKind kind, Guid targetId,
        CancellationToken cancellationToken);
    void Add(BusinessImage image);
    void AddAudit(PlatformAuditEntry audit);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record UploadedImage(string FileName, string ContentType, ReadOnlyMemory<byte> Content);

public interface IBusinessImageUseCases
{
    Task<IReadOnlyList<BusinessImageDto>> ListAsync(PlatformActor actor, Guid businessId,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// <paramref name="targetId"/> identifica la fila del catálogo cuando el tipo es Service o
    /// Product; para logo, portada y galería no se envía.
    /// </summary>
    Task<BusinessImageDto> UploadAsync(PlatformActor actor, Guid businessId, string kind, UploadedImage file,
        string? altText, Guid? targetId = null, CancellationToken cancellationToken = default);
    Task<BusinessImageDto> DescribeAsync(PlatformActor actor, Guid businessId, Guid imageId,
        UpdateBusinessImageRequest request, CancellationToken cancellationToken = default);
    Task RemoveAsync(PlatformActor actor, Guid businessId, Guid imageId, long version,
        CancellationToken cancellationToken = default);
}

// ---------------------------------------------------------------------------
// Textos legales configurables
// ---------------------------------------------------------------------------

/// <summary>
/// Versión vigente de la política de tratamiento de datos que se debe aceptar en los formularios
/// públicos. Se toma de <c>Legal__PolicyVersion</c>; los formularios envían la versión que mostraron
/// y el servidor rechaza cualquier otra.
/// </summary>
public interface IConsentPolicyProvider
{
    string CurrentVersion { get; }
}

/// <summary>Estado funcional de la instalación, para la pantalla administrativa de salud.</summary>
public interface IPlatformHealthProvider
{
    Task<PlatformHealthDto> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Datos jurídicos reales del responsable del tratamiento. No tienen valores por defecto:
/// si faltan en Production, la aplicación no arranca.
/// </summary>
public sealed class LegalOptions
{
    public const string SectionName = "Legal";
    public string ResponsibleName { get; set; } = "";
    public string Identification { get; set; } = "";
    public string Address { get; set; } = "";
    public string PrivacyEmail { get; set; } = "";
    public string SupportEmail { get; set; } = "";
    public string PolicyVersion { get; set; } = "";
    public string PolicyEffectiveDate { get; set; } = "";

    public IReadOnlyList<string> MissingKeys()
    {
        var missing = new List<string>();
        void Check(string value, string key) { if (string.IsNullOrWhiteSpace(value)) missing.Add(key); }
        Check(ResponsibleName, "Legal__ResponsibleName");
        Check(Identification, "Legal__Identification");
        Check(Address, "Legal__Address");
        Check(PrivacyEmail, "Legal__PrivacyEmail");
        Check(SupportEmail, "Legal__SupportEmail");
        Check(PolicyVersion, "Legal__PolicyVersion");
        Check(PolicyEffectiveDate, "Legal__PolicyEffectiveDate");
        return missing;
    }
}
