using UrabaConecta.Contracts;

namespace UrabaConecta.Web.Client.Components;

/// <summary>Un hito real de la historia del pedido. Nunca se fabrica: cada uno tiene su hora.</summary>
/// <param name="Id">Identidad estable del hecho. La del aviso guardado, o la del hito derivado.</param>
/// <param name="Kind">El tipo del aviso cuando el hito viene de uno. Nulo cuando se deriva.</param>
public sealed record PickupOrderMilestone(string Id, string Status, string Title, string Body,
    DateTimeOffset AtUtc, string? Kind);

/// <summary>
/// La historia de un pedido, armada con lo que ya existe.
///
/// POR QUÉ NO HAY TABLA NUEVA. El dominio guarda un único <c>Status</c> y su <c>UpdatedAtUtc</c>:
/// no hay historial de transiciones. Pero cada cambio de estado publica un aviso al cliente, y esa
/// fila sí se guarda con su hora y con una clave única por (audiencia, tipo, pedido). Es decir: ya
/// existía un registro append-only, uno por etapa, imposible de duplicar. La historia se lee de
/// ahí. Inventar timestamps o crear una tabla de auditoría sería sustituir un dato real por uno
/// fabricado.
///
/// LAS DOS COSTURAS. El aviso cubre lo que hace el negocio, no lo que hace la persona. Faltan dos
/// hitos, y los dos tienen fecha verdadera en el propio pedido: el momento en que se creó
/// (<c>CreatedAtUtc</c>) y una cancelación hecha desde el enlace público, que cambia el estado sin
/// publicar aviso. Por eso el estado actual siempre se representa, con <c>UpdatedAtUtc</c> si no
/// llegó por aviso: el resumen de arriba y la historia de abajo no pueden contar cosas distintas.
/// </summary>
public static class PickupOrderTimeline
{
    /// <summary>El orden verdadero del recorrido. Sólo desempata hitos con la misma hora.</summary>
    private static readonly string[] Recorrido =
        ["Pending", "Accepted", "Preparing", "ReadyForPickup", "Delivered", "Rejected", "Cancelled"];

    private static string? EstadoDe(string kind) => kind switch
    {
        "OrderAccepted" => "Accepted",
        "OrderPreparing" => "Preparing",
        "OrderReady" => "ReadyForPickup",
        "OrderDelivered" => "Delivered",
        "OrderRejected" => "Rejected",
        "OrderCancelled" => "Cancelled",
        _ => null
    };

    /// <summary>Un estado cerrado: ya no puede pasar nada más.</summary>
    public static bool EstaCerrado(string status) =>
        status is "Delivered" or "Rejected" or "Cancelled";

    /// <summary>Lo que falta. Nulo cuando el pedido ya está cerrado: entonces no falta nada.</summary>
    public static string? LoQueSigue(string status) => status switch
    {
        "Pending" => "Falta que el negocio confirme el pedido.",
        "Accepted" => "Falta que empiecen a prepararlo.",
        "Preparing" => "Falta que quede listo.",
        "ReadyForPickup" => "Falta que pases a recogerlo.",
        _ => null
    };

    /// <summary>Cuando el pedido espera algo de la persona, no del negocio.</summary>
    public static bool EsperaALaPersona(string status) => status == "ReadyForPickup";

    /// <summary>
    /// Los hitos en orden verdadero, del más antiguo al más reciente. Se deduplica por estado
    /// porque un pedido pasa una sola vez por cada etapa; la clave única del aviso ya lo garantiza
    /// en la base, y esto sostiene lo mismo si alguna vez llegara repetido.
    /// </summary>
    public static IReadOnlyList<PickupOrderMilestone> Construir(PickupOrderTrackingDto order,
        IReadOnlyList<NotificationDto> updates)
    {
        var hitos = new List<PickupOrderMilestone>();
        if (order.CreatedAtUtc != default)
            hitos.Add(new("recibido", "Pending", "Pedido recibido",
                $"Enviaste el pedido #{order.OrderNumber} y quedó registrado.", order.CreatedAtUtc, null));
        foreach (var aviso in updates)
        {
            var estado = EstadoDe(aviso.Kind);
            if (estado is null) continue;
            hitos.Add(new(aviso.Id.ToString(), estado, aviso.Title, aviso.Body, aviso.CreatedAtUtc, aviso.Kind));
        }
        var ordenados = hitos
            .OrderBy(x => x.AtUtc)
            .ThenBy(x => Array.IndexOf(Recorrido, x.Status))
            .GroupBy(x => x.Status).Select(g => g.First())
            .OrderBy(x => x.AtUtc)
            .ThenBy(x => Array.IndexOf(Recorrido, x.Status))
            .ToList();
        // La cancelación desde el enlace público no publica aviso, así que sin esto la historia se
        // quedaría en "aceptado" mientras el encabezado dice "cancelado".
        if (!ordenados.Any(x => x.Status == order.Status))
            ordenados.Add(new("estado-actual", order.Status, order.StatusLabel,
                TextoDerivado(order.Status), order.UpdatedAtUtc, null));
        return ordenados;
    }

    private static string TextoDerivado(string status) => status switch
    {
        "Cancelled" => "El pedido quedó cancelado.",
        "Rejected" => "El negocio no pudo tomar el pedido.",
        "Delivered" => "El pedido quedó entregado.",
        _ => "El pedido cambió de estado."
    };
}
