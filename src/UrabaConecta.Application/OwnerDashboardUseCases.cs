using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Application;

/// <summary>Ventana del día local de un negocio, ya convertida a UTC para consultar.</summary>
public sealed record BusinessDayWindow(Guid BusinessId, DateTimeOffset FromUtc, DateTimeOffset ToUtc);

/// <summary>
/// Consultas agregadas del resumen operativo. Reciben TODOS los negocios de una vez a propósito: el
/// panel de una socia con quince negocios tiene que costar lo mismo que el de una propietaria con
/// uno. Cada método es una sola ida a la base, y ninguno devuelve filas de operación, sólo cuentas.
/// </summary>
public interface IOwnerDashboardStore
{
    /// <summary>
    /// <paramref name="nowUtc"/> viaja aparte de la ventana porque los contadores miran el día
    /// entero y la próxima cita sólo mira de ahora en adelante: son dos cortes distintos sobre el
    /// mismo rango, y mezclarlos daría un "próxima cita" que ya pasó.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, AppointmentsSummaryDto>> AppointmentsAsync(
        IReadOnlyCollection<BusinessDayWindow> windows, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, QueueSummaryDto>> QueuesAsync(
        IReadOnlyCollection<BusinessDayWindow> windows, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, OrdersSummaryDto>> OrdersAsync(
        IReadOnlyCollection<BusinessDayWindow> windows, CancellationToken cancellationToken = default);
}

public interface IOwnerDashboardUseCases
{
    /// <summary>
    /// Resume los negocios que YA fueron resueltos como suyos. Recibe la lista en vez de volver a
    /// pedirla para dos cosas: no repetir la consulta de membresías que el panel acaba de hacer, y
    /// dejar claro que el alcance lo fija el servidor —el cliente nunca manda identificadores.
    /// </summary>
    Task<IReadOnlyList<OwnerDashboardSummaryDto>> SummarizeAsync(IReadOnlyList<MyBusinessDto> mine,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Arma el resumen de los negocios que la persona opera. El cliente no elige de qué negocios pide
/// métricas: los decide el servidor a partir de sus membresías, así que no hay lista de identificadores
/// que falsificar.
/// </summary>
public sealed class OwnerDashboardUseCases(
    IOwnerDashboardStore store,
    IBusinessTimeZoneResolver zones,
    TimeProvider clock,
    IOwnerDashboardDiagnostics diagnostics) : IOwnerDashboardUseCases
{
    public async Task<IReadOnlyList<OwnerDashboardSummaryDto>> SummarizeAsync(
        IReadOnlyList<MyBusinessDto> mine, CancellationToken cancellationToken = default)
    {
        if (mine.Count == 0) return [];

        var now = clock.GetUtcNow();
        var timeZones = await zones.ResolveAsync(mine.Select(x => x.Id).ToList(), cancellationToken);

        // Cada negocio aporta su propia ventana: "hoy" en Turbo y "hoy" en Apartadó son el mismo día,
        // pero el rango se calcula por negocio para no atarlo a una zona escrita a mano.
        BusinessDayWindow Window(Guid id) => LocalDay(id, timeZones.GetValueOrDefault(id), now, diagnostics);

        var conCitas = mine.Where(x => x.ShowAppointments).Select(x => Window(x.Id)).ToList();
        var conTurnos = mine.Where(x => x.ShowQueues).Select(x => Window(x.Id)).ToList();
        var conPedidos = mine.Where(x => x.ShowOrders).Select(x => Window(x.Id)).ToList();

        // Como mucho tres agregaciones, y sólo de los módulos que alguien tiene encendidos. El número
        // de consultas no crece con la cantidad de negocios: crece —hasta tres— con la de módulos.
        var citas = conCitas.Count > 0
            ? await store.AppointmentsAsync(conCitas, now, cancellationToken)
            : EmptyOf<AppointmentsSummaryDto>();
        var turnos = conTurnos.Count > 0
            ? await store.QueuesAsync(conTurnos, cancellationToken)
            : EmptyOf<QueueSummaryDto>();
        var pedidos = conPedidos.Count > 0
            ? await store.OrdersAsync(conPedidos, cancellationToken)
            : EmptyOf<OrdersSummaryDto>();

        return mine.Select(business => new OwnerDashboardSummaryDto(
            business.Id, business.Name,
            business.ShowAppointments
                ? citas.GetValueOrDefault(business.Id) ?? EmptyAppointments
                : null,
            business.ShowQueues
                ? turnos.GetValueOrDefault(business.Id) ?? EmptyQueues
                : null,
            business.ShowOrders
                ? pedidos.GetValueOrDefault(business.Id) ?? EmptyOrders
                : null)).ToList();
    }

    /// <summary>
    /// El día local del negocio llevado a UTC. Se calcula una vez por negocio y viaja a la consulta
    /// como un rango, en lugar de convertir la hora de cada fila.
    /// </summary>
    public static BusinessDayWindow LocalDay(Guid businessId, string? timeZoneId, DateTimeOffset nowUtc,
        IOwnerDashboardDiagnostics? diagnostics = null)
    {
        var zone = Resolve(timeZoneId, businessId, diagnostics);
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, zone);
        var startLocal = DateTime.SpecifyKind(localNow.Date, DateTimeKind.Unspecified);
        var from = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(startLocal, zone));
        var to = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(startLocal.AddDays(1), zone));
        return new(businessId, from, to);
    }

    private static TimeZoneInfo Resolve(string? timeZoneId, Guid businessId,
        IOwnerDashboardDiagnostics? diagnostics)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return Bogota;
        // Una zona mal escrita en un negocio no puede tumbar el panel de los demás. Se cae a Bogotá
        // para que ese negocio siga teniendo métricas, pero se avisa: un panel que calla el error
        // muestra el día equivocado sin que nadie se entere de por qué.
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (TimeZoneNotFoundException) { diagnostics?.InvalidTimeZone(businessId, timeZoneId); return Bogota; }
        catch (InvalidTimeZoneException) { diagnostics?.InvalidTimeZone(businessId, timeZoneId); return Bogota; }
    }

    private static readonly TimeZoneInfo Bogota = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");

    private static readonly AppointmentsSummaryDto EmptyAppointments = new(0, 0, 0, 0, null, null);
    private static readonly QueueSummaryDto EmptyQueues = new(0, 0, 0, null);
    private static readonly OrdersSummaryDto EmptyOrders = new(0, 0, 0, 0);

    private static IReadOnlyDictionary<Guid, T> EmptyOf<T>() => new Dictionary<Guid, T>();
}

/// <summary>Zona horaria configurada de cada negocio, leída de una sola vez.</summary>
public interface IBusinessTimeZoneResolver
{
    Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(IReadOnlyCollection<Guid> businessIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Avisos del panel que no interrumpen la respuesta. Es un puerto y no un registrador directo para
/// que esta capa no dependa de la infraestructura de trazas, y para que una prueba pueda afirmar que
/// el aviso se emitió en vez de leer la salida del proceso.
/// </summary>
public interface IOwnerDashboardDiagnostics
{
    /// <summary>
    /// El negocio tiene configurada una zona horaria que el sistema no reconoce. Se resuelve con
    /// Bogotá y el panel continúa para todos los demás. Sólo viajan el identificador del negocio y el
    /// valor rechazado: nada de esto describe a una persona.
    /// </summary>
    void InvalidTimeZone(Guid businessId, string timeZoneId);
}
