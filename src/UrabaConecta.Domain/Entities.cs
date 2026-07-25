namespace UrabaConecta.Domain;

public enum BusinessStatus { Active, Suspended }
public enum MembershipRole { Owner, Worker }
public enum AppointmentStatus { Pending, Confirmed, Rejected, Cancelled, Completed, NoShow }
public enum AvailabilityExceptionType { ClosedAllDay, ClosedInterval, ExtraordinaryOpening }

public interface IBusinessOwned
{
    Guid BusinessId { get; }
}

public sealed class Municipality
{
    private Municipality() { }
    public Municipality(Guid id, string slug, string name) => (Id, Slug, Name) = (id, slug, name);
    public Guid Id { get; private set; }
    public string Slug { get; private set; } = "";
    public string Name { get; private set; } = "";
    public bool IsActive { get; private set; } = true;
}

public sealed class Category
{
    private Category() { }
    public Category(Guid id, string slug, string name) => (Id, Slug, Name) = (id, slug, name);
    public Guid Id { get; private set; }
    public string Slug { get; private set; } = "";
    public string Name { get; private set; } = "";
    public bool IsActive { get; private set; } = true;
}

public sealed class Business
{
    private Business() { }
    public Business(Guid id, string slug, string name, Guid municipalityId, Guid categoryId,
        string description, string address, string publicPhone)
    {
        Id = id; Slug = slug; Name = name; MunicipalityId = municipalityId; CategoryId = categoryId;
        Description = description; Address = address; PublicPhone = publicPhone;
    }
    public Guid Id { get; private set; }
    public string Slug { get; private set; } = "";
    public string Name { get; private set; } = "";
    public Guid MunicipalityId { get; private set; }
    public Guid CategoryId { get; private set; }
    public string Description { get; private set; } = "";
    public string Address { get; private set; } = "";
    public string PublicPhone { get; private set; } = "";
    public string TimeZoneId { get; private set; } = "America/Bogota";
    public BusinessStatus Status { get; private set; } = BusinessStatus.Active;
    public bool IsPublished { get; private set; } = true;
    public void Unpublish() => IsPublished = false;
}

