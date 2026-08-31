using Microsoft.Playwright;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.EndToEndTests;

public sealed class SchedulingJourneyTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    [Fact]
    public async Task Visitor_owner_and_tracking_complete_the_vertical()
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{fixture.BaseUrl}/categorias/belleza-cuidado-personal?municipio=apartado");
        await Expect(page.GetByText("Salón Bella Urabá")).ToBeVisibleAsync();
        await page.GotoAsync($"{fixture.BaseUrl}/negocios/salon-bella-uraba");
        // La ficha dejó de listar los servicios como tarjetas sueltas: ahora usan la misma anatomía
        // de catálogo que los productos, con fotografía, duración y precio en una fila.
        var service = page.Locator("article.catalogo-item").Filter(new() { HasTextString = "Corte femenino" });
        await Expect(service).ToBeVisibleAsync();
        await page.GotoAsync($"{fixture.BaseUrl}/negocios/salon-bella-uraba/citas?serviceId=10000000-0000-0000-0000-000000000001");
        await page.GetByRole(AriaRole.Button, new() { Name = "Ver horas disponibles" }).ClickAsync();
        await page.Locator("button.slot").First.WaitForAsync();
        await page.Locator("button.slot").First.ClickAsync();
        await page.GetByLabel("Nombre o alias").FillAsync("E2E Ana");
        await page.GetByLabel("Teléfono").FillAsync("3004567890");
        await page.GetByLabel("Observación corta (opcional)").FillAsync("Cita de prueba");
        await page.GetByRole(AriaRole.Checkbox).CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Enviar solicitud" }).ClickAsync();
        await Expect(page.GetByText("Solicitud enviada.")).ToBeVisibleAsync();
        var code = (await page.GetByTestId("tracking-code").InnerTextAsync()).Trim();
        await Expect(page.GetByText("Pendiente")).ToBeVisibleAsync();

        await page.GotoAsync($"{fixture.BaseUrl}/Account/Login");
        await page.GetByLabel("Correo").FillAsync(DevelopmentSeeder.BellaOwnerEmail);
        await page.GetByLabel("Contraseña").FillAsync(DevelopmentSeeder.DemoPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Ingresar" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/panel"));
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.BellaBusinessId}/citas");
        var card = page.Locator("[data-testid=appointment-card]").Filter(new() { HasTextString = "E2E Ana" });
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await ClickUntilStatus(card, "Confirmar", "Confirmada");

        await page.GotoAsync($"{fixture.BaseUrl}/seguimiento/citas/{code}");
        // Exact: el seguimiento lista también las novedades, y "Cita confirmada" contiene la palabra.
        await Expect(page.GetByText("Confirmada", new() { Exact = true })).ToBeVisibleAsync();
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.BellaBusinessId}/citas");
        card = page.Locator("[data-testid=appointment-card]").Filter(new() { HasTextString = "E2E Ana" });
        await ClickUntilStatus(card, "Completar", "Completada");
        await page.GotoAsync($"{fixture.BaseUrl}/seguimiento/citas/{code}");
        await Expect(page.GetByText("Completada", new() { Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Second_owner_cannot_open_bella_appointments()
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{fixture.BaseUrl}/Account/Login");
        await page.GetByLabel("Correo").FillAsync(DevelopmentSeeder.OtherOwnerEmail);
        await page.GetByLabel("Contraseña").FillAsync(DevelopmentSeeder.DemoPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Ingresar" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/panel"));
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.BellaBusinessId}/citas");
        await Expect(page.GetByText("No tiene acceso a este establecimiento.")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("appointment-card")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Cambiar de servicio dejaba en pantalla las horas del anterior —elegibles, y con la que ya
    /// estuviera elegida todavía marcada— hasta que alguien recordaba pulsar "Ver horas
    /// disponibles". Aquí se fija lo contrario: la rejilla pertenece siempre al servicio elegido,
    /// llega sola y no arrastra la elección anterior.
    /// </summary>
    [Fact]
    public async Task Changing_the_service_replaces_the_hours_without_asking()
    {
        // Los dos servicios sembrados para Bella: 60 y 45 minutos, ambos con personal asignado.
        const string corte = "10000000-0000-0000-0000-000000000001";
        const string cepillado = "10000000-0000-0000-0000-000000000002";
        await using var context = await fixture.Browser.NewContextAsync(new()
        { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{fixture.BaseUrl}/negocios/salon-bella-uraba/citas?serviceId={corte}");
        await Expect(page.Locator($"[data-servicio='{corte}'] button.slot").First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await page.Locator("button.slot").First.ClickAsync();
        await Expect(page.Locator("button.slot[aria-checked='true']")).ToHaveCountAsync(1);
        await Expect(page.Locator("p.reserva-resumen")).ToBeVisibleAsync();

        // Sin volver a pulsar "Ver horas disponibles": las horas del servicio nuevo llegan solas.
        await page.GetByLabel("Servicio")
            .SelectOptionAsync(new SelectOptionValue { Label = "Cepillado · 45 min" });
        await Expect(page.Locator($"[data-servicio='{cepillado}'] button.slot").First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(page.Locator($"[data-servicio='{corte}']")).ToHaveCountAsync(0);
        // Y la hora elegida para el servicio anterior no sobrevive al cambio: ni marcada en la
        // rejilla ni en el resumen de lo que se va a enviar.
        await Expect(page.Locator("button.slot[aria-checked='true']")).ToHaveCountAsync(0);
        await Expect(page.Locator("p.reserva-resumen")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Dos clientas sobre la misma hora. A la que llega tarde se le recargan las horas solas, y el
    /// motivo —"Ese horario acaba de ocuparse"— tiene que seguir en pantalla después de esa recarga:
    /// es lo único que explica por qué la rejilla cambió sin que ella tocara nada. Antes la recarga
    /// automática borraba ese aviso al empezar y la dejaba sin explicación.
    /// </summary>
    [Fact]
    public async Task A_taken_hour_keeps_explaining_itself_after_the_refresh()
    {
        var url = $"{fixture.BaseUrl}/negocios/salon-bella-uraba/citas" +
                  "?serviceId=10000000-0000-0000-0000-000000000001";

        // La que se quedará sin la hora elige primero y se queda mirando su rejilla, sin enviar.
        await using var tarde = await fixture.Browser.NewContextAsync(new()
        { ViewportSize = new() { Width = 390, Height = 844 } });
        var perdedora = await tarde.NewPageAsync();
        await perdedora.GotoAsync(url);
        await Expect(perdedora.Locator("button.slot").First).ToBeVisibleAsync(new() { Timeout = 20_000 });
        // Una de la tarde, para no disputarle la primera hora a los otros recorridos de esta clase.
        var disputada = System.Text.RegularExpressions.Regex.Replace(
            await perdedora.Locator("button.slot").Nth(20).InnerTextAsync(), @"\s+", " ").Trim();
        await perdedora.Locator("button.slot").Nth(20).ClickAsync();
        await Expect(perdedora.Locator("button.slot[aria-checked='true']")).ToHaveCountAsync(1);

        // Otra clienta se lleva esa misma hora mientras la primera todavía rellena sus datos.
        await using var pronto = await fixture.Browser.NewContextAsync(new()
        { ViewportSize = new() { Width = 390, Height = 844 } });
        var ganadora = await pronto.NewPageAsync();
        await ganadora.GotoAsync(url);
        await Expect(ganadora.Locator("button.slot").First).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await ganadora.Locator("button.slot").Filter(new() { HasTextString = disputada }).First.ClickAsync();
        await RellenarYEnviar(ganadora, "E2E Puntual");
        await Expect(ganadora.GetByText("Solicitud enviada.")).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // La primera envía sobre una hora que ya no existe: 409 SLOT_UNAVAILABLE.
        await RellenarYEnviar(perdedora, "E2E Tarde");
        // La hora desaparece de la rejilla, así que la recarga automática ya terminó...
        await Expect(perdedora.Locator("button.slot").Filter(new() { HasTextString = disputada }))
            .ToHaveCountAsync(0, new() { Timeout = 20_000 });
        await Expect(perdedora.Locator("button.slot[aria-checked='true']")).ToHaveCountAsync(0);
        // ...y el motivo sigue ahí después de esa recarga, que es lo que se perdía.
        await Expect(perdedora.Locator("p.error[role='alert']"))
            .ToHaveTextAsync("Ese horario acaba de ocuparse. Elija otro.");
        await Expect(perdedora.GetByRole(AriaRole.Button, new() { Name = "Enviar solicitud" }))
            .ToBeEnabledAsync();

        // Y puede elegir otra hora y terminar sin recargar la página ni volver a escribir sus datos.
        await perdedora.Locator("button.slot").First.ClickAsync();
        await perdedora.GetByRole(AriaRole.Button, new() { Name = "Enviar solicitud" }).ClickAsync();
        await Expect(perdedora.GetByText("Solicitud enviada.")).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    private static async Task RellenarYEnviar(IPage page, string alias)
    {
        await page.GetByLabel("Nombre o alias").FillAsync(alias);
        await page.GetByLabel("Teléfono").FillAsync("3004567890");
        await page.GetByRole(AriaRole.Checkbox).CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Enviar solicitud" }).ClickAsync();
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
    private static async Task ClickUntilStatus(ILocator card, string action, string expected)
    {
        if ((await card.InnerTextAsync()).Contains(expected, StringComparison.Ordinal)) return;
        var button = card.GetByRole(AriaRole.Button, new() { Name = action });
        await Assertions.Expect(button).ToBeEnabledAsync(new() { Timeout = 15_000 });
        await button.ClickAsync();
        await Assertions.Expect(card.GetByText(expected, new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
