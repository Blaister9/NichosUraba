using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using UrabaConecta.Application;

namespace UrabaConecta.Web.Services;

/// <summary>
/// El circuito en vivo de pedidos, citas y bandeja. La fila virtual conserva su propio concentrador
/// —funciona y tiene su propio vocabulario de sesión y turno—, así que aquí no se toca.
///
/// Lo que viaja por el cable es una señal, nunca datos: "algo cambió en este canal". Quien la recibe
/// vuelve a pedir el estado por la API, que es donde vive la autorización de verdad. Así, perder la
/// conexión sólo cuesta inmediatez, y un mensaje mal dirigido no puede filtrar el contenido de una
/// operación ajena.
/// </summary>
public sealed class OperationsHub(IRealtimeAccessGuard guard) : Hub
{
    public async Task SubscribeBusiness(Guid businessId, string channel)
    {
        var raw = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(raw, out var userId) ||
            !await guard.CanSubscribeBusinessAsync(userId, businessId, channel, Context.ConnectionAborted))
            throw new HubException("No autorizado.");
        await Groups.AddToGroupAsync(Context.ConnectionId, BusinessGroup(businessId, channel));
    }

    /// <summary>
    /// El cliente se engancha con el mismo código con el que consulta su seguimiento. No hace falta
    /// cuenta: el código es su credencial, igual que en la página que ya está mirando.
    /// </summary>
    public async Task SubscribeTracking(string entityType, string trackingCode)
    {
        var entityId = await guard.ResolveTrackedEntityAsync(entityType, trackingCode, Context.ConnectionAborted)
            ?? throw new HubException("Operación no encontrada.");
        await Groups.AddToGroupAsync(Context.ConnectionId, TrackingGroup(entityType, entityId));
    }

    public static string BusinessGroup(Guid businessId, string channel) => $"ops:{businessId:N}:{channel}";
    public static string TrackingGroup(string entityType, Guid entityId)
        => $"track:{entityType}:{entityId:N}";
}

public sealed class SignalRRealtimeNotifier(IHubContext<OperationsHub> hub) : IRealtimeNotifier
{
    public Task BusinessChangedAsync(Guid businessId, string channel, CancellationToken cancellationToken = default)
        => hub.Clients.Group(OperationsHub.BusinessGroup(businessId, channel))
            .SendAsync("OperationsChanged", channel, cancellationToken: cancellationToken);

    public Task TrackingChangedAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default)
        => hub.Clients.Group(OperationsHub.TrackingGroup(entityType, entityId))
            .SendAsync("TrackingChanged", entityType, cancellationToken: cancellationToken);
}
