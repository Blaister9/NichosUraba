using System.Text.RegularExpressions;

namespace UrabaConecta.Domain;

/// <summary>
/// Las tres primeras son operaciones que el negocio abre al público. Las tres últimas son el
/// material que esas operaciones consumen y se derivan de ellas mientras nadie las fije a mano;
/// la regla vive en <see cref="BusinessCapabilities"/>. Se guardan en la misma tabla porque son
/// la misma decisión —qué tiene este negocio— y separarlas obligaría a consultar dos sitios para
/// responder una sola pregunta.
/// </summary>
public enum BusinessModuleKind { Appointments, VirtualQueues, PickupOrders, Services, Products, Staff }
public enum PlatformAuditAction
{
    BusinessCreated, BusinessUpdated, ModulesChanged, OwnerAssigned, OwnerChanged,
    BusinessActivated, BusinessSuspended, BusinessReactivated, BusinessArchived,
    PilotAccountCreated, TemporaryPasswordChanged, BusinessDeleted,
    BusinessSubmittedForReview, BusinessReviewRejected,
    ImageUploaded, ImageRemoved, InvitationCreated, InvitationAccepted,
    InvitationRevoked, InvitationResent
}

/// <summary>Datos editables del perfil comercial público de un negocio.</summary>
public sealed record BusinessProfileEdit(
    string Slug, string Name, Guid MunicipalityId, Guid CategoryId,
    string ShortDescription, string Description, string? Address, string? ReferencePoint,
    string? PublicPhone, string? WhatsAppUrl, string? PublicEmail,
    string? InstagramUrl, string? FacebookUrl, string? LocationUrl, string? CustomerInstructions);

public sealed partial class Business
{
    public string? WhatsAppUrl { get; private set; }
    public string? LocationUrl { get; private set; }
    public string? SuspensionReason { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public long Version { get; private set; }

    public string ShortDescription { get; private set; } = "";
    public string? ReferencePoint { get; private set; }
    public string? PublicEmail { get; private set; }
    public string? InstagramUrl { get; private set; }
    public string? FacebookUrl { get; private set; }
    public string? CustomerInstructions { get; private set; }
    public string? ReviewNotes { get; private set; }
    public DateTimeOffset? SubmittedForReviewAtUtc { get; private set; }
    public DateTimeOffset? PublishedAtUtc { get; private set; }

    /// <summary>Socia o administrador que dio de alta el negocio. Delimita lo que una socia puede ver.</summary>
    public Guid? CreatedByUserId { get; private set; }

    public void AssignCreator(Guid userId)
    {
        if (CreatedByUserId is not null)
            throw new DomainException("CREATOR_ALREADY_ASSIGNED", "El negocio ya tiene responsable de alta.");
        CreatedByUserId = userId;
    }

    /// <summary>Reemplaza el perfil comercial completo, incluidos los campos añadidos en V5.</summary>
    public void UpdateCommercialProfile(BusinessProfileEdit edit, DateTimeOffset now, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Status == BusinessStatus.Archived)
            throw new DomainException("BUSINESS_ARCHIVED", "Un negocio archivado no se puede editar.");
        ValidateProfile(edit.Slug, edit.Name, edit.Description, edit.Address, edit.PublicPhone,
            edit.WhatsAppUrl, edit.LocationUrl);
        ValidateExtendedProfile(edit);
        Slug = NormalizeSlug(edit.Slug); Name = edit.Name.Trim();
        MunicipalityId = edit.MunicipalityId; CategoryId = edit.CategoryId;
        ShortDescription = edit.ShortDescription.Trim(); Description = edit.Description.Trim();
        Address = edit.Address?.Trim() ?? ""; ReferencePoint = Clean(edit.ReferencePoint);
        PublicPhone = edit.PublicPhone?.Trim() ?? ""; WhatsAppUrl = CleanUrl(edit.WhatsAppUrl);
        PublicEmail = Clean(edit.PublicEmail)?.ToLowerInvariant();
        InstagramUrl = CleanUrl(edit.InstagramUrl); FacebookUrl = CleanUrl(edit.FacebookUrl);
        LocationUrl = CleanUrl(edit.LocationUrl); CustomerInstructions = Clean(edit.CustomerInstructions);
        Touch(now);
    }

