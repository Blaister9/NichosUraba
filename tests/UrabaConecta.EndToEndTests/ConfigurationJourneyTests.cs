using Microsoft.Playwright;
using System.Text.Json;
using UrabaConecta.Contracts;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.EndToEndTests;

public sealed class ConfigurationJourneyTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    [Fact]
    public async Task Owner_creates_edits_deactivates_and_reactivates_service()
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        { ViewportSize = new() { Width = 360, Height = 800 } });
        var page = await context.NewPageAsync();
        await Login(page, DevelopmentSeeder.BellaOwnerEmail);
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.BellaBusinessId}/configuracion/servicios");
        var name = $"Servicio E2E {Guid.NewGuid():N}"[..25];
        await Expect(page.GetByTestId("save-service")).ToBeEnabledAsync(new() { Timeout = 15_000 });
        await page.GetByLabel("Nombre").FillAsync(name);
        await page.GetByLabel("Descripción corta").FillAsync("Creado desde navegador móvil");
        await page.GetByLabel("Duración en minutos").FillAsync("30");
        await page.GetByLabel("Precio de referencia (COP)").FillAsync("19000");
        await page.GetByLabel("Orden de visualización").FillAsync("2");
        await page.GetByTestId("save-service").ClickAsync();
        var row = page.GetByTestId("service-row").Filter(new() { HasTextString = name });
        await Expect(row).ToBeVisibleAsync();

        await row.GetByRole(AriaRole.Button, new() { Name = "Editar" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Editar servicio" })).ToBeVisibleAsync();
        var edited = $"{name} editado";
        await page.GetByLabel("Nombre").FillAsync(edited);
        await page.GetByLabel("Nombre").BlurAsync();
        await page.GetByTestId("save-service").ClickAsync();
        row = page.GetByTestId("service-row").Filter(new() { HasTextString = edited });
        await Expect(row).ToBeVisibleAsync();
        await row.GetByRole(AriaRole.Button, new() { Name = "Desactivar" }).ClickAsync();
        await Expect(row.GetByText("Inactivo")).ToBeVisibleAsync();

        await page.GotoAsync($"{fixture.BaseUrl}/negocios/salon-bella-uraba");
        await Expect(page.GetByText(edited)).ToHaveCountAsync(0);
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.BellaBusinessId}/configuracion/servicios");
        row = page.GetByTestId("service-row").Filter(new() { HasTextString = edited });
        await row.GetByRole(AriaRole.Button, new() { Name = "Activar" }).ClickAsync();
        await Expect(row.GetByText("Activo", new() { Exact = true })).ToBeVisibleAsync();
        await page.GotoAsync($"{fixture.BaseUrl}/negocios/salon-bella-uraba");
        await Expect(page.GetByText(edited)).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Hours_and_interval_exception_change_public_availability()
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await context.NewPageAsync();
        await Login(page, DevelopmentSeeder.BellaOwnerEmail);
        var date = NextMonday();

        await page.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.BellaBusinessId}/configuracion/horarios");
        var monday = page.GetByTestId("hour-row").Filter(new()
        { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Lunes", Exact = true }) });
        await Expect(monday).ToBeVisibleAsync();
        var closed = monday.GetByRole(AriaRole.Checkbox, new() { Name = "Día cerrado" });
        if (await closed.IsCheckedAsync()) await closed.UncheckAsync();
        await monday.GetByLabel("Apertura Lunes").FillAsync("09:00");
        await monday.GetByLabel("Cierre Lunes").FillAsync("12:00");
        await monday.GetByRole(AriaRole.Button, new() { Name = "Guardar Lunes" }).ClickAsync();
        await Expect(page.GetByText("Horario guardado.", new() { Exact = false })).ToBeVisibleAsync();

        await page.GotoAsync($"{fixture.BaseUrl}/negocios/salon-bella-uraba");
        await Expect(page.GetByText("Corte femenino")).ToBeVisibleAsync();
        var publicSlotsUrl = $"{fixture.BaseUrl}/api/v1/public/businesses/salon-bella-uraba/appointment-slots?serviceId=10000000-0000-0000-0000-000000000001&date={date:yyyy-MM-dd}";
        var beforeSlots = await ReadSlots(page, publicSlotsUrl);
        Assert.NotEmpty(beforeSlots.Slots);

        await page.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.BellaBusinessId}/configuracion/excepciones");
        await Expect(page.GetByTestId("save-exception")).ToBeEnabledAsync(new() { Timeout = 15_000 });
        var staffSelect = page.GetByTestId("exception-staff");
        var staffValue = await staffSelect.Locator("option").Nth(1).GetAttributeAsync("value");
        await staffSelect.SelectOptionAsync(staffValue!);
        await page.GetByTestId("exception-date").FillAsync(date.ToString("yyyy-MM-dd"));
        await page.GetByTestId("exception-type").SelectOptionAsync("ClosedInterval");
        await page.GetByLabel("Hora inicial").FillAsync("10:00");
        await page.GetByLabel("Hora final").FillAsync("11:00");
        await page.GetByLabel("Motivo interno (opcional)").FillAsync("Prueba E2E");
        await page.GetByTestId("save-exception").ClickAsync();
        await Expect(page.GetByText("Excepción guardada.", new() { Exact = false })).ToBeVisibleAsync();
        var exceptionRow = page.GetByTestId("exception-row").Filter(new() { HasTextString = date.ToString("dd/MM/yyyy") });
        await Expect(exceptionRow.GetByText("Cierre parcial")).ToBeVisibleAsync();
        await Expect(exceptionRow.GetByText("10:00–11:00")).ToBeVisibleAsync();

        var afterSlots = await ReadSlots(page, publicSlotsUrl);
        Assert.True(afterSlots.Slots.Count < beforeSlots.Slots.Count,
            $"Antes: {beforeSlots.Slots.Count}. Después: {afterSlots.Slots.Count}");
    }

    [Fact]
    public async Task Second_owner_cannot_open_bella_configuration_routes()
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await context.NewPageAsync();
        await Login(page, DevelopmentSeeder.OtherOwnerEmail);
        foreach (var suffix in new[] { "configuracion", "configuracion/servicios", "configuracion/horarios" })
        {
            await page.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.BellaBusinessId}/{suffix}");
            await Expect(page.GetByText("No tiene acceso a este establecimiento.")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("service-row")).ToHaveCountAsync(0);
            await Expect(page.GetByTestId("hour-row")).ToHaveCountAsync(0);
        }
    }

    [Fact]
    public async Task Configuration_home_has_no_horizontal_overflow_at_360_pixels()
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        { ViewportSize = new() { Width = 360, Height = 800 } });
        var page = await context.NewPageAsync();
        await Login(page, DevelopmentSeeder.BellaOwnerEmail);
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.BellaBusinessId}/configuracion");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Configuración del negocio" })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Servicios" }).First).ToBeVisibleAsync();
        var overflow = await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth");
        Assert.False(overflow);
    }

    private static DateOnly NextMonday()
    {
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(20));
        while (date.DayOfWeek != DayOfWeek.Monday) date = date.AddDays(1);
        return date;
    }

    private static async Task<SlotListDto> ReadSlots(IPage page, string url)
    {
        var response = await page.GotoAsync(url);
        Assert.True(response?.Ok, $"La consulta pública devolvió {response?.Status}.");
        var json = await page.Locator("body").InnerTextAsync();
        return JsonSerializer.Deserialize<SlotListDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("La respuesta pública no contenía franjas.");
    }

    private async Task Login(IPage page, string email)
    {
        await page.GotoAsync($"{fixture.BaseUrl}/Account/Login");
        await page.GetByLabel("Correo").FillAsync(email);
        await page.GetByLabel("Contraseña").FillAsync(DevelopmentSeeder.DemoPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Ingresar" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/panel"));
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
