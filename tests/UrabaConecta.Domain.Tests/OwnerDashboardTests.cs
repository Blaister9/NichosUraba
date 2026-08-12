using UrabaConecta.Application;
using UrabaConecta.Contracts;

namespace UrabaConecta.Domain.Tests;

/// <summary>
/// El resumen operativo del propietario. Lo que se fija aquí no es el conteo —eso lo hace la base—
/// sino las tres decisiones de composición que pueden salir mal en silencio: que un negocio no reciba
/// métricas de otro, que un módulo apagado no aparezca como un cero, y que se pida exactamente una
/// agregación por familia de módulo por muchos negocios que haya.
/// </summary>
public sealed class OwnerDashboardTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 8, 11, 20, 30, 0, TimeSpan.Zero); // 3:30 p. m. en Bogotá

    private static MyBusinessDto Negocio(Guid id, string nombre, bool citas = false, bool turnos = false,
        bool pedidos = false)
        => new(id, nombre, nombre.ToLowerInvariant().Replace(' ', '-'), "Owner",
            CanManageConfiguration: true, CanManageAppointments: true, CanManageMembers: true,
            CanManageQueues: true, CanManageOrders: true, SupportsPickupOrdering: pedidos,
            BusinessStatus: "Active",
            HasAppointments: citas, HasVirtualQueues: turnos, HasPickupOrders: pedidos);

    private static OwnerDashboardUseCases Sut(FakeDashboardStore store,
        RecordingDiagnostics? diagnostics = null, string zona = "America/Bogota")
        => new(store, new FakeZones(zona), new FrozenClock(Ahora), diagnostics ?? new RecordingDiagnostics());

    // ------------------------------------------------------------------ gating por módulo

    [Fact]
    public async Task Each_business_only_gets_the_summary_of_the_modules_it_has_enabled()
    {
        var bella = Guid.NewGuid(); var corte = Guid.NewGuid(); var sazon = Guid.NewGuid();
        var store = new FakeDashboardStore
        {
            Appointments = { [bella] = new(3, 1, 2, 0, Ahora, "Corte y cepillado") },
            Queues = { [corte] = new(4, 1, 7, 12) },
            Orders = { [sazon] = new(2, 1, 1, 5) }
        };

        var resumen = await Sut(store).SummarizeAsync([
            Negocio(bella, "Salon Bella", citas: true),
            Negocio(corte, "Barberia El Corte", turnos: true),
            Negocio(sazon, "Sazon Local", pedidos: true)
        ]);

        var deBella = resumen.Single(x => x.BusinessId == bella);
        Assert.NotNull(deBella.Appointments);
        // Nulo, no cero: un módulo apagado no es un módulo vacío.
        Assert.Null(deBella.Queues);
        Assert.Null(deBella.Orders);

        var deCorte = resumen.Single(x => x.BusinessId == corte);
        Assert.NotNull(deCorte.Queues);
        Assert.Null(deCorte.Appointments);
        Assert.Null(deCorte.Orders);

        var deSazon = resumen.Single(x => x.BusinessId == sazon);
        Assert.NotNull(deSazon.Orders);
        Assert.Null(deSazon.Appointments);
        Assert.Null(deSazon.Queues);
    }

    [Fact]
    public async Task A_disabled_module_is_never_asked_for()
    {
        var soloCitas = Guid.NewGuid();
        var store = new FakeDashboardStore();
        await Sut(store).SummarizeAsync([Negocio(soloCitas, "Solo citas", citas: true)]);
        // Ni una consulta de turnos ni de pedidos: nadie los tiene encendidos.
        Assert.Equal(1, store.AppointmentCalls);
        Assert.Equal(0, store.QueueCalls);
        Assert.Equal(0, store.OrderCalls);
    }

    [Fact]
    public async Task A_module_enabled_without_permission_is_not_shown()
    {
        // El negocio tiene citas, pero esta persona no puede administrarlas: ShowAppointments es false
        // y el resumen no debe inventarle una operación que no le corresponde.
        var id = Guid.NewGuid();
        var sinPermiso = Negocio(id, "Sin permiso", citas: true) with { CanManageAppointments = false };
        var resumen = await Sut(new FakeDashboardStore()).SummarizeAsync([sinPermiso]);
        Assert.Null(resumen.Single().Appointments);
    }

    // ------------------------------------------------------------------ aislamiento

    [Fact]
    public async Task Metrics_never_cross_from_one_business_to_another()
    {
        var uno = Guid.NewGuid(); var dos = Guid.NewGuid();
        var store = new FakeDashboardStore
        {
            Appointments = { [uno] = new(9, 9, 0, 0, null, null), [dos] = new(1, 0, 1, 0, null, null) }
        };

        var resumen = await Sut(store).SummarizeAsync([
            Negocio(uno, "Primero", citas: true), Negocio(dos, "Segundo", citas: true)
        ]);

        Assert.Equal(9, resumen.Single(x => x.BusinessId == uno).Appointments!.TodayTotal);
        Assert.Equal(1, resumen.Single(x => x.BusinessId == dos).Appointments!.TodayTotal);
    }

    [Fact]
    public async Task A_business_without_activity_today_reports_zeros_and_not_a_missing_module()
    {
        // La base no devuelve fila para un negocio sin actividad. Eso significa "hoy no ha pasado
        // nada", no "no tiene citas habilitadas": la diferencia decide si la pantalla muestra el
        // estado vacío o esconde el bloque entero.
        var id = Guid.NewGuid();
        var resumen = await Sut(new FakeDashboardStore()).SummarizeAsync([Negocio(id, "Tranquilo", citas: true)]);
        var citas = resumen.Single().Appointments;
        Assert.NotNull(citas);
        Assert.Equal(0, citas!.TodayTotal);
        Assert.Null(citas.NextAppointmentAtUtc);
    }

    // ------------------------------------------------------------------ coste

    [Fact]
    public async Task The_number_of_aggregations_does_not_grow_with_the_number_of_businesses()
    {
        // Quince negocios con los tres módulos encendidos. Si el resumen se pidiera negocio a negocio
        // serían cuarenta y cinco idas a la base; el panel de una socia con muchos negocios seria
        // inusable contra una base que esta a 73 ms.
        var muchos = Enumerable.Range(0, 15)
            .Select(i => Negocio(Guid.NewGuid(), $"Negocio {i}", citas: true, turnos: true, pedidos: true))
            .ToList();
        var store = new FakeDashboardStore();

        await Sut(store).SummarizeAsync(muchos);

        Assert.Equal(1, store.AppointmentCalls);
        Assert.Equal(1, store.QueueCalls);
        Assert.Equal(1, store.OrderCalls);
        // Y cada agregación recibió los quince de una vez.
        Assert.Equal(15, store.LastAppointmentWindows);
    }

    // ------------------------------------------------------------------ día local

    [Theory]
    [InlineData("America/Bogota")]
    [InlineData(null)]
    [InlineData("Zona/Inexistente")]
    public void The_local_day_window_covers_exactly_twenty_four_hours_from_local_midnight(string? zona)
    {
        var ventana = OwnerDashboardUseCases.LocalDay(Guid.NewGuid(), zona, Ahora);
        Assert.Equal(TimeSpan.FromDays(1), ventana.ToUtc - ventana.FromUtc);
        // Bogotá es UTC-5 todo el año: la medianoche local del 11 son las 05:00Z del 11.
        Assert.Equal(new DateTimeOffset(2026, 8, 11, 5, 0, 0, TimeSpan.Zero), ventana.FromUtc);
        Assert.True(ventana.FromUtc <= Ahora && Ahora < ventana.ToUtc);
    }

    [Fact]
    public void An_unknown_time_zone_falls_back_instead_of_bringing_the_panel_down()
    {
        // Un TimeZoneId mal escrito en un negocio no puede dejar sin panel a los demás.
        var ventana = OwnerDashboardUseCases.LocalDay(Guid.NewGuid(), "no-existe", Ahora);
        Assert.Equal(TimeSpan.FromDays(1), ventana.ToUtc - ventana.FromUtc);
    }

    [Fact]
    public async Task An_unknown_time_zone_is_reported_instead_of_failing_quietly()
    {
        // Caer a Bogotá salva la respuesta, pero deja al negocio contando un día que no es el suyo.
        // Si eso no se avisa, el error sólo se descubre cuando alguien duda de sus propias cifras.
        var id = Guid.NewGuid();
        var avisos = new RecordingDiagnostics();

        var resumen = await Sut(new FakeDashboardStore(), avisos, "Zona/Inexistente")
            .SummarizeAsync([Negocio(id, "Zona rota", citas: true)]);

        var aviso = Assert.Single(avisos.Invalid);
        Assert.Equal(id, aviso.BusinessId);
        Assert.Equal("Zona/Inexistente", aviso.TimeZoneId);
        // Y el negocio sigue apareciendo: el aviso no lo saca del panel.
        Assert.NotNull(resumen.Single().Appointments);
    }

    [Fact]
    public async Task A_valid_time_zone_reports_nothing()
    {
        var avisos = new RecordingDiagnostics();
        await Sut(new FakeDashboardStore(), avisos, "America/Bogota")
            .SummarizeAsync([Negocio(Guid.NewGuid(), "Zona buena", citas: true)]);
        Assert.Empty(avisos.Invalid);
    }

    // ------------------------------------------------------------------ dobles

    private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeZones(string zona) : IBusinessTimeZoneResolver
    {
        public Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(IReadOnlyCollection<Guid> businessIds,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(
                businessIds.ToDictionary(x => x, _ => zona));
    }

    private sealed class RecordingDiagnostics : IOwnerDashboardDiagnostics
    {
        public List<(Guid BusinessId, string TimeZoneId)> Invalid { get; } = [];
        public void InvalidTimeZone(Guid businessId, string timeZoneId) => Invalid.Add((businessId, timeZoneId));
    }

    private sealed class FakeDashboardStore : IOwnerDashboardStore
    {
        public Dictionary<Guid, AppointmentsSummaryDto> Appointments { get; } = [];
        public Dictionary<Guid, QueueSummaryDto> Queues { get; } = [];
        public Dictionary<Guid, OrdersSummaryDto> Orders { get; } = [];
        public int AppointmentCalls, QueueCalls, OrderCalls, LastAppointmentWindows;

        public Task<IReadOnlyDictionary<Guid, AppointmentsSummaryDto>> AppointmentsAsync(
            IReadOnlyCollection<BusinessDayWindow> windows, DateTimeOffset nowUtc,
            CancellationToken cancellationToken = default)
        {
            AppointmentCalls++; LastAppointmentWindows = windows.Count;
            return Task.FromResult<IReadOnlyDictionary<Guid, AppointmentsSummaryDto>>(Appointments);
        }

        public Task<IReadOnlyDictionary<Guid, QueueSummaryDto>> QueuesAsync(
            IReadOnlyCollection<BusinessDayWindow> windows, CancellationToken cancellationToken = default)
        {
            QueueCalls++;
            return Task.FromResult<IReadOnlyDictionary<Guid, QueueSummaryDto>>(Queues);
        }

        public Task<IReadOnlyDictionary<Guid, OrdersSummaryDto>> OrdersAsync(
            IReadOnlyCollection<BusinessDayWindow> windows, CancellationToken cancellationToken = default)
        {
            OrderCalls++;
            return Task.FromResult<IReadOnlyDictionary<Guid, OrdersSummaryDto>>(Orders);
        }
    }
}
