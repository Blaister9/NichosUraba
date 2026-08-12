using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using UrabaConecta.Application;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Infrastructure.Persistence;

/// <summary>
/// Agregaciones del resumen operativo. Cada método es una ida a la base para TODOS los negocios, no
/// una por negocio: el panel de una socia con quince negocios cuesta lo mismo que el de una
/// propietaria con uno. Nada de esto materializa filas de operación; sólo cuentas y, en el caso de la
/// próxima cita, una fila diminuta por negocio.
/// </summary>
public sealed class OwnerDashboardStore(AppDbContext db) : IOwnerDashboardStore
{
    public async Task<IReadOnlyDictionary<Guid, AppointmentsSummaryDto>> AppointmentsAsync(
        IReadOnlyCollection<BusinessDayWindow> windows, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        // Los cuatro contadores miran el día entero de cada negocio, así que la ventana es toda la
        // condición: dentro del grupo ya no hace falta volver a preguntar por fechas.
        var delDia = WithinItsOwnDay<Appointment>(windows, x => x.StartAtUtc);

        var contadores = await db.Appointments.AsNoTracking().Where(delDia)
            .GroupBy(x => x.BusinessId)
            .Select(g => new
            {
                BusinessId = g.Key,
                Total = g.Count(),
                Pending = g.Count(x => x.Status == AppointmentStatus.Pending),
                Confirmed = g.Count(x => x.Status == AppointmentStatus.Confirmed),
                Completed = g.Count(x => x.Status == AppointmentStatus.Completed)
            })
            .ToListAsync(cancellationToken);

        // La próxima cita va aparte porque mira otro corte del mismo rango —de ahora en adelante— y
        // necesita el ServiceName de la MISMA fila, no un Min(StartAtUtc) suelto que podría emparejar
        // la hora de una cita con el servicio de otra. Sigue siendo una sola sentencia para todos los
        // negocios: se agrupa por negocio y de cada grupo se toma la primera por hora.
        var proximas = await db.Appointments.AsNoTracking().Where(delDia)
            .Where(x => x.StartAtUtc >= nowUtc)
            // Rechazada, cancelada, completada y no asistió ya no son "lo que viene".
            .Where(x => x.Status == AppointmentStatus.Pending || x.Status == AppointmentStatus.Confirmed)
            .Select(x => new { x.BusinessId, x.StartAtUtc, x.ServiceName })
            .GroupBy(x => x.BusinessId)
            .Select(g => g.OrderBy(x => x.StartAtUtc).First())
            .ToListAsync(cancellationToken);

        var proximaPorNegocio = proximas.ToDictionary(x => x.BusinessId);
        return contadores.ToDictionary(x => x.BusinessId, x =>
        {
            proximaPorNegocio.TryGetValue(x.BusinessId, out var proxima);
            return new AppointmentsSummaryDto(x.Total, x.Pending, x.Confirmed, x.Completed,
                proxima?.StartAtUtc, proxima?.ServiceName);
        });
    }

    public async Task<IReadOnlyDictionary<Guid, QueueSummaryDto>> QueuesAsync(
        IReadOnlyCollection<BusinessDayWindow> windows, CancellationToken cancellationToken = default)
    {
        // Esperando y en atención son estados vivos: valen sin importar el día. Atendidos hoy sí
        // depende del día, y se apoya en CompletedAtUtc, que se escribe exactamente al completar el
        // turno. Las dos condiciones se unen con un OR en el WHERE en lugar de repetir la ventana
        // dentro de cada contador: así el grupo sólo tiene que contar por estado.
        var negocios = windows.Select(x => x.BusinessId).ToList();
        var filtro = Or<QueueTicket>(
            x => negocios.Contains(x.BusinessId) &&
                 (x.Status == QueueTicketStatus.Waiting || x.Status == QueueTicketStatus.InService),
            And(WithinItsOwnDay<QueueTicket>(windows, x => x.CompletedAtUtc),
                x => x.Status == QueueTicketStatus.Completed));

        var filas = await db.QueueTickets.AsNoTracking().Where(filtro)
            .GroupBy(x => x.BusinessId)
            .Select(g => new
            {
                BusinessId = g.Key,
                Waiting = g.Count(x => x.Status == QueueTicketStatus.Waiting),
                InService = g.Count(x => x.Status == QueueTicketStatus.InService),
                // Sólo llegaron aquí los completados dentro de la ventana del negocio.
                ServedToday = g.Count(x => x.Status == QueueTicketStatus.Completed),
                CurrentTicket = g.Min(x => x.Status == QueueTicketStatus.InService ? (int?)x.Number : null)
            })
            .ToListAsync(cancellationToken);

        return filas.ToDictionary(x => x.BusinessId,
            x => new QueueSummaryDto(x.Waiting, x.InService, x.ServedToday, x.CurrentTicket));
    }

