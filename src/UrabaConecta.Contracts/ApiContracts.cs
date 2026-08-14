using System.ComponentModel.DataAnnotations;

namespace UrabaConecta.Contracts;

public sealed record OptionDto(string Slug, string Name);

public sealed record PushConfigurationDto(bool Enabled, string? PublicKey);
public sealed class WebPushKeysRequest
{
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
}
public sealed class WebPushSubscriptionRequest
{
    public string Endpoint { get; set; } = "";
    public WebPushKeysRequest Keys { get; set; } = new();
}
public sealed class WebPushUnsubscribeRequest { public string Endpoint { get; set; } = ""; }
public sealed record WebPushSubscriptionDto(bool Active, DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Una categoría del directorio con cuántos negocios publicados tiene. El conteo es lo que decide
/// si vale la pena ofrecerla: una categoría vacía enviada a una pantalla sin resultados es peor que
/// no mostrarla.
/// </summary>
public sealed record CategoryCardDto(string Slug, string Name, int BusinessCount);

/// <param name="PriceFrom">
/// Precio del servicio activo más barato. Nulo cuando el negocio no agenda o no tiene servicios con
/// precio: un "desde $0" diría algo distinto de lo que ocurre.
/// </param>
/// <param name="QueueIsOpen">
/// Si la fila está recibiendo gente ahora. Es una señal de estado, no un conteo: el directorio se
/// cachea un minuto y "3 esperando" envejece mal, mientras que "fila abierta" sigue siendo cierto.
/// </param>
public sealed record BusinessCardDto(string Slug, string Name, OptionDto Category, OptionDto Municipality,
    string Description, string Address, bool HasVirtualQueue = false, bool HasPickupOrdering = false,
    bool HasScheduling = false, string? LogoUrl = null, string? CoverUrl = null,
    string? LogoAltText = null, string? CoverAltText = null, string ShortDescription = "",
    decimal? PriceFrom = null, bool QueueIsOpen = false, string? OpenStatus = null);
public sealed record BusinessHourDto(DayOfWeek Day, string OpensAt, string ClosesAt);
/// <summary>
/// <paramref name="DepositAmount"/> llega ya calculado por el servidor: el cliente nunca repite la
/// aritmética del adelanto ni el redondeo a pesos.
/// </summary>
public sealed record ServiceDto(Guid Id, string Name, string Description, int DurationMinutes, decimal ReferencePrice,
    int DisplayOrder, bool IsActive, int FutureAppointmentCount = 0, long Version = 0,
    bool RequiresDeposit = false, string DepositType = "None", decimal DepositValue = 0,
    decimal DepositAmount = 0, string DepositInstructions = "", string DepositWhatsAppNumber = "",
    string? ImageUrl = null, string? ImageAltText = null);
public sealed record BusinessProfileDto(string Slug, string Name, string Description, string Address, string PublicPhone,
    OptionDto Category, OptionDto Municipality, IReadOnlyList<BusinessHourDto> Hours, IReadOnlyList<ServiceDto> Services,
    bool HasVirtualQueue = false, bool HasPickupOrdering = false,
    string ShortDescription = "", string? ReferencePoint = null, string? WhatsAppUrl = null,
    string? PublicEmail = null, string? InstagramUrl = null, string? FacebookUrl = null,
    string? LocationUrl = null, string? CustomerInstructions = null,
    IReadOnlyList<BusinessImageDto>? Images = null, string? OpenStatus = null, bool IsPreview = false,
    /// <summary>
    /// Un adelanto del catálogo de pedidos, no la carta entera. Para un negocio de productos la
    /// ficha es el escaparate: mandarlo a otra pantalla para ver el primer artículo es pedirle un
    /// clic antes de haber enseñado nada.
    /// </summary>
    IReadOnlyList<ProductDto>? Products = null);
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

public sealed record AppointmentCreatedDto(string TrackingCode, string Status, string ServiceName, DateTimeOffset Start,
    string DepositStatus = "NotRequired", decimal DepositAmount = 0);
/// <summary>
/// Estado que ve el cliente con su código. <paramref name="DepositWhatsAppUrl"/> ya viene armado y
/// codificado: el enlace es la única forma en que UrabáConecta participa del adelanto.
/// </summary>
public sealed record AppointmentTrackingDto(string Status, string StatusLabel, string BusinessName, string ServiceName,
    DateTimeOffset Start, string PhoneMasked, bool CanCancel, DateTimeOffset UpdatedAt,
    decimal ServicePrice = 0, bool RequiresDeposit = false, string DepositType = "None",
    decimal DepositAmount = 0, string DepositStatus = "NotRequired", string DepositStatusLabel = "",
    string DepositInstructions = "", string? DepositWhatsAppUrl = null, bool CanReportDeposit = false,
    string? DepositRejectionReason = null);

/// <summary>
/// Un permiso no basta para mostrar una acción: el módulo también tiene que estar habilitado en
/// ese negocio. Sin las banderas de módulo, una propietaria veía las tres operaciones aunque su
/// establecimiento sólo tuviera una activa.
/// </summary>
public sealed record MyBusinessDto(Guid Id, string Name, string Slug, string MembershipRole,
    bool CanManageConfiguration = false, bool CanManageAppointments = true, bool CanManageMembers = false,
    bool CanManageQueues = false, bool CanManageOrders = false, bool SupportsPickupOrdering = false,
    string BusinessStatus = "Active",
    bool HasAppointments = false, bool HasVirtualQueues = false, bool HasPickupOrders = false)
{
    /// <summary>Perfil e imágenes son del propietario; un trabajador no las administra.</summary>
    public bool IsOwner => string.Equals(MembershipRole, "Owner", StringComparison.OrdinalIgnoreCase);

    /// <summary>Mostrar acción = módulo habilitado en el negocio Y permiso del usuario.</summary>
    public bool ShowAppointments => HasAppointments && CanManageAppointments;
    public bool ShowQueues => HasVirtualQueues && CanManageQueues;
    public bool ShowOrders => HasPickupOrders && CanManageOrders;
}
// ---------------------------------------------------------------------------
// Resumen operativo del propietario
// ---------------------------------------------------------------------------

/// <summary>
/// Lo que pasa hoy en un negocio. Los submódulos son nulos cuando la función no está habilitada:
/// un cero y un "no aplica" no significan lo mismo, y mandar ceros de un módulo apagado obligaría a
/// la pantalla a adivinar cuál de los dos está viendo.
/// </summary>
/// <param name="TimeZoneId">
/// La zona con la que se calculó el día de este negocio, ya resuelto el respaldo. Viaja con el
/// resumen porque las horas se miden en UTC pero se leen en la hora del local: sin ella la pantalla
/// tendría que adivinar, y "próxima cita 8:00 p. m." en vez de 3:00 p. m. es peor que no decir nada.
/// </param>
public sealed record OwnerDashboardSummaryDto(Guid BusinessId, string BusinessName,
    AppointmentsSummaryDto? Appointments, QueueSummaryDto? Queues, OrdersSummaryDto? Orders,
    string TimeZoneId = "America/Bogota");

public sealed record AppointmentsSummaryDto(int TodayTotal, int Pending, int Confirmed, int Completed,
    DateTimeOffset? NextAppointmentAtUtc, string? NextAppointmentServiceName);

public sealed record QueueSummaryDto(int Waiting, int InService, int ServedToday, int? CurrentTicketNumber);

public sealed record OrdersSummaryDto(int Pending, int Preparing, int Ready, int DeliveredToday);

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
    bool IsEnabled, long Version, string TimeZoneId = "America/Bogota");
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
public sealed class CreateQueueTicketRequest
{
    [StringLength(40)] public string? Alias { get; set; }
    [Required] public string ConsentNoticeVersion { get; set; } = "";
    public bool ConsentAccepted { get; set; }
}
public sealed class QueueTicketCommandRequest
{
    public long TicketVersion { get; set; }
    public long SessionVersion { get; set; }
}
public sealed record AppointmentAdminDto(Guid Id, Guid BusinessId, string ServiceName, DateTimeOffset Start,
    DateTimeOffset End, string CustomerAlias, string Phone, string Notes, string Status,
    DateTimeOffset CreatedAt, string ConsentNoticeVersion, DateTimeOffset ConsentAcceptedAt, uint Version,
    decimal ServicePrice = 0, bool RequiresDeposit = false, string DepositType = "None",
    decimal DepositConfiguredValue = 0, decimal DepositAmount = 0, string DepositStatus = "NotRequired",
    string DepositStatusLabel = "", string DepositInstructions = "", string DepositWhatsAppNumber = "",
    DateTimeOffset? DepositReportedAt = null, DateTimeOffset? DepositVerifiedAt = null,
    string? DepositVerifiedBy = null, string? DepositRejectionReason = null);
