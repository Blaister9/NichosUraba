using System.ComponentModel.DataAnnotations;

namespace UrabaConecta.Contracts;

public sealed record OptionDto(string Slug, string Name);
public sealed record BusinessCardDto(string Slug, string Name, OptionDto Category, OptionDto Municipality,
    string Description, string Address);
public sealed record BusinessHourDto(DayOfWeek Day, string OpensAt, string ClosesAt);
public sealed record ServiceDto(Guid Id, string Name, int DurationMinutes, decimal ReferencePrice, bool IsActive = true);
public sealed record BusinessProfileDto(string Slug, string Name, string Description, string Address, string PublicPhone,
    OptionDto Category, OptionDto Municipality, IReadOnlyList<BusinessHourDto> Hours, IReadOnlyList<ServiceDto> Services);
public sealed record SlotDto(DateTimeOffset Start, DateTimeOffset End);
public sealed record SlotListDto(string BusinessTimeZone, DateOnly Date, IReadOnlyList<SlotDto> Slots);

public sealed class CreateAppointmentRequest
{
    [Required] public Guid ServiceId { get; set; }
    [Required] public DateTimeOffset Start { get; set; }
    [Required, StringLength(100, MinimumLength = 2)] public string CustomerAlias { get; set; } = "";
    [Required, RegularExpression(@"^\+?[0-9]{7,15}$")] public string Phone { get; set; } = "";
    [StringLength(300)] public string? Notes { get; set; }
    [Required] public string ConsentNoticeVersion { get; set; } = "pilot-1";
    [Range(typeof(bool), "true", "true", ErrorMessage = "Debe aceptar el aviso.")] public bool ConsentAccepted { get; set; }
}

public sealed record AppointmentCreatedDto(string TrackingCode, string Status, string ServiceName, DateTimeOffset Start);
public sealed record AppointmentTrackingDto(string Status, string StatusLabel, string BusinessName, string ServiceName,
    DateTimeOffset Start, string PhoneMasked, bool CanCancel, DateTimeOffset UpdatedAt);

public sealed record MyBusinessDto(Guid Id, string Name, string Slug, string MembershipRole);
public sealed record AppointmentAdminDto(Guid Id, Guid BusinessId, string ServiceName, DateTimeOffset Start,
    DateTimeOffset End, string CustomerAlias, string Phone, string Notes, string Status,
    DateTimeOffset CreatedAt, string ConsentNoticeVersion, DateTimeOffset ConsentAcceptedAt, uint Version);
public sealed class ChangeAppointmentStatusRequest
{
    [Required] public string TargetStatus { get; set; } = "";
    [StringLength(160)] public string? Reason { get; set; }
}
public sealed class UpdateServiceRequest
{
    [Required, StringLength(120, MinimumLength = 2)] public string Name { get; set; } = "";
    [Range(5, 480)] public int DurationMinutes { get; set; }
    [Range(0, 100000000)] public decimal ReferencePrice { get; set; }
    public bool IsActive { get; set; }
}
public sealed class CreateServiceRequest
{
    [Required, StringLength(120, MinimumLength = 2)] public string Name { get; set; } = "";
    [Range(5, 480)] public int DurationMinutes { get; set; }
    [Range(0, 100000000)] public decimal ReferencePrice { get; set; }
}
public sealed record StaffMemberDto(Guid Id, string DisplayName, bool IsActive, IReadOnlyList<Guid> ServiceIds);
public sealed class SaveStaffMemberRequest
{
    [Required, StringLength(100, MinimumLength = 2)] public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    [MinLength(1)] public List<Guid> ServiceIds { get; set; } = [];
}
public sealed class SaveBusinessHourRequest
{
    public bool IsClosed { get; set; }
    [Required] public TimeOnly OpensAt { get; set; }
    [Required] public TimeOnly ClosesAt { get; set; }
}
public sealed record AvailabilityExceptionDto(Guid Id, Guid StaffMemberId, DateOnly Date, bool IsUnavailable,
    TimeOnly? OpensAt, TimeOnly? ClosesAt);
public sealed class SaveAvailabilityExceptionRequest
{
    [Required] public Guid StaffMemberId { get; set; }
    [Required] public DateOnly Date { get; set; }
    public bool IsUnavailable { get; set; }
    public TimeOnly? OpensAt { get; set; }
    public TimeOnly? ClosesAt { get; set; }
}

public sealed class ApiException(string code, string message, int statusCode = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public interface IUrabaConectaApi
{
    Task<IReadOnlyList<BusinessCardDto>> GetBusinessesAsync(string? search = null, string? municipality = null,
        string? category = null, CancellationToken cancellationToken = default);
    Task<BusinessProfileDto?> GetBusinessAsync(string slug, CancellationToken cancellationToken = default);
    Task<SlotListDto> GetSlotsAsync(string slug, Guid serviceId, DateOnly date, CancellationToken cancellationToken = default);
    Task<AppointmentCreatedDto> CreateAppointmentAsync(string slug, CreateAppointmentRequest request,
        CancellationToken cancellationToken = default);
    Task<AppointmentTrackingDto?> GetAppointmentTrackingAsync(string code, CancellationToken cancellationToken = default);
    Task CancelAppointmentAsync(string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MyBusinessDto>> GetMyBusinessesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AppointmentAdminDto>> GetAppointmentsAsync(Guid businessId, DateOnly? date = null,
        string? status = null, CancellationToken cancellationToken = default);
    Task<AppointmentAdminDto> ChangeAppointmentStatusAsync(Guid businessId, Guid appointmentId,
        ChangeAppointmentStatusRequest request, CancellationToken cancellationToken = default);
    Task<ServiceDto> UpdateServiceAsync(Guid businessId, Guid serviceId, UpdateServiceRequest request,
        CancellationToken cancellationToken = default);
}