    public async Task<IReadOnlyDictionary<Guid, OrdersSummaryDto>> OrdersAsync(
        IReadOnlyCollection<BusinessDayWindow> windows, CancellationToken cancellationToken = default)
    {
        // "Entregados hoy" se apoya en UpdatedAtUtc, y eso sólo es correcto porque Transition es el
        // único mutador de PickupOrder y Delivered es terminal: en un pedido entregado esa marca ES
        // el instante de la entrega, porque después de ella ya nada vuelve a tocar la fila. La prueba
        // de dominio Delivered_is_terminal_because_a_metric_depends_on_it sostiene esa propiedad; si
        // alguien abre una transición desde Delivered, esa prueba falla antes de que este número
        // empiece a mentir en silencio.
        var negocios = windows.Select(x => x.BusinessId).ToList();
        var filtro = Or<PickupOrder>(
            x => negocios.Contains(x.BusinessId) &&
                 (x.Status == PickupOrderStatus.Pending || x.Status == PickupOrderStatus.Preparing ||
                  x.Status == PickupOrderStatus.ReadyForPickup),
            And(WithinItsOwnDay<PickupOrder>(windows, x => x.UpdatedAtUtc),
                x => x.Status == PickupOrderStatus.Delivered));

        var filas = await db.PickupOrders.AsNoTracking().Where(filtro)
            .GroupBy(x => x.BusinessId)
            .Select(g => new
            {
                BusinessId = g.Key,
                Pending = g.Count(x => x.Status == PickupOrderStatus.Pending),
                Preparing = g.Count(x => x.Status == PickupOrderStatus.Preparing),
                Ready = g.Count(x => x.Status == PickupOrderStatus.ReadyForPickup),
                // Sólo llegaron aquí los entregados dentro de la ventana del negocio.
                DeliveredToday = g.Count(x => x.Status == PickupOrderStatus.Delivered)
            })
            .ToListAsync(cancellationToken);

        return filas.ToDictionary(x => x.BusinessId,
            x => new OrdersSummaryDto(x.Pending, x.Preparing, x.Ready, x.DeliveredToday));
    }

    // ---------------------------------------------------------------------------------------------
    // Composición de la condición
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// La ventana propia de cada negocio en una sola condición: OR de (negocio = A AND marca dentro
    /// del rango de A). Crece en parámetros, nunca en idas a la base, y respeta que dos negocios en
    /// zonas horarias distintas no comparten el mismo "hoy": una entrega de las 11 p. m. puede ser de
    /// hoy para uno y de ayer para el otro.
    /// </summary>
    private static Expression<Func<T, bool>> WithinItsOwnDay<T>(
        IReadOnlyCollection<BusinessDayWindow> windows,
        Expression<Func<T, DateTimeOffset>> timestamp) where T : IBusinessOwned
        => WithinItsOwnDay<T>(windows, timestamp.Parameters[0], timestamp.Body);

    /// <summary>Igual, para marcas opcionales: una fila sin marca no cae en ninguna ventana.</summary>
    private static Expression<Func<T, bool>> WithinItsOwnDay<T>(
        IReadOnlyCollection<BusinessDayWindow> windows,
        Expression<Func<T, DateTimeOffset?>> timestamp) where T : IBusinessOwned
        => WithinItsOwnDay<T>(windows, timestamp.Parameters[0], timestamp.Body);

    private static Expression<Func<T, bool>> WithinItsOwnDay<T>(
        IReadOnlyCollection<BusinessDayWindow> windows, ParameterExpression fila, Expression marca)
        where T : IBusinessOwned
    {
        var negocio = Expression.Property(fila, nameof(IBusinessOwned.BusinessId));

        Expression? acumulado = null;
        foreach (var window in windows)
        {
            var clausula = Expression.AndAlso(
                Expression.AndAlso(
                    Expression.Equal(negocio, Parameter(window.BusinessId)),
                    Expression.GreaterThanOrEqual(marca, Parameter(window.FromUtc, marca.Type))),
                Expression.LessThan(marca, Parameter(window.ToUtc, marca.Type)));
            acumulado = acumulado is null ? clausula : Expression.OrElse(acumulado, clausula);
        }

        // Sin ventanas no hay negocio que consultar: una condición imposible devuelve cero filas sin
        // inventar un WHERE vacío que traería la tabla entera.
        return Expression.Lambda<Func<T, bool>>(acumulado ?? Expression.Constant(false), fila);
    }

    /// <summary>
    /// EF convierte en parámetro lo que lee de un objeto, y en literal incrustado lo que ve como
    /// constante. Los valores de la ventana pasan por aquí para que quince negocios generen el mismo
    /// SQL con distintos parámetros, y no quince sentencias distintas que ensucian el caché de planes.
    /// </summary>
    private static Expression Parameter<TValue>(TValue value, Type? asType = null)
    {
        var caja = Expression.Property(Expression.Constant(new Boxed<TValue>(value)), nameof(Boxed<TValue>.Value));
        return asType is null || asType == typeof(TValue) ? caja : Expression.Convert(caja, asType);
    }

    private sealed class Boxed<TValue>(TValue value)
    {
        public TValue Value { get; } = value;
    }

    private static Expression<Func<T, bool>> Or<T>(Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right)
        => Join(left, right, Expression.OrElse);

    private static Expression<Func<T, bool>> And<T>(Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right)
        => Join(left, right, Expression.AndAlso);

    /// <summary>
    /// Une dos condiciones reescribiendo la segunda sobre el parámetro de la primera. Sin esto EF ve
    /// dos filas distintas en la misma condición y no la puede traducir.
    /// </summary>
    private static Expression<Func<T, bool>> Join<T>(Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right, Func<Expression, Expression, BinaryExpression> combine)
    {
        var fila = left.Parameters[0];
        var derecha = new Rebind(right.Parameters[0], fila).Visit(right.Body);
        return Expression.Lambda<Func<T, bool>>(combine(left.Body, derecha), fila);
    }

    private sealed class Rebind(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : node;
    }
}