/// <summary>
/// El listado de citas junto con el negocio al que pertenece. El nombre y la zona viajan una vez por
/// pantalla y no por fila: quien entra directo por dirección tiene que saber qué establecimiento
/// está operando aunque ese día no haya ninguna cita, y con el dato dentro de cada fila una lista
/// vacía no podría decirlo.
/// </summary>
public sealed record AppointmentBoardDto(Guid BusinessId, string BusinessName, string TimeZoneId,
    IReadOnlyList<AppointmentAdminDto> Items);

public sealed record AppointmentDepositAuditDto(Guid Id, Guid AppointmentId, string ActorKind,
    Guid? ActorUserId, string PreviousStatus, string NewStatus, DateTimeOffset OccurredAtUtc, string? Reason);
public sealed class ChangeAppointmentStatusRequest
{
    [Required] public string TargetStatus { get; set; } = "";
    [StringLength(160)] public string? Reason { get; set; }
}
/// <summary>Acciones del negocio sobre el adelanto: verificar, rechazar, reportar o reabrir.</summary>
public sealed class DepositCommandRequest
{
    [StringLength(160)] public string? Reason { get; set; }
}
/// <summary>
/// Configuración del adelanto. Se comparte entre crear y editar servicio para que ambas rutas
/// exijan exactamente lo mismo.
/// </summary>
public abstract class ServiceDepositFields
{
    public bool RequiresDeposit { get; set; }
    /// <summary>None, FixedAmount o Percentage.</summary>
    [StringLength(20)] public string DepositType { get; set; } = "None";
    [Range(0, 100000000)] public decimal DepositValue { get; set; }
    [StringLength(400)] public string? DepositInstructions { get; set; }
    [StringLength(30)] public string? DepositWhatsAppNumber { get; set; }
}
public sealed class UpdateServiceRequest : ServiceDepositFields
{
    [Required, StringLength(120, MinimumLength = 2)] public string Name { get; set; } = "";
    [StringLength(500)] public string? Description { get; set; }
    [Range(5, 480)] public int DurationMinutes { get; set; }
    [Range(0, 100000000)] public decimal ReferencePrice { get; set; }
    [Range(0, 10000)] public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
    public long Version { get; set; }
}
public sealed class CreateServiceRequest : ServiceDepositFields
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
public sealed record ScheduleIntervalDto(TimeOnly OpensAt, TimeOnly ClosesAt);
/// <summary>
/// La jornada de un día. <see cref="Intervals"/> la describe completa; <see cref="OpensAt"/> y
/// <see cref="ClosesAt"/> siguen exponiendo el primer tramo para no romper a quien ya los leía.
/// </summary>
public sealed record BusinessHourAdminDto(DayOfWeek Day, bool IsClosed, TimeOnly? OpensAt, TimeOnly? ClosesAt,
    long Version = 0, IReadOnlyList<ScheduleIntervalDto>? Intervals = null)
{
    public IReadOnlyList<ScheduleIntervalDto> Schedule => Intervals ??
        (OpensAt is not null && ClosesAt is not null
            ? [new ScheduleIntervalDto(OpensAt.Value, ClosesAt.Value)] : []);
}
public sealed class SaveBusinessHourRequest
{
    public bool IsClosed { get; set; }
    public TimeOnly? OpensAt { get; set; }
    public TimeOnly? ClosesAt { get; set; }
    public long Version { get; set; }
    /// <summary>
    /// Cuando llega, reemplaza la jornada completa de ese día y admite pausas. Si no llega, se
    /// conserva el comportamiento anterior de un único tramo.
    /// </summary>
    public List<ScheduleIntervalDto>? Intervals { get; set; }
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
    decimal ReferencePrice, int DisplayOrder, bool IsActive, long Version,
    string? ImageUrl = null, string? ImageAltText = null);
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
/// <summary>Los pedidos junto con el negocio que los atiende, por la misma razón que las citas.</summary>
public sealed record PickupOrderBoardDto(Guid BusinessId, string BusinessName, string TimeZoneId,
    IReadOnlyList<PickupOrderAdminDto> Items);

