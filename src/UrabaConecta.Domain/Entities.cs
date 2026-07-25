namespace UrabaConecta.Domain;

public enum BusinessStatus { Active, Suspended }
public enum MembershipRole { Owner, Worker }
public enum AppointmentStatus { Pending, Confirmed, Rejected, Cancelled, Completed, NoShow }

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
    public BusinessMembership(Guid id, Guid businessId, Guid userId, MembershipRole role)
        => (Id, BusinessId, UserId, Role) = (id, businessId, userId, role);
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid UserId { get; private set; }
    public MembershipRole Role { get; private set; }
    public bool IsActive { get; private set; } = true;
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
    public void Update(TimeOnly opensAt, TimeOnly closesAt)
    {
        if (closesAt <= opensAt) throw new DomainException("INVALID_HOURS", "La hora de cierre debe ser posterior.");
        OpensAt = opensAt; ClosesAt = closesAt;
    }
}

public sealed class Service : IBusinessOwned
{
    private Service() { }
    public Service(Guid id, Guid businessId, string name, int durationMinutes, decimal referencePrice)
    {
        if (durationMinutes is < 5 or > 480) throw new DomainException("INVALID_DURATION", "Duración inválida.");
        if (referencePrice < 0) throw new DomainException("INVALID_PRICE", "Precio inválido.");
        (Id, BusinessId, Name, DurationMinutes, ReferencePrice) =
            (id, businessId, name, durationMinutes, referencePrice);
    }
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public string Name { get; private set; } = "";
    public int DurationMinutes { get; private set; }
    public decimal ReferencePrice { get; private set; }
    public bool IsActive { get; private set; } = true;
    public void Update(string name, int durationMinutes, decimal referencePrice, bool active)
    {
        if (string.IsNullOrWhiteSpace(name) || durationMinutes is < 5 or > 480 || referencePrice < 0)
            throw new DomainException("INVALID_SERVICE", "Los datos del servicio no son válidos.");
        Name = name.Trim(); DurationMinutes = durationMinutes; ReferencePrice = referencePrice; IsActive = active;
    }
}

public sealed class StaffMember : IBusinessOwned
{
    private StaffMember() { }
    public StaffMember(Guid id, Guid businessId, string displayName)
        => (Id, BusinessId, DisplayName) = (id, businessId, displayName);
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public string DisplayName { get; private set; } = "";
    public bool IsActive { get; private set; } = true;
    public void Update(string displayName, bool active)
    {
        if (string.IsNullOrWhiteSpace(displayName)) throw new DomainException("INVALID_STAFF", "El nombre es obligatorio.");
        DisplayName = displayName.Trim(); IsActive = active;
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
    public AvailabilityException(Guid id, Guid businessId, Guid staffMemberId, DateOnly date, bool unavailable,
        TimeOnly? opensAt = null, TimeOnly? closesAt = null)
        => (Id, BusinessId, StaffMemberId, Date, IsUnavailable, OpensAt, ClosesAt) =
            (id, businessId, staffMemberId, date, unavailable, opensAt, closesAt);
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid StaffMemberId { get; private set; }
    public DateOnly Date { get; private set; }
    public bool IsUnavailable { get; private set; }
    public TimeOnly? OpensAt { get; private set; }
    public TimeOnly? ClosesAt { get; private set; }
    public void Update(bool unavailable, TimeOnly? opensAt, TimeOnly? closesAt)
    {
        if (!unavailable && (opensAt is null || closesAt is null || closesAt <= opensAt))
            throw new DomainException("INVALID_EXCEPTION", "El horario alternativo no es válido.");
        IsUnavailable = unavailable; OpensAt = opensAt; ClosesAt = closesAt;
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
