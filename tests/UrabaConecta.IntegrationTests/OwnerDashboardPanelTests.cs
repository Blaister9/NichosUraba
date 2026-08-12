using System.Net;
using Microsoft.Extensions.DependencyInjection;
using UrabaConecta.Domain;

namespace UrabaConecta.IntegrationTests;

/// <summary>
/// El panel ya renderizado. Estas pruebas miran el HTML que recibe el navegador, que es donde se
/// comprueban las decisiones que un DTO correcto todavía puede arruinar: que un negocio sólo enseñe
/// su módulo, que un cero venga acompañado de una explicación, que la operación y la configuración no
/// pesen lo mismo, y que la pantalla no deshaga el trabajo del pipeline pidiendo negocio por negocio.
/// </summary>
public sealed class OwnerDashboardPanelTests(DashboardWebFactory factory) : IClassFixture<DashboardWebFactory>
{
    private OwnerDashboardFixture Given => new(factory);
    private SqlRecorder Sql => factory.Services.GetRequiredService<SqlRecorder>();
    private static string Email() => $"panel-ui-{Guid.NewGuid():N}@prueba.local";

    private async Task<string> PanelAsync(string email)
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        await PlatformAdministrationApiTests.Login(client, email);
        var response = await client.GetAsync("/panel");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // ------------------------------------------------------------------ módulos por negocio

    [Fact]
    public async Task A_business_with_appointments_only_shows_appointments_and_nothing_else()
    {
        var email = Email();
        var owner = await Given.UserAsync(email);
        await Given.BusinessAsync(owner, "Salón de prueba", appointments: true);

        var html = await PanelAsync(email);

        Assert.Contains("data-testid=\"appointments-summary\"", html);
        Assert.DoesNotContain("data-testid=\"queues-summary\"", html);
        Assert.DoesNotContain("data-testid=\"orders-summary\"", html);
        // Y la operación que ofrece es la suya.
        Assert.Contains("Administrar citas", html);
        Assert.DoesNotContain("Operar turnos", html);
        Assert.DoesNotContain("Operar pedidos", html);
    }

    [Fact]
    public async Task A_business_with_queues_only_shows_queues_and_nothing_else()
    {
        var email = Email();
        var owner = await Given.UserAsync(email);
        await Given.BusinessAsync(owner, "Barbería de prueba", queues: true);

        var html = await PanelAsync(email);

        Assert.Contains("data-testid=\"queues-summary\"", html);
        Assert.DoesNotContain("data-testid=\"appointments-summary\"", html);
        Assert.DoesNotContain("data-testid=\"orders-summary\"", html);
        Assert.Contains("Personas esperando", html);
        Assert.Contains("Operar turnos", html);
        Assert.DoesNotContain("Administrar citas", html);
    }

    [Fact]
    public async Task A_business_with_orders_only_shows_orders_and_nothing_else()
    {
        var email = Email();
        var owner = await Given.UserAsync(email);
        await Given.BusinessAsync(owner, "Restaurante de prueba", orders: true);

        var html = await PanelAsync(email);

        Assert.Contains("data-testid=\"orders-summary\"", html);
        Assert.DoesNotContain("data-testid=\"appointments-summary\"", html);
        Assert.DoesNotContain("data-testid=\"queues-summary\"", html);
        Assert.Contains("Listos para recoger", html);
        Assert.Contains("Operar pedidos", html);
    }

    // ------------------------------------------------------------------ cifras y próxima cita

