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
    Task<bool> CanManageConfigurationAsync(Guid userId, Guid businessId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MyBusinessDto>> GetMembershipsAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AppointmentRecord>> GetAppointmentsAsync(Guid businessId, DateOnly? date,
        AppointmentStatus? status, CancellationToken cancellationToken);
    Task<AppointmentRecord?> GetAppointmentAsync(Guid businessId, Guid appointmentId, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ServiceDto>> GetServicesAsync(Guid businessId, DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
    Task<Service?> GetServiceAsync(Guid businessId, Guid serviceId, CancellationToken cancellationToken);
    void AddService(Service service);
    Task<IReadOnlyList<StaffMemberDto>> GetStaffAsync(Guid businessId, CancellationToken cancellationToken);
    Task<StaffMember?> GetStaffMemberAsync(Guid businessId, Guid staffId, CancellationToken cancellationToken);
    Task<bool> SetStaffServicesAsync(Guid businessId, Guid staffId, IReadOnlyCollection<Guid> serviceIds,
        CancellationToken cancellationToken);
    void AddStaffMember(StaffMember staff);
    Task<BusinessHour?> GetBusinessHourAsync(Guid businessId, DayOfWeek day, CancellationToken cancellationToken);
    Task<IReadOnlyList<BusinessHour>> GetBusinessHoursAsync(Guid businessId, CancellationToken cancellationToken);
    void AddBusinessHour(BusinessHour hour);
    void RemoveBusinessHour(BusinessHour hour);
    Task<IReadOnlyList<AvailabilityException>> GetAvailabilityExceptionsAsync(Guid businessId,
        CancellationToken cancellationToken);
    Task<AvailabilityException?> GetAvailabilityExceptionAsync(Guid businessId, Guid exceptionId,
        CancellationToken cancellationToken);
    Task<bool> StaffBelongsToBusinessAsync(Guid businessId, Guid staffId, CancellationToken cancellationToken);
    Task<int> CountFutureAppointmentConflictsAsync(Guid businessId, Guid? staffId, DateOnly date,
        TimeOnly? startsAt, TimeOnly? endsAt, bool conflictsOutsideInterval, CancellationToken cancellationToken);
    void AddAvailabilityException(AvailabilityException exception);
    void RemoveAvailabilityException(AvailabilityException exception);
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
    Task<IReadOnlyList<ServiceDto>> GetServicesAsync(Guid userId, Guid businessId,
        CancellationToken cancellationToken = default);
    Task<ServiceDto> UpdateServiceAsync(Guid userId, Guid businessId, Guid serviceId,
        UpdateServiceRequest request, CancellationToken cancellationToken = default);
    Task<ServiceDto> CreateServiceAsync(Guid userId, Guid businessId, CreateServiceRequest request,
        CancellationToken cancellationToken = default);
    Task DeactivateServiceAsync(Guid userId, Guid businessId, Guid serviceId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StaffMemberDto>> GetStaffAsync(Guid userId, Guid businessId,
        CancellationToken cancellationToken = default);
    Task<StaffMemberDto> CreateStaffAsync(Guid userId, Guid businessId, SaveStaffMemberRequest request,
        CancellationToken cancellationToken = default);
    Task<StaffMemberDto> UpdateStaffAsync(Guid userId, Guid businessId, Guid staffId, SaveStaffMemberRequest request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BusinessHourAdminDto>> GetBusinessHoursAsync(Guid userId, Guid businessId,
        CancellationToken cancellationToken = default);
    Task<ConfigurationImpactDto> SetBusinessHourAsync(Guid userId, Guid businessId, DayOfWeek day, SaveBusinessHourRequest request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AvailabilityExceptionDto>> GetAvailabilityExceptionsAsync(Guid userId, Guid businessId,
        DateOnly? from = null, CancellationToken cancellationToken = default);
    Task<AvailabilityExceptionDto> SaveAvailabilityExceptionAsync(Guid userId, Guid businessId,
        SaveAvailabilityExceptionRequest request, CancellationToken cancellationToken = default);
    Task DeleteAvailabilityExceptionAsync(Guid userId, Guid businessId, Guid exceptionId, long version,
        CancellationToken cancellationToken = default);
}
