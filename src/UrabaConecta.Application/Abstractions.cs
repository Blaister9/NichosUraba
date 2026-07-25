using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Application;

public sealed record SchedulingContext(Business Business, Service Service, IReadOnlyList<BusinessHour> Hours,
    IReadOnlyList<StaffMember> EligibleStaff, IReadOnlyList<AvailabilityException> Exceptions,
    IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End, Guid StaffId)> Occupied);

public sealed record AppointmentRecord(Appointment Appointment, Business Business, ConsentReceipt Consent);

public interface IUrabaStore
{
    Task<IReadOnlyList<BusinessCardDto>> FindBusinessesAsync(string? search, string? municipality, string? category,
        CancellationToken cancellationToken);
    Task<BusinessProfileDto?> GetBusinessProfileAsync(string slug, CancellationToken cancellationToken);
    Task<SchedulingContext?> GetSchedulingContextAsync(string slug, Guid serviceId, DateOnly date,
        CancellationToken cancellationToken);
    Task<bool> AddAppointmentAsync(Appointment appointment, ConsentReceipt consent, CancellationToken cancellationToken);
    Task<AppointmentRecord?> FindAppointmentByCodeHashAsync(string codeHash, CancellationToken cancellationToken);
    Task<bool> IsMemberAsync(Guid userId, Guid businessId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MyBusinessDto>> GetMembershipsAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AppointmentRecord>> GetAppointmentsAsync(Guid businessId, DateOnly? date,
        AppointmentStatus? status, CancellationToken cancellationToken);
    Task<AppointmentRecord?> GetAppointmentAsync(Guid businessId, Guid appointmentId, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task<Service?> GetServiceAsync(Guid businessId, Guid serviceId, CancellationToken cancellationToken);
    void AddService(Service service);
}

public interface IPublicCodeService
{
    (string PlainText, string Hash, int Version) Generate();
    string Hash(string plainText);
}

public interface IPersonalDataProtector
{
    string Protect(string value);
    string Unprotect(string value);
}

public interface IUrabaUseCases
{
    Task<IReadOnlyList<BusinessCardDto>> GetBusinessesAsync(string? search, string? municipality, string? category,
        CancellationToken cancellationToken = default);
    Task<BusinessProfileDto?> GetBusinessAsync(string slug, CancellationToken cancellationToken = default);
    Task<SlotListDto> GetSlotsAsync(string slug, Guid serviceId, DateOnly date, CancellationToken cancellationToken = default);
    Task<AppointmentCreatedDto> CreateAppointmentAsync(string slug, CreateAppointmentRequest request,
        CancellationToken cancellationToken = default);
    Task<AppointmentTrackingDto?> GetTrackingAsync(string code, CancellationToken cancellationToken = default);
    Task CancelAsync(string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MyBusinessDto>> GetMyBusinessesAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AppointmentAdminDto>> GetAppointmentsAsync(Guid userId, Guid businessId, DateOnly? date,
        string? status, CancellationToken cancellationToken = default);
    Task<AppointmentAdminDto> ChangeStatusAsync(Guid userId, Guid businessId, Guid appointmentId,
        ChangeAppointmentStatusRequest request, CancellationToken cancellationToken = default);
    Task<ServiceDto> UpdateServiceAsync(Guid userId, Guid businessId, Guid serviceId,
        UpdateServiceRequest request, CancellationToken cancellationToken = default);
    Task<ServiceDto> CreateServiceAsync(Guid userId, Guid businessId, CreateServiceRequest request,
        CancellationToken cancellationToken = default);
    Task DeactivateServiceAsync(Guid userId, Guid businessId, Guid serviceId,
        CancellationToken cancellationToken = default);
}