public sealed class PickupOrderCommandRequest
{
    public long Version { get; set; }
    [StringLength(160)] public string? Reason { get; set; }
}

public sealed record PlatformOptionDto(Guid Id, string Slug, string Name);
public sealed record ReadinessItemDto(string Key, string Label, bool IsApplicable, bool IsComplete,
    string? MissingHint = null);
public sealed record PlatformBusinessDto(Guid Id, string Name, string Slug, string Municipality,
    string Category, string Status, bool IsPublished, IReadOnlyList<string> Modules,
    string? OwnerName, string? OwnerEmail, IReadOnlyList<ReadinessItemDto> Readiness,
    bool IsReady, string? SuspensionReason, long Version, Guid MunicipalityId = default,
    Guid CategoryId = default, string Description = "", string Address = "", string PublicPhone = "",
    string? WhatsAppUrl = null, string? LocationUrl = null,
    string ShortDescription = "", string? ReferencePoint = null, string? PublicEmail = null,
    string? InstagramUrl = null, string? FacebookUrl = null, string? CustomerInstructions = null,
    int CompletionPercentage = 0, IReadOnlyList<string>? MissingLabels = null, string? ReviewNotes = null,
    IReadOnlyList<BusinessImageDto>? Images = null);
public sealed record PlatformBusinessListDto(IReadOnlyList<PlatformBusinessDto> Items,
    IReadOnlyList<PlatformOptionDto> Municipalities, IReadOnlyList<PlatformOptionDto> Categories,
    bool PilotAccountCreationEnabled);
