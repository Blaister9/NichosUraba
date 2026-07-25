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
    public Task<IReadOnlyList<MyBusinessDto>> GetMyBusinessesAsync(CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<MyBusinessDto>>("api/v1/businesses/mine", cancellationToken);
    public Task<IReadOnlyList<AppointmentAdminDto>> GetAppointmentsAsync(Guid businessId, DateOnly? date = null,
        string? status = null, CancellationToken cancellationToken = default)
        => Get<IReadOnlyList<AppointmentAdminDto>>($"api/v1/businesses/{businessId}/appointments?date={date:yyyy-MM-dd}&status={E(status)}", cancellationToken);
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

    private async Task<BusinessMemberDto> PostVersion(Guid businessId, Guid membershipId, string action, long version,
        CancellationToken cancellationToken)
        => await Read<BusinessMemberDto>(await http.PostAsJsonAsync(
            $"api/v1/businesses/{businessId}/memberships/{membershipId}/{action}",
            new MembershipVersionRequest { Version = version }, Json, cancellationToken), cancellationToken);

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
