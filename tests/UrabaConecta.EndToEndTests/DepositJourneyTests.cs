using Microsoft.Playwright;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.EndToEndTests;

/// <summary>
/// El recorrido completo del adelanto en un navegador real: la propietaria lo configura, una
/// clienta reserva, envía el comprobante por WhatsApp, el negocio lo rechaza, la clienta reintenta,
/// el negocio verifica y recién entonces confirma la cita.
/// </summary>
public sealed class DepositJourneyTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    private const string Instrucciones = "Transfiera a Bancolombia ahorros 000-000-000 a nombre del salón.";
    private const string WhatsApp = "573001112233";

    [Fact]
    public async Task The_owner_configures_a_deposit_and_the_customer_completes_the_manual_cycle()
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await context.NewPageAsync();

        // 1. La propietaria configura el servicio: $80.000 con 50 % de adelanto.
        await LoginAsync(page, DevelopmentSeeder.BellaOwnerEmail);
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.BellaBusinessId}/configuracion/servicios");
        // Hasta que el circuito no está vivo, escribir en el formulario no llega al modelo.
        await Expect(page.GetByTestId("save-service")).ToBeEnabledAsync(new() { Timeout = 30_000 });
        await page.GetByTestId("service-name").FillAsync("Tinturado E2E");
        await page.GetByLabel("Duración en minutos").FillAsync("60");
        await page.GetByLabel("Precio de referencia (COP)").FillAsync("80000");
        await page.GetByTestId("requires-deposit").CheckAsync();
        await page.GetByTestId("deposit-type").SelectOptionAsync("Percentage");
        await page.GetByTestId("deposit-value").FillAsync("50");
        await page.GetByTestId("deposit-whatsapp").FillAsync(WhatsApp);
        await page.GetByTestId("deposit-instructions").FillAsync(Instrucciones);
        // El adelanto se calcula mientras se escribe, antes de guardar.
        await Expect(page.GetByTestId("deposit-preview")).ToContainTextAsync("40.000");
        await page.GetByTestId("save-service").ClickAsync();
        await Expect(page.GetByText("Servicio creado.")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        var fila = page.Locator("[data-testid=service-row]").Filter(new() { HasTextString = "Tinturado E2E" });
        await Expect(fila.GetByTestId("service-deposit")).ToContainTextAsync("40.000");

        // La profesional que atiende el servicio nuevo, para que haya horas disponibles.
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.BellaBusinessId}/configuracion/personal");
        await Expect(page.GetByTestId("save-staff")).ToBeEnabledAsync(new() { Timeout = 30_000 });
        await page.GetByTestId("staff-name").FillAsync("Profesional Tinturado");
        await page.Locator("label.check-option").Filter(new() { HasTextString = "Tinturado E2E" })
            .Locator("input[type=checkbox]").CheckAsync();
        await page.GetByTestId("save-staff").ClickAsync();
        await Expect(page.GetByText("Perfil creado.")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // 2 y 3. La clienta ve el adelanto calculado antes de confirmar y reserva.
        await using var visitante = await fixture.Browser.NewContextAsync(new()
        { ViewportSize = new() { Width = 390, Height = 844 } });
        var publica = await visitante.NewPageAsync();
        await publica.GotoAsync($"{fixture.BaseUrl}/negocios/salon-bella-uraba/citas");
        await publica.GetByLabel("Servicio").SelectOptionAsync(new SelectOptionValue { Label = "Tinturado E2E · 60 min" });
        await publica.GetByRole(AriaRole.Button, new() { Name = "Ver horas disponibles" }).ClickAsync();
        await publica.Locator("button.slot").First.WaitForAsync(new() { Timeout = 15_000 });
        await Expect(publica.GetByTestId("deposit-amount")).ToContainTextAsync("40.000");
        await Expect(publica.GetByTestId("deposit-summary")).ToContainTextAsync("80.000");
        await Expect(publica.GetByText("directamente al negocio")).ToBeVisibleAsync();
        await publica.Locator("button.slot").First.ClickAsync();
        await publica.GetByLabel("Nombre o alias").FillAsync("E2E Adelanto");
        await publica.GetByLabel("Teléfono").FillAsync("3004567890");
        await publica.GetByRole(AriaRole.Checkbox).CheckAsync();
        await publica.GetByRole(AriaRole.Button, new() { Name = "Enviar solicitud" }).ClickAsync();
        await Expect(publica.GetByText("Solicitud enviada.")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        var codigo = (await publica.GetByTestId("tracking-code").InnerTextAsync()).Trim();
        await Expect(publica.GetByTestId("deposit-status")).ToHaveTextAsync("Adelanto pendiente");
        await Expect(publica.GetByTestId("deposit-amount")).ToContainTextAsync("40.000");

        // 4 y 5. El enlace de WhatsApp lleva el número normalizado y el mensaje codificado.
        var enlace = await publica.GetByTestId("whatsapp-link").GetAttributeAsync("href");
        Assert.StartsWith($"https://wa.me/{WhatsApp}?text=", enlace);
        var mensaje = Uri.UnescapeDataString(enlace![$"https://wa.me/{WhatsApp}?text=".Length..]);
        Assert.Contains("Hola, realicé el adelanto de mi cita.", mensaje);
        Assert.Contains("Negocio: Salón Bella Urabá", mensaje);
        Assert.Contains("Servicio: Tinturado E2E", mensaje);
        Assert.Contains($"Código: {codigo}", mensaje);
        Assert.Contains("Adjunto el comprobante para su verificación.", mensaje);
        Assert.DoesNotContain("E2E Adelanto", mensaje);
        Assert.DoesNotContain("3004567890", mensaje);

        // 6. La clienta marca que ya lo envió.
        await publica.GetByTestId("report-deposit").ClickAsync();
        await Expect(publica.GetByTestId("deposit-status")).ToHaveTextAsync("Comprobante reportado",
            new() { Timeout = 15_000 });

        // 7, 8 y 9. La propietaria lo ve reportado y lo rechaza.
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.BellaBusinessId}/citas");
        var tarjeta = page.Locator("[data-testid=appointment-card]").Filter(new() { HasTextString = "E2E Adelanto" });
        await Expect(tarjeta).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(tarjeta.GetByTestId("deposit-status")).ToHaveTextAsync("Comprobante reportado");
        await Expect(tarjeta.GetByTestId("deposit-amount")).ToContainTextAsync("40.000");
        // Todavía no se puede confirmar: falta verificar el adelanto.
        await tarjeta.GetByRole(AriaRole.Button, new() { Name = "Confirmar" }).ClickAsync();
        await Expect(page.GetByText("Verifique el adelanto antes de confirmar la cita."))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await tarjeta.GetByTestId("deposit-reject").ClickAsync();
        await Expect(tarjeta.GetByTestId("deposit-status")).ToHaveTextAsync("Comprobante rechazado",
            new() { Timeout = 15_000 });

        // 10 y 11. La clienta vuelve a ver el botón de WhatsApp y reintenta.
        await publica.GotoAsync($"{fixture.BaseUrl}/seguimiento/citas/{codigo}");
        await Expect(publica.GetByTestId("deposit-status")).ToHaveTextAsync("Comprobante rechazado");
        await Expect(publica.GetByTestId("whatsapp-link")).ToBeVisibleAsync();
        await publica.GetByTestId("report-deposit").ClickAsync();
        await Expect(publica.GetByTestId("deposit-status")).ToHaveTextAsync("Comprobante reportado",
            new() { Timeout = 15_000 });

        // 12 y 13. La propietaria verifica y ahora sí confirma la cita.
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.BellaBusinessId}/citas");
        tarjeta = page.Locator("[data-testid=appointment-card]").Filter(new() { HasTextString = "E2E Adelanto" });
        await tarjeta.GetByTestId("deposit-verify").ClickAsync();
        await Expect(tarjeta.GetByTestId("deposit-status")).ToHaveTextAsync("Adelanto verificado",
            new() { Timeout = 15_000 });
        await Expect(tarjeta).ToContainTextAsync("Propietaria Bella");
        await tarjeta.GetByRole(AriaRole.Button, new() { Name = "Confirmar" }).ClickAsync();
        await Expect(tarjeta.GetByText("Confirmada", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // 15. Y la clienta ve el resultado en su seguimiento, leído de nuevo desde PostgreSQL.
        await publica.GotoAsync($"{fixture.BaseUrl}/seguimiento/citas/{codigo}");
        // Exact: la pantalla de seguimiento lleva ahora las novedades guardadas, y "Cita confirmada"
        // contiene esta misma palabra. Lo que se afirma aquí es el estado, no el historial.
        await Expect(publica.GetByText("Confirmada", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(publica.GetByTestId("deposit-status")).ToHaveTextAsync("Adelanto verificado");
        await Expect(publica.GetByText("El negocio verificó su adelanto. No tiene que enviar nada más."))
            .ToBeVisibleAsync();
        await Expect(publica.GetByTestId("report-deposit")).ToHaveCountAsync(0);
    }

    private async Task LoginAsync(IPage page, string email)
    {
        await page.GotoAsync($"{fixture.BaseUrl}/Account/Login");
        await page.GetByLabel("Correo").FillAsync(email);
        await page.GetByLabel("Contraseña").FillAsync(DevelopmentSeeder.DemoPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Ingresar" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/panel"));
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
