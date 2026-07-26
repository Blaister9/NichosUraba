using System.ComponentModel.DataAnnotations;

namespace UrabaConecta.Contracts;

public sealed record OptionDto(string Slug, string Name);
public sealed record BusinessCardDto(string Slug, string Name, OptionDto Category, OptionDto Municipality,
    string Description, string Address, bool HasVirtualQueue = false, bool HasPickupOrdering = false);
public sealed record BusinessHourDto(DayOfWeek Day, string OpensAt, string ClosesAt);
public sealed record ServiceDto(Guid Id, string Name, string Description, int DurationMinutes, decimal ReferencePrice,
    int DisplayOrder, bool IsActive, int FutureAppointmentCount = 0, long Version = 0);
public sealed record BusinessProfileDto(string Slug, string Name, string Description, string Address, string PublicPhone,
    OptionDto Category, OptionDto Municipality, IReadOnlyList<BusinessHourDto> Hours, IReadOnlyList<ServiceDto> Services,
    bool HasVirtualQueue = false, bool HasPickupOrdering = false);
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

public sealed record MyBusinessDto(Guid Id, string Name, string Slug, string MembershipRole,
    bool CanManageConfiguration = false, bool CanManageAppointments = true, bool CanManageMembers = false,
    bool CanManageQueues = false, bool CanManageOrders = false, bool SupportsPickupOrdering = false);
public sealed record MembershipPermissionsDto(bool CanManageAppointments, bool CanManageConfiguration,
    bool CanManageMembers, bool CanManageQueues = false, bool CanManageOrders = false);
public sealed record BusinessMemberDto(Guid Id, string DisplayName, string Email, bool IsActive, bool IsOwner,
    MembershipPermissionsDto Permissions, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, long Version);
public sealed record BusinessMemberListDto(IReadOnlyList<BusinessMemberDto> Items,
    bool DevelopmentAccountCreationEnabled, Guid CurrentMembershipId);
public class LinkExistingMemberRequest
{
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    public bool CanManageAppointments { get; set; }
    public bool CanManageConfiguration { get; set; }
    public bool CanManageMembers { get; set; }
    public bool CanManageQueues { get; set; }
    public bool CanManageOrders { get; set; }
}
public sealed class CreateDevelopmentMemberRequest : LinkExistingMemberRequest
{
    [Required, StringLength(100, MinimumLength = 2)] public string DisplayName { get; set; } = "";
}
public sealed record DevelopmentMemberCreatedDto(BusinessMemberDto Member, string TemporaryPassword);
public class UpdateMemberPermissionsRequest
{
    public bool CanManageAppointments { get; set; }
    public bool CanManageConfiguration { get; set; }
    public bool CanManageMembers { get; set; }
    public bool CanManageQueues { get; set; }
    public bool CanManageOrders { get; set; }
    public long Version { get; set; }
}
public sealed class MembershipVersionRequest
{
    public long Version { get; set; }
}
public sealed class RevokeOwnershipRequest : UpdateMemberPermissionsRequest;
public sealed record MembershipAuditDto(Guid Id, string Action, Guid ActorUserId,
    DateTimeOffset OccurredAtUtc, string PreviousState, string NewState);

public sealed record QueueDefinitionDto(Guid Id, Guid BusinessId, string BusinessName, string BusinessSlug,
    string Name, int AverageDurationMinutes, int MaximumWaiting, string PublicMessage,
    bool IsEnabled, long Version);
public sealed record QueuePublicStatusDto(string BusinessName, string BusinessSlug, string QueueName,
    string PublicMessage, bool IsEnabled, string SessionStatus, int? CurrentNumber,
    int WaitingCount, int ApproximateWaitMinutes, bool CanJoin, long Version);
public sealed record QueueTicketCreatedDto(int Number, string TrackingCode, string Status,
    int PeopleAhead, int ApproximateWaitMinutes);
public sealed record QueueTicketTrackingDto(int Number, string Status, string StatusLabel,
    string BusinessName, string QueueName, int PeopleAhead, int ApproximateWaitMinutes,
    bool CanCancel, DateTimeOffset UpdatedAtUtc, long Version);
public sealed record QueueTicketAdminDto(Guid Id, int Number, string? Alias, string Source, string Status,
    int CallCount, int RestoreCount, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, long Version);
public sealed record QueueAdminDto(QueueDefinitionDto Definition, string SessionStatus, Guid? SessionId,
    long? SessionVersion, int? CurrentNumber, int WaitingCount, int NextNumber,
    IReadOnlyList<QueueTicketAdminDto> Tickets);
