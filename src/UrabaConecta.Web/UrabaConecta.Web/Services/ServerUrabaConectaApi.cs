using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using UrabaConecta.Application;
using UrabaConecta.Contracts;

namespace UrabaConecta.Web.Services;

public sealed class ServerUrabaConectaApi(IUrabaUseCases useCases, IQueueUseCases queues, IOrderingUseCases orders,
    IPlatformAdministrationUseCases platform, IAccessInvitationUseCases invitations,
    IBusinessImageUseCases images, IPlatformHealthProvider health, IOptions<LegalOptions> legal,
    IConsentPolicyProvider consentPolicy, IHttpContextAccessor httpContext,
    AuthenticationStateProvider authentication, IServiceScopeFactory scopeFactory,
    IOwnerDashboardUseCases dashboard, IPushNotificationService push) : IUrabaConectaApi
{
    // InteractiveServer mantiene una instancia scoped durante todo el circuito. Una navegación
    // hacia una ficha y un Atrás inmediato pueden solapar la última consulta de una pantalla con
    // la primera de la siguiente; EF no permite dos operaciones simultáneas sobre el mismo contexto.
    private readonly SemaphoreSlim publicReadGate = new(1, 1);

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
    public Task<AppointmentCreatedDto> CreateAppointmentAsync(string slug, CreateAppointmentRequest request,
        CancellationToken cancellationToken = default) => useCases.CreateAppointmentAsync(slug, request, cancellationToken);
    public Task<AppointmentTrackingDto?> GetAppointmentTrackingAsync(string code, CancellationToken cancellationToken = default)
        => useCases.GetTrackingAsync(code, cancellationToken);
    public Task CancelAppointmentAsync(string code, CancellationToken cancellationToken = default)
        => useCases.CancelAsync(code, cancellationToken);
    public Task<AppointmentTrackingDto> ReportAppointmentDepositAsync(string code,
        CancellationToken cancellationToken = default) => useCases.ReportDepositAsync(code, cancellationToken);
    public async Task<AppointmentAdminDto> ChangeAppointmentDepositAsync(Guid businessId, Guid appointmentId,
        string action, DepositCommandRequest request, CancellationToken cancellationToken = default)
        => await useCases.ChangeDepositAsync(await UserId(), businessId, appointmentId, action, request,
            (await authentication.GetAuthenticationStateAsync()).User.IsInRole("PlatformAdmin"), cancellationToken);
    public Task<IReadOnlyList<AppointmentDepositAuditDto>> GetAppointmentDepositAuditAsync(Guid appointmentId,
        CancellationToken cancellationToken = default)
        => useCases.GetDepositAuditAsync(appointmentId, cancellationToken);
    public async Task<IReadOnlyList<MyBusinessDto>> GetMyBusinessesAsync(CancellationToken cancellationToken = default)
        => await useCases.GetMyBusinessesAsync(await UserId(), cancellationToken);
    /// <summary>
    /// El alcance sale de las membresías, igual que en la ruta HTTP: la pantalla nunca dice de qué
    /// negocios quiere métricas.
    /// </summary>
    public async Task<IReadOnlyList<OwnerDashboardSummaryDto>> GetOwnerDashboardAsync(
        CancellationToken cancellationToken = default)
        => await dashboard.SummarizeAsync(
            await useCases.GetMyBusinessesAsync(await UserId(), cancellationToken), cancellationToken);
    public async Task<AppointmentBoardDto> GetAppointmentsAsync(Guid businessId, DateOnly? date = null,
        string? status = null, CancellationToken cancellationToken = default)
        => await useCases.GetAppointmentsAsync(await UserId(), businessId, date, status, cancellationToken);
    public async Task<AppointmentAdminDto> ChangeAppointmentStatusAsync(Guid businessId, Guid appointmentId,
        ChangeAppointmentStatusRequest request, CancellationToken cancellationToken = default)
        => await useCases.ChangeStatusAsync(await UserId(), businessId, appointmentId, request, cancellationToken);
    public async Task<IReadOnlyList<ServiceDto>> GetServicesAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await useCases.GetServicesAsync(await UserId(), businessId, cancellationToken);
    public async Task<ServiceDto> CreateServiceAsync(Guid businessId, CreateServiceRequest request,
        CancellationToken cancellationToken = default)
        => await useCases.CreateServiceAsync(await UserId(), businessId, request, cancellationToken);
    public async Task<ServiceDto> UpdateServiceAsync(Guid businessId, Guid serviceId, UpdateServiceRequest request,
        CancellationToken cancellationToken = default)
        => await useCases.UpdateServiceAsync(await UserId(), businessId, serviceId, request, cancellationToken);
    public async Task<IReadOnlyList<StaffMemberDto>> GetStaffAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await useCases.GetStaffAsync(await UserId(), businessId, cancellationToken);
    public async Task<StaffMemberDto> CreateStaffAsync(Guid businessId, SaveStaffMemberRequest request,
        CancellationToken cancellationToken = default)
        => await useCases.CreateStaffAsync(await UserId(), businessId, request, cancellationToken);
    public async Task<StaffMemberDto> UpdateStaffAsync(Guid businessId, Guid staffId,
        SaveStaffMemberRequest request, CancellationToken cancellationToken = default)
        => await useCases.UpdateStaffAsync(await UserId(), businessId, staffId, request, cancellationToken);
    public async Task<IReadOnlyList<BusinessHourAdminDto>> GetBusinessHoursAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await useCases.GetBusinessHoursAsync(await UserId(), businessId, cancellationToken);
    public async Task<ConfigurationImpactDto> SetBusinessHourAsync(Guid businessId, DayOfWeek day,
        SaveBusinessHourRequest request, CancellationToken cancellationToken = default)
        => await useCases.SetBusinessHourAsync(await UserId(), businessId, day, request, cancellationToken);
    public async Task<IReadOnlyList<AvailabilityExceptionDto>> GetAvailabilityExceptionsAsync(Guid businessId,
        DateOnly? from = null, CancellationToken cancellationToken = default)
        => await useCases.GetAvailabilityExceptionsAsync(await UserId(), businessId, from, cancellationToken);
    public async Task<AvailabilityExceptionDto> SaveAvailabilityExceptionAsync(Guid businessId,
        SaveAvailabilityExceptionRequest request, CancellationToken cancellationToken = default)
        => await useCases.SaveAvailabilityExceptionAsync(await UserId(), businessId, request, cancellationToken);
    public async Task DeleteAvailabilityExceptionAsync(Guid businessId, Guid exceptionId, long version,
        CancellationToken cancellationToken = default)
        => await useCases.DeleteAvailabilityExceptionAsync(await UserId(), businessId, exceptionId, version,
            cancellationToken);
    public async Task<BusinessMemberListDto> ListMembersAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await useCases.ListMembersAsync(await UserId(), businessId, cancellationToken);
    public async Task<BusinessMemberDto> GetMemberAsync(Guid businessId, Guid membershipId,
        CancellationToken cancellationToken = default)
        => await useCases.GetMemberAsync(await UserId(), businessId, membershipId, cancellationToken);
    public async Task<BusinessMemberDto> LinkExistingMemberAsync(Guid businessId, LinkExistingMemberRequest request,
        CancellationToken cancellationToken = default)
        => await useCases.LinkExistingMemberAsync(await UserId(), businessId, request, cancellationToken);
    public async Task<DevelopmentMemberCreatedDto> CreateDevelopmentMemberAsync(Guid businessId,
        CreateDevelopmentMemberRequest request, CancellationToken cancellationToken = default)
        => await useCases.CreateDevelopmentMemberAsync(await UserId(), businessId, request, cancellationToken);
    public async Task<BusinessMemberDto> UpdateMemberPermissionsAsync(Guid businessId, Guid membershipId,
        UpdateMemberPermissionsRequest request, CancellationToken cancellationToken = default)
        => await useCases.UpdateMemberPermissionsAsync(await UserId(), businessId, membershipId, request,
            cancellationToken);
    public async Task<BusinessMemberDto> ActivateMemberAsync(Guid businessId, Guid membershipId, long version,
        CancellationToken cancellationToken = default)
        => await useCases.ActivateMemberAsync(await UserId(), businessId, membershipId, version, cancellationToken);
    public async Task<BusinessMemberDto> DeactivateMemberAsync(Guid businessId, Guid membershipId, long version,
        CancellationToken cancellationToken = default)
        => await useCases.DeactivateMemberAsync(await UserId(), businessId, membershipId, version, cancellationToken);
    public async Task<BusinessMemberDto> GrantOwnershipAsync(Guid businessId, Guid membershipId, long version,
        CancellationToken cancellationToken = default)
        => await useCases.GrantOwnershipAsync(await UserId(), businessId, membershipId, version, cancellationToken);
    public async Task<BusinessMemberDto> RevokeOwnershipAsync(Guid businessId, Guid membershipId,
        RevokeOwnershipRequest request, CancellationToken cancellationToken = default)
        => await useCases.RevokeOwnershipAsync(await UserId(), businessId, membershipId, request, cancellationToken);
    public async Task<IReadOnlyList<MembershipAuditDto>> ListMembershipAuditAsync(Guid businessId,
        Guid membershipId, CancellationToken cancellationToken = default)
        => await useCases.ListMembershipAuditAsync(await UserId(), businessId, membershipId, cancellationToken);

    public Task<QueuePublicStatusDto?> GetPublicQueueAsync(string slug, CancellationToken cancellationToken = default)
        => Serialized(() => queues.GetPublicAsync(slug, cancellationToken), cancellationToken);
    public Task<QueueTicketCreatedDto> JoinQueueAsync(string slug, CreateQueueTicketRequest request,
        CancellationToken cancellationToken = default) => queues.JoinAsync(slug, request, cancellationToken);
    public Task<QueueTicketTrackingDto?> GetQueueTicketAsync(string code, CancellationToken cancellationToken = default)
        => queues.TrackAsync(code, cancellationToken);
    public Task CancelQueueTicketAsync(string code, long version, CancellationToken cancellationToken = default)
        => queues.CancelPublicAsync(code, version, cancellationToken);
    public async Task<QueueAdminDto> GetQueueAdminAsync(Guid businessId, CancellationToken cancellationToken = default)
        => await queues.GetAdminAsync(await UserId(), businessId, cancellationToken);
    public async Task<QueueDefinitionDto> SaveQueueDefinitionAsync(Guid businessId, SaveQueueDefinitionRequest request,
        CancellationToken cancellationToken = default)
        => await queues.SaveDefinitionAsync(await UserId(), businessId, request, cancellationToken);
    public async Task<QueueAdminDto> OpenQueueAsync(Guid businessId, CancellationToken cancellationToken = default)
        => await queues.OpenAsync(await UserId(), businessId, cancellationToken);
    public async Task<QueueAdminDto> PauseQueueAsync(Guid businessId, long version, CancellationToken cancellationToken = default)
        => await queues.PauseAsync(await UserId(), businessId, version, cancellationToken);
    public async Task<QueueAdminDto> ResumeQueueAsync(Guid businessId, long version, CancellationToken cancellationToken = default)
        => await queues.ResumeAsync(await UserId(), businessId, version, cancellationToken);
    public async Task<QueueAdminDto> CloseQueueAsync(Guid businessId, long version, CancellationToken cancellationToken = default)
        => await queues.CloseAsync(await UserId(), businessId, version, cancellationToken);
    public async Task<QueueTicketCreatedDto> AddWalkInAsync(Guid businessId, CreateQueueTicketRequest request,
        CancellationToken cancellationToken = default)
        => await queues.WalkInAsync(await UserId(), businessId, request, cancellationToken);
    public async Task<QueueAdminDto> CallNextAsync(Guid businessId, long sessionVersion,
        CancellationToken cancellationToken = default)
        => await queues.CallNextAsync(await UserId(), businessId, sessionVersion, cancellationToken);
    public async Task<QueueAdminDto> ChangeQueueTicketAsync(Guid businessId, Guid ticketId, string action,
        QueueTicketCommandRequest request, CancellationToken cancellationToken = default)
        => await queues.ChangeTicketAsync(await UserId(), businessId, ticketId, action, request, cancellationToken);

    public Task<PickupMenuDto?> GetPickupMenuAsync(string slug, CancellationToken cancellationToken = default)
        => Serialized(() => orders.GetMenuAsync(slug, cancellationToken), cancellationToken);
    public Task<PickupSlotListDto> GetPickupSlotsAsync(string slug, DateOnly? date = null,
        CancellationToken cancellationToken = default)
        => Serialized(() => orders.GetSlotsAsync(slug, date, cancellationToken), cancellationToken);
    public Task<PickupOrderCreatedDto> CreatePickupOrderAsync(string slug, CreatePickupOrderRequest request,
        CancellationToken cancellationToken = default) => orders.CreateAsync(slug, request, cancellationToken);
    public Task<PickupOrderTrackingDto?> GetPickupOrderAsync(string code, CancellationToken cancellationToken = default)
        => orders.TrackAsync(code, cancellationToken);
    public Task CancelPickupOrderAsync(string code, long version, CancellationToken cancellationToken = default)
        => orders.CancelPublicAsync(code, version, cancellationToken);
    public async Task<PickupOrderSettingsDto> GetPickupOrderSettingsAsync(Guid businessId,
        CancellationToken cancellationToken = default) => await orders.GetSettingsAsync(await UserId(), businessId, cancellationToken);
    public async Task<PickupOrderSettingsDto> SavePickupOrderSettingsAsync(Guid businessId,
        SavePickupOrderSettingsRequest request, CancellationToken cancellationToken = default)
        => await orders.SaveSettingsAsync(await UserId(), businessId, request, cancellationToken);
    public async Task<IReadOnlyList<ProductCategoryDto>> GetProductCategoriesAsync(Guid businessId,
        CancellationToken cancellationToken = default) => await orders.GetCategoriesAsync(await UserId(), businessId, cancellationToken);
    public async Task<ProductCategoryDto> SaveProductCategoryAsync(Guid businessId, Guid? categoryId,
        SaveProductCategoryRequest request, CancellationToken cancellationToken = default)
        => await orders.SaveCategoryAsync(await UserId(), businessId, categoryId, request, cancellationToken);
    public async Task<IReadOnlyList<ProductDto>> GetProductsAsync(Guid businessId,
        CancellationToken cancellationToken = default) => await orders.GetProductsAsync(await UserId(), businessId, cancellationToken);
    public async Task<ProductDto> SaveProductAsync(Guid businessId, Guid? productId, SaveProductRequest request,
        CancellationToken cancellationToken = default)
        => await orders.SaveProductAsync(await UserId(), businessId, productId, request, cancellationToken);
    public Task<IReadOnlyList<BusinessPromotionDto>> GetPublicPromotionsAsync(
        CancellationToken cancellationToken = default)
        => Serialized(() => push.GetPublicPromotionsAsync(cancellationToken), cancellationToken);
    public async Task<IReadOnlyList<BusinessPromotionDto>> GetBusinessPromotionsAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await push.GetBusinessPromotionsAsync(await UserId(), businessId, cancellationToken);
    public async Task<BusinessPromotionSaveResultDto> SaveBusinessPromotionAsync(Guid businessId,
        Guid? promotionId, SaveBusinessPromotionRequest request, CancellationToken cancellationToken = default)
        => await push.SavePromotionAsync(await UserId(), businessId, promotionId, request, cancellationToken);
    public async Task<PickupOrderBoardDto> GetPickupOrdersAsync(Guid businessId, string? status = null,
        DateOnly? date = null, CancellationToken cancellationToken = default)
        => await orders.ListOrdersAsync(await UserId(), businessId, status, date, cancellationToken);
    public async Task<PickupOrderAdminDto> ChangePickupOrderAsync(Guid businessId, Guid orderId, string action,
        PickupOrderCommandRequest request, CancellationToken cancellationToken = default)
        => await orders.ChangeStatusAsync(await UserId(), businessId, orderId, action, request, cancellationToken);

    public async Task<PlatformBusinessListDto> GetPlatformBusinessesAsync(string? search = null,
        string? municipality = null, string? status = null, string? module = null,
        CancellationToken cancellationToken = default)
        => await platform.ListAsync(await Actor(), search, municipality, status, module, cancellationToken);
    public async Task<PlatformBusinessDto> GetPlatformBusinessAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await platform.GetAsync(await Actor(), businessId, cancellationToken);
    public async Task<PlatformBusinessCreatedDto> CreatePlatformBusinessAsync(CreatePlatformBusinessRequest request,
        CancellationToken cancellationToken = default)
        => await platform.CreateAsync(await Actor(), request, cancellationToken);
    public async Task<PlatformBusinessDto> UpdatePlatformBusinessAsync(Guid businessId,
        UpdatePlatformBusinessRequest request, CancellationToken cancellationToken = default)
        => await platform.UpdateAsync(await Actor(), businessId, request, cancellationToken);
    public async Task<PlatformBusinessDto> ChangePlatformBusinessStateAsync(Guid businessId, string action,
        PlatformBusinessStateRequest request, CancellationToken cancellationToken = default)
        => await platform.ChangeStateAsync(await Actor(), businessId, action, request, cancellationToken);
    public async Task<PlatformBusinessDto> UpdatePlatformModulesAsync(Guid businessId,
        UpdatePlatformModulesRequest request, CancellationToken cancellationToken = default)
        => await platform.UpdateModulesAsync(await Actor(), businessId, request, cancellationToken);
    public async Task<PlatformBusinessDto> SavePlatformBusinessProfileAsync(Guid businessId,
        SaveBusinessProfileRequest request, CancellationToken cancellationToken = default)
        => await platform.SaveProfileAsync(await Actor(), businessId, request, cancellationToken);
    public async Task<PlatformBusinessDto> SubmitBusinessForReviewAsync(Guid businessId,
        SubmitForReviewRequest request, CancellationToken cancellationToken = default)
        => await platform.SubmitForReviewAsync(await Actor(), businessId, request, cancellationToken);
    public async Task<PlatformBusinessDto> RejectBusinessReviewAsync(Guid businessId, RejectReviewRequest request,
        CancellationToken cancellationToken = default)
        => await platform.RejectReviewAsync(await Actor(), businessId, request, cancellationToken);
    public async Task<BusinessProfileDto> PreviewBusinessAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await platform.PreviewAsync(await Actor(), businessId, cancellationToken);
    public async Task<IReadOnlyList<BusinessStatusChangeDto>> GetBusinessStatusHistoryAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await platform.ListStatusHistoryAsync(await Actor(), businessId, cancellationToken);
    public async Task<IReadOnlyList<PlatformAuditEntryDto>> GetBusinessAuditAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await platform.ListAuditAsync(await Actor(), businessId, cancellationToken);
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
        => await platform.GetAsync(await Actor(), businessId, cancellationToken);
    public async Task<PlatformBusinessDto> SaveOwnerProfileAsync(Guid businessId,
        SaveOwnerProfileRequest request, CancellationToken cancellationToken = default)
        => await platform.SaveOwnerProfileAsync(await Actor(), businessId, request, cancellationToken);
    public async Task<IReadOnlyList<BusinessImageDto>> GetOwnerImagesAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await images.ListAsync(await Actor(), businessId, cancellationToken);
    public async Task<IReadOnlyList<BusinessImageDto>> GetOwnerCatalogImagesAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await images.ListCatalogAsync(await Actor(), businessId, cancellationToken);
    public async Task<BusinessImageDto> UploadOwnerImageAsync(Guid businessId, string kind, string fileName,
        string contentType, byte[] content, string? altText, Guid? targetId = null,
        CancellationToken cancellationToken = default)
        => await images.UploadAsync(await Actor(), businessId, kind,
            new UploadedImage(fileName, contentType, content), altText, targetId, cancellationToken);
    public async Task RemoveOwnerImageAsync(Guid businessId, Guid imageId, long version,
        CancellationToken cancellationToken = default)
        => await images.RemoveAsync(await Actor(), businessId, imageId, version, cancellationToken);

    public async Task<IReadOnlyList<BusinessImageDto>> GetBusinessImagesAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => await images.ListAsync(await Actor(), businessId, cancellationToken);
    public async Task<BusinessImageDto> UploadBusinessImageAsync(Guid businessId, string kind, string fileName,
        string contentType, byte[] content, string? altText, Guid? targetId = null,
        CancellationToken cancellationToken = default)
        => await images.UploadAsync(await Actor(), businessId, kind,
            new UploadedImage(fileName, contentType, content), altText, targetId, cancellationToken);
    public async Task<BusinessImageDto> UpdateBusinessImageAsync(Guid businessId, Guid imageId,
        UpdateBusinessImageRequest request, CancellationToken cancellationToken = default)
        => await images.DescribeAsync(await Actor(), businessId, imageId, request, cancellationToken);
    public async Task RemoveBusinessImageAsync(Guid businessId, Guid imageId, long version,
        CancellationToken cancellationToken = default)
        => await images.RemoveAsync(await Actor(), businessId, imageId, version, cancellationToken);

    public async Task<InvitationIssuedDto> CreateInvitationAsync(CreateInvitationRequest request,
        CancellationToken cancellationToken = default)
        => await invitations.InviteAsync(await Actor(), request, cancellationToken);
    public async Task<IReadOnlyList<InvitationDto>> GetInvitationsAsync(Guid? businessId = null,
        CancellationToken cancellationToken = default)
        => await invitations.ListAsync(await Actor(), businessId, cancellationToken);
    public async Task<InvitationIssuedDto> ResendInvitationAsync(Guid invitationId,
        CancellationToken cancellationToken = default)
        => await invitations.ResendAsync(await Actor(), invitationId, cancellationToken);
    public async Task RevokeInvitationAsync(Guid invitationId, CancellationToken cancellationToken = default)
        => await invitations.RevokeAsync(await Actor(), invitationId, cancellationToken);
    public async Task<InvitationIssuedDto> ResetAccessAsync(ResetAccessRequest request,
        CancellationToken cancellationToken = default)
        => await invitations.ResetAccessAsync(await Actor(), request, cancellationToken);
    public async Task<IReadOnlyList<PlatformAccountDto>> GetPartnerOperatorsAsync(
        CancellationToken cancellationToken = default)
        => await invitations.ListPartnerOperatorsAsync(await Actor(), cancellationToken);
    public async Task RevokePartnerOperatorAsync(Guid userId, CancellationToken cancellationToken = default)
        => await invitations.RevokePartnerOperatorAsync(await Actor(), userId, cancellationToken);
    public async Task<IReadOnlyList<PlatformAccessAuditDto>> GetAccessAuditAsync(Guid? businessId = null,
        CancellationToken cancellationToken = default)
        => await invitations.ListAuditAsync(await Actor(), businessId, cancellationToken);

    public async Task<PlatformHealthDto> GetPlatformHealthAsync(CancellationToken cancellationToken = default)
    {
        if (!(await Actor()).IsPlatformAdmin)
            throw new ApiException("FORBIDDEN", "Solo la administración de plataforma consulta la salud.", 403);
        return await health.GetAsync(cancellationToken);
    }

    public Task<LegalInfoDto> GetLegalInfoAsync(CancellationToken cancellationToken = default)
    {
        var value = legal.Value;
        // PolicyVersion es la versión efectiva que el servidor exigirá en los formularios públicos.
        return Task.FromResult(new LegalInfoDto(value.ResponsibleName, value.Identification, value.Address,
            value.PrivacyEmail, value.SupportEmail, consentPolicy.CurrentVersion, value.PolicyEffectiveDate));
    }

    private async Task<T> Serialized<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        await publicReadGate.WaitAsync(cancellationToken);
        try { return await operation(); }
        finally { publicReadGate.Release(); }
    }

    private async Task<Guid> UserId()
    {
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        return Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    }

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
