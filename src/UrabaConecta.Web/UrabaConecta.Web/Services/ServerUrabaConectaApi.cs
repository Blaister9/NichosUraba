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
    public async Task<ServiceDto> UpdateServiceAsync(Guid businessId, Guid serviceId, UpdateServiceRequest request,
        CancellationToken cancellationToken = default)
        => await useCases.UpdateServiceAsync(await UserId(), businessId, serviceId, request, cancellationToken);

    private async Task<Guid> UserId()
    {
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        return Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    }
}