public sealed class SaveQueueDefinitionRequest
{
    [Required, StringLength(80, MinimumLength = 1)] public string Name { get; set; } = "";
    [Range(1, 480)] public int AverageDurationMinutes { get; set; }
    [Range(1, 500)] public int MaximumWaiting { get; set; }
    [StringLength(160)] public string? PublicMessage { get; set; }
    public bool IsEnabled { get; set; }
    public long Version { get; set; }
}
public sealed class QueueSessionCommandRequest { public long Version { get; set; } }
public sealed class CreateQueueTicketRequest { [StringLength(40)] public string? Alias { get; set; } }
public sealed class QueueTicketCommandRequest
{
    public long TicketVersion { get; set; }
    public long SessionVersion { get; set; }
}
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
    [StringLength(500)] public string? Description { get; set; }
    [Range(5, 480)] public int DurationMinutes { get; set; }
    [Range(0, 100000000)] public decimal ReferencePrice { get; set; }
    [Range(0, 10000)] public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
    public long Version { get; set; }
}
public sealed class CreateServiceRequest
{
    [Required, StringLength(120, MinimumLength = 2)] public string Name { get; set; } = "";
    [StringLength(500)] public string? Description { get; set; }
    [Range(5, 480)] public int DurationMinutes { get; set; }
    [Range(0, 100000000)] public decimal ReferencePrice { get; set; }
    [Range(0, 10000)] public int DisplayOrder { get; set; }
}
public sealed record StaffMemberDto(Guid Id, string DisplayName, bool IsActive, bool ParticipatesInAvailability,
    IReadOnlyList<Guid> ServiceIds, long Version = 0);
public sealed class SaveStaffMemberRequest
{
    [Required, StringLength(100, MinimumLength = 2)] public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool ParticipatesInAvailability { get; set; } = true;
    [MinLength(1)] public List<Guid> ServiceIds { get; set; } = [];
    public long Version { get; set; }
}
public sealed record BusinessHourAdminDto(DayOfWeek Day, bool IsClosed, TimeOnly? OpensAt, TimeOnly? ClosesAt,
    long Version = 0);
public sealed class SaveBusinessHourRequest
{
    public bool IsClosed { get; set; }
    public TimeOnly? OpensAt { get; set; }
    public TimeOnly? ClosesAt { get; set; }
    public long Version { get; set; }
}
public sealed record ConfigurationImpactDto(int FutureAppointmentConflicts);
public sealed record AvailabilityExceptionDto(Guid Id, Guid StaffMemberId, DateOnly Date, string Type,
    TimeOnly? OpensAt, TimeOnly? ClosesAt, string Reason, int FutureAppointmentConflicts = 0, long Version = 0);
public sealed class SaveAvailabilityExceptionRequest
{
    [Required] public Guid StaffMemberId { get; set; }
    [Required] public DateOnly Date { get; set; }
    [Required] public string Type { get; set; } = "ClosedAllDay";
    public TimeOnly? OpensAt { get; set; }
    public TimeOnly? ClosesAt { get; set; }
    [StringLength(160)] public string? Reason { get; set; }
    public long Version { get; set; }
}

public sealed record ProductCategoryDto(Guid Id, string Name, int DisplayOrder, bool IsActive, long Version);
public sealed record ProductDto(Guid Id, Guid CategoryId, string Name, string Description,
    decimal ReferencePrice, int DisplayOrder, bool IsActive, long Version);
public sealed record PickupMenuDto(string BusinessName, string BusinessSlug, string PublicMessage,
    IReadOnlyList<ProductCategoryDto> Categories, IReadOnlyList<ProductDto> Products);
public sealed record PickupSlotDto(DateTimeOffset Start, DateTimeOffset End, int RemainingCapacity);
public sealed record PickupSlotListDto(string BusinessTimeZone, IReadOnlyList<PickupSlotDto> Slots);
public sealed record PickupOrderSettingsDto(Guid Id, Guid BusinessId, bool IsEnabled, string PublicMessage,
    int MinimumPreparationMinutes, int SlotIntervalMinutes, int MaximumActivePerSlot,
    TimeOnly ReceivesFrom, TimeOnly ReceivesUntil, int NextOrderNumber, long Version);