    /// <summary>Envía el negocio a revisión administrativa. No lo publica.</summary>
    public void SubmitForReview(bool ready, DateTimeOffset now, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (!ready) throw new DomainException("BUSINESS_NOT_READY", "Complete el checklist antes de enviar a revisión.");
        if (Status is not (BusinessStatus.Draft or BusinessStatus.PendingConfiguration or BusinessStatus.PendingReview))
            throw new DomainException("INVALID_BUSINESS_TRANSITION", "El estado actual no permite enviar a revisión.");
        Status = BusinessStatus.PendingReview; IsPublished = false; ReviewNotes = null;
        SubmittedForReviewAtUtc = now; Touch(now);
    }

    /// <summary>Devuelve el negocio a configuración con observaciones para la socia.</summary>
    public void RejectReview(string notes, DateTimeOffset now, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Status != BusinessStatus.PendingReview)
            throw new DomainException("INVALID_BUSINESS_TRANSITION", "Solo se rechaza un negocio en revisión.");
        if (string.IsNullOrWhiteSpace(notes) || notes.Trim().Length > 400)
            throw new DomainException("REVIEW_NOTES_REQUIRED", "Escriba las observaciones para la socia.");
        Status = BusinessStatus.PendingConfiguration; IsPublished = false;
        ReviewNotes = notes.Trim(); Touch(now);
    }

    /// <summary>
    /// Alta de un borrador. Toma la descripción breve desde el principio: el checklist la exige para
    /// publicar, así que capturarla sólo en la edición dejaba al negocio recién creado con un
    /// requisito imposible de entender desde el alta.
    /// </summary>
    public static Business CreateDraft(Guid id, string slug, string name, Guid municipalityId, Guid categoryId,
        string shortDescription, string description, string? address, string? publicPhone, string? whatsAppUrl,
        string? locationUrl, DateTimeOffset now)
    {
        ValidateProfile(slug, name, description, address, publicPhone, whatsAppUrl, locationUrl);
        ValidateShortDescription(shortDescription);
        var business = new Business(id, NormalizeSlug(slug), name.Trim(), municipalityId, categoryId,
            description.Trim(), address?.Trim() ?? "", publicPhone?.Trim() ?? "")
        {
            Status = BusinessStatus.Draft, IsPublished = false, ShortDescription = shortDescription.Trim(),
            WhatsAppUrl = CleanUrl(whatsAppUrl),
            LocationUrl = CleanUrl(locationUrl), CreatedAtUtc = now, UpdatedAtUtc = now
        };
        return business;
    }

    public void UpdatePlatformProfile(string slug, string name, Guid municipalityId, Guid categoryId,
        string description, string? address, string? publicPhone, string? whatsAppUrl, string? locationUrl,
        DateTimeOffset now, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Status == BusinessStatus.Archived)
            throw new DomainException("BUSINESS_ARCHIVED", "Un negocio archivado no se puede editar.");
        ValidateProfile(slug, name, description, address, publicPhone, whatsAppUrl, locationUrl);
        Slug = NormalizeSlug(slug); Name = name.Trim(); MunicipalityId = municipalityId; CategoryId = categoryId;
        Description = description.Trim(); Address = address?.Trim() ?? ""; PublicPhone = publicPhone?.Trim() ?? "";
        WhatsAppUrl = CleanUrl(whatsAppUrl); LocationUrl = CleanUrl(locationUrl); Touch(now);
    }

    public void MarkPending(DateTimeOffset now, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Status is not (BusinessStatus.Draft or BusinessStatus.PendingConfiguration or BusinessStatus.PendingReview
            or BusinessStatus.Active))
            throw new DomainException("INVALID_BUSINESS_TRANSITION", "El negocio no puede pasar a configuración pendiente.");
        Status = BusinessStatus.PendingConfiguration; IsPublished = false; SuspensionReason = null; Touch(now);
    }

    public void ConfigurationChanged(DateTimeOffset now, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Status == BusinessStatus.Archived)
            throw new DomainException("BUSINESS_ARCHIVED", "Un negocio archivado no se puede configurar.");
        // Un cambio de configuración invalida tanto una publicación vigente como una revisión en curso.
        if (Status is BusinessStatus.Active or BusinessStatus.PendingReview)
        {
            Status = BusinessStatus.PendingConfiguration;
            IsPublished = false;
        }
        Touch(now);
    }

    public void Activate(bool ready, DateTimeOffset now, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (!ready) throw new DomainException("BUSINESS_NOT_READY", "Complete los requisitos antes de activar.");
        if (Status is BusinessStatus.Archived)
            throw new DomainException("INVALID_BUSINESS_TRANSITION", "Un negocio archivado requiere restauración explícita.");
        if (Status is not (BusinessStatus.Draft or BusinessStatus.PendingConfiguration or BusinessStatus.PendingReview
            or BusinessStatus.Suspended or BusinessStatus.Active))
            throw new DomainException("INVALID_BUSINESS_TRANSITION", "El estado actual no permite activar.");
        Status = BusinessStatus.Active; IsPublished = true; SuspensionReason = null; ReviewNotes = null;
        PublishedAtUtc ??= now; Touch(now);
    }

    public void Suspend(string reason, DateTimeOffset now, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Status != BusinessStatus.Active)
            throw new DomainException("INVALID_BUSINESS_TRANSITION", "Solo un negocio activo se puede suspender.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 240)
            throw new DomainException("SUSPENSION_REASON_REQUIRED", "Registre un motivo administrativo.");
        Status = BusinessStatus.Suspended; IsPublished = false; SuspensionReason = reason.Trim(); Touch(now);
    }

    public void Archive(DateTimeOffset now, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Status == BusinessStatus.Archived)
            throw new DomainException("INVALID_BUSINESS_TRANSITION", "El negocio ya está archivado.");
        Status = BusinessStatus.Archived; IsPublished = false; Touch(now);
    }

    public static string NormalizeSlug(string value)
    {
        var slug = value.Trim().ToLowerInvariant();
        slug = Regex.Replace(slug.Normalize(System.Text.NormalizationForm.FormD), @"\p{Mn}", "");
        slug = Regex.Replace(slug, @"[^a-z0-9]+", "-").Trim('-');
        if (slug.Length is < 3 or > 120)
            throw new DomainException("INVALID_SLUG", "El identificador debe tener entre 3 y 120 caracteres.");
        return slug;
    }

    private static void ValidateProfile(string slug, string name, string description, string? address,
        string? phone, string? whatsApp, string? location)
    {
        _ = NormalizeSlug(slug);
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 160)
            throw new DomainException("INVALID_BUSINESS_NAME", "El nombre comercial es obligatorio.");
        if (description?.Length > 600 || address?.Length > 240 || phone?.Length > 30)
            throw new DomainException("INVALID_BUSINESS_PROFILE", "Revise la longitud de la información pública.");
        ValidateUrl(whatsApp); ValidateUrl(location);
        if (new[] { name, description, address, phone }.Any(x =>
            x?.Contains('<') == true || x?.Contains('>') == true))
            throw new DomainException("INVALID_BUSINESS_PROFILE", "No se admite HTML en la información pública.");
    }
    /// <summary>
    /// Rellena sólo la descripción breve de un negocio anterior a V5, sin revalidar el resto del
    /// perfil. Se separa de <see cref="UpdateCommercialProfile"/> a propósito: aquel revalida el
    /// perfil completo, así que un dato heredado que hoy no pasaría la validación —un teléfono con
    /// formato antiguo, por ejemplo— hacía fallar un relleno que no lo tocaba, y como esto corre
    /// durante el arranque el contenedor entero se quedaba sin levantar.
    /// </summary>
    public void BackfillShortDescription(string shortDescription, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(shortDescription))
            throw new DomainException("INVALID_SHORT_DESCRIPTION", "La descripción breve es obligatoria.");
        ShortDescription = shortDescription.Trim().Length > 160
            ? shortDescription.Trim()[..160] : shortDescription.Trim();
        Touch(now);
    }

    /// <summary>
    /// Regla única de la descripción breve. La comparten el alta y la edición del perfil para que no
    /// puedan divergir: un campo obligatorio en un sitio y opcional en el otro fue justamente el
    /// defecto que dejaba el checklist reclamando algo que la socia no podía escribir.
    /// </summary>
    private static void ValidateShortDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 160)
            throw new DomainException("INVALID_SHORT_DESCRIPTION",
                "La descripción breve es obligatoria y admite máximo 160 caracteres.");
        if (value.Contains('<') || value.Contains('>'))
            throw new DomainException("INVALID_BUSINESS_PROFILE", "No se admite HTML en la información pública.");
    }

    private static void ValidateExtendedProfile(BusinessProfileEdit edit)
    {
        ValidateShortDescription(edit.ShortDescription);
        if (edit.ReferencePoint?.Length > 160)
            throw new DomainException("INVALID_REFERENCE_POINT", "El punto de referencia admite máximo 160 caracteres.");
        if (edit.CustomerInstructions?.Length > 600)
            throw new DomainException("INVALID_INSTRUCTIONS", "Las instrucciones admiten máximo 600 caracteres.");
        ValidatePhone(edit.PublicPhone);
        ValidateEmail(edit.PublicEmail);
        ValidateSocialUrl(edit.InstagramUrl, "instagram.com");
        ValidateSocialUrl(edit.FacebookUrl, "facebook.com");
        if (new[] { edit.ReferencePoint, edit.CustomerInstructions, edit.PublicEmail }
            .Any(x => x?.Contains('<') == true || x?.Contains('>') == true))
            throw new DomainException("INVALID_BUSINESS_PROFILE", "No se admite HTML en la información pública.");
    }

    private static void ValidatePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var digits = value.Count(char.IsDigit);
        if (digits is < 7 or > 15 || !Regex.IsMatch(value.Trim(), @"^[+]?[0-9()\s.-]{7,30}$"))
            throw new DomainException("INVALID_PHONE", "Ingrese un teléfono válido de 7 a 15 dígitos.");
    }

    private static void ValidateEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (value.Trim().Length > 160 || !Regex.IsMatch(value.Trim(), @"^[^@\s]+@[^@\s.]+(\.[^@\s.]+)+$"))
            throw new DomainException("INVALID_EMAIL", "Ingrese un correo electrónico válido.");
    }

    private static void ValidateSocialUrl(string? value, string expectedHost)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        ValidateUrl(value);
        var host = new Uri(value.Trim()).Host.ToLowerInvariant();
        if (host != expectedHost && !host.EndsWith('.' + expectedHost))
            throw new DomainException("INVALID_SOCIAL_URL", $"El enlace debe apuntar a {expectedHost}.");
    }

    private static void ValidateUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (value.Length > 500 || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            throw new DomainException("INVALID_URL", "Ingrese un enlace web válido.");
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? CleanUrl(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private void EnsureVersion(long expected)
    {
        if (Version != expected) throw new DomainException("CONCURRENCY_CONFLICT", "El negocio cambió. Recargue e intente de nuevo.");
    }
    private void Touch(DateTimeOffset now) { UpdatedAtUtc = now; Version++; }
}

public sealed class BusinessModule : IBusinessOwned
{
    private BusinessModule() { }
    public BusinessModule(Guid businessId, BusinessModuleKind module, bool enabled, DateTimeOffset now)
        => (BusinessId, Module, IsEnabled, UpdatedAtUtc) = (businessId, module, enabled, now);
    public Guid BusinessId { get; private set; }
    public BusinessModuleKind Module { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public long Version { get; private set; }
    public void SetEnabled(bool enabled, DateTimeOffset now, long expectedVersion)
    {
        if (Version != expectedVersion) throw new DomainException("CONCURRENCY_CONFLICT", "Las funciones cambiaron. Recargue.");
        IsEnabled = enabled; UpdatedAtUtc = now; Version++;
    }
}

public sealed class PlatformAuditEntry : IBusinessOwned
{
    private PlatformAuditEntry() { }
    public PlatformAuditEntry(Guid id, Guid businessId, Guid actorUserId, PlatformAuditAction action,
        string previousState, string newState, DateTimeOffset occurredAtUtc, string? correlationId = null)
        => (Id, BusinessId, ActorUserId, Action, PreviousState, NewState, OccurredAtUtc, CorrelationId) =
            (id, businessId, actorUserId, action, previousState, newState, occurredAtUtc, correlationId);
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public PlatformAuditAction Action { get; private set; }
    public string PreviousState { get; private set; } = "{}";
    public string NewState { get; private set; } = "{}";
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string? CorrelationId { get; private set; }
}

public sealed record ReadinessRequirement(string Key, string Label, bool IsApplicable, bool IsComplete,
    string? MissingHint = null);
public sealed record BusinessReadiness(IReadOnlyList<ReadinessRequirement> Requirements)
{
    public bool IsReady => Requirements.Where(x => x.IsApplicable).All(x => x.IsComplete);

    /// <summary>Porcentaje de requisitos aplicables completos, entre 0 y 100.</summary>
    public int CompletionPercentage
    {
        get
        {
            var applicable = Requirements.Where(x => x.IsApplicable).ToList();
            return applicable.Count == 0 ? 100
                : (int)Math.Round(applicable.Count(x => x.IsComplete) * 100d / applicable.Count,
                    MidpointRounding.ToZero);
        }
    }

    public IReadOnlyList<string> MissingLabels
        => Requirements.Where(x => x.IsApplicable && !x.IsComplete).Select(x => x.MissingHint ?? x.Label).ToList();
}

/// <summary>Señales de identidad visual y contacto que alimentan el checklist de onboarding.</summary>
public sealed record BusinessCompletionSignals(bool HasContact = true, bool HasLocation = true,
    bool HasLogo = true, bool HasCover = true);

public static class BusinessReadinessCalculator
{
    /// <summary>
    /// La información básica se reporta como tres requisitos separados y no como uno agrupado: un
    /// solo mensaje con "el nombre, la descripción breve o la descripción completa" obliga a la
    /// socia a adivinar cuál de los tres le falta.
    /// </summary>
    public static BusinessReadiness Calculate(bool hasName, bool hasShortDescription, bool hasDescription,
        bool activeOwner,
        IReadOnlyCollection<BusinessModuleKind> enabledModules, bool hasHours, bool hasService,
        bool hasQueueDefinition, bool hasPickupSettings, bool hasProductCategory, bool hasProduct,
        BusinessCompletionSignals? signals = null)
    {
        var s = signals ?? new BusinessCompletionSignals();
        var appointments = enabledModules.Contains(BusinessModuleKind.Appointments);
        var queues = enabledModules.Contains(BusinessModuleKind.VirtualQueues);
        var orders = enabledModules.Contains(BusinessModuleKind.PickupOrders);
        return new([
            new("business-name", "Nombre", true, hasName, "Falta el nombre del negocio."),
            new("short-description", "Descripción breve", true, hasShortDescription,
                "Falta la descripción breve."),
            new("full-description", "Descripción completa", true, hasDescription,
                "Falta la descripción completa."),
            new("contact", "Contacto", true, s.HasContact,
                "Registre al menos un teléfono, un WhatsApp o un correo público."),
            new("location", "Ubicación", true, s.HasLocation, "Falta la dirección del establecimiento."),
            new("logo", "Logo", true, s.HasLogo, "Cargue el logo del negocio."),
            new("cover", "Imagen de portada", true, s.HasCover, "Cargue la imagen de portada."),
            // Se cuentan sólo las operaciones que se abren al público: un negocio con "productos"
            // encendido pero sin pedidos, citas ni fila no tiene nada que ofrecer todavía.
            new("modules", "Funciones disponibles", true,
                enabledModules.Any(BusinessCapabilities.Operations.Contains),
                "Habilite al menos una función."),
            new("hours", "Horario", appointments, !appointments || hasHours, "Configure el horario de atención."),
            new("services", "Servicios", appointments, !appointments || hasService,
                "Cree al menos un servicio activo."),
            new("queue", "Fila virtual", queues, !queues || hasQueueDefinition, "Configure la fila virtual."),
            new("pickup-settings", "Franjas para recoger", orders, !orders || hasPickupSettings,
                "Configure las franjas de recogida."),
            new("catalog-category", "Categoría del menú", orders, !orders || hasProductCategory,
                "Cree al menos una categoría del menú."),
            new("catalog-product", "Producto activo", orders, !orders || hasProduct,
                "Cree al menos un producto activo."),
            new("active-owner", "Propietario", true, activeOwner,
                "Invite o asigne a la persona propietaria."),
            new("permissions", "Permisos del propietario", true, activeOwner,
                "La persona propietaria debe tener membresía activa.")
        ]);
    }
}