public sealed class CreatePlatformBusinessRequest
{
    [Required, StringLength(160, MinimumLength = 2)] public string Name { get; set; } = "";
    [Required, StringLength(120, MinimumLength = 3)] public string Slug { get; set; } = "";
    [Required] public Guid MunicipalityId { get; set; }
    [Required] public Guid CategoryId { get; set; }
    [Required(ErrorMessage = "La descripción breve es obligatoria."),
     StringLength(160, ErrorMessage = "La descripción breve admite máximo 160 caracteres.")]
    public string ShortDescription { get; set; } = "";
    [StringLength(600)] public string Description { get; set; } = "";
    [StringLength(240)] public string? Address { get; set; }
    [StringLength(30)] public string? PublicPhone { get; set; }
    [Url, StringLength(500)] public string? WhatsAppUrl { get; set; }
    [Url, StringLength(500)] public string? LocationUrl { get; set; }
    public bool Appointments { get; set; }
    public bool VirtualQueues { get; set; }
    public bool PickupOrders { get; set; }
    public string? ExistingOwnerEmail { get; set; }
    [StringLength(100)] public string? PilotDisplayName { get; set; }
    [EmailAddress, StringLength(256)] public string? PilotEmail { get; set; }
    [Range(1, 480)] public int QueueAverageMinutes { get; set; } = 20;
    [Range(1, 500)] public int QueueMaximumWaiting { get; set; } = 20;
    [StringLength(160)] public string QueueMessage { get; set; } = "Toma tu turno y consulta el avance.";
    [Range(0, 1440)] public int PickupPreparationMinutes { get; set; } = 30;
    [Range(5, 240)] public int PickupSlotMinutes { get; set; } = 15;
    [Range(1, 500)] public int PickupCapacity { get; set; } = 5;
    [StringLength(120)] public string? InitialServiceName { get; set; }
    [Range(5, 480)] public int InitialServiceDurationMinutes { get; set; } = 45;
    [StringLength(100)] public string? InitialProductCategory { get; set; }
    [StringLength(120)] public string? InitialProductName { get; set; }
    [Range(0, 100000000)] public decimal InitialProductPrice { get; set; }
    public bool SaveAsDraft { get; set; } = true;
}
public sealed record PlatformBusinessCreatedDto(PlatformBusinessDto Business, string? TemporaryPassword);
public sealed class UpdatePlatformBusinessRequest
{
    [Required, StringLength(160, MinimumLength = 2)] public string Name { get; set; } = "";
    [Required, StringLength(120, MinimumLength = 3)] public string Slug { get; set; } = "";
    [Required] public Guid MunicipalityId { get; set; }
    [Required] public Guid CategoryId { get; set; }
    [StringLength(600)] public string Description { get; set; } = "";
    [StringLength(240)] public string? Address { get; set; }
    [StringLength(30)] public string? PublicPhone { get; set; }
    [Url, StringLength(500)] public string? WhatsAppUrl { get; set; }
    [Url, StringLength(500)] public string? LocationUrl { get; set; }
    public long Version { get; set; }
}
public sealed class PlatformBusinessStateRequest
{
    public long Version { get; set; }
    [StringLength(240)] public string? Reason { get; set; }
}
public sealed class UpdatePlatformModulesRequest
{
    public bool Appointments { get; set; }
    public bool VirtualQueues { get; set; }
    public bool PickupOrders { get; set; }
    public long Version { get; set; }
}
public sealed class ChangeTemporaryPasswordRequest
{
    [Required] public string CurrentPassword { get; set; } = "";
    [Required, StringLength(100, MinimumLength = 10)] public string NewPassword { get; set; } = "";
    [Required, Compare(nameof(NewPassword))] public string ConfirmPassword { get; set; } = "";
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
    /// <summary>Categorías con negocios publicados. Opcionalmente acotadas a un municipio.</summary>
    Task<IReadOnlyList<CategoryCardDto>> GetCategoriesAsync(string? municipality = null,
        CancellationToken cancellationToken = default);
    Task<BusinessProfileDto?> GetBusinessAsync(string slug, CancellationToken cancellationToken = default);
    Task<SlotListDto> GetSlotsAsync(string slug, Guid serviceId, DateOnly date, CancellationToken cancellationToken = default);
    Task<AppointmentCreatedDto> CreateAppointmentAsync(string slug, CreateAppointmentRequest request,
        CancellationToken cancellationToken = default);
    Task<AppointmentTrackingDto?> GetAppointmentTrackingAsync(string code, CancellationToken cancellationToken = default);
    Task CancelAppointmentAsync(string code, CancellationToken cancellationToken = default);
    /// <summary>El cliente avisa que envió el comprobante. Es su única acción sobre el adelanto.</summary>
    Task<AppointmentTrackingDto> ReportAppointmentDepositAsync(string code,
        CancellationToken cancellationToken = default);
    /// <summary>Acción del negocio sobre el adelanto: verify, reject, report o reopen.</summary>
    Task<AppointmentAdminDto> ChangeAppointmentDepositAsync(Guid businessId, Guid appointmentId, string action,
        DepositCommandRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AppointmentDepositAuditDto>> GetAppointmentDepositAuditAsync(Guid appointmentId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MyBusinessDto>> GetMyBusinessesAsync(CancellationToken cancellationToken = default);
    /// <summary>
    /// Lo que pasa hoy en los negocios que la persona opera. No recibe identificadores: el alcance lo
    /// resuelve el servidor a partir de sus membresías.
    /// </summary>
    Task<IReadOnlyList<OwnerDashboardSummaryDto>> GetOwnerDashboardAsync(
        CancellationToken cancellationToken = default);
    Task<AppointmentBoardDto> GetAppointmentsAsync(Guid businessId, DateOnly? date = null,
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
    Task<PickupOrderBoardDto> GetPickupOrdersAsync(Guid businessId, string? status = null,
        DateOnly? date = null, CancellationToken cancellationToken = default);
    Task<PickupOrderAdminDto> ChangePickupOrderAsync(Guid businessId, Guid orderId, string action,
        PickupOrderCommandRequest request, CancellationToken cancellationToken = default);
    Task<PlatformBusinessListDto> GetPlatformBusinessesAsync(string? search = null, string? municipality = null,
        string? status = null, string? module = null, CancellationToken cancellationToken = default);
    Task<PlatformBusinessDto> GetPlatformBusinessAsync(Guid businessId, CancellationToken cancellationToken = default);
    Task<PlatformBusinessCreatedDto> CreatePlatformBusinessAsync(CreatePlatformBusinessRequest request,
        CancellationToken cancellationToken = default);
    Task<PlatformBusinessDto> UpdatePlatformBusinessAsync(Guid businessId, UpdatePlatformBusinessRequest request,
        CancellationToken cancellationToken = default);
    Task<PlatformBusinessDto> ChangePlatformBusinessStateAsync(Guid businessId, string action,
        PlatformBusinessStateRequest request, CancellationToken cancellationToken = default);
    Task<PlatformBusinessDto> UpdatePlatformModulesAsync(Guid businessId, UpdatePlatformModulesRequest request,
        CancellationToken cancellationToken = default);
    Task<PlatformBusinessDto> SavePlatformBusinessProfileAsync(Guid businessId, SaveBusinessProfileRequest request,
        CancellationToken cancellationToken = default);
    // --- Perfil e imágenes vistos por el propietario del negocio -----------------------------
    // Mismos DTOs que la administración; cambia la ruta, y con ella quién está autorizado.
    Task<PlatformBusinessDto> GetOwnerProfileAsync(Guid businessId,
        CancellationToken cancellationToken = default);
    Task<PlatformBusinessDto> SaveOwnerProfileAsync(Guid businessId, SaveOwnerProfileRequest request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BusinessImageDto>> GetOwnerImagesAsync(Guid businessId,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// <paramref name="targetId"/> sólo viaja para las fotografías de un servicio o de un producto:
    /// es la fila del catálogo a la que pertenece la imagen.
    /// </summary>
    Task<BusinessImageDto> UploadOwnerImageAsync(Guid businessId, string kind, string fileName,
        string contentType, byte[] content, string? altText, Guid? targetId = null,
        CancellationToken cancellationToken = default);
    Task RemoveOwnerImageAsync(Guid businessId, Guid imageId, long version,
        CancellationToken cancellationToken = default);
    Task<PlatformBusinessDto> SubmitBusinessForReviewAsync(Guid businessId, SubmitForReviewRequest request,
        CancellationToken cancellationToken = default);
    Task<PlatformBusinessDto> RejectBusinessReviewAsync(Guid businessId, RejectReviewRequest request,
        CancellationToken cancellationToken = default);
    Task<BusinessProfileDto> PreviewBusinessAsync(Guid businessId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BusinessStatusChangeDto>> GetBusinessStatusHistoryAsync(Guid businessId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlatformAuditEntryDto>> GetBusinessAuditAsync(Guid businessId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BusinessHourAdminDto>> GetPlatformBusinessHoursAsync(Guid businessId,
        CancellationToken cancellationToken = default);
    Task<ConfigurationImpactDto> SetPlatformBusinessHourAsync(Guid businessId, DayOfWeek day,
        SaveBusinessHourRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StaffMemberDto>> GetPlatformSchedulingStaffAsync(Guid businessId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AvailabilityExceptionDto>> GetPlatformSchedulingExceptionsAsync(Guid businessId,
        DateOnly? from = null, CancellationToken cancellationToken = default);
    Task<AvailabilityExceptionDto> SavePlatformSchedulingExceptionAsync(Guid businessId,
        SaveAvailabilityExceptionRequest request, CancellationToken cancellationToken = default);
    Task DeletePlatformSchedulingExceptionAsync(Guid businessId, Guid exceptionId, long version,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BusinessImageDto>> GetBusinessImagesAsync(Guid businessId,
        CancellationToken cancellationToken = default);
    Task<BusinessImageDto> UploadBusinessImageAsync(Guid businessId, string kind, string fileName,
        string contentType, byte[] content, string? altText, Guid? targetId = null,
        CancellationToken cancellationToken = default);
    Task<BusinessImageDto> UpdateBusinessImageAsync(Guid businessId, Guid imageId,
        UpdateBusinessImageRequest request, CancellationToken cancellationToken = default);
    Task RemoveBusinessImageAsync(Guid businessId, Guid imageId, long version,
        CancellationToken cancellationToken = default);
    Task<InvitationIssuedDto> CreateInvitationAsync(CreateInvitationRequest request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InvitationDto>> GetInvitationsAsync(Guid? businessId = null,
        CancellationToken cancellationToken = default);
    Task<InvitationIssuedDto> ResendInvitationAsync(Guid invitationId, CancellationToken cancellationToken = default);
    Task RevokeInvitationAsync(Guid invitationId, CancellationToken cancellationToken = default);
    Task<InvitationIssuedDto> ResetAccessAsync(ResetAccessRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlatformAccountDto>> GetPartnerOperatorsAsync(CancellationToken cancellationToken = default);
    Task RevokePartnerOperatorAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlatformAccessAuditDto>> GetAccessAuditAsync(Guid? businessId = null,
        CancellationToken cancellationToken = default);
    Task<PlatformHealthDto> GetPlatformHealthAsync(CancellationToken cancellationToken = default);
    Task<LegalInfoDto> GetLegalInfoAsync(CancellationToken cancellationToken = default);
}
