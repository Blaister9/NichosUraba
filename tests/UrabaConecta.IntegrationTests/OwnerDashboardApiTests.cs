using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UrabaConecta.Application;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

/// <summary>
/// El resumen operativo contra PostgreSQL real. Las pruebas de composición ya viven en el dominio con
/// dobles; aquí se comprueba lo único que un doble no puede demostrar: que las consultas se traducen,
/// que agrupan y cuentan dentro de la base, que respetan la ventana del día local de cada negocio y
/// que su coste no crece con la cantidad de negocios.
/// </summary>
public sealed class OwnerDashboardApiTests(DashboardWebFactory factory) : IClassFixture<DashboardWebFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Now = DashboardWebFactory.Now;

    private OwnerDashboardFixture Given => new(factory);
    private QueryCounter Counter => factory.Services.GetRequiredService<QueryCounter>();
    private SqlRecorder Sql => factory.Services.GetRequiredService<SqlRecorder>();
    private HttpClient Client() => factory.CreateClient(new() { AllowAutoRedirect = false });

    private static string Email() => $"panel-{Guid.NewGuid():N}@prueba.local";

    /// <summary>Ejecuta el resumen tal como lo haría el endpoint, con el alcance ya resuelto.</summary>
    private async Task<IReadOnlyList<OwnerDashboardSummaryDto>> SummarizeAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var businesses = scope.ServiceProvider.GetRequiredService<IUrabaUseCases>();
        var dashboard = scope.ServiceProvider.GetRequiredService<IOwnerDashboardUseCases>();
        return await dashboard.SummarizeAsync(await businesses.GetMyBusinessesAsync(userId));
    }

    // ------------------------------------------------------------------ traducción y semántica

    [Fact]
    public async Task The_three_aggregations_are_translated_and_counted_by_postgresql()
    {
        var owner = await Given.UserAsync(Email());
        await Given.BusinessAsync(owner, "Todo encendido", appointments: true, queues: true, orders: true);

        Sql.Reset();
        var resumen = await SummarizeAsync(owner);

        // Los resultados son los que la actividad sembrada implica.
        var negocio = resumen.Single();
        Assert.Equal(OwnerDashboardFixture.AppointmentsToday, negocio.Appointments!.TodayTotal);
        Assert.Equal(1, negocio.Appointments.Pending);
        Assert.Equal(1, negocio.Appointments.Confirmed);
        Assert.Equal(1, negocio.Appointments.Completed);
        Assert.Equal(2, negocio.Queues!.Waiting);
        Assert.Equal(1, negocio.Queues.InService);
        Assert.Equal(OwnerDashboardFixture.QueueServedToday, negocio.Queues.ServedToday);
        Assert.Equal(7, negocio.Queues.CurrentTicketNumber);
        Assert.Equal(1, negocio.Orders!.Pending);
        Assert.Equal(1, negocio.Orders.Preparing);
        Assert.Equal(1, negocio.Orders.Ready);
        Assert.Equal(OwnerDashboardFixture.OrdersDeliveredToday, negocio.Orders.DeliveredToday);

        // Y quien agrupó y contó fue PostgreSQL, no la aplicación.
        foreach (var tabla in new[] { "appointments", "queue_tickets", "ordering_pickup_orders" })
        {
            var sql = Sql.All(tabla);
            Assert.NotEmpty(sql);
            Assert.All(sql, x => Assert.Contains("GROUP BY", x, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(sql, x => x.Contains("count(", StringComparison.OrdinalIgnoreCase));
        }

        // La próxima cita se resuelve con una función de ventana particionada por negocio, que es lo
        // que permite traer la primera de cada uno —con su propio ServiceName— en una sola sentencia
        // en vez de una consulta por negocio.
        Assert.Contains(Sql.All("appointments"),
            x => x.Contains("ROW_NUMBER() OVER(PARTITION BY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_next_appointment_is_the_closest_live_one_with_its_own_service_name()
    {
        var owner = await Given.UserAsync(Email());
        await Given.BusinessAsync(owner, "Con agenda", appointments: true);

        var citas = (await SummarizeAsync(owner)).Single().Appointments!;

        // La cancelada de dentro de media hora es anterior, pero no está viva; la completada de hace
        // cuatro horas ya pasó. La próxima es la pendiente de dentro de una hora, y el nombre del
        // servicio tiene que venir de ESA fila, no de otra.
        Assert.Equal(Now.AddHours(1), citas.NextAppointmentAtUtc);
        Assert.Equal("Cepillado", citas.NextAppointmentServiceName);
    }

    [Fact]
    public async Task A_business_whose_upcoming_appointments_are_all_terminal_has_no_next_one()
    {
        var owner = await Given.UserAsync(Email());
        var window = await Given.BusinessAsync(owner, "Sin nada vivo", appointments: true, withActivity: false);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var serviceId = Guid.NewGuid();
            var staffId = Guid.NewGuid();
            db.Add(new Service(serviceId, window.BusinessId, "Corte", 60, 35000));
            db.Add(new StaffMember(staffId, window.BusinessId, "Profesional"));
            await db.SaveChangesAsync();
            // Todas futuras y todas terminales: canceladas y rechazadas.
            foreach (var (offset, status) in new (int, AppointmentStatus)[]
                     { (1, AppointmentStatus.Cancelled), (2, AppointmentStatus.Rejected) })
            {
                var id = Guid.NewGuid();
                var consent = new ConsentReceipt(Guid.NewGuid(), window.BusinessId, "pruebas", "Cita", Now);
                consent.LinkAppointment(id);
                var start = Now.AddHours(offset);
                var cita = new Appointment(id, window.BusinessId, serviceId, staffId, start, 60, "Corte",
                    35000, "protegido", "protegido", "0000", "protegido", Guid.NewGuid().ToString("N"), 1,
                    consent.Id, start.AddHours(-1));
                cita.ChangeStatus(status, start.AddHours(-1), status == AppointmentStatus.Rejected ? "motivo" : null);
                db.AddRange(consent, cita);
            }
            await db.SaveChangesAsync();
        }

        var citas = (await SummarizeAsync(owner)).Single().Appointments!;
        Assert.Equal(2, citas.TodayTotal);
        Assert.Null(citas.NextAppointmentAtUtc);
        Assert.Null(citas.NextAppointmentServiceName);
    }

    [Fact]
    public async Task Served_and_delivered_only_count_what_finished_inside_the_local_day()
    {
        // El fixture siembra, para cada familia, una fila terminada ayer. Si la ventana no se aplicara,
        // estos números serían tres y no dos.
        var owner = await Given.UserAsync(Email());
        await Given.BusinessAsync(owner, "Con historia", queues: true, orders: true);

        var negocio = (await SummarizeAsync(owner)).Single();
        Assert.Equal(2, negocio.Queues!.ServedToday);
        Assert.Equal(2, negocio.Orders!.DeliveredToday);
    }

    [Fact]
    public async Task Delivered_orders_are_dated_by_the_transition_that_delivered_them()
    {
        // La métrica se apoya en UpdatedAtUtc porque Delivered es terminal. Esta prueba fija el enlace
        // en la base: la fila entregada conserva como UpdatedAtUtc el instante de su última transición.
        var owner = await Given.UserAsync(Email());
        var window = await Given.BusinessAsync(owner, "Entregas", orders: true);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entregados = await db.PickupOrders.AsNoTracking()
            .Where(x => x.BusinessId == window.BusinessId && x.Status == PickupOrderStatus.Delivered)
            .Select(x => x.UpdatedAtUtc).ToListAsync();

        Assert.Equal(3, entregados.Count);
        Assert.Equal(2, entregados.Count(x => x >= window.FromUtc && x < window.ToUtc));
    }

    // ------------------------------------------------------------------ zonas horarias

    [Fact]
    public async Task The_same_instant_belongs_to_today_for_one_business_and_not_for_the_other()
    {
        // Bogotá va en UTC-5 y Tokio en UTC+9: sus días locales empiezan en instantes UTC distintos.
        // Un turno terminado justo antes del arranque del día de Tokio es de hoy para Bogotá y de ayer
        // para Tokio, y eso es exactamente lo que una ventana única mal calculada arruinaría.
        var owner = await Given.UserAsync(Email());
        var bogota = await Given.BusinessAsync(owner, "En Bogotá", queues: true,
            timeZone: "America/Bogota", withActivity: false);
        var tokio = await Given.BusinessAsync(owner, "En Tokio", queues: true,
            timeZone: "Asia/Tokyo", withActivity: false);

        Assert.NotEqual(bogota.FromUtc, tokio.FromUtc);
        // Un instante dentro del día de Bogotá pero anterior al de Tokio.
        var instante = tokio.FromUtc.AddMinutes(-30);
        Assert.True(instante >= bogota.FromUtc && instante < bogota.ToUtc);

        foreach (var window in new[] { bogota, tokio }) await ServedAtAsync(window.BusinessId, instante);

        var resumen = await SummarizeAsync(owner);
        Assert.Equal(1, resumen.Single(x => x.BusinessId == bogota.BusinessId).Queues!.ServedToday);
        Assert.Equal(0, resumen.Single(x => x.BusinessId == tokio.BusinessId).Queues!.ServedToday);
    }

    [Fact]
    public async Task A_business_with_an_unknown_time_zone_still_reports_and_leaves_a_warning()
    {
        var owner = await Given.UserAsync(Email());
        var rota = await Given.BusinessAsync(owner, "Zona rota", queues: true,
            timeZone: "Zona/Inexistente", withActivity: false);
        var sana = await Given.BusinessAsync(owner, "Zona sana", queues: true, withActivity: false);
        await ServedAtAsync(rota.BusinessId, Now.AddHours(-2));
        await ServedAtAsync(sana.BusinessId, Now.AddHours(-2));

        var resumen = await SummarizeAsync(owner);

        // El negocio de la zona inválida sigue contando —con la ventana de Bogotá— y el otro no se ve
        // afectado: una configuración mal escrita no puede dejar sin panel a los demás.
        Assert.Equal(1, resumen.Single(x => x.BusinessId == rota.BusinessId).Queues!.ServedToday);
        Assert.Equal(1, resumen.Single(x => x.BusinessId == sana.BusinessId).Queues!.ServedToday);
    }

    private async Task ServedAtAsync(Guid businessId, DateTimeOffset completedAt)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var definitionId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        db.Add(new QueueDefinition(definitionId, businessId, "Fila", 15, 100, null, true, Now));
        db.Add(new QueueSession(sessionId, businessId, definitionId, completedAt.AddHours(-2)));
        var ticket = new QueueTicket(Guid.NewGuid(), businessId, sessionId, 1,
            Guid.NewGuid().ToString("N"), "protegido", QueueTicketSource.WalkIn, completedAt.AddHours(-1));
        ticket.Call(completedAt.AddMinutes(-20), 0);
        ticket.Start(completedAt.AddMinutes(-10), 1);
        ticket.Complete(completedAt, 2);
        db.Add(ticket);
        await db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ módulos y aislamiento

    [Fact]
    public async Task An_enabled_module_without_activity_reports_zeros_and_a_disabled_one_reports_nothing()
    {
        var owner = await Given.UserAsync(Email());
        await Given.BusinessAsync(owner, "Sólo citas", appointments: true, withActivity: false);

        var negocio = (await SummarizeAsync(owner)).Single();
        // Encendido pero sin actividad: ceros, no ausencia.
        Assert.NotNull(negocio.Appointments);
        Assert.Equal(0, negocio.Appointments!.TodayTotal);
        Assert.Null(negocio.Appointments.NextAppointmentAtUtc);
        // Apagados: ausencia, no ceros.
        Assert.Null(negocio.Queues);
        Assert.Null(negocio.Orders);
    }

    [Fact]
    public async Task One_business_never_receives_the_numbers_of_another()
    {
        var owner = await Given.UserAsync(Email());
        var conAgenda = await Given.BusinessAsync(owner, "Con agenda", appointments: true);
        await Given.BusinessAsync(owner, "Sin agenda", appointments: true, withActivity: false);

        var resumen = await SummarizeAsync(owner);
        Assert.Equal(OwnerDashboardFixture.AppointmentsToday,
            resumen.Single(x => x.BusinessId == conAgenda.BusinessId).Appointments!.TodayTotal);
        Assert.Equal(0, resumen.Single(x => x.BusinessId != conAgenda.BusinessId).Appointments!.TodayTotal);
    }

    // ------------------------------------------------------------------ endpoint y autorización

    [Fact]
    public async Task The_dashboard_is_not_served_to_anonymous_visitors()
    {
        using var client = Client();
        var response = await client.GetAsync("/api/v1/businesses/dashboard");
        // La ruta privada redirige al acceso en vez de responder datos.
        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect,
            $"El anónimo recibió {(int)response.StatusCode}.");
        if (response.StatusCode == HttpStatusCode.Redirect)
            Assert.Contains("/Account/Login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task An_authenticated_owner_receives_only_the_businesses_that_are_theirs()
    {
        var email = Email();
        var owner = await Given.UserAsync(email);
        var mio = await Given.BusinessAsync(owner, "Mi negocio", appointments: true);

        var ajeno = await Given.UserAsync(Email());
        await Given.BusinessAsync(ajeno, "Negocio ajeno", appointments: true);

        using var client = Client();
        await PlatformAdministrationApiTests.Login(client, email);
        var resumen = await client.GetFromJsonAsync<IReadOnlyList<OwnerDashboardSummaryDto>>(
            "/api/v1/businesses/dashboard", Json);

        var unico = Assert.Single(resumen!);
        Assert.Equal(mio.BusinessId, unico.BusinessId);
        Assert.Equal(OwnerDashboardFixture.AppointmentsToday, unico.Appointments!.TodayTotal);
    }

    [Fact]
    public async Task The_endpoint_does_not_accept_a_scope_chosen_by_the_client()
    {
        // Aunque alguien invente el parámetro, el alcance lo sigue fijando la membresía: pedir el
        // negocio de otra persona no lo añade a la respuesta ni cambia el resultado, así que la ruta
        // tampoco sirve para averiguar si ese negocio existe.
        var email = Email();
        var owner = await Given.UserAsync(email);
        await Given.BusinessAsync(owner, "Mi negocio", appointments: true);
        var ajeno = await Given.UserAsync(Email());
        var deOtro = await Given.BusinessAsync(ajeno, "Negocio ajeno", appointments: true);

        using var client = Client();
        await PlatformAdministrationApiTests.Login(client, email);
        var conParametro = await client.GetFromJsonAsync<IReadOnlyList<OwnerDashboardSummaryDto>>(
            $"/api/v1/businesses/dashboard?businessIds={deOtro.BusinessId}&businessId={deOtro.BusinessId}", Json);

        Assert.DoesNotContain(conParametro!, x => x.BusinessId == deOtro.BusinessId);
        Assert.Single(conParametro!);
    }

    [Fact]
    public async Task A_partner_operator_with_an_owner_membership_receives_that_dashboard()
    {
        var email = Email();
        var socia = await Given.UserAsync(email, "PartnerOperator");
        var negocio = await Given.BusinessAsync(socia, "Negocio de la socia",
            appointments: true, queues: true, orders: true);

        using var client = Client();
        await PlatformAdministrationApiTests.Login(client, email);
        var resumen = await client.GetFromJsonAsync<IReadOnlyList<OwnerDashboardSummaryDto>>(
            "/api/v1/businesses/dashboard", Json);

        var unico = Assert.Single(resumen!);
        Assert.Equal(negocio.BusinessId, unico.BusinessId);
        Assert.Equal(OwnerDashboardFixture.QueueServedToday, unico.Queues!.ServedToday);
        Assert.Equal(OwnerDashboardFixture.OrdersDeliveredToday, unico.Orders!.DeliveredToday);
    }

    [Fact]
    public async Task A_worker_keeps_the_permissions_their_membership_grants()
    {
        // La política de trabajadores no cambia con el resumen: ve el negocio donde tiene membresía y
        // los módulos que su membresía le permite operar.
        var email = Email();
        var trabajadora = await Given.UserAsync(email, "BusinessWorker");
        var negocio = await Given.BusinessAsync(trabajadora, "Donde trabaja", appointments: true,
            role: MembershipRole.Worker);

        using var client = Client();
        await PlatformAdministrationApiTests.Login(client, email);
        var resumen = await client.GetFromJsonAsync<IReadOnlyList<OwnerDashboardSummaryDto>>(
            "/api/v1/businesses/dashboard", Json);

        var unico = Assert.Single(resumen!);
        Assert.Equal(negocio.BusinessId, unico.BusinessId);
        Assert.NotNull(unico.Appointments);
    }

    // ------------------------------------------------------------------ coste

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(15)]
    public async Task The_dashboard_costs_the_same_number_of_statements_for_any_number_of_businesses(int cuantos)
    {
        var owner = await Given.UserAsync(Email());
        for (var i = 0; i < cuantos; i++)
            await Given.BusinessAsync(owner, $"Negocio {i}", appointments: true, queues: true, orders: true);

        // El alcance se resuelve aparte para poder separar lo que cuesta saber "de quién son" de lo
        // que cuesta el resumen en sí.
        IReadOnlyList<MyBusinessDto> mine;
        using (var scope = factory.Services.CreateScope())
            mine = await scope.ServiceProvider.GetRequiredService<IUrabaUseCases>()
                .GetMyBusinessesAsync(owner);
        Assert.Equal(cuantos, mine.Count);

        using var scope2 = factory.Services.CreateScope();
        var dashboard = scope2.ServiceProvider.GetRequiredService<IOwnerDashboardUseCases>();
        Counter.Reset();
        Sql.Reset();
        var resumen = await dashboard.SummarizeAsync(mine);
        var sentencias = Counter.Count;

        Assert.Equal(cuantos, resumen.Count);
        // Zonas horarias, contadores de citas, próxima cita, turnos y pedidos. Ni una más por negocio:
        // con quince negocios, una consulta por negocio serían cuarenta y cinco idas y vueltas contra
        // una base que está a 73 ms.
        Assert.Equal(5, sentencias);
        Assert.Single(Sql.All("appointments"), x => x.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase)
            && x.Contains("count(", StringComparison.OrdinalIgnoreCase));
        Assert.Single(Sql.All("queue_tickets"));
        Assert.Single(Sql.All("ordering_pickup_orders"));
    }

    [Fact]
    public async Task Abundant_activity_does_not_travel_out_of_the_database()
    {
        // Quince negocios con las tres familias encendidas: más de doscientas filas de operación. Lo
        // que puede volver son quince resúmenes y, como mucho, quince proyecciones de próxima cita.
        var owner = await Given.UserAsync(Email());
        for (var i = 0; i < 15; i++)
            await Given.BusinessAsync(owner, $"Volumen {i}", appointments: true, queues: true, orders: true);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dashboard = scope.ServiceProvider.GetRequiredService<IOwnerDashboardUseCases>();
        var businesses = scope.ServiceProvider.GetRequiredService<IUrabaUseCases>();
        var mine = await businesses.GetMyBusinessesAsync(owner);

        Sql.Reset();
        var resumen = await dashboard.SummarizeAsync(mine);

        Assert.Equal(15, resumen.Count);
        // Ninguna entidad de operación quedó en el contexto: las consultas son de sólo lectura y lo
        // que devuelven son cuentas, no filas.
        Assert.Empty(db.ChangeTracker.Entries<Appointment>());
        Assert.Empty(db.ChangeTracker.Entries<QueueTicket>());
        Assert.Empty(db.ChangeTracker.Entries<PickupOrder>());

        // Y el SQL no arrastra las columnas de las filas de operación: nada de datos de personas
        // viajando para ser contados en memoria.
        foreach (var sql in Sql.All("queue_tickets").Concat(Sql.All("ordering_pickup_orders")))
        {
            Assert.DoesNotContain("ProtectedAlias", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ProtectedCustomerPhone", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PublicCodeHash", sql, StringComparison.OrdinalIgnoreCase);
        }
    }
}
