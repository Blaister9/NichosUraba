using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using UrabaConecta.Contracts;

namespace UrabaConecta.Web.Client.Services;

public sealed class HttpUrabaConectaApi(HttpClient http) : IUrabaConectaApi
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public Task<IReadOnlyList<BusinessCardDto>> GetBusinessesAsync(string? search = null, string? municipality = null,
        string? category = null, CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<BusinessCardDto>>($"api/v1/public/businesses?q={E(search)}&municipality={E(municipality)}&category={E(category)}", cancellationToken);
    public Task<IReadOnlyList<CategoryCardDto>> GetCategoriesAsync(string? municipality = null,
        CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<CategoryCardDto>>($"api/v1/public/categories?municipality={E(municipality)}", cancellationToken);
    public async Task<BusinessProfileDto?> GetBusinessAsync(string slug, CancellationToken cancellationToken = default)
    {
        var response = await http.GetAsync($"api/v1/public/businesses/{Uri.EscapeDataString(slug)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        return await Read<BusinessProfileDto>(response, cancellationToken);
    }
    public Task<SlotListDto> GetSlotsAsync(string slug, Guid serviceId, DateOnly date, CancellationToken cancellationToken = default)
        => Get<SlotListDto>($"api/v1/public/businesses/{Uri.EscapeDataString(slug)}/appointment-slots?serviceId={serviceId}&date={date:yyyy-MM-dd}", cancellationToken);
    public async Task<AppointmentCreatedDto> CreateAppointmentAsync(string slug, CreateAppointmentRequest request,
        CancellationToken cancellationToken = default)
        => await Read<AppointmentCreatedDto>(await http.PostAsJsonAsync($"api/v1/public/businesses/{Uri.EscapeDataString(slug)}/appointments",
            request, Json, cancellationToken), cancellationToken);
    public async Task<AppointmentTrackingDto?> GetAppointmentTrackingAsync(string code, CancellationToken cancellationToken = default)
    {
        var response = await http.GetAsync($"api/v1/public/appointments/{Uri.EscapeDataString(code)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        return await Read<AppointmentTrackingDto>(response, cancellationToken);
    }
    public async Task CancelAppointmentAsync(string code, CancellationToken cancellationToken = default)
        => await Ensure(await http.PostAsync($"api/v1/public/appointments/{Uri.EscapeDataString(code)}/cancel", null, cancellationToken), cancellationToken);
    public async Task<AppointmentTrackingDto> ReportAppointmentDepositAsync(string code,
        CancellationToken cancellationToken = default)
        => await Read<AppointmentTrackingDto>(await http.PostAsync(
            $"api/v1/public/appointments/{Uri.EscapeDataString(code)}/deposit-reported", null, cancellationToken),
            cancellationToken);
    public async Task<AppointmentAdminDto> ChangeAppointmentDepositAsync(Guid businessId, Guid appointmentId,
        string action, DepositCommandRequest request, CancellationToken cancellationToken = default)
        => await Read<AppointmentAdminDto>(await http.PostAsJsonAsync(
            $"api/v1/businesses/{businessId}/appointments/{appointmentId}/deposit/{Uri.EscapeDataString(action)}",
            request, Json, cancellationToken), cancellationToken);
    public Task<IReadOnlyList<AppointmentDepositAuditDto>> GetAppointmentDepositAuditAsync(Guid appointmentId,
        CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<AppointmentDepositAuditDto>>(
            $"api/v1/admin/appointments/{appointmentId}/deposit-audit", cancellationToken);
    public Task<IReadOnlyList<MyBusinessDto>> GetMyBusinessesAsync(CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<MyBusinessDto>>("api/v1/businesses/mine", cancellationToken);
    public Task<IReadOnlyList<OwnerDashboardSummaryDto>> GetOwnerDashboardAsync(
        CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<OwnerDashboardSummaryDto>>("api/v1/businesses/dashboard", cancellationToken);
    public Task<AppointmentBoardDto> GetAppointmentsAsync(Guid businessId, DateOnly? date = null,
        string? status = null, CancellationToken cancellationToken = default)
        => Get<AppointmentBoardDto>($"api/v1/businesses/{businessId}/appointments?date={date:yyyy-MM-dd}&status={E(status)}", cancellationToken);
    public async Task<AppointmentAdminDto> ChangeAppointmentStatusAsync(Guid businessId, Guid appointmentId,
        ChangeAppointmentStatusRequest request, CancellationToken cancellationToken = default)
        => await Read<AppointmentAdminDto>(await http.PostAsJsonAsync($"api/v1/businesses/{businessId}/appointments/{appointmentId}/status",
            request, Json, cancellationToken), cancellationToken);
    public Task<IReadOnlyList<ServiceDto>> GetServicesAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<ServiceDto>>($"api/v1/businesses/{businessId}/services", cancellationToken);
    public async Task<ServiceDto> CreateServiceAsync(Guid businessId, CreateServiceRequest request,
        CancellationToken cancellationToken = default)
        => await Read<ServiceDto>(await http.PostAsJsonAsync($"api/v1/businesses/{businessId}/services",
            request, Json, cancellationToken), cancellationToken);
    public async Task<ServiceDto> UpdateServiceAsync(Guid businessId, Guid serviceId, UpdateServiceRequest request,
        CancellationToken cancellationToken = default)
        => await Read<ServiceDto>(await http.PutAsJsonAsync($"api/v1/businesses/{businessId}/services/{serviceId}",
            request, Json, cancellationToken), cancellationToken);
    public Task<IReadOnlyList<StaffMemberDto>> GetStaffAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<StaffMemberDto>>($"api/v1/businesses/{businessId}/staff", cancellationToken);
    public async Task<StaffMemberDto> CreateStaffAsync(Guid businessId, SaveStaffMemberRequest request,
        CancellationToken cancellationToken = default)
        => await Read<StaffMemberDto>(await http.PostAsJsonAsync($"api/v1/businesses/{businessId}/staff",
            request, Json, cancellationToken), cancellationToken);
    public async Task<StaffMemberDto> UpdateStaffAsync(Guid businessId, Guid staffId,
        SaveStaffMemberRequest request, CancellationToken cancellationToken = default)
        => await Read<StaffMemberDto>(await http.PutAsJsonAsync($"api/v1/businesses/{businessId}/staff/{staffId}",
            request, Json, cancellationToken), cancellationToken);
    public Task<IReadOnlyList<BusinessHourAdminDto>> GetBusinessHoursAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<BusinessHourAdminDto>>($"api/v1/businesses/{businessId}/hours", cancellationToken);
    public async Task<ConfigurationImpactDto> SetBusinessHourAsync(Guid businessId, DayOfWeek day,
        SaveBusinessHourRequest request, CancellationToken cancellationToken = default)
        => await Read<ConfigurationImpactDto>(await http.PutAsJsonAsync(
            $"api/v1/businesses/{businessId}/hours/{day}", request, Json, cancellationToken), cancellationToken);
    public Task<IReadOnlyList<AvailabilityExceptionDto>> GetAvailabilityExceptionsAsync(Guid businessId,
        DateOnly? from = null, CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<AvailabilityExceptionDto>>(
            $"api/v1/businesses/{businessId}/availability-exceptions?from={from:yyyy-MM-dd}", cancellationToken);
    public async Task<AvailabilityExceptionDto> SaveAvailabilityExceptionAsync(Guid businessId,
        SaveAvailabilityExceptionRequest request, CancellationToken cancellationToken = default)
        => await Read<AvailabilityExceptionDto>(await http.PostAsJsonAsync(
            $"api/v1/businesses/{businessId}/availability-exceptions", request, Json, cancellationToken),
            cancellationToken);
    public async Task DeleteAvailabilityExceptionAsync(Guid businessId, Guid exceptionId, long version,
        CancellationToken cancellationToken = default)
        => await Ensure(await http.DeleteAsync(
            $"api/v1/businesses/{businessId}/availability-exceptions/{exceptionId}?version={version}",
            cancellationToken), cancellationToken);
    public Task<BusinessMemberListDto> ListMembersAsync(Guid businessId, CancellationToken cancellationToken = default)
        => Get<BusinessMemberListDto>($"api/v1/businesses/{businessId}/memberships", cancellationToken);
    public Task<BusinessMemberDto> GetMemberAsync(Guid businessId, Guid membershipId,
        CancellationToken cancellationToken = default)
        => Get<BusinessMemberDto>($"api/v1/businesses/{businessId}/memberships/{membershipId}", cancellationToken);
    public async Task<BusinessMemberDto> LinkExistingMemberAsync(Guid businessId, LinkExistingMemberRequest request,
        CancellationToken cancellationToken = default)
        => await Read<BusinessMemberDto>(await http.PostAsJsonAsync(
            $"api/v1/businesses/{businessId}/memberships/link-existing", request, Json, cancellationToken), cancellationToken);
    public async Task<DevelopmentMemberCreatedDto> CreateDevelopmentMemberAsync(Guid businessId,
        CreateDevelopmentMemberRequest request, CancellationToken cancellationToken = default)
        => await Read<DevelopmentMemberCreatedDto>(await http.PostAsJsonAsync(
            $"api/v1/businesses/{businessId}/memberships/create-development", request, Json, cancellationToken), cancellationToken);
    public async Task<BusinessMemberDto> UpdateMemberPermissionsAsync(Guid businessId, Guid membershipId,
        UpdateMemberPermissionsRequest request, CancellationToken cancellationToken = default)
        => await Read<BusinessMemberDto>(await http.PutAsJsonAsync(
            $"api/v1/businesses/{businessId}/memberships/{membershipId}/permissions", request, Json, cancellationToken),
            cancellationToken);
    public Task<BusinessMemberDto> ActivateMemberAsync(Guid businessId, Guid membershipId, long version,
        CancellationToken cancellationToken = default)
        => PostVersion(businessId, membershipId, "activate", version, cancellationToken);
    public Task<BusinessMemberDto> DeactivateMemberAsync(Guid businessId, Guid membershipId, long version,
        CancellationToken cancellationToken = default)
        => PostVersion(businessId, membershipId, "deactivate", version, cancellationToken);
    public Task<BusinessMemberDto> GrantOwnershipAsync(Guid businessId, Guid membershipId, long version,
        CancellationToken cancellationToken = default)
        => PostVersion(businessId, membershipId, "grant-owner", version, cancellationToken);
    public async Task<BusinessMemberDto> RevokeOwnershipAsync(Guid businessId, Guid membershipId,
        RevokeOwnershipRequest request, CancellationToken cancellationToken = default)
        => await Read<BusinessMemberDto>(await http.PostAsJsonAsync(
            $"api/v1/businesses/{businessId}/memberships/{membershipId}/revoke-owner", request, Json, cancellationToken),
            cancellationToken);
    public Task<IReadOnlyList<MembershipAuditDto>> ListMembershipAuditAsync(Guid businessId, Guid membershipId,
        CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<MembershipAuditDto>>(
            $"api/v1/businesses/{businessId}/memberships/{membershipId}/audit", cancellationToken);
    public async Task<QueuePublicStatusDto?> GetPublicQueueAsync(string slug, CancellationToken cancellationToken = default)
    {
        var response = await http.GetAsync($"api/v1/public/businesses/{E(slug)}/queue", cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await Read<QueuePublicStatusDto>(response, cancellationToken);
    }
    public async Task<QueueTicketCreatedDto> JoinQueueAsync(string slug, CreateQueueTicketRequest request,
        CancellationToken cancellationToken = default)
        => await Read<QueueTicketCreatedDto>(await http.PostAsJsonAsync(
            $"api/v1/public/businesses/{E(slug)}/queue/tickets", request, Json, cancellationToken), cancellationToken);
    public async Task<QueueTicketTrackingDto?> GetQueueTicketAsync(string code, CancellationToken cancellationToken = default)
    {
        var response = await http.GetAsync($"api/v1/public/queue/tickets/{E(code)}", cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await Read<QueueTicketTrackingDto>(response, cancellationToken);
    }
    public async Task CancelQueueTicketAsync(string code, long version, CancellationToken cancellationToken = default)
        => await Ensure(await http.PostAsJsonAsync($"api/v1/public/queue/tickets/{E(code)}/cancel",
            new QueueSessionCommandRequest { Version = version }, Json, cancellationToken), cancellationToken);
    public Task<QueueAdminDto> GetQueueAdminAsync(Guid businessId, CancellationToken cancellationToken = default)
        => Get<QueueAdminDto>($"api/v1/businesses/{businessId}/queue", cancellationToken);
    public async Task<QueueDefinitionDto> SaveQueueDefinitionAsync(Guid businessId, SaveQueueDefinitionRequest request,
        CancellationToken cancellationToken = default)
        => await Read<QueueDefinitionDto>(await http.PutAsJsonAsync(
            $"api/v1/businesses/{businessId}/queue-definition", request, Json, cancellationToken), cancellationToken);
    public async Task<QueueAdminDto> OpenQueueAsync(Guid businessId, CancellationToken cancellationToken = default)
        => await Read<QueueAdminDto>(await http.PostAsync($"api/v1/businesses/{businessId}/queue/open", null, cancellationToken), cancellationToken);
    public Task<QueueAdminDto> PauseQueueAsync(Guid businessId, long version, CancellationToken cancellationToken = default)
        => QueueSessionCommand(businessId, "pause", version, cancellationToken);
    public Task<QueueAdminDto> ResumeQueueAsync(Guid businessId, long version, CancellationToken cancellationToken = default)
        => QueueSessionCommand(businessId, "resume", version, cancellationToken);
    public Task<QueueAdminDto> CloseQueueAsync(Guid businessId, long version, CancellationToken cancellationToken = default)
        => QueueSessionCommand(businessId, "close", version, cancellationToken);
    public async Task<QueueTicketCreatedDto> AddWalkInAsync(Guid businessId, CreateQueueTicketRequest request,
        CancellationToken cancellationToken = default)
        => await Read<QueueTicketCreatedDto>(await http.PostAsJsonAsync(
            $"api/v1/businesses/{businessId}/queue/tickets/walk-in", request, Json, cancellationToken), cancellationToken);
    public async Task<QueueAdminDto> CallNextAsync(Guid businessId, long sessionVersion,
        CancellationToken cancellationToken = default)
        => await Read<QueueAdminDto>(await http.PostAsJsonAsync(
            $"api/v1/businesses/{businessId}/queue/call-next",
            new QueueSessionCommandRequest { Version = sessionVersion }, Json, cancellationToken), cancellationToken);
    public async Task<QueueAdminDto> ChangeQueueTicketAsync(Guid businessId, Guid ticketId, string action,
        QueueTicketCommandRequest request, CancellationToken cancellationToken = default)
        => await Read<QueueAdminDto>(await http.PostAsJsonAsync(
            $"api/v1/businesses/{businessId}/queue/tickets/{ticketId}/{E(action)}",
            request, Json, cancellationToken), cancellationToken);

    public async Task<PickupMenuDto?> GetPickupMenuAsync(string slug, CancellationToken cancellationToken = default)
    {
        var response = await http.GetAsync($"api/v1/public/businesses/{E(slug)}/menu", cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await Read<PickupMenuDto>(response, cancellationToken);
    }
    public Task<PickupSlotListDto> GetPickupSlotsAsync(string slug, DateOnly? date = null,
        CancellationToken cancellationToken = default)
        => Get<PickupSlotListDto>($"api/v1/public/businesses/{E(slug)}/pickup-slots?date={date:yyyy-MM-dd}", cancellationToken);
    public async Task<PickupOrderCreatedDto> CreatePickupOrderAsync(string slug, CreatePickupOrderRequest request,
        CancellationToken cancellationToken = default)
        => await Read<PickupOrderCreatedDto>(await http.PostAsJsonAsync(
            $"api/v1/public/businesses/{E(slug)}/orders", request, Json, cancellationToken), cancellationToken);
    public async Task<PickupOrderTrackingDto?> GetPickupOrderAsync(string code, CancellationToken cancellationToken = default)
    {
        var response = await http.GetAsync($"api/v1/public/orders/{E(code)}", cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await Read<PickupOrderTrackingDto>(response, cancellationToken);
    }
    public async Task CancelPickupOrderAsync(string code, long version, CancellationToken cancellationToken = default)
        => await Ensure(await http.PostAsJsonAsync($"api/v1/public/orders/{E(code)}/cancel",
            new PickupOrderCommandRequest { Version = version }, Json, cancellationToken), cancellationToken);
    public Task<PickupOrderSettingsDto> GetPickupOrderSettingsAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => Get<PickupOrderSettingsDto>($"api/v1/businesses/{businessId}/order-settings", cancellationToken);
    public async Task<PickupOrderSettingsDto> SavePickupOrderSettingsAsync(Guid businessId,
        SavePickupOrderSettingsRequest request, CancellationToken cancellationToken = default)
        => await Read<PickupOrderSettingsDto>(await http.PutAsJsonAsync(
            $"api/v1/businesses/{businessId}/order-settings", request, Json, cancellationToken), cancellationToken);
    public Task<IReadOnlyList<ProductCategoryDto>> GetProductCategoriesAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<ProductCategoryDto>>($"api/v1/businesses/{businessId}/product-categories", cancellationToken);
    public async Task<ProductCategoryDto> SaveProductCategoryAsync(Guid businessId, Guid? categoryId,
        SaveProductCategoryRequest request, CancellationToken cancellationToken = default)
        => await Read<ProductCategoryDto>(categoryId.HasValue
            ? await http.PutAsJsonAsync($"api/v1/businesses/{businessId}/product-categories/{categoryId}", request, Json, cancellationToken)
            : await http.PostAsJsonAsync($"api/v1/businesses/{businessId}/product-categories", request, Json, cancellationToken), cancellationToken);
    public Task<IReadOnlyList<ProductDto>> GetProductsAsync(Guid businessId, CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<ProductDto>>($"api/v1/businesses/{businessId}/products", cancellationToken);
    public async Task<ProductDto> SaveProductAsync(Guid businessId, Guid? productId, SaveProductRequest request,
        CancellationToken cancellationToken = default)
        => await Read<ProductDto>(productId.HasValue
            ? await http.PutAsJsonAsync($"api/v1/businesses/{businessId}/products/{productId}", request, Json, cancellationToken)
            : await http.PostAsJsonAsync($"api/v1/businesses/{businessId}/products", request, Json, cancellationToken), cancellationToken);
    public Task<IReadOnlyList<BusinessPromotionDto>> GetPublicPromotionsAsync(
        CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<BusinessPromotionDto>>("api/v1/public/promotions", cancellationToken);
    public Task<IReadOnlyList<BusinessPromotionDto>> GetBusinessPromotionsAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<BusinessPromotionDto>>($"api/v1/businesses/{businessId}/promotions", cancellationToken);
    public async Task<BusinessPromotionSaveResultDto> SaveBusinessPromotionAsync(Guid businessId,
        Guid? promotionId, SaveBusinessPromotionRequest request, CancellationToken cancellationToken = default)
        => await Read<BusinessPromotionSaveResultDto>(promotionId.HasValue
            ? await http.PutAsJsonAsync($"api/v1/businesses/{businessId}/promotions/{promotionId}", request,
                Json, cancellationToken)
            : await http.PostAsJsonAsync($"api/v1/businesses/{businessId}/promotions", request,
                Json, cancellationToken), cancellationToken);
    public Task<PickupOrderBoardDto> GetPickupOrdersAsync(Guid businessId, string? status = null,
        DateOnly? date = null, CancellationToken cancellationToken = default)
        => Get<PickupOrderBoardDto>(
            $"api/v1/businesses/{businessId}/orders?status={E(status)}&date={date:yyyy-MM-dd}", cancellationToken);
    public async Task<PickupOrderAdminDto> ChangePickupOrderAsync(Guid businessId, Guid orderId, string action,
        PickupOrderCommandRequest request, CancellationToken cancellationToken = default)
        => await Read<PickupOrderAdminDto>(await http.PostAsJsonAsync(
            $"api/v1/businesses/{businessId}/orders/{orderId}/{E(action)}", request, Json, cancellationToken), cancellationToken);

    public Task<PlatformBusinessListDto> GetPlatformBusinessesAsync(string? search = null,
        string? municipality = null, string? status = null, string? module = null,
        CancellationToken cancellationToken = default)
        => Get<PlatformBusinessListDto>(
            $"api/v1/admin/businesses?q={E(search)}&municipality={E(municipality)}&status={E(status)}&module={E(module)}",
            cancellationToken);
    public Task<PlatformBusinessDto> GetPlatformBusinessAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => Get<PlatformBusinessDto>($"api/v1/admin/businesses/{businessId}", cancellationToken);
    public async Task<PlatformBusinessCreatedDto> CreatePlatformBusinessAsync(
        CreatePlatformBusinessRequest request, CancellationToken cancellationToken = default)
        => await Read<PlatformBusinessCreatedDto>(await http.PostAsJsonAsync(
            "api/v1/admin/businesses", request, Json, cancellationToken), cancellationToken);
    public async Task<PlatformBusinessDto> UpdatePlatformBusinessAsync(Guid businessId,
        UpdatePlatformBusinessRequest request, CancellationToken cancellationToken = default)
        => await Read<PlatformBusinessDto>(await http.PutAsJsonAsync(
            $"api/v1/admin/businesses/{businessId}", request, Json, cancellationToken), cancellationToken);
    public async Task<PlatformBusinessDto> ChangePlatformBusinessStateAsync(Guid businessId, string action,
        PlatformBusinessStateRequest request, CancellationToken cancellationToken = default)
        => await Read<PlatformBusinessDto>(await http.PostAsJsonAsync(
            $"api/v1/admin/businesses/{businessId}/{E(action)}", request, Json, cancellationToken), cancellationToken);
    public async Task<PlatformBusinessDto> UpdatePlatformModulesAsync(Guid businessId,
        UpdatePlatformModulesRequest request, CancellationToken cancellationToken = default)
        => await Read<PlatformBusinessDto>(await http.PutAsJsonAsync(
            $"api/v1/admin/businesses/{businessId}/modules", request, Json, cancellationToken), cancellationToken);

    public async Task<PlatformBusinessDto> SavePlatformBusinessProfileAsync(Guid businessId,
        SaveBusinessProfileRequest request, CancellationToken cancellationToken = default)
        => await Read<PlatformBusinessDto>(await http.PutAsJsonAsync(
            $"api/v1/admin/businesses/{businessId}/profile", request, Json, cancellationToken), cancellationToken);
    public async Task<PlatformBusinessDto> SubmitBusinessForReviewAsync(Guid businessId,
        SubmitForReviewRequest request, CancellationToken cancellationToken = default)
        => await Read<PlatformBusinessDto>(await http.PostAsJsonAsync(
            $"api/v1/admin/businesses/{businessId}/submit-review", request, Json, cancellationToken), cancellationToken);
    public async Task<PlatformBusinessDto> RejectBusinessReviewAsync(Guid businessId, RejectReviewRequest request,
        CancellationToken cancellationToken = default)
        => await Read<PlatformBusinessDto>(await http.PostAsJsonAsync(
            $"api/v1/admin/businesses/{businessId}/reject-review", request, Json, cancellationToken), cancellationToken);
    public Task<BusinessProfileDto> PreviewBusinessAsync(Guid businessId, CancellationToken cancellationToken = default)
        => Get<BusinessProfileDto>($"api/v1/admin/businesses/{businessId}/preview", cancellationToken);
    public Task<IReadOnlyList<BusinessStatusChangeDto>> GetBusinessStatusHistoryAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<BusinessStatusChangeDto>>(
            $"api/v1/admin/businesses/{businessId}/status-history", cancellationToken);
    public Task<IReadOnlyList<PlatformAuditEntryDto>> GetBusinessAuditAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<PlatformAuditEntryDto>>($"api/v1/admin/businesses/{businessId}/audit", cancellationToken);
    public Task<IReadOnlyList<BusinessHourAdminDto>> GetPlatformBusinessHoursAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<BusinessHourAdminDto>>($"api/v1/admin/businesses/{businessId}/hours", cancellationToken);
    public async Task<ConfigurationImpactDto> SetPlatformBusinessHourAsync(Guid businessId, DayOfWeek day,
        SaveBusinessHourRequest request, CancellationToken cancellationToken = default)
        => await Read<ConfigurationImpactDto>(await http.PutAsJsonAsync(
            $"api/v1/admin/businesses/{businessId}/hours/{day}", request, Json, cancellationToken), cancellationToken);
    public Task<IReadOnlyList<StaffMemberDto>> GetPlatformSchedulingStaffAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<StaffMemberDto>>($"api/v1/admin/businesses/{businessId}/scheduling-staff", cancellationToken);
    public Task<IReadOnlyList<AvailabilityExceptionDto>> GetPlatformSchedulingExceptionsAsync(Guid businessId,
        DateOnly? from = null, CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<AvailabilityExceptionDto>>(
            $"api/v1/admin/businesses/{businessId}/scheduling-exceptions?from={from:yyyy-MM-dd}", cancellationToken);
    public async Task<AvailabilityExceptionDto> SavePlatformSchedulingExceptionAsync(Guid businessId,
        SaveAvailabilityExceptionRequest request, CancellationToken cancellationToken = default)
        => await Read<AvailabilityExceptionDto>(await http.PostAsJsonAsync(
            $"api/v1/admin/businesses/{businessId}/scheduling-exceptions", request, Json, cancellationToken), cancellationToken);
    public async Task DeletePlatformSchedulingExceptionAsync(Guid businessId, Guid exceptionId, long version,
        CancellationToken cancellationToken = default)
        => await Ensure(await http.DeleteAsync(
            $"api/v1/admin/businesses/{businessId}/scheduling-exceptions/{exceptionId}?version={version}",
            cancellationToken), cancellationToken);

    public Task<PlatformBusinessDto> GetOwnerProfileAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => Get<PlatformBusinessDto>($"api/v1/businesses/{businessId}/profile", cancellationToken);
    public async Task<PlatformBusinessDto> SaveOwnerProfileAsync(Guid businessId,
        SaveOwnerProfileRequest request, CancellationToken cancellationToken = default)
        => await Read<PlatformBusinessDto>(await http.PutAsJsonAsync(
            $"api/v1/businesses/{businessId}/profile", request, Json, cancellationToken), cancellationToken);
    public Task<IReadOnlyList<BusinessImageDto>> GetOwnerImagesAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<BusinessImageDto>>($"api/v1/businesses/{businessId}/images", cancellationToken);
    public Task<IReadOnlyList<BusinessImageDto>> GetOwnerCatalogImagesAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<BusinessImageDto>>($"api/v1/businesses/{businessId}/catalog-images", cancellationToken);
    public async Task<BusinessImageDto> UploadOwnerImageAsync(Guid businessId, string kind, string fileName,
        string contentType, byte[] content, string? altText, Guid? targetId = null,
        CancellationToken cancellationToken = default)
        => await Read<BusinessImageDto>(await http.PostAsync($"api/v1/businesses/{businessId}/images",
            ImageForm(kind, fileName, contentType, content, altText, targetId), cancellationToken),
            cancellationToken);
    public async Task RemoveOwnerImageAsync(Guid businessId, Guid imageId, long version,
        CancellationToken cancellationToken = default)
        => await Ensure(await http.DeleteAsync(
            $"api/v1/businesses/{businessId}/images/{imageId}?version={version}", cancellationToken),
            cancellationToken);

    public Task<IReadOnlyList<BusinessImageDto>> GetBusinessImagesAsync(Guid businessId,
        CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<BusinessImageDto>>($"api/v1/admin/businesses/{businessId}/images", cancellationToken);
    public async Task<BusinessImageDto> UploadBusinessImageAsync(Guid businessId, string kind, string fileName,
        string contentType, byte[] content, string? altText, Guid? targetId = null,
        CancellationToken cancellationToken = default)
        => await Read<BusinessImageDto>(await http.PostAsync($"api/v1/admin/businesses/{businessId}/images",
            ImageForm(kind, fileName, contentType, content, altText, targetId), cancellationToken),
            cancellationToken);

    /// <summary>El cuerpo multipart que esperan las dos rutas de subida, propietaria y administrativa.</summary>
    private static MultipartFormDataContent ImageForm(string kind, string fileName, string contentType,
        byte[] content, string? altText, Guid? targetId)
    {
        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(content);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(part, "file", fileName);
        form.Add(new StringContent(kind), "kind");
        if (!string.IsNullOrWhiteSpace(altText)) form.Add(new StringContent(altText), "altText");
        if (targetId is { } target) form.Add(new StringContent(target.ToString()), "targetId");
        return form;
    }
    public async Task<BusinessImageDto> UpdateBusinessImageAsync(Guid businessId, Guid imageId,
        UpdateBusinessImageRequest request, CancellationToken cancellationToken = default)
        => await Read<BusinessImageDto>(await http.PutAsJsonAsync(
            $"api/v1/admin/businesses/{businessId}/images/{imageId}", request, Json, cancellationToken), cancellationToken);
    public async Task RemoveBusinessImageAsync(Guid businessId, Guid imageId, long version,
        CancellationToken cancellationToken = default)
        => await Ensure(await http.DeleteAsync(
            $"api/v1/admin/businesses/{businessId}/images/{imageId}?version={version}", cancellationToken),
            cancellationToken);

    public async Task<InvitationIssuedDto> CreateInvitationAsync(CreateInvitationRequest request,
        CancellationToken cancellationToken = default)
        => await Read<InvitationIssuedDto>(await http.PostAsJsonAsync(
            "api/v1/admin/invitations", request, Json, cancellationToken), cancellationToken);
    public Task<IReadOnlyList<InvitationDto>> GetInvitationsAsync(Guid? businessId = null,
        CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<InvitationDto>>(
            $"api/v1/admin/invitations{(businessId is { } id ? $"?businessId={id}" : "")}", cancellationToken);
    public async Task<InvitationIssuedDto> ResendInvitationAsync(Guid invitationId,
        CancellationToken cancellationToken = default)
        => await Read<InvitationIssuedDto>(await http.PostAsync(
            $"api/v1/admin/invitations/{invitationId}/resend", null, cancellationToken), cancellationToken);
    public async Task RevokeInvitationAsync(Guid invitationId, CancellationToken cancellationToken = default)
        => await Ensure(await http.DeleteAsync($"api/v1/admin/invitations/{invitationId}", cancellationToken),
            cancellationToken);
    public async Task<InvitationIssuedDto> ResetAccessAsync(ResetAccessRequest request,
        CancellationToken cancellationToken = default)
        => await Read<InvitationIssuedDto>(await http.PostAsJsonAsync(
            "api/v1/admin/access-resets", request, Json, cancellationToken), cancellationToken);
    public Task<IReadOnlyList<PlatformAccountDto>> GetPartnerOperatorsAsync(CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<PlatformAccountDto>>("api/v1/admin/partner-operators", cancellationToken);
    public async Task RevokePartnerOperatorAsync(Guid userId, CancellationToken cancellationToken = default)
        => await Ensure(await http.DeleteAsync($"api/v1/admin/partner-operators/{userId}", cancellationToken),
            cancellationToken);
    public Task<IReadOnlyList<PlatformAccessAuditDto>> GetAccessAuditAsync(Guid? businessId = null,
        CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<PlatformAccessAuditDto>>(
            $"api/v1/admin/access-audit{(businessId is { } id ? $"?businessId={id}" : "")}", cancellationToken);
    public Task<PlatformHealthDto> GetPlatformHealthAsync(CancellationToken cancellationToken = default)
        => Get<PlatformHealthDto>("api/v1/admin/health", cancellationToken);
    public Task<LegalInfoDto> GetLegalInfoAsync(CancellationToken cancellationToken = default)
        => Get<LegalInfoDto>("api/v1/public/legal", cancellationToken);

    private async Task<BusinessMemberDto> PostVersion(Guid businessId, Guid membershipId, string action, long version,
        CancellationToken cancellationToken)
        => await Read<BusinessMemberDto>(await http.PostAsJsonAsync(
            $"api/v1/businesses/{businessId}/memberships/{membershipId}/{action}",
            new MembershipVersionRequest { Version = version }, Json, cancellationToken), cancellationToken);
    private async Task<QueueAdminDto> QueueSessionCommand(Guid businessId, string action, long version,
        CancellationToken cancellationToken)
        => await Read<QueueAdminDto>(await http.PostAsJsonAsync($"api/v1/businesses/{businessId}/queue/{action}",
            new QueueSessionCommandRequest { Version = version }, Json, cancellationToken), cancellationToken);

    private async Task<T> Get<T>(string url, CancellationToken ct) => await Read<T>(await http.GetAsync(url, ct), ct);
    private static async Task<T> Read<T>(HttpResponseMessage response, CancellationToken ct)
    {
        await Ensure(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
            ?? throw new ApiException("EMPTY_RESPONSE", "El servidor devolvió una respuesta vacía.", 500);
    }
    private static async Task Ensure(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string message = "No fue posible completar la solicitud.";
        string code = $"HTTP_{(int)response.StatusCode}";
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
            if (problem.TryGetProperty("title", out var title)) message = title.GetString() ?? message;
            if (problem.TryGetProperty("code", out var errorCode)) code = errorCode.GetString() ?? code;
        }
        catch (JsonException) { }
        throw new ApiException(code, message, (int)response.StatusCode);
    }
    private static string E(string? value) => Uri.EscapeDataString(value ?? "");
}