    [Fact]
    public async Task The_panel_shows_the_numbers_of_the_day_and_the_next_appointment_in_local_time()
    {
        var email = Email();
        var owner = await Given.UserAsync(email);
        await Given.BusinessAsync(owner, "Con agenda", appointments: true);

        var html = await PanelAsync(email);

        Assert.Contains("Citas de hoy", html);
        Assert.Contains("Pendientes", html);
        Assert.Contains("Confirmadas", html);
        Assert.Contains("Completadas", html);
        Assert.Contains("data-testid=\"next-appointment\"", html);
        // El servicio viene de la fila de esa cita, no de otra.
        Assert.Contains("Cepillado", html);

        // La hora congelada son las 20:00Z, así que la próxima cita ocurre a las 21:00Z. En Bogotá eso
        // son las 4:00 de la tarde, y es la hora que tiene que leerse: mostrar la UTC convierte el
        // panel en un volcado de datos. El separador antes de "p. m." es un espacio fino de es-CO, por
        // eso se afirma la hora y la tarde por separado y no la cadena completa.
        Assert.Contains("4:00", html);
        Assert.Contains("p.", html);
        Assert.DoesNotContain("21:00", html);
        Assert.DoesNotContain("UTC", html);
    }

    [Fact]
    public async Task The_panel_never_shows_raw_status_names()
    {
        var email = Email();
        var owner = await Given.UserAsync(email);
        await Given.BusinessAsync(owner, "Todo", appointments: true, queues: true, orders: true);

        var html = await PanelAsync(email);

        foreach (var crudo in new[] { "ReadyForPickup", "InService", "NoShow", "AppointmentStatus" })
            Assert.DoesNotContain(crudo, html);
    }

    // ------------------------------------------------------------------ estados vacíos

    [Fact]
    public async Task An_enabled_module_without_activity_explains_itself_instead_of_showing_bare_zeros()
    {
        var email = Email();
        var owner = await Given.UserAsync(email);
        await Given.BusinessAsync(owner, "Tranquilo", appointments: true, queues: true, orders: true,
            withActivity: false);

        var html = await PanelAsync(email);

        Assert.Contains("Aún no tienes citas para hoy.", html);
        Assert.Contains("En este momento no hay personas esperando turno.", html);
        Assert.Contains("Aún no hay pedidos pendientes.", html);
        // Sin actividad no se pinta ninguna cuadrícula de ceros: un módulo encendido y en calma se
        // explica con una frase, no con doce ceros que la persona tiene que interpretar sola.
        Assert.DoesNotContain("metric-grid", html);
        Assert.DoesNotContain("data-testid=\"next-appointment\"", html);
    }

    [Fact]
    public async Task A_queue_with_nobody_waiting_still_reports_the_people_already_served_today()
    {
        // Decir sólo "no hay nadie esperando" borraría el trabajo del día.
        var email = Email();
        var owner = await Given.UserAsync(email);
        await Given.BusinessAsync(owner, "Ya atendió", queues: true);

        var html = await PanelAsync(email);

        Assert.Contains("Atendidos hoy", html);
        Assert.Contains("data-testid=\"queues-summary\"", html);
    }

    // ------------------------------------------------------------------ varios negocios

    [Fact]
    public async Task Each_business_gets_its_own_card_actions_and_configuration_link()
    {
        var email = Email();
        var owner = await Given.UserAsync(email);
        var citas = await Given.BusinessAsync(owner, "Uno con citas", appointments: true);
        var turnos = await Given.BusinessAsync(owner, "Dos con turnos", queues: true);

        var html = await PanelAsync(email);

        // Una tarjeta por negocio, cada una identificada.
        Assert.Contains($"data-business-id=\"{citas.BusinessId}\"", html);
        Assert.Contains($"data-business-id=\"{turnos.BusinessId}\"", html);
        // Y cada configuración apunta a SU negocio: enviar a la persona a configurar otro
        // establecimiento es de los errores que sólo se descubren cuando ya guardó algo.
        Assert.Contains($"/panel/{citas.BusinessId}/configuracion", html);
        Assert.Contains($"/panel/{turnos.BusinessId}/configuracion", html);
        Assert.Contains($"/panel/{citas.BusinessId}/citas", html);
        Assert.Contains($"/panel/{turnos.BusinessId}/turnos", html);
        // Sin mezclar operaciones entre negocios.
        Assert.DoesNotContain($"/panel/{citas.BusinessId}/turnos", html);
        Assert.DoesNotContain($"/panel/{turnos.BusinessId}/citas", html);
    }

