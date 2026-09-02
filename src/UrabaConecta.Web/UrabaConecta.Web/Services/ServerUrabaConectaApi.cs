using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using UrabaConecta.Application;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Web.Services;

public sealed class ServerUrabaConectaApi(IUrabaUseCases useCases, IOrderingUseCases orders,
    IPlatformAdministrationUseCases platform, IAccessInvitationUseCases invitations,
    IBusinessImageUseCases images, IPlatformHealthProvider health, IOptions<LegalOptions> legal,
    IConsentPolicyProvider consentPolicy, IHttpContextAccessor httpContext,
    AuthenticationStateProvider authentication, IServiceScopeFactory scopeFactory,
    IOwnerDashboardUseCases dashboard, IPushNotificationService push,
    INotificationUseCases inbox) : IUrabaConectaApi
{
    // InteractiveServer mantiene una instancia scoped —y con ella un solo AppDbContext— durante
    // todo el circuito, y EF no admite dos operaciones simultáneas sobre el mismo contexto.
    //
    // La cola cubría sólo las lecturas públicas, y eso dejaba fuera el caso más común: una pantalla
    // que empieza a cargar sus datos y, en el primer render, monta un componente que carga los
    // suyos. Blazor no espera a que termine OnParametersSetAsync para pintar la primera vez, así que
    // las dos consultas salen a la vez y la petición muere con un 500 intermitente que la caché
    // esconde hasta el siguiente arranque en frío. Ahora pasa por aquí todo lo que toca la base.
    //
    // No es reentrante: ningún método de esta clase puede llamar a otro público de la misma clase.
    private readonly SemaphoreSlim gate = new(1, 1);

    public Task<IReadOnlyList<BusinessCardDto>> GetBusinessesAsync(string? search = null, string? municipality = null,
        string? category = null, CancellationToken cancellationToken = default)
        => Serialized(() => useCases.GetBusinessesAsync(search, municipality, category, cancellationToken),
            cancellationToken);
    public Task<IReadOnlyList<CategoryCardDto>> GetCategoriesAsync(string? municipality = null,
        CancellationToken cancellationToken = default)
        => Serialized(() => useCases.GetCategoriesAsync(municipality, cancellationToken), cancellationToken);
    public Task<BusinessProfileDto?> GetBusinessAsync(string slug, CancellationToken cancellationToken = default)
        => Serialized(() => useCases.GetBusinessAsync(slug, cancellationToken), cancellationToken);
    public Task<SlotListDto> GetSlotsAsync(string slug, Guid serviceId, DateOnly date, CancellationToken cancellationToken = default)
        => Serialized(() => useCases.GetSlotsAsync(slug, serviceId, date, cancellationToken), cancellationToken);
    public Task<SlotListDto?> FindNextAvailabilityAsync(string slug, Guid serviceId, DateOnly from, int days,
        CancellationToken cancellationToken = default)
        => Serialized(() => useCases.FindNextAvailabilityAsync(slug, serviceId, from, days, cancellationToken),
            cancellationToken);
    public Task<HomeFeedDto> GetHomeFeedAsync(DateOnly today, int availabilityDays,
        CancellationToken cancellationToken = default)
        => Serialized(() => useCases.GetHomeFeedAsync(today, availabilityDays, cancellationToken),
            cancellationToken);
    public Task<AppointmentCreatedDto> CreateAppointmentAsync(string slug, CreateAppointmentRequest request,
        CancellationToken cancellationToken = default)
        => Serialized(() => useCases.CreateAppointmentAsync(slug, request, cancellationToken), cancellationToken);
    public Task<AppointmentTrackingDto?> GetAppointmentTrackingAsync(string code, CancellationToken cancellationToken = default)
        => Serialized(() => useCases.GetTrackingAsync(code, cancellationToken), cancellationToken);
    public Task CancelAppointmentAsync(string code, CancellationToken cancellationToken = default)
        => Serialized(() => useCases.CancelAsync(code, cancellationToken), cancellationToken);
    public Task<AppointmentTrackingDto> ReportAppointmentDepositAsync(string code,
        CancellationToken cancellationToken = default)
        => Serialized(() => useCases.ReportDepositAsync(code, cancellationToken), cancellationToken);
    public async Task<AppointmentAdminDto> ChangeAppointmentDepositAsync(Guid businessId, Guid appointmentId,
        string action, DepositCommandRequest request, CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.ChangeDepositAsync(await UserId(), businessId, appointmentId, action, request,
            (await authentication.GetAuthenticationStateAsync()).User.IsInRole("PlatformAdmin"), cancellationToken), cancellationToken);
    public Task<IReadOnlyList<AppointmentDepositAuditDto>> GetAppointmentDepositAuditAsync(Guid appointmentId,
        CancellationToken cancellationToken = default)
        => Serialized(() => useCases.GetDepositAuditAsync(appointmentId, cancellationToken), cancellationToken);
    public async Task<IReadOnlyList<MyBusinessDto>> GetMyBusinessesAsync(CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.GetMyBusinessesAsync(await UserId(), cancellationToken), cancellationToken);
    /// <summary>
    /// El alcance sale de las membresías, igual que en la ruta HTTP: la pantalla nunca dice de qué
    /// negocios quiere métricas.
    /// </summary>
    public async Task<IReadOnlyList<OwnerDashboardSummaryDto>> GetOwnerDashboardAsync(
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await dashboard.SummarizeAsync(
            await useCases.GetMyBusinessesAsync(await UserId(), cancellationToken), cancellationToken), cancellationToken);
    public async Task<AppointmentBoardDto> GetAppointmentsAsync(Guid businessId, DateOnly? date = null,
        string? status = null, CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.GetAppointmentsAsync(await UserId(), businessId, date, status, cancellationToken), cancellationToken);
    public async Task<AppointmentAdminDto> ChangeAppointmentStatusAsync(Guid businessId, Guid appointmentId,
        ChangeAppointmentStatusRequest request, CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.ChangeStatusAsync(await UserId(), businessId, appointmentId, request, cancellationToken), cancellationToken);
    public async Task<IReadOnlyList<ServiceDto>> GetServicesAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.GetServicesAsync(await UserId(), businessId, cancellationToken,
            await IsPlatformAdmin()), cancellationToken);
    public async Task<ServiceDto> CreateServiceAsync(Guid businessId, CreateServiceRequest request,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.CreateServiceAsync(await UserId(), businessId, request, cancellationToken,
            await IsPlatformAdmin()), cancellationToken);
    public async Task<ServiceDto> UpdateServiceAsync(Guid businessId, Guid serviceId, UpdateServiceRequest request,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.UpdateServiceAsync(await UserId(), businessId, serviceId, request,
            cancellationToken, await IsPlatformAdmin()), cancellationToken);
    public async Task<IReadOnlyList<StaffMemberDto>> GetStaffAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.GetStaffAsync(await UserId(), businessId, cancellationToken,
            await IsPlatformAdmin()), cancellationToken);
    public async Task<StaffMemberDto> CreateStaffAsync(Guid businessId, SaveStaffMemberRequest request,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.CreateStaffAsync(await UserId(), businessId, request, cancellationToken,
            await IsPlatformAdmin()), cancellationToken);
    public async Task<StaffMemberDto> UpdateStaffAsync(Guid businessId, Guid staffId,
        SaveStaffMemberRequest request, CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.UpdateStaffAsync(await UserId(), businessId, staffId, request,
            cancellationToken, await IsPlatformAdmin()), cancellationToken);
    public async Task<IReadOnlyList<BusinessHourAdminDto>> GetBusinessHoursAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.GetBusinessHoursAsync(await UserId(), businessId, cancellationToken), cancellationToken);
    public async Task<ConfigurationImpactDto> SetBusinessHourAsync(Guid businessId, DayOfWeek day,
        SaveBusinessHourRequest request, CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.SetBusinessHourAsync(await UserId(), businessId, day, request, cancellationToken), cancellationToken);
    public async Task<IReadOnlyList<AvailabilityExceptionDto>> GetAvailabilityExceptionsAsync(Guid businessId,
        DateOnly? from = null, CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.GetAvailabilityExceptionsAsync(await UserId(), businessId, from, cancellationToken), cancellationToken);
    public async Task<AvailabilityExceptionDto> SaveAvailabilityExceptionAsync(Guid businessId,
        SaveAvailabilityExceptionRequest request, CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.SaveAvailabilityExceptionAsync(await UserId(), businessId, request, cancellationToken), cancellationToken);
    public async Task DeleteAvailabilityExceptionAsync(Guid businessId, Guid exceptionId, long version,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.DeleteAvailabilityExceptionAsync(await UserId(), businessId, exceptionId, version,
            cancellationToken), cancellationToken);
    public async Task<BusinessMemberListDto> ListMembersAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.ListMembersAsync(await UserId(), businessId, cancellationToken), cancellationToken);
    public async Task<BusinessMemberDto> GetMemberAsync(Guid businessId, Guid membershipId,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.GetMemberAsync(await UserId(), businessId, membershipId, cancellationToken), cancellationToken);
    public async Task<BusinessMemberDto> LinkExistingMemberAsync(Guid businessId, LinkExistingMemberRequest request,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.LinkExistingMemberAsync(await UserId(), businessId, request, cancellationToken), cancellationToken);
    public async Task<DevelopmentMemberCreatedDto> CreateDevelopmentMemberAsync(Guid businessId,
        CreateDevelopmentMemberRequest request, CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.CreateDevelopmentMemberAsync(await UserId(), businessId, request, cancellationToken), cancellationToken);
    public async Task<BusinessMemberDto> UpdateMemberPermissionsAsync(Guid businessId, Guid membershipId,
        UpdateMemberPermissionsRequest request, CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.UpdateMemberPermissionsAsync(await UserId(), businessId, membershipId, request,
            cancellationToken), cancellationToken);
    public async Task<BusinessMemberDto> ActivateMemberAsync(Guid businessId, Guid membershipId, long version,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.ActivateMemberAsync(await UserId(), businessId, membershipId, version, cancellationToken), cancellationToken);
    public async Task<BusinessMemberDto> DeactivateMemberAsync(Guid businessId, Guid membershipId, long version,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.DeactivateMemberAsync(await UserId(), businessId, membershipId, version, cancellationToken), cancellationToken);
    public async Task<BusinessMemberDto> GrantOwnershipAsync(Guid businessId, Guid membershipId, long version,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.GrantOwnershipAsync(await UserId(), businessId, membershipId, version, cancellationToken), cancellationToken);
    public async Task<BusinessMemberDto> RevokeOwnershipAsync(Guid businessId, Guid membershipId,
        RevokeOwnershipRequest request, CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.RevokeOwnershipAsync(await UserId(), businessId, membershipId, request, cancellationToken), cancellationToken);
    public async Task<IReadOnlyList<MembershipAuditDto>> ListMembershipAuditAsync(Guid businessId,
        Guid membershipId, CancellationToken cancellationToken = default)
        => await Serialized(async () => await useCases.ListMembershipAuditAsync(await UserId(), businessId, membershipId, cancellationToken), cancellationToken);

    public Task<QueuePublicStatusDto?> GetPublicQueueAsync(string slug, CancellationToken cancellationToken = default)
        => QueueOperation(queues => queues.GetPublicAsync(slug, cancellationToken), cancellationToken);
    public Task<QueueTicketCreatedDto> JoinQueueAsync(string slug, CreateQueueTicketRequest request,
        CancellationToken cancellationToken = default)
        => QueueOperation(queues => queues.JoinAsync(slug, request, cancellationToken), cancellationToken);
    public Task<QueueTicketTrackingDto?> GetQueueTicketAsync(string code, CancellationToken cancellationToken = default)
        => QueueOperation(queues => queues.TrackAsync(code, cancellationToken), cancellationToken);
    public Task CancelQueueTicketAsync(string code, long version, CancellationToken cancellationToken = default)
        => QueueOperation(queues => queues.CancelPublicAsync(code, version, cancellationToken), cancellationToken);
    public async Task<QueueAdminDto> GetQueueAdminAsync(Guid businessId, CancellationToken cancellationToken = default)
        => await QueueOperation(async queues => await queues.GetAdminAsync(await UserId(), businessId, cancellationToken), cancellationToken);
    public async Task<QueueDefinitionDto> SaveQueueDefinitionAsync(Guid businessId, SaveQueueDefinitionRequest request,
        CancellationToken cancellationToken = default)
        => await QueueOperation(async queues => await queues.SaveDefinitionAsync(await UserId(), businessId, request, cancellationToken), cancellationToken);
    public async Task<QueueAdminDto> OpenQueueAsync(Guid businessId, CancellationToken cancellationToken = default)
        => await QueueOperation(async queues => await queues.OpenAsync(await UserId(), businessId, cancellationToken), cancellationToken);
    public async Task<QueueAdminDto> PauseQueueAsync(Guid businessId, long version, CancellationToken cancellationToken = default)
        => await QueueOperation(async queues => await queues.PauseAsync(await UserId(), businessId, version, cancellationToken), cancellationToken);
    public async Task<QueueAdminDto> ResumeQueueAsync(Guid businessId, long version, CancellationToken cancellationToken = default)
        => await QueueOperation(async queues => await queues.ResumeAsync(await UserId(), businessId, version, cancellationToken), cancellationToken);
    public async Task<QueueAdminDto> CloseQueueAsync(Guid businessId, long version, CancellationToken cancellationToken = default)
        => await QueueOperation(async queues => await queues.CloseAsync(await UserId(), businessId, version, cancellationToken), cancellationToken);
    public async Task<QueueTicketCreatedDto> AddWalkInAsync(Guid businessId, CreateQueueTicketRequest request,
        CancellationToken cancellationToken = default)
        => await QueueOperation(async queues => await queues.WalkInAsync(await UserId(), businessId, request, cancellationToken), cancellationToken);
    public async Task<QueueAdminDto> CallNextAsync(Guid businessId, long sessionVersion,
        CancellationToken cancellationToken = default)
        => await QueueOperation(async queues => await queues.CallNextAsync(await UserId(), businessId, sessionVersion, cancellationToken), cancellationToken);
    public async Task<QueueAdminDto> ChangeQueueTicketAsync(Guid businessId, Guid ticketId, string action,
        QueueTicketCommandRequest request, CancellationToken cancellationToken = default)
        => await QueueOperation(async queues => await queues.ChangeTicketAsync(await UserId(), businessId, ticketId, action, request, cancellationToken), cancellationToken);

    public Task<PickupMenuDto?> GetPickupMenuAsync(string slug, CancellationToken cancellationToken = default)
        => Serialized(() => orders.GetMenuAsync(slug, cancellationToken), cancellationToken);
    public Task<PickupSlotListDto> GetPickupSlotsAsync(string slug, DateOnly? date = null,
        CancellationToken cancellationToken = default)
        => Serialized(() => orders.GetSlotsAsync(slug, date, cancellationToken), cancellationToken);
    public Task<PickupOrderCreatedDto> CreatePickupOrderAsync(string slug, CreatePickupOrderRequest request,
        CancellationToken cancellationToken = default)
        => Serialized(() => orders.CreateAsync(slug, request, cancellationToken), cancellationToken);
    public Task<PickupOrderTrackingDto?> GetPickupOrderAsync(string code, CancellationToken cancellationToken = default)
        => Serialized(() => orders.TrackAsync(code, cancellationToken), cancellationToken);
    public Task CancelPickupOrderAsync(string code, long version, CancellationToken cancellationToken = default)
        => Serialized(() => orders.CancelPublicAsync(code, version, cancellationToken), cancellationToken);
    public async Task<PickupOrderSettingsDto> GetPickupOrderSettingsAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await orders.GetSettingsAsync(await UserId(), businessId, cancellationToken), cancellationToken);
    public async Task<PickupOrderSettingsDto> SavePickupOrderSettingsAsync(Guid businessId,
        SavePickupOrderSettingsRequest request, CancellationToken cancellationToken = default)
        => await Serialized(async () => await orders.SaveSettingsAsync(await UserId(), businessId, request, cancellationToken), cancellationToken);
    public async Task<IReadOnlyList<ProductCategoryDto>> GetProductCategoriesAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await orders.GetCategoriesAsync(await UserId(), businessId, cancellationToken), cancellationToken);
    public async Task<ProductCategoryDto> SaveProductCategoryAsync(Guid businessId, Guid? categoryId,
        SaveProductCategoryRequest request, CancellationToken cancellationToken = default)
        => await Serialized(async () => await orders.SaveCategoryAsync(await UserId(), businessId, categoryId, request, cancellationToken), cancellationToken);
    public async Task<IReadOnlyList<ProductDto>> GetProductsAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await orders.GetProductsAsync(await UserId(), businessId, cancellationToken), cancellationToken);
    public async Task<ProductDto> SaveProductAsync(Guid businessId, Guid? productId, SaveProductRequest request,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await orders.SaveProductAsync(await UserId(), businessId, productId, request, cancellationToken), cancellationToken);
    public Task<IReadOnlyList<BusinessPromotionDto>> GetPublicPromotionsAsync(
        CancellationToken cancellationToken = default)
        => Serialized(() => push.GetPublicPromotionsAsync(cancellationToken), cancellationToken);
    public async Task<IReadOnlyList<BusinessPromotionDto>> GetBusinessPromotionsAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await push.GetBusinessPromotionsAsync(await UserId(), businessId, cancellationToken), cancellationToken);
    public async Task<BusinessPromotionSaveResultDto> SaveBusinessPromotionAsync(Guid businessId,
        Guid? promotionId, SaveBusinessPromotionRequest request, CancellationToken cancellationToken = default)
        => await Serialized(async () => await push.SavePromotionAsync(await UserId(), businessId, promotionId, request, cancellationToken), cancellationToken);
    public async Task<PickupOrderBoardDto> GetPickupOrdersAsync(Guid businessId, string? status = null,
        DateOnly? date = null, CancellationToken cancellationToken = default)
        => await Serialized(async () => await orders.ListOrdersAsync(await UserId(), businessId, status, date, cancellationToken), cancellationToken);
    public async Task<PickupOrderAdminDto> ChangePickupOrderAsync(Guid businessId, Guid orderId, string action,
        PickupOrderCommandRequest request, CancellationToken cancellationToken = default)
        => await Serialized(async () => await orders.ChangeStatusAsync(await UserId(), businessId, orderId, action, request, cancellationToken), cancellationToken);

    public async Task<PlatformBusinessListDto> GetPlatformBusinessesAsync(string? search = null,
        string? municipality = null, string? status = null, string? module = null,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await platform.ListAsync(await Actor(), search, municipality, status, module, cancellationToken), cancellationToken);
    public async Task<PlatformBusinessDto> GetPlatformBusinessAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await platform.GetAsync(await Actor(), businessId, cancellationToken), cancellationToken);
    public async Task<PlatformBusinessCreatedDto> CreatePlatformBusinessAsync(CreatePlatformBusinessRequest request,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await platform.CreateAsync(await Actor(), request, cancellationToken), cancellationToken);
    public async Task<PlatformBusinessDto> UpdatePlatformBusinessAsync(Guid businessId,
        UpdatePlatformBusinessRequest request, CancellationToken cancellationToken = default)
        => await Serialized(async () => await platform.UpdateAsync(await Actor(), businessId, request, cancellationToken), cancellationToken);
    public async Task<PlatformBusinessDto> ChangePlatformBusinessStateAsync(Guid businessId, string action,
        PlatformBusinessStateRequest request, CancellationToken cancellationToken = default)
        => await Serialized(async () => await platform.ChangeStateAsync(await Actor(), businessId, action, request, cancellationToken), cancellationToken);
    public async Task<PlatformBusinessDto> UpdatePlatformModulesAsync(Guid businessId,
        UpdatePlatformModulesRequest request, CancellationToken cancellationToken = default)
        => await Serialized(async () => await platform.UpdateModulesAsync(await Actor(), businessId, request, cancellationToken), cancellationToken);
    public async Task<PlatformBusinessDto> SavePlatformBusinessProfileAsync(Guid businessId,
        SaveBusinessProfileRequest request, CancellationToken cancellationToken = default)
        => await Serialized(async () => await platform.SaveProfileAsync(await Actor(), businessId, request, cancellationToken), cancellationToken);
    public async Task<PlatformBusinessDto> SubmitBusinessForReviewAsync(Guid businessId,
        SubmitForReviewRequest request, CancellationToken cancellationToken = default)
        => await Serialized(async () => await platform.SubmitForReviewAsync(await Actor(), businessId, request, cancellationToken), cancellationToken);
    public async Task<PlatformBusinessDto> RejectBusinessReviewAsync(Guid businessId, RejectReviewRequest request,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await platform.RejectReviewAsync(await Actor(), businessId, request, cancellationToken), cancellationToken);
    public async Task<BusinessProfileDto> PreviewBusinessAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await platform.PreviewAsync(await Actor(), businessId, cancellationToken), cancellationToken);
    public async Task<IReadOnlyList<BusinessStatusChangeDto>> GetBusinessStatusHistoryAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await platform.ListStatusHistoryAsync(await Actor(), businessId, cancellationToken), cancellationToken);
    public async Task<IReadOnlyList<PlatformAuditEntryDto>> GetBusinessAuditAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await platform.ListAuditAsync(await Actor(), businessId, cancellationToken), cancellationToken);
    public async Task<IReadOnlyList<BusinessHourAdminDto>> GetPlatformBusinessHoursAsync(Guid businessId,
        CancellationToken cancellationToken = default)
    {
        var actor = await Actor();
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPlatformAdministrationUseCases>()
            .ListHoursAsync(actor, businessId, cancellationToken);
    }

    public async Task<ConfigurationImpactDto> SetPlatformBusinessHourAsync(Guid businessId, DayOfWeek day,
        SaveBusinessHourRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await Actor();
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPlatformAdministrationUseCases>()
            .SetHourAsync(actor, businessId, day, request, cancellationToken);
    }

    public async Task<IReadOnlyList<StaffMemberDto>> GetPlatformSchedulingStaffAsync(Guid businessId,
        CancellationToken cancellationToken = default)
    {
        var actor = await Actor();
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPlatformAdministrationUseCases>()
            .ListSchedulingStaffAsync(actor, businessId, cancellationToken);
    }

    public async Task<IReadOnlyList<AvailabilityExceptionDto>> GetPlatformSchedulingExceptionsAsync(Guid businessId,
        DateOnly? from = null, CancellationToken cancellationToken = default)
    {
        var actor = await Actor();
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPlatformAdministrationUseCases>()
            .ListSchedulingExceptionsAsync(actor, businessId, from, cancellationToken);
    }

    public async Task<AvailabilityExceptionDto> SavePlatformSchedulingExceptionAsync(Guid businessId,
        SaveAvailabilityExceptionRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await Actor();
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPlatformAdministrationUseCases>()
            .SaveSchedulingExceptionAsync(actor, businessId, request, cancellationToken);
    }

    public async Task DeletePlatformSchedulingExceptionAsync(Guid businessId, Guid exceptionId, long version,
        CancellationToken cancellationToken = default)
    {
        var actor = await Actor();
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPlatformAdministrationUseCases>()
            .DeleteSchedulingExceptionAsync(actor, businessId, exceptionId, version, cancellationToken);
    }

    // --- Superficie del propietario -------------------------------------------------------------
    // Los mismos casos de uso que la administración. Quien entra lo decide la autorización, no la ruta.
    public async Task<PlatformBusinessDto> GetOwnerProfileAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await platform.GetAsync(await Actor(), businessId, cancellationToken), cancellationToken);
    public async Task<PlatformBusinessDto> SaveOwnerProfileAsync(Guid businessId,
        SaveOwnerProfileRequest request, CancellationToken cancellationToken = default)
        => await Serialized(async () => await platform.SaveOwnerProfileAsync(await Actor(), businessId, request, cancellationToken), cancellationToken);
    public async Task<IReadOnlyList<BusinessImageDto>> GetOwnerImagesAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await images.ListAsync(await Actor(), businessId, cancellationToken), cancellationToken);
    public async Task<IReadOnlyList<BusinessImageDto>> GetOwnerCatalogImagesAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await images.ListCatalogAsync(await Actor(), businessId, cancellationToken), cancellationToken);
    public async Task<BusinessImageDto> UploadOwnerImageAsync(Guid businessId, string kind, string fileName,
        string contentType, byte[] content, string? altText, Guid? targetId = null,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await images.UploadAsync(await Actor(), businessId, kind,
            new UploadedImage(fileName, contentType, content), altText, targetId, cancellationToken), cancellationToken);
    public async Task RemoveOwnerImageAsync(Guid businessId, Guid imageId, long version,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await images.RemoveAsync(await Actor(), businessId, imageId, version, cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<BusinessImageDto>> GetBusinessImagesAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await images.ListAsync(await Actor(), businessId, cancellationToken), cancellationToken);
    public async Task<BusinessImageDto> UploadBusinessImageAsync(Guid businessId, string kind, string fileName,
        string contentType, byte[] content, string? altText, Guid? targetId = null,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await images.UploadAsync(await Actor(), businessId, kind,
            new UploadedImage(fileName, contentType, content), altText, targetId, cancellationToken), cancellationToken);
    public async Task<BusinessImageDto> UpdateBusinessImageAsync(Guid businessId, Guid imageId,
        UpdateBusinessImageRequest request, CancellationToken cancellationToken = default)
        => await Serialized(async () => await images.DescribeAsync(await Actor(), businessId, imageId, request, cancellationToken), cancellationToken);
    public async Task RemoveBusinessImageAsync(Guid businessId, Guid imageId, long version,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await images.RemoveAsync(await Actor(), businessId, imageId, version, cancellationToken), cancellationToken);

    public async Task<InvitationIssuedDto> CreateInvitationAsync(CreateInvitationRequest request,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await invitations.InviteAsync(await Actor(), request, cancellationToken), cancellationToken);
    public async Task<IReadOnlyList<InvitationDto>> GetInvitationsAsync(Guid? businessId = null,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await invitations.ListAsync(await Actor(), businessId, cancellationToken), cancellationToken);
    public async Task<InvitationIssuedDto> ResendInvitationAsync(Guid invitationId,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await invitations.ResendAsync(await Actor(), invitationId, cancellationToken), cancellationToken);
    public async Task RevokeInvitationAsync(Guid invitationId, CancellationToken cancellationToken = default)
        => await Serialized(async () => await invitations.RevokeAsync(await Actor(), invitationId, cancellationToken), cancellationToken);
    public async Task<InvitationIssuedDto> ResetAccessAsync(ResetAccessRequest request,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await invitations.ResetAccessAsync(await Actor(), request, cancellationToken), cancellationToken);
    public async Task<IReadOnlyList<PlatformAccountDto>> GetPartnerOperatorsAsync(
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await invitations.ListPartnerOperatorsAsync(await Actor(), cancellationToken), cancellationToken);
    public async Task RevokePartnerOperatorAsync(Guid userId, CancellationToken cancellationToken = default)
        => await Serialized(async () => await invitations.RevokePartnerOperatorAsync(await Actor(), userId, cancellationToken), cancellationToken);
    public async Task<IReadOnlyList<PlatformAccessAuditDto>> GetAccessAuditAsync(Guid? businessId = null,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await invitations.ListAuditAsync(await Actor(), businessId, cancellationToken), cancellationToken);

    public async Task<PlatformHealthDto> GetPlatformHealthAsync(CancellationToken cancellationToken = default)
    {
        if (!(await Actor()).IsPlatformAdmin)
            throw new ApiException("FORBIDDEN", "Solo la administración de plataforma consulta la salud.", 403);
        return await health.GetAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationCountDto>> GetUnreadNotificationCountsAsync(
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await inbox.GetUnreadCountsAsync(await UserId(), cancellationToken),
            cancellationToken);
    public async Task<NotificationPageDto> GetBusinessNotificationsAsync(Guid businessId,
        bool unreadOnly = false, int take = 30, CancellationToken cancellationToken = default)
        => await Serialized(async () => await inbox.GetBusinessInboxAsync(await UserId(), businessId,
            unreadOnly, take, cancellationToken), cancellationToken);
    public async Task<NotificationPageDto> MarkNotificationsReadAsync(Guid businessId,
        MarkNotificationsReadRequest request, CancellationToken cancellationToken = default)
        => await Serialized(async () => await inbox.MarkReadAsync(await UserId(), businessId, request, cancellationToken), cancellationToken);
    public async Task<NotificationDiagnosticsDto> GetNotificationDiagnosticsAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await Serialized(async () => await inbox.GetDiagnosticsAsync(await UserId(), businessId, cancellationToken), cancellationToken);
    public Task<IReadOnlyList<NotificationDto>> GetTrackingNotificationsAsync(string kind, string code,
        CancellationToken cancellationToken = default)
        => Serialized(() => inbox.GetCustomerInboxAsync(TrackingAudience(kind), code, cancellationToken),
            cancellationToken);
    public async Task<NotificationHealthDto> GetNotificationHealthAsync(CancellationToken cancellationToken = default)
    {
        if (!(await Actor()).IsPlatformAdmin)
            throw new ApiException("FORBIDDEN", "Solo la administración de plataforma consulta el buzón.", 403);
        return await inbox.GetPlatformHealthAsync(cancellationToken);
    }

    private static PushAudience TrackingAudience(string kind) => kind switch
    {
        "citas" => PushAudience.Appointment,
        "pedidos" => PushAudience.PickupOrder,
        "turnos" => PushAudience.QueueTicket,
        _ => throw new ApiException("INVALID_TRACKING_KIND", "El tipo de seguimiento no es válido.")
    };

    public Task<LegalInfoDto> GetLegalInfoAsync(CancellationToken cancellationToken = default)
    {
        var value = legal.Value;
        // PolicyVersion es la versión efectiva que el servidor exigirá en los formularios públicos.
        return Task.FromResult(new LegalInfoDto(value.ResponsibleName, value.Identification, value.Address,
            value.PrivacyEmail, value.SupportEmail, consentPolicy.CurrentVersion, value.PolicyEffectiveDate));
    }

    // Una operación de fila debe leer lo que otro circuito acaba de guardar. El contexto del
    // circuito conservaba tickets Waiting aunque PostgreSQL ya los tuviera Completed/Cancelled.
    // El scope dura toda la operación (incluida su transacción), igual que en la API HTTP.
    private Task<T> QueueOperation<T>(Func<IQueueUseCases, Task<T>> operation, CancellationToken ct)
        => Serialized(async () =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            return await operation(scope.ServiceProvider.GetRequiredService<IQueueUseCases>());
        }, ct);

    private Task QueueOperation(Func<IQueueUseCases, Task> operation, CancellationToken ct)
        => Serialized(async () =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await operation(scope.ServiceProvider.GetRequiredService<IQueueUseCases>());
        }, ct);

    /// <summary>Misma cola para las operaciones que no devuelven nada.</summary>
    private async Task Serialized(Func<Task> operation, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { await operation(); }
        finally { gate.Release(); }
    }

    private async Task<T> Serialized<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { return await operation(); }
        finally { gate.Release(); }
    }

    private async Task<Guid> UserId()
    {
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        return Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    }

    private async Task<bool> IsPlatformAdmin()
        => (await authentication.GetAuthenticationStateAsync()).User.IsInRole("PlatformAdmin");

    /// <summary>El rol se lee de los claims de la petición, nunca de un parámetro del cliente.</summary>
    private async Task<PlatformActor> Actor()
    {
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        var id = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : Guid.Empty;
        return new(id, user.IsInRole("PlatformAdmin"), user.IsInRole("PartnerOperator"),
            httpContext.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            httpContext.HttpContext?.TraceIdentifier,
            // El rol no concede nada por sí solo: el caso de uso confirma la membresía del negocio.
            user.IsInRole("BusinessOwner"));
    }
}
