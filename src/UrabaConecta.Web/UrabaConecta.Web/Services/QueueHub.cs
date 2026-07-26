using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using UrabaConecta.Application;

namespace UrabaConecta.Web.Services;

public sealed class QueueHub(IQueueStore store, IPublicCodeService codes) : Hub
{
    public async Task SubscribePublic(string slug)
    {
        var definition = await store.GetPublicDefinitionAsync(slug, Context.ConnectionAborted)
            ?? throw new HubException("Fila no disponible.");
        await Groups.AddToGroupAsync(Context.ConnectionId, PublicGroup(definition.Id));
    }

    public async Task SubscribeTicket(string trackingCode)
    {
        var ticket = await store.FindTicketAsync(codes.Hash(trackingCode), Context.ConnectionAborted)
            ?? throw new HubException("Turno no encontrado.");
        await Groups.AddToGroupAsync(Context.ConnectionId, TicketGroup(ticket.Ticket.Id));
    }

    public async Task SubscribeOperations(Guid businessId)
    {
        var raw = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(raw, out var userId) ||
            !await store.CanManageQueuesAsync(userId, businessId, Context.ConnectionAborted))
            throw new HubException("No autorizado.");
        await Groups.AddToGroupAsync(Context.ConnectionId, OperationsGroup(businessId));
    }

    public static string PublicGroup(Guid id) => $"queue-public:{id:N}";
    public static string TicketGroup(Guid id) => $"queue-ticket:{id:N}";
    public static string OperationsGroup(Guid id) => $"queue-operations:{id:N}";
}

public sealed class SignalRQueueChangeNotifier(IHubContext<QueueHub> hub) : IQueueChangeNotifier
{
    public Task PublicChangedAsync(Guid definitionId, CancellationToken ct)
        => hub.Clients.Group(QueueHub.PublicGroup(definitionId)).SendAsync("QueueChanged", cancellationToken: ct);
    public Task TicketChangedAsync(Guid ticketId, CancellationToken ct)
        => hub.Clients.Group(QueueHub.TicketGroup(ticketId)).SendAsync("TicketChanged", cancellationToken: ct);
    public Task OperationsChangedAsync(Guid businessId, CancellationToken ct)
        => hub.Clients.Group(QueueHub.OperationsGroup(businessId)).SendAsync("OperationsChanged", cancellationToken: ct);
}