public sealed class BusinessMembership : IBusinessOwned
{
    private BusinessMembership() { }
    public BusinessMembership(Guid id, Guid businessId, Guid userId, MembershipRole role,
        bool canManageConfiguration = false)
        => (Id, BusinessId, UserId, Role, CanManageConfiguration) =
            (id, businessId, userId, role, canManageConfiguration);
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid UserId { get; private set; }
    public MembershipRole Role { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool CanManageConfiguration { get; private set; }
}

public sealed class BusinessHour : IBusinessOwned
{
    private BusinessHour() { }
    public BusinessHour(Guid id, Guid businessId, DayOfWeek day, TimeOnly opensAt, TimeOnly closesAt)
    {
        if (closesAt <= opensAt) throw new DomainException("INVALID_HOURS", "La hora de cierre debe ser posterior.");
        (Id, BusinessId, Day, OpensAt, ClosesAt) = (id, businessId, day, opensAt, closesAt);
    }
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public DayOfWeek Day { get; private set; }
    public TimeOnly OpensAt { get; private set; }
    public TimeOnly ClosesAt { get; private set; }
    public long Version { get; private set; }
    public void Update(TimeOnly opensAt, TimeOnly closesAt, long? expectedVersion = null)
    {
        EnsureVersion(expectedVersion);
        if (closesAt <= opensAt) throw new DomainException("INVALID_HOURS", "La hora de cierre debe ser posterior.");
        OpensAt = opensAt; ClosesAt = closesAt;
        Version++;
    }
    private void EnsureVersion(long? expectedVersion)
    {
        if (expectedVersion.HasValue && expectedVersion.Value != Version)
            throw new DomainException("CONCURRENCY_CONFLICT", "El horario cambió. Recargue la información.");
    }
}

public sealed class Service : IBusinessOwned
{
    private Service() { }
    public Service(Guid id, Guid businessId, string name, int durationMinutes, decimal referencePrice,
        string? description = null, int displayOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("INVALID_SERVICE", "El nombre es obligatorio.");
        if (durationMinutes is < 5 or > 480) throw new DomainException("INVALID_DURATION", "Duración inválida.");
        if (referencePrice < 0) throw new DomainException("INVALID_PRICE", "Precio inválido.");
        if (description?.Length > 500) throw new DomainException("INVALID_SERVICE", "La descripción es demasiado larga.");
        if (displayOrder < 0) throw new DomainException("INVALID_SERVICE", "El orden no puede ser negativo.");
        (Id, BusinessId, Name, Description, DurationMinutes, ReferencePrice, DisplayOrder) =
            (id, businessId, name.Trim(), description?.Trim() ?? "", durationMinutes, referencePrice, displayOrder);
    }
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public string Name { get; private set; } = "";
    public string Description { get; private set; } = "";
    public int DurationMinutes { get; private set; }
    public decimal ReferencePrice { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    public long Version { get; private set; }
    public void EnsureActive()
    {
        if (!IsActive) throw new DomainException("SERVICE_INACTIVE", "El servicio no está disponible.");
    }
    public void Update(string name, int durationMinutes, decimal referencePrice, bool active,
        string? description = null, int displayOrder = 0, long? expectedVersion = null)
    {
        if (expectedVersion.HasValue && expectedVersion.Value != Version)
            throw new DomainException("CONCURRENCY_CONFLICT", "El servicio cambió. Recargue la información.");
        if (string.IsNullOrWhiteSpace(name) || durationMinutes is < 5 or > 480 || referencePrice < 0 ||
            description?.Length > 500 || displayOrder < 0)
            throw new DomainException("INVALID_SERVICE", "Los datos del servicio no son válidos.");
        Name = name.Trim(); Description = description?.Trim() ?? ""; DurationMinutes = durationMinutes;
        ReferencePrice = referencePrice; DisplayOrder = displayOrder; IsActive = active; Version++;
    }
}

public sealed class StaffMember : IBusinessOwned
{
    private StaffMember() { }
    public StaffMember(Guid id, Guid businessId, string displayName, bool participatesInAvailability = true)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException("INVALID_STAFF", "El nombre es obligatorio.");
        (Id, BusinessId, DisplayName, ParticipatesInAvailability) =
            (id, businessId, displayName.Trim(), participatesInAvailability);
    }
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public string DisplayName { get; private set; } = "";
    public bool IsActive { get; private set; } = true;
    public bool ParticipatesInAvailability { get; private set; } = true;
    public long Version { get; private set; }
    public void Update(string displayName, bool active, bool participatesInAvailability = true,
        long? expectedVersion = null)
    {
        if (expectedVersion.HasValue && expectedVersion.Value != Version)
            throw new DomainException("CONCURRENCY_CONFLICT", "El perfil cambió. Recargue la información.");
        if (string.IsNullOrWhiteSpace(displayName)) throw new DomainException("INVALID_STAFF", "El nombre es obligatorio.");
        DisplayName = displayName.Trim(); IsActive = active;
        ParticipatesInAvailability = participatesInAvailability; Version++;
    }
}

public sealed class StaffService : IBusinessOwned
{
    private StaffService() { }
    public StaffService(Guid businessId, Guid staffMemberId, Guid serviceId)
        => (BusinessId, StaffMemberId, ServiceId) = (businessId, staffMemberId, serviceId);
    public Guid BusinessId { get; private set; }
    public Guid StaffMemberId { get; private set; }
    public Guid ServiceId { get; private set; }
}

public sealed class AvailabilityException : IBusinessOwned
{
    private AvailabilityException() { }
    public AvailabilityException(Guid id, Guid businessId, Guid staffMemberId, DateOnly date,
        AvailabilityExceptionType type, TimeOnly? opensAt = null, TimeOnly? closesAt = null, string? reason = null)
    {
        (Id, BusinessId, StaffMemberId, Date) = (id, businessId, staffMemberId, date);
        Apply(type, opensAt, closesAt, reason);
    }
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid StaffMemberId { get; private set; }
    public DateOnly Date { get; private set; }
    public bool IsUnavailable { get; private set; }
    public AvailabilityExceptionType Type { get; private set; }
    public TimeOnly? OpensAt { get; private set; }
    public TimeOnly? ClosesAt { get; private set; }
    public string Reason { get; private set; } = "";
    public long Version { get; private set; }
    public void Update(AvailabilityExceptionType type, TimeOnly? opensAt, TimeOnly? closesAt,
        string? reason = null, long? expectedVersion = null)
    {
        if (expectedVersion.HasValue && expectedVersion.Value != Version)
            throw new DomainException("CONCURRENCY_CONFLICT", "La excepción cambió. Recargue la información.");
        Apply(type, opensAt, closesAt, reason);
        Version++;
    }
    private void Apply(AvailabilityExceptionType type, TimeOnly? opensAt, TimeOnly? closesAt, string? reason)
    {
        if (type != AvailabilityExceptionType.ClosedAllDay &&
            (opensAt is null || closesAt is null || closesAt <= opensAt))
            throw new DomainException("INVALID_EXCEPTION", "El intervalo de la excepción no es válido.");
        if ((reason?.Length ?? 0) > 160)
            throw new DomainException("INVALID_EXCEPTION", "El motivo puede tener máximo 160 caracteres.");
        Type = type;
        IsUnavailable = type == AvailabilityExceptionType.ClosedAllDay;
        OpensAt = type == AvailabilityExceptionType.ClosedAllDay ? null : opensAt;
        ClosesAt = type == AvailabilityExceptionType.ClosedAllDay ? null : closesAt;
        Reason = reason?.Trim() ?? "";
    }
}

public sealed class ConsentReceipt : IBusinessOwned
{
    private ConsentReceipt() { }
    public ConsentReceipt(Guid id, Guid businessId, string noticeVersion, string purpose, DateTimeOffset acceptedAtUtc)
        => (Id, BusinessId, NoticeVersion, Purpose, AcceptedAtUtc) =
            (id, businessId, noticeVersion, purpose, acceptedAtUtc);
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public string NoticeVersion { get; private set; } = "";
    public string Purpose { get; private set; } = "";
    public DateTimeOffset AcceptedAtUtc { get; private set; }
    public Guid? AppointmentId { get; private set; }
    public void LinkAppointment(Guid appointmentId) => AppointmentId = appointmentId;
}
