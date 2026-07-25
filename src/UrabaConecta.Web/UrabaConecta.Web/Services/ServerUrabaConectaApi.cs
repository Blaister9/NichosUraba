using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using UrabaConecta.Application;
using UrabaConecta.Contracts;

namespace UrabaConecta.Web.Services;

public sealed class ServerUrabaConectaApi(IUrabaUseCases useCases, AuthenticationStateProvider authentication) : IUrabaConectaApi
{
    public Task<IReadOnlyList<BusinessCardDto>> GetBusinessesAsync(string? search = null, string? municipality = null,
        string? category = null, CancellationToken cancellationToken = default)
        => useCases.GetBusinessesAsync(search, municipality, category, cancellationToken);
    public Task<BusinessProfileDto?> GetBusinessAsync(string slug, CancellationToken cancellationToken = default)
        => useCases.GetBusinessAsync(slug, cancellationToken);
    public Task<SlotListDto> GetSlotsAsync(string slug, Guid serviceId, DateOnly date, CancellationToken cancellationToken = default)
        => useCases.GetSlotsAsync(slug, serviceId, date, cancellationToken);
    public Task<AppointmentCreatedDto> CreateAppointmentAsync(string slug, CreateAppointmentRequest request,
        CancellationToken cancellationToken = default) => useCases.CreateAppointmentAsync(slug, request, cancellationToken);
    public Task<AppointmentTrackingDto?> GetAppointmentTrackingAsync(string code, CancellationToken cancellationToken = default)
        => useCases.GetTrackingAsync(code, cancellationToken);
    public Task CancelAppointmentAsync(string code, CancellationToken cancellationToken = default)
        => useCases.CancelAsync(code, cancellationToken);
    public async Task<IReadOnlyList<MyBusinessDto>> GetMyBusinessesAsync(CancellationToken cancellationToken = default)
        => await useCases.GetMyBusinessesAsync(await UserId(), cancellationToken);
    public async Task<IReadOnlyList<AppointmentAdminDto>> GetAppointmentsAsync(Guid businessId, DateOnly? date = null,
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

    private async Task<Guid> UserId()
    {
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        return Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    }
}