public sealed class SavePickupOrderSettingsRequest
{
    public bool IsEnabled { get; set; }
    [StringLength(200)] public string? PublicMessage { get; set; }
    [Range(0, 1440)] public int MinimumPreparationMinutes { get; set; }
    [Range(5, 240)] public int SlotIntervalMinutes { get; set; }
    [Range(1, 500)] public int MaximumActivePerSlot { get; set; }
    public TimeOnly ReceivesFrom { get; set; }
    public TimeOnly ReceivesUntil { get; set; }
    public long Version { get; set; }
}
public sealed class SaveProductCategoryRequest
{
    [Required, StringLength(100, MinimumLength = 1)] public string Name { get; set; } = "";
    [Range(0, 10000)] public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public long Version { get; set; }
}
public sealed class SaveProductRequest
{
    [Required] public Guid CategoryId { get; set; }
    [Required, StringLength(120, MinimumLength = 1)] public string Name { get; set; } = "";
    [StringLength(500)] public string? Description { get; set; }
    [Range(0, 100000000)] public decimal ReferencePrice { get; set; }
    [Range(0, 10000)] public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public long Version { get; set; }
}
public sealed class CreatePickupOrderLineRequest
{
    [Required] public Guid ProductId { get; set; }
    [Range(1, 20)] public int Quantity { get; set; }
    [StringLength(160)] public string? Notes { get; set; }
}
public sealed class CreatePickupOrderRequest
{
    [Required] public DateTimeOffset PickupStart { get; set; }
    [Required, StringLength(100, MinimumLength = 2)] public string CustomerAlias { get; set; } = "";
    [Required, RegularExpression(@"^\+?[0-9]{7,15}$")] public string Phone { get; set; } = "";
    [StringLength(300)] public string? Notes { get; set; }
    public List<CreatePickupOrderLineRequest> Lines { get; set; } = [];
    [Required] public string ConsentNoticeVersion { get; set; } = "pilot-1";
    [Range(typeof(bool), "true", "true")] public bool ConsentAccepted { get; set; }
}
public sealed record PickupOrderLineDto(Guid? ProductId, string ProductName, decimal UnitPrice,
    int Quantity, decimal LineTotal, string? Notes);
public sealed record PickupOrderCreatedDto(int OrderNumber, string TrackingCode, string Status,
    decimal Total, DateTimeOffset PickupStart);
public sealed record PickupOrderTrackingDto(int OrderNumber, string Status, string StatusLabel,
    string BusinessName, DateTimeOffset PickupStart, decimal Total, string PhoneMasked,
    IReadOnlyList<PickupOrderLineDto> Lines, bool CanCancel, DateTimeOffset UpdatedAtUtc, long Version);
public sealed record PickupOrderAdminDto(Guid Id, int OrderNumber, string Status, string CustomerAlias,
    string Phone, string? Notes, DateTimeOffset PickupStart, decimal Total,
    IReadOnlyList<PickupOrderLineDto> Lines, string? CancellationReason,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, long Version);
