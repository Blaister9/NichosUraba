using System.Text.RegularExpressions;

namespace UrabaConecta.Domain;

public enum BusinessModuleKind { Appointments, VirtualQueues, PickupOrders }
public enum PlatformAuditAction
{
    BusinessCreated, BusinessUpdated, ModulesChanged, OwnerAssigned, OwnerChanged,
    BusinessActivated, BusinessSuspended, BusinessReactivated, BusinessArchived,
    PilotAccountCreated, TemporaryPasswordChanged, BusinessDeleted
}

public sealed partial class Business
{
    public string? WhatsAppUrl { get; private set; }
    public string? LocationUrl { get; private set; }
    public string? SuspensionReason { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public long Version { get; private set; }

    public static Business CreateDraft(Guid id, string slug, string name, Guid municipalityId, Guid categoryId,
        string description, string? address, string? publicPhone, string? whatsAppUrl, string? locationUrl,
        DateTimeOffset now)
    {
        ValidateProfile(slug, name, description, address, publicPhone, whatsAppUrl, locationUrl);
        var business = new Business(id, NormalizeSlug(slug), name.Trim(), municipalityId, categoryId,
            description.Trim(), address?.Trim() ?? "", publicPhone?.Trim() ?? "")
        {
            Status = BusinessStatus.Draft, IsPublished = false, WhatsAppUrl = CleanUrl(whatsAppUrl),
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
        if (Status is not (BusinessStatus.Draft or BusinessStatus.PendingConfiguration or BusinessStatus.Active))
            throw new DomainException("INVALID_BUSINESS_TRANSITION", "El negocio no puede pasar a configuración pendiente.");
        Status = BusinessStatus.PendingConfiguration; IsPublished = false; SuspensionReason = null; Touch(now);
    }

    public void ConfigurationChanged(DateTimeOffset now, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Status == BusinessStatus.Archived)
            throw new DomainException("BUSINESS_ARCHIVED", "Un negocio archivado no se puede configurar.");
        if (Status == BusinessStatus.Active)
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
        if (Status is not (BusinessStatus.Draft or BusinessStatus.PendingConfiguration or BusinessStatus.Suspended or BusinessStatus.Active))
            throw new DomainException("INVALID_BUSINESS_TRANSITION", "El estado actual no permite activar.");
        Status = BusinessStatus.Active; IsPublished = true; SuspensionReason = null; Touch(now);
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
    private static void ValidateUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (value.Length > 500 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            throw new DomainException("INVALID_URL", "Ingrese un enlace web válido.");
    }
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

public sealed record ReadinessRequirement(string Key, string Label, bool IsApplicable, bool IsComplete);
public sealed record BusinessReadiness(IReadOnlyList<ReadinessRequirement> Requirements)
{
    public bool IsReady => Requirements.Where(x => x.IsApplicable).All(x => x.IsComplete);
}

public static class BusinessReadinessCalculator
{
    public static BusinessReadiness Calculate(bool publicInformation, bool activeOwner,
        IReadOnlyCollection<BusinessModuleKind> enabledModules, bool hasHours, bool hasService,
        bool hasQueueDefinition, bool hasPickupSettings, bool hasProductCategory, bool hasProduct)
    {
        var appointments = enabledModules.Contains(BusinessModuleKind.Appointments);
        var queues = enabledModules.Contains(BusinessModuleKind.VirtualQueues);
        var orders = enabledModules.Contains(BusinessModuleKind.PickupOrders);
        return new([
            new("public-information", "Información pública", true, publicInformation),
            new("active-owner", "Propietario activo", true, activeOwner),
            new("modules", "Funciones disponibles", true, enabledModules.Count > 0),
            new("hours", "Horario", appointments, !appointments || hasHours),
            new("services", "Servicios", appointments, !appointments || hasService),
            new("queue", "Fila virtual", queues, !queues || hasQueueDefinition),
            new("pickup-settings", "Franjas para recoger", orders, !orders || hasPickupSettings),
            new("catalog-category", "Categoría del menú", orders, !orders || hasProductCategory),
            new("catalog-product", "Producto activo", orders, !orders || hasProduct),
            new("permissions", "Permisos del propietario", true, activeOwner)
        ]);
    }
}
