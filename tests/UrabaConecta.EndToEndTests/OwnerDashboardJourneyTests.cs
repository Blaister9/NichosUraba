using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.EndToEndTests;

/// <summary>
/// El panel visto por quien opera. Lo que se comprueba aquí no son las cifras —de eso responden las
/// pruebas contra PostgreSQL— sino que cada negocio vea únicamente su operación, que la acción
/// principal lleve a donde dice, y que la pantalla siga siendo usable en un teléfono.
///
/// Antes de esto, /panel era una fila de botones idénticos: no decía en ningún momento cómo iba el día.
/// </summary>
public sealed class OwnerDashboardJourneyTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    [Fact]
    public async Task The_salon_sees_only_its_appointments_and_reaches_them_from_the_panel()
    {
        var page = await PanelAsync(DevelopmentSeeder.BellaOwnerEmail);
        var tarjeta = Card(page, DevelopmentSeeder.BellaBusinessId);

        await Expect(tarjeta).ToBeVisibleAsync();
        await Expect(tarjeta.Locator("[data-testid=appointments-summary]")).ToBeVisibleAsync();
        // Un salón sin turnos ni pedidos no puede ver esas operaciones.
        await Expect(tarjeta.Locator("[data-testid=queues-summary]")).ToHaveCountAsync(0);
        await Expect(tarjeta.Locator("[data-testid=orders-summary]")).ToHaveCountAsync(0);

        // La acción principal lleva a la operación, no a la configuración.
        var operar = tarjeta.Locator("[data-testid=primary-operation-action]");
        await Expect(operar).ToHaveTextAsync("Administrar citas");
        await operar.ClickAsync();
        await page.WaitForURLAsync(url => url.Contains($"/panel/{DevelopmentSeeder.BellaBusinessId}/citas"));

        // Y la configuración sigue estando, un escalón por debajo.
        await page.GoBackAsync();
        await Expect(Card(page, DevelopmentSeeder.BellaBusinessId)
            .Locator("[data-testid=business-configuration-action]")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_barbershop_sees_only_its_queue()
    {
        var page = await PanelAsync(DevelopmentSeeder.CorteOwnerEmail);
        var tarjeta = Card(page, DevelopmentSeeder.CorteBusinessId);

        await Expect(tarjeta.Locator("[data-testid=queues-summary]")).ToBeVisibleAsync();
        await Expect(tarjeta.Locator("[data-testid=appointments-summary]")).ToHaveCountAsync(0);
        await Expect(tarjeta.Locator("[data-testid=orders-summary]")).ToHaveCountAsync(0);

        var operar = tarjeta.Locator("[data-testid=primary-operation-action]");
        await Expect(operar).ToHaveTextAsync("Operar turnos");
        await operar.ClickAsync();
        await page.WaitForURLAsync(url => url.Contains($"/panel/{DevelopmentSeeder.CorteBusinessId}/turnos"));
    }

    [Fact]
    public async Task The_restaurant_sees_only_its_orders()
    {
        var page = await PanelAsync(DevelopmentSeeder.SazonOwnerEmail);
        var tarjeta = Card(page, DevelopmentSeeder.SazonBusinessId);

        await Expect(tarjeta.Locator("[data-testid=orders-summary]")).ToBeVisibleAsync();
        await Expect(tarjeta.Locator("[data-testid=appointments-summary]")).ToHaveCountAsync(0);
        await Expect(tarjeta.Locator("[data-testid=queues-summary]")).ToHaveCountAsync(0);

        var operar = tarjeta.Locator("[data-testid=primary-operation-action]");
        await Expect(operar).ToHaveTextAsync("Operar pedidos");
        await operar.ClickAsync();
        await page.WaitForURLAsync(url => url.Contains($"/panel/{DevelopmentSeeder.SazonBusinessId}/pedidos"));
    }

    [Fact]
    public async Task A_partner_operator_who_also_operates_a_business_keeps_both_things_on_the_same_screen()
    {
        // Este caso ya se rompió una vez: al ganar el panel de socia se perdió el de propietaria.
        // La misma persona puede dar de alta negocios Y operar uno donde tiene membresía.
        await GrantOwnershipAsync(DevelopmentSeeder.PartnerOperatorEmail, DevelopmentSeeder.SazonBusinessId);
        var page = await PanelAsync(DevelopmentSeeder.PartnerOperatorEmail);

        await Expect(page.Locator("[data-testid=crear-negocio]").First).ToBeVisibleAsync();
        await Expect(Card(page, DevelopmentSeeder.SazonBusinessId)).ToBeVisibleAsync();
        await Expect(Card(page, DevelopmentSeeder.SazonBusinessId)
            .Locator("[data-testid=orders-summary]")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_panel_fits_a_phone_without_sideways_scrolling()
    {
        var page = await PanelAsync(DevelopmentSeeder.CorteOwnerEmail, width: 375, height: 812);
        await Expect(Card(page, DevelopmentSeeder.CorteBusinessId)).ToBeVisibleAsync();

        Assert.False(await page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > document.documentElement.clientWidth + 1"));
        // La acción principal sigue alcanzable sin buscarla.
        await Expect(Card(page, DevelopmentSeeder.CorteBusinessId)
            .Locator("[data-testid=primary-operation-action]")).ToBeVisibleAsync();
    }

    // ------------------------------------------------------------------ una sola carga

    /// <summary>
    /// InteractiveServer inicializa el componente dos veces —al prerenderizar y al abrir el circuito—
    /// y el resumen se resolvía entero las dos. Aquí se cuenta el SQL de una carga real de navegador:
    /// tiene que haber UNA agregación de cada familia, no dos.
    ///
    /// Es la prueba más cercana posible a las dos fases juntas: una prueba de integración sólo puede
    /// provocar el prerender, porque no abre circuito.
    /// </summary>
    [Fact]
    public async Task Handing_over_from_prerender_to_the_circuit_does_not_ask_for_the_summary_again()
    {
        var context = await fixture.Browser.NewContextAsync(
            new() { ViewportSize = new() { Width = 1366, Height = 768 } });
        var page = await context.NewPageAsync();
        await LoginAsync(page, DevelopmentSeeder.CorteOwnerEmail);

        var antes = fixture.CountInLog(TurnosSql);
        // Esperar la negociación confirma que el circuito llegó a abrirse: sin eso la prueba pasaría
        // sola, contando una única carga porque la interactividad nunca arrancó.
        var negociacion = page.WaitForResponseAsync(r => r.Url.Contains("/_blazor/negotiate"));
        await page.GotoAsync($"{fixture.BaseUrl}/panel");
        await negociacion;
        await Expect(Card(page, DevelopmentSeeder.CorteBusinessId)).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("() => window.Blazor !== undefined");
        // El registro llega por otro hilo, así que primero se espera a ver la consulta del prerender
        // y sólo después se deja el margen en el que antes aparecía la segunda.
        await WaitForLogAsync(TurnosSql, antes + 1);
        await page.WaitForTimeoutAsync(2500);

        var consultas = fixture.CountInLog(TurnosSql) - antes;
        Assert.Equal(1, consultas);

        // Y el resumen sigue en pantalla después de hidratar: ni "Cargando…" otra vez ni ceros nuevos.
        await Expect(page.Locator("[data-testid=dashboard-loading]")).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-testid=dashboard-error]")).ToHaveCountAsync(0);
        await Expect(Card(page, DevelopmentSeeder.CorteBusinessId)
            .Locator("[data-testid=queues-summary]")).ToBeVisibleAsync();
    }

    /// <summary>
    /// Un fallo del resumen no puede quedar congelado: el reintento tiene que volver a consultar de
    /// verdad. La avería se provoca escondiendo la tabla de turnos, que es la que alimenta la única
    /// familia de este negocio, y se deshace antes de reintentar.
    /// </summary>
    [Fact]
    public async Task A_failed_summary_can_be_retried_and_recovers()
    {
        var context = await fixture.Browser.NewContextAsync(
            new() { ViewportSize = new() { Width = 1366, Height = 768 } });
        var page = await context.NewPageAsync();
        await LoginAsync(page, DevelopmentSeeder.CorteOwnerEmail);

        var intentosAlEmpezar = fixture.CountInLog(TurnosSql);
        await HideQueueTicketsAsync(hide: true);
        try
        {
            await page.GotoAsync($"{fixture.BaseUrl}/panel");
            await Expect(page.Locator("[data-testid=dashboard-error]")).ToBeVisibleAsync();
            // La página sigue siendo utilizable: el negocio y sus accesos no desaparecen.
            await Expect(Card(page, DevelopmentSeeder.CorteBusinessId)).ToBeVisibleAsync();
            await Expect(page.Locator("[data-testid=queues-summary]")).ToHaveCountAsync(0);

            // Se deja que el circuito arranque con la avería todavía puesta. Que vuelva a intentarlo
            // —dos intentos, no uno— es la prueba de que un prerender fallido no se guardó como si
            // hubiera ido bien: si se hubiera persistido, el circuito habría heredado el error sin
            // tocar la base y no habría forma de salir de ahí.
            await page.WaitForFunctionAsync("() => window.Blazor !== undefined");
            Assert.True(await WaitForLogAsync(TurnosSql, intentosAlEmpezar + 2),
                "El circuito no reintentó tras el fallo del prerender.");
            await Expect(page.Locator("[data-testid=dashboard-error]")).ToBeVisibleAsync();
        }
        finally
        {
            await HideQueueTicketsAsync(hide: false);
        }

        // Con la avería resuelta, reintentar tiene que consultar de nuevo. Que aparezcan los turnos lo
        // demuestra por sí solo: el estado anterior no tenía resumen de este negocio, así que esas
        // cifras no pueden venir de nada guardado —sólo de una consulta que acaba de ocurrir—.
        Assert.True(await QueueTicketsExistsAsync(), "La tabla no volvió a su sitio antes del reintento.");
        await page.Locator("[data-testid=dashboard-retry]").ClickAsync();
        await Expect(Card(page, DevelopmentSeeder.CorteBusinessId)
            .Locator("[data-testid=queues-summary]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid=dashboard-error]")).ToHaveCountAsync(0);
    }

    /// <summary>Fragmento que identifica la agregación de turnos en el registro de EF.</summary>
    private const string TurnosSql = "AS \"ServedToday\"";

    /// <summary>
    /// Espera a que el registro alcance ese número de apariciones. La aplicación escribe su salida
    /// por otro hilo, así que contar justo después de ver la pantalla puede leer una línea que aún no
    /// ha llegado; sin esta espera la medición fallaría por el reloj, no por el comportamiento.
    /// </summary>
    private async Task<bool> WaitForLogAsync(string needle, int atLeast, int timeoutMs = 10000)
    {
        var limit = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < limit)
        {
            if (fixture.CountInLog(needle) >= atLeast) return true;
            await Task.Delay(100);
        }
        return false;
    }

    /// <summary>
    /// Esconde y repone la tabla de turnos. Los nombres van literales —un identificador no se puede
    /// parametrizar— y son constantes de la prueba, nunca entrada de nadie.
    /// </summary>
    private async Task<bool> QueueTicketsExistsAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.ConnectionString).Options;
        await using var db = new AppDbContext(options);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT to_regclass('public.queue_tickets') IS NOT NULL;";
        await db.Database.OpenConnectionAsync();
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private async Task HideQueueTicketsAsync(bool hide)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.ConnectionString).Options;
        await using var db = new AppDbContext(options);
        await db.Database.ExecuteSqlRawAsync(hide
            ? "ALTER TABLE IF EXISTS queue_tickets RENAME TO queue_tickets_oculta;"
            : "ALTER TABLE IF EXISTS queue_tickets_oculta RENAME TO queue_tickets;");
    }

    // ------------------------------------------------------------------ apoyos

    private static ILocator Card(IPage page, Guid businessId)
        => page.Locator($"[data-testid=business-dashboard][data-business-id='{businessId}']");

    private async Task<IPage> PanelAsync(string email, int width = 1366, int height = 768)
    {
        var context = await fixture.Browser.NewContextAsync(
            new() { ViewportSize = new() { Width = width, Height = height } });
        var page = await context.NewPageAsync();
        await LoginAsync(page, email);
        await page.GotoAsync($"{fixture.BaseUrl}/panel");
        return page;
    }

    private async Task LoginAsync(IPage page, string email)
    {
        await page.GotoAsync($"{fixture.BaseUrl}/Account/Login");
        await page.GetByLabel("Correo").FillAsync(email);
        await page.GetByLabel("Contraseña").FillAsync(DevelopmentSeeder.DemoPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Ingresar" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/panel") || url.Contains("/Account/ChangeTemporaryPassword"));
    }

    /// <summary>
    /// El sembrado no da membresías a la socia, así que este escenario se monta añadiéndola. Se hace
    /// contra la base porque la aplicación corre en otro proceso.
    /// </summary>
    private async Task GrantOwnershipAsync(string email, Guid businessId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.ConnectionString).Options;
        await using var db = new AppDbContext(options);
        var userId = await db.Users.Where(x => x.Email == email).Select(x => x.Id).SingleAsync();
        if (await db.BusinessMemberships.AnyAsync(x => x.UserId == userId && x.BusinessId == businessId)) return;
        db.BusinessMemberships.Add(new BusinessMembership(Guid.NewGuid(), businessId, userId,
            MembershipRole.Owner));
        await db.SaveChangesAsync();
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