public sealed class PickupOrderCommandRequest
{
    public long Version { get; set; }
    [StringLength(160)] public string? Reason { get; set; }
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
    Task<IReadOnlyList<ServiceDto>> GetServicesAsync(Guid businessId, CancellationToken cancellationToken = default);
    Task<ServiceDto> CreateServiceAsync(Guid businessId, CreateServiceRequest request,
        CancellationToken cancellationToken = default);
    Task<ServiceDto> UpdateServiceAsync(Guid businessId, Guid serviceId, UpdateServiceRequest request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StaffMemberDto>> GetStaffAsync(Guid businessId, CancellationToken cancellationToken = default);
    Task<StaffMemberDto> CreateStaffAsync(Guid businessId, SaveStaffMemberRequest request,
        CancellationToken cancellationToken = default);
    Task<StaffMemberDto> UpdateStaffAsync(Guid businessId, Guid staffId, SaveStaffMemberRequest request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BusinessHourAdminDto>> GetBusinessHoursAsync(Guid businessId,
        CancellationToken cancellationToken = default);
    Task<ConfigurationImpactDto> SetBusinessHourAsync(Guid businessId, DayOfWeek day,
        SaveBusinessHourRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AvailabilityExceptionDto>> GetAvailabilityExceptionsAsync(Guid businessId,
        DateOnly? from = null, CancellationToken cancellationToken = default);
    Task<AvailabilityExceptionDto> SaveAvailabilityExceptionAsync(Guid businessId,
        SaveAvailabilityExceptionRequest request, CancellationToken cancellationToken = default);
    Task DeleteAvailabilityExceptionAsync(Guid businessId, Guid exceptionId, long version,
        CancellationToken cancellationToken = default);
    Task<BusinessMemberListDto> ListMembersAsync(Guid businessId, CancellationToken cancellationToken = default);
    Task<BusinessMemberDto> GetMemberAsync(Guid businessId, Guid membershipId,
        CancellationToken cancellationToken = default);
    Task<BusinessMemberDto> LinkExistingMemberAsync(Guid businessId, LinkExistingMemberRequest request,
        CancellationToken cancellationToken = default);
    Task<DevelopmentMemberCreatedDto> CreateDevelopmentMemberAsync(Guid businessId,
        CreateDevelopmentMemberRequest request, CancellationToken cancellationToken = default);
    Task<BusinessMemberDto> UpdateMemberPermissionsAsync(Guid businessId, Guid membershipId,
        UpdateMemberPermissionsRequest request, CancellationToken cancellationToken = default);
    Task<BusinessMemberDto> ActivateMemberAsync(Guid businessId, Guid membershipId, long version,
        CancellationToken cancellationToken = default);
    Task<BusinessMemberDto> DeactivateMemberAsync(Guid businessId, Guid membershipId, long version,
        CancellationToken cancellationToken = default);
    Task<BusinessMemberDto> GrantOwnershipAsync(Guid businessId, Guid membershipId, long version,
        CancellationToken cancellationToken = default);
    Task<BusinessMemberDto> RevokeOwnershipAsync(Guid businessId, Guid membershipId,
        RevokeOwnershipRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MembershipAuditDto>> ListMembershipAuditAsync(Guid businessId, Guid membershipId,
        CancellationToken cancellationToken = default);
    Task<QueuePublicStatusDto?> GetPublicQueueAsync(string slug, CancellationToken cancellationToken = default);
    Task<QueueTicketCreatedDto> JoinQueueAsync(string slug, CreateQueueTicketRequest request,
        CancellationToken cancellationToken = default);
    Task<QueueTicketTrackingDto?> GetQueueTicketAsync(string code, CancellationToken cancellationToken = default);
    Task CancelQueueTicketAsync(string code, long version, CancellationToken cancellationToken = default);
    Task<QueueAdminDto> GetQueueAdminAsync(Guid businessId, CancellationToken cancellationToken = default);
    Task<QueueDefinitionDto> SaveQueueDefinitionAsync(Guid businessId, SaveQueueDefinitionRequest request,
        CancellationToken cancellationToken = default);
    Task<QueueAdminDto> OpenQueueAsync(Guid businessId, CancellationToken cancellationToken = default);
    Task<QueueAdminDto> PauseQueueAsync(Guid businessId, long version, CancellationToken cancellationToken = default);
    Task<QueueAdminDto> ResumeQueueAsync(Guid businessId, long version, CancellationToken cancellationToken = default);
    Task<QueueAdminDto> CloseQueueAsync(Guid businessId, long version, CancellationToken cancellationToken = default);
    Task<QueueTicketCreatedDto> AddWalkInAsync(Guid businessId, CreateQueueTicketRequest request,
        CancellationToken cancellationToken = default);
    Task<QueueAdminDto> CallNextAsync(Guid businessId, long sessionVersion, CancellationToken cancellationToken = default);
    Task<QueueAdminDto> ChangeQueueTicketAsync(Guid businessId, Guid ticketId, string action,
        QueueTicketCommandRequest request, CancellationToken cancellationToken = default);
    Task<PickupMenuDto?> GetPickupMenuAsync(string slug, CancellationToken cancellationToken = default);
    Task<PickupSlotListDto> GetPickupSlotsAsync(string slug, DateOnly? date = null,
        CancellationToken cancellationToken = default);
    Task<PickupOrderCreatedDto> CreatePickupOrderAsync(string slug, CreatePickupOrderRequest request,
        CancellationToken cancellationToken = default);
    Task<PickupOrderTrackingDto?> GetPickupOrderAsync(string code, CancellationToken cancellationToken = default);
    Task CancelPickupOrderAsync(string code, long version, CancellationToken cancellationToken = default);
    Task<PickupOrderSettingsDto> GetPickupOrderSettingsAsync(Guid businessId, CancellationToken cancellationToken = default);
    Task<PickupOrderSettingsDto> SavePickupOrderSettingsAsync(Guid businessId, SavePickupOrderSettingsRequest request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductCategoryDto>> GetProductCategoriesAsync(Guid businessId, CancellationToken cancellationToken = default);
    Task<ProductCategoryDto> SaveProductCategoryAsync(Guid businessId, Guid? categoryId,
        SaveProductCategoryRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductDto>> GetProductsAsync(Guid businessId, CancellationToken cancellationToken = default);
    Task<ProductDto> SaveProductAsync(Guid businessId, Guid? productId, SaveProductRequest request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PickupOrderAdminDto>> GetPickupOrdersAsync(Guid businessId, string? status = null,
        DateOnly? date = null, CancellationToken cancellationToken = default);
    Task<PickupOrderAdminDto> ChangePickupOrderAsync(Guid businessId, Guid orderId, string action,
        PickupOrderCommandRequest request, CancellationToken cancellationToken = default);
}
