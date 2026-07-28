using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Application;

public sealed record SchedulingContext(Business Business, Service Service, IReadOnlyList<BusinessHour> Hours,
    IReadOnlyList<StaffMember> EligibleStaff, IReadOnlyList<AvailabilityException> Exceptions,
    IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End, Guid StaffId)> Occupied);

public sealed record AppointmentRecord(Appointment Appointment, Business Business, ConsentReceipt Consent);
public sealed record IdentityAccount(Guid UserId, string Email, string DisplayName, bool MustChangePassword = false);
public sealed record CreatedIdentityAccount(IdentityAccount Account, string TemporaryPassword);

public interface IApplicationTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public interface IOrderingStore
{
    Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
    Task<(Business Business, PickupOrderSettings Settings)?> GetPublicContextAsync(string slug, CancellationToken cancellationToken);
    Task<PickupOrderSettings?> GetSettingsAsync(Guid businessId, CancellationToken cancellationToken);
    Task<PickupOrderSettings?> LockSettingsAsync(Guid businessId, CancellationToken cancellationToken);
    Task LockSlotAsync(Guid businessId, DateTimeOffset start, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductCategory>> GetCategoriesAsync(Guid businessId, bool activeOnly, CancellationToken cancellationToken);
    Task<IReadOnlyList<Product>> GetProductsAsync(Guid businessId, bool activeOnly, CancellationToken cancellationToken);
    Task<ProductCategory?> GetCategoryAsync(Guid businessId, Guid id, CancellationToken cancellationToken);
    Task<Product?> GetProductAsync(Guid businessId, Guid id, CancellationToken cancellationToken);
    Task<Business?> GetBusinessAsync(Guid businessId, CancellationToken cancellationToken);
    Task<IReadOnlyList<BusinessHour>> GetHoursAsync(Guid businessId, CancellationToken cancellationToken);
    Task<int> CountActiveInSlotAsync(Guid businessId, DateTimeOffset start, CancellationToken cancellationToken);
    Task<PickupOrder?> FindByCodeAsync(string hash, CancellationToken cancellationToken);
    Task<PickupOrder?> GetOrderAsync(Guid businessId, Guid orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PickupOrder>> ListOrdersAsync(Guid businessId, string? status, DateOnly? date,
        CancellationToken cancellationToken);
    Task<bool> CanManageOrdersAsync(Guid userId, Guid businessId, CancellationToken cancellationToken);
    Task<bool> CanManageConfigurationAsync(Guid userId, Guid businessId, CancellationToken cancellationToken);
    void AddCategory(ProductCategory category);
    void AddProduct(Product product);
    void AddSettings(PickupOrderSettings settings);
    void AddOrder(PickupOrder order);
    void AddConsent(ConsentReceipt consent);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IOrderingUseCases
{
    Task<PickupMenuDto?> GetMenuAsync(string slug, CancellationToken cancellationToken = default);
    Task<PickupSlotListDto> GetSlotsAsync(string slug, DateOnly? date = null, CancellationToken cancellationToken = default);
    Task<PickupOrderCreatedDto> CreateAsync(string slug, CreatePickupOrderRequest request,
        CancellationToken cancellationToken = default);
    Task<PickupOrderTrackingDto?> TrackAsync(string code, CancellationToken cancellationToken = default);
    Task CancelPublicAsync(string code, long version, CancellationToken cancellationToken = default);
    Task<PickupOrderSettingsDto> GetSettingsAsync(Guid userId, Guid businessId, CancellationToken cancellationToken = default);
    Task<PickupOrderSettingsDto> SaveSettingsAsync(Guid userId, Guid businessId, SavePickupOrderSettingsRequest request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductCategoryDto>> GetCategoriesAsync(Guid userId, Guid businessId, CancellationToken cancellationToken = default);
    Task<ProductCategoryDto> SaveCategoryAsync(Guid userId, Guid businessId, Guid? categoryId,
        SaveProductCategoryRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductDto>> GetProductsAsync(Guid userId, Guid businessId, CancellationToken cancellationToken = default);
    Task<ProductDto> SaveProductAsync(Guid userId, Guid businessId, Guid? productId,
        SaveProductRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PickupOrderAdminDto>> ListOrdersAsync(Guid userId, Guid businessId, string? status,
        DateOnly? date, CancellationToken cancellationToken = default);
    Task<PickupOrderAdminDto> ChangeStatusAsync(Guid userId, Guid businessId, Guid orderId, string action,
        PickupOrderCommandRequest request, CancellationToken cancellationToken = default);
}

public interface IIdentityAccountManager
{
    bool DevelopmentAccountCreationEnabled { get; }
    Task<IdentityAccount?> FindByExactEmailAsync(string email, CancellationToken cancellationToken);
    Task<CreatedIdentityAccount> CreateDevelopmentAsync(string displayName, string email,
        CancellationToken cancellationToken);
    Task<CreatedIdentityAccount> CreatePilotAsync(string displayName, string email,
        CancellationToken cancellationToken);
}

public sealed record PlatformBusinessRecord(Business Business, string Municipality, string Category,
    IReadOnlyList<BusinessModule> Modules, IdentityAccount? Owner, bool HasHours, bool HasService,
    bool HasQueueDefinition, bool HasPickupSettings, bool HasProductCategory, bool HasProduct, int OperationCount,
    IReadOnlyList<BusinessImage>? Images = null)
{
    public IReadOnlyList<BusinessImage> LiveImages => (Images ?? []).Where(x => !x.IsDeleted).ToList();
    public bool HasLogo => LiveImages.Any(x => x.Kind == BusinessImageKind.Logo);
    public bool HasCover => LiveImages.Any(x => x.Kind == BusinessImageKind.Cover);
}

public interface IPlatformAdministrationStore
{
    Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PlatformBusinessRecord>> ListAsync(string? search, string? municipality, string? status,
        string? module, Guid? createdByUserId, CancellationToken cancellationToken);
    void AddStatusChange(BusinessStatusChange change);
    Task<IReadOnlyList<BusinessStatusChangeDto>> ListStatusHistoryAsync(Guid businessId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<PlatformAuditEntryDto>> ListBusinessAuditAsync(Guid businessId, int take,
        CancellationToken cancellationToken);
    Task<PlatformBusinessRecord?> GetAsync(Guid businessId, CancellationToken cancellationToken);
    Task<Business?> LockBusinessAsync(Guid businessId, CancellationToken cancellationToken);
    Task<bool> SlugExistsAsync(string slug, Guid? excludingId, CancellationToken cancellationToken);
    Task<bool> MunicipalityExistsAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> CategoryExistsAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlatformOptionDto>> ListMunicipalitiesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PlatformOptionDto>> ListCategoriesAsync(CancellationToken cancellationToken);
    Task<BusinessMembership?> GetOwnerAsync(Guid businessId, CancellationToken cancellationToken);
    Task<BusinessMembership?> GetMembershipByUserAsync(Guid businessId, Guid userId, CancellationToken cancellationToken);
    void AddBusiness(Business business);
    void AddModule(BusinessModule module);
    void AddMembership(BusinessMembership membership);
    void AddHour(BusinessHour hour);
    void AddService(Service service);
    void AddStaff(StaffMember staff);
    void AddStaffService(StaffService link);
    void AddQueueDefinition(QueueDefinition definition);
    void AddPickupSettings(PickupOrderSettings settings);
    void AddProductCategory(ProductCategory category);
    void AddProduct(Product product);
    void AddAudit(PlatformAuditEntry audit);
    void RemoveBusiness(Business business);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IPlatformAdministrationUseCases
{
    Task<PlatformBusinessListDto> ListAsync(PlatformActor actor, string? search, string? municipality, string? status,
        string? module, CancellationToken cancellationToken = default);
    Task<PlatformBusinessDto> GetAsync(PlatformActor actor, Guid businessId,
        CancellationToken cancellationToken = default);
    Task<PlatformBusinessCreatedDto> CreateAsync(PlatformActor actor, CreatePlatformBusinessRequest request,
        CancellationToken cancellationToken = default);
    Task<PlatformBusinessDto> UpdateAsync(PlatformActor actor, Guid businessId,
        UpdatePlatformBusinessRequest request, CancellationToken cancellationToken = default);
    /// <summary>Guarda el perfil comercial completo, incluidos los campos añadidos en V5.</summary>
    Task<PlatformBusinessDto> SaveProfileAsync(PlatformActor actor, Guid businessId,
        SaveBusinessProfileRequest request, CancellationToken cancellationToken = default);
    Task<PlatformBusinessDto> ChangeStateAsync(PlatformActor actor, Guid businessId, string action,
        PlatformBusinessStateRequest request, CancellationToken cancellationToken = default);
    Task<PlatformBusinessDto> UpdateModulesAsync(PlatformActor actor, Guid businessId,
        UpdatePlatformModulesRequest request, CancellationToken cancellationToken = default);
    Task<PlatformBusinessDto> SubmitForReviewAsync(PlatformActor actor, Guid businessId,
        SubmitForReviewRequest request, CancellationToken cancellationToken = default);
    Task<PlatformBusinessDto> RejectReviewAsync(PlatformActor actor, Guid businessId,
        RejectReviewRequest request, CancellationToken cancellationToken = default);
    /// <summary>Ficha pública tal como se verá al publicar, accesible antes de publicar.</summary>
    Task<BusinessProfileDto> PreviewAsync(PlatformActor actor, Guid businessId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BusinessStatusChangeDto>> ListStatusHistoryAsync(PlatformActor actor, Guid businessId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlatformAuditEntryDto>> ListAuditAsync(PlatformActor actor, Guid businessId,
        CancellationToken cancellationToken = default);
}

public interface IMembershipAdministrationStore
{
    Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<BusinessMembership>> LockBusinessMembershipsAsync(Guid businessId,
        CancellationToken cancellationToken);
    Task<BusinessMembership?> GetMembershipAsync(Guid businessId, Guid membershipId,
        CancellationToken cancellationToken);
    Task<BusinessMembership?> GetMembershipByUserAsync(Guid businessId, Guid userId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<BusinessMemberDto>> ListMembersAsync(Guid businessId, CancellationToken cancellationToken);
    Task<BusinessMemberDto?> GetMemberDtoAsync(Guid businessId, Guid membershipId,
        CancellationToken cancellationToken);
    void AddMembership(BusinessMembership membership);
    void AddAudit(MembershipAuditEntry entry);
    Task<IReadOnlyList<MembershipAuditDto>> ListAuditAsync(Guid businessId, Guid membershipId,
        CancellationToken cancellationToken);
    Task SaveMembershipChangesAsync(CancellationToken cancellationToken);
}

public interface IUrabaStore
{
    Task<IReadOnlyList<BusinessCardDto>> FindBusinessesAsync(string? search, string? municipality, string? category,
        CancellationToken cancellationToken);
    /// <summary>Ficha pública. Con <paramref name="requirePublished"/> en false sirve la vista previa administrativa.</summary>
    Task<BusinessProfileDto?> GetBusinessProfileAsync(string slug, bool requirePublished,
        CancellationToken cancellationToken);
    Task<SchedulingContext?> GetSchedulingContextAsync(string slug, Guid serviceId, DateOnly date,
        CancellationToken cancellationToken);
    Task<bool> AddAppointmentAsync(Appointment appointment, ConsentReceipt consent, CancellationToken cancellationToken);
    Task<AppointmentRecord?> FindAppointmentByCodeHashAsync(string codeHash, CancellationToken cancellationToken);
    Task<bool> IsMemberAsync(Guid userId, Guid businessId, CancellationToken cancellationToken);
    Task<bool> CanManageAppointmentsAsync(Guid userId, Guid businessId, CancellationToken cancellationToken);
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

public interface IQueueStore
{
    Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
    Task<QueueDefinition?> GetPublicDefinitionAsync(string slug, CancellationToken cancellationToken);
    Task<(QueueDefinition Definition, Business Business)?> GetPublicContextAsync(string slug, CancellationToken cancellationToken);
    Task<(QueueTicket Ticket, QueueDefinition Definition, Business Business)?> FindTicketAsync(string codeHash, CancellationToken cancellationToken);
    Task<QueueDefinition?> GetDefinitionAsync(Guid businessId, CancellationToken cancellationToken);
    Task<QueueSession?> GetCurrentSessionAsync(Guid businessId, CancellationToken cancellationToken);
    Task<QueueSession?> LockCurrentSessionAsync(Guid businessId, CancellationToken cancellationToken);
    Task<QueueTicket?> GetTicketAsync(Guid businessId, Guid ticketId, CancellationToken cancellationToken);
    Task<IReadOnlyList<QueueTicket>> GetSessionTicketsAsync(Guid businessId, Guid sessionId, CancellationToken cancellationToken);
    Task<int> CountWaitingAsync(Guid businessId, Guid sessionId, CancellationToken cancellationToken);
    Task<int> CountActiveAsync(Guid businessId, Guid sessionId, CancellationToken cancellationToken);
    Task<QueueTicket?> GetNextWaitingAsync(Guid businessId, Guid sessionId, CancellationToken cancellationToken);
    Task<bool> CanManageQueuesAsync(Guid userId, Guid businessId, CancellationToken cancellationToken);
    Task<bool> IsBusinessActiveAsync(Guid businessId, CancellationToken cancellationToken);
    Task<(string BusinessName, string BusinessSlug)?> GetBusinessNameAsync(Guid businessId, CancellationToken cancellationToken);
    void AddDefinition(QueueDefinition definition);
    void AddSession(QueueSession session);
    void AddTicket(QueueTicket ticket);
    void AddConsent(ConsentReceipt consent);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IQueueChangeNotifier
{
    Task PublicChangedAsync(Guid definitionId, CancellationToken cancellationToken);
    Task TicketChangedAsync(Guid ticketId, CancellationToken cancellationToken);
    Task OperationsChangedAsync(Guid businessId, CancellationToken cancellationToken);
}

public interface IQueueUseCases
{
    Task<QueuePublicStatusDto?> GetPublicAsync(string slug, CancellationToken cancellationToken = default);
    Task<QueueTicketCreatedDto> JoinAsync(string slug, CreateQueueTicketRequest request, CancellationToken cancellationToken = default);
    Task<QueueTicketTrackingDto?> TrackAsync(string code, CancellationToken cancellationToken = default);
    Task CancelPublicAsync(string code, long version, CancellationToken cancellationToken = default);
    Task<QueueAdminDto> GetAdminAsync(Guid userId, Guid businessId, CancellationToken cancellationToken = default);
    Task<QueueDefinitionDto> SaveDefinitionAsync(Guid userId, Guid businessId, SaveQueueDefinitionRequest request, CancellationToken cancellationToken = default);
    Task<QueueAdminDto> OpenAsync(Guid userId, Guid businessId, CancellationToken cancellationToken = default);
    Task<QueueAdminDto> PauseAsync(Guid userId, Guid businessId, long version, CancellationToken cancellationToken = default);
    Task<QueueAdminDto> ResumeAsync(Guid userId, Guid businessId, long version, CancellationToken cancellationToken = default);
    Task<QueueAdminDto> CloseAsync(Guid userId, Guid businessId, long version, CancellationToken cancellationToken = default);
    Task<QueueTicketCreatedDto> WalkInAsync(Guid userId, Guid businessId, CreateQueueTicketRequest request, CancellationToken cancellationToken = default);
    Task<QueueAdminDto> CallNextAsync(Guid userId, Guid businessId, long sessionVersion, CancellationToken cancellationToken = default);
    Task<QueueAdminDto> ChangeTicketAsync(Guid userId, Guid businessId, Guid ticketId, string action, QueueTicketCommandRequest request, CancellationToken cancellationToken = default);
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
    Task<BusinessMemberListDto> ListMembersAsync(Guid userId, Guid businessId,
        CancellationToken cancellationToken = default);
    Task<BusinessMemberDto> GetMemberAsync(Guid userId, Guid businessId, Guid membershipId,
        CancellationToken cancellationToken = default);
    Task<BusinessMemberDto> LinkExistingMemberAsync(Guid userId, Guid businessId, LinkExistingMemberRequest request,
        CancellationToken cancellationToken = default);
    Task<DevelopmentMemberCreatedDto> CreateDevelopmentMemberAsync(Guid userId, Guid businessId,
        CreateDevelopmentMemberRequest request, CancellationToken cancellationToken = default);
    Task<BusinessMemberDto> UpdateMemberPermissionsAsync(Guid userId, Guid businessId, Guid membershipId,
        UpdateMemberPermissionsRequest request, CancellationToken cancellationToken = default);
    Task<BusinessMemberDto> ActivateMemberAsync(Guid userId, Guid businessId, Guid membershipId, long version,
        CancellationToken cancellationToken = default);
    Task<BusinessMemberDto> DeactivateMemberAsync(Guid userId, Guid businessId, Guid membershipId, long version,
        CancellationToken cancellationToken = default);
    Task<BusinessMemberDto> GrantOwnershipAsync(Guid userId, Guid businessId, Guid membershipId, long version,
        CancellationToken cancellationToken = default);
    Task<BusinessMemberDto> RevokeOwnershipAsync(Guid userId, Guid businessId, Guid membershipId,
        RevokeOwnershipRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MembershipAuditDto>> ListMembershipAuditAsync(Guid userId, Guid businessId,
        Guid membershipId, CancellationToken cancellationToken = default);
}
