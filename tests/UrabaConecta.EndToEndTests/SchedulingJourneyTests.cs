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
        await page.GotoAsync(fixture.BaseUrl);
        await Expect(page.GetByText("Salón Bella Urabá")).ToBeVisibleAsync();
        await page.GotoAsync($"{fixture.BaseUrl}/negocios/salon-bella-uraba");
        var service = page.Locator("article.service-card").Filter(new() { HasTextString = "Corte femenino" });
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
        await Expect(page.GetByText("Confirmada")).ToBeVisibleAsync();
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.BellaBusinessId}/citas");
        card = page.Locator("[data-testid=appointment-card]").Filter(new() { HasTextString = "E2E Ana" });
        await ClickUntilStatus(card, "Completar", "Completada");
        await page.GotoAsync($"{fixture.BaseUrl}/seguimiento/citas/{code}");
        await Expect(page.GetByText("Completada")).ToBeVisibleAsync();
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

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
    private static async Task ClickUntilStatus(ILocator card, string action, string expected)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if ((await card.InnerTextAsync()).Contains(expected, StringComparison.Ordinal)) return;
            var button = card.GetByRole(AriaRole.Button, new() { Name = action });
            if (await button.CountAsync() > 0) await button.ClickAsync();
            await Task.Delay(500);
        }
        Assert.Contains(expected, await card.InnerTextAsync());
    }
}