    // ------------------------------------------------------------------ socia + propietaria

    [Fact]
    public async Task A_partner_operator_who_also_owns_a_business_keeps_both_capabilities()
    {
        // El defecto de V6.5a: al ganar el panel de socia se perdía el de propietaria, o al revés.
        // Las dos cosas tienen que caber en la misma pantalla.
        var email = Email();
        var socia = await Given.UserAsync(email, "PartnerOperator");
        var suyo = await Given.BusinessAsync(socia, "Negocio de la socia", orders: true);

        var html = await PanelAsync(email);

        Assert.Contains("data-testid=\"crear-negocio\"", html);
        Assert.Contains("data-testid=\"owner-dashboard\"", html);
        Assert.Contains($"data-business-id=\"{suyo.BusinessId}\"", html);
        Assert.Contains("Operar pedidos", html);
    }

    // ------------------------------------------------------------------ coste

    [Fact]
    public async Task The_panel_asks_for_the_summary_once_and_not_once_per_business()
    {
        var email = Email();
        var owner = await Given.UserAsync(email);
        for (var i = 0; i < 5; i++)
            await Given.BusinessAsync(owner, $"Negocio {i}", appointments: true, queues: true, orders: true);

        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        await PlatformAdministrationApiTests.Login(client, email);
        Sql.Reset();
        var response = await client.GetAsync("/panel");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Cinco negocios, y una sola agregación por familia: la pantalla consume el resumen entero
        // en una llamada en vez de preguntar negocio por negocio, que es lo que costaba el panel antes.
        Assert.Single(Sql.All("queue_tickets"));
        Assert.Single(Sql.All("ordering_pickup_orders"));
        Assert.Equal(2, Sql.All("appointments").Count); // contadores y próxima cita
    }
}

/// <summary>
/// El panel cuando el resumen falla. Se sustituye el caso de uso por uno que revienta para comprobar
/// lo único que importa entonces: que la persona conserve la página y sus accesos.
/// </summary>
public sealed class OwnerDashboardPanelFailureTests(BrokenDashboardWebFactory factory)
    : IClassFixture<BrokenDashboardWebFactory>
{
    [Fact]
    public async Task When_the_summary_fails_the_page_stays_usable_and_shows_no_invented_zeros()
    {
        var given = new OwnerDashboardFixture(factory);
        var email = $"panel-roto-{Guid.NewGuid():N}@prueba.local";
        var owner = await given.UserAsync(email);
        var negocio = await given.BusinessAsync(owner, "Con resumen roto",
            appointments: true, queues: true, orders: true);

        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        await PlatformAdministrationApiTests.Login(client, email);
        var response = await client.GetAsync("/panel");

        // La página responde: una excepción del resumen no puede tumbar el circuito ni la pantalla.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("data-testid=\"dashboard-error\"", html);
        Assert.Contains("No pudimos cargar el resumen de tus negocios.", html);
        Assert.Contains("data-testid=\"dashboard-retry\"", html);

        // El negocio y sus accesos siguen ahí: se perdieron las cifras, no la sesión de trabajo.
        Assert.Contains($"data-business-id=\"{negocio.BusinessId}\"", html);
        Assert.Contains($"/panel/{negocio.BusinessId}/citas", html);
        Assert.Contains($"/panel/{negocio.BusinessId}/configuracion", html);

        // Y sin cifras no se inventa ninguna: un cero aquí se leería como "hoy no ha pasado nada".
        Assert.DoesNotContain("Citas de hoy", html);
        Assert.DoesNotContain("Personas esperando", html);
        Assert.DoesNotContain("Entregados hoy", html);
    }
}
