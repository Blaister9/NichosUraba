using Microsoft.Playwright;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.EndToEndTests;

public sealed class CommercialReadinessTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public async Task Critical_form_is_locked_before_interactivity_and_enabled_after_the_circuit_connects()
    {
        await using var staticContext = await fixture.Browser.NewContextAsync(new() { JavaScriptEnabled = false });
        var staticPage = await staticContext.NewPageAsync();
        await staticPage.GotoAsync($"{fixture.BaseUrl}/negocios/salon-bella-uraba/citas");
        await Assertions.Expect(staticPage.GetByRole(AriaRole.Heading, new() { Name = "Solicitar una cita" }))
            .ToBeVisibleAsync();
        Assert.True(await staticPage.GetByLabel("Nombre o alias").IsDisabledAsync());
        Assert.True(await staticPage.GetByRole(AriaRole.Button, new() { Name = "Enviar solicitud" }).IsDisabledAsync());

        await using var interactiveContext = await fixture.Browser.NewContextAsync();
        var page = await interactiveContext.NewPageAsync();
        await page.GotoAsync($"{fixture.BaseUrl}/negocios/salon-bella-uraba/citas");
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Ver horas disponibles" }))
            .ToBeEnabledAsync();
        await Assertions.Expect(page.GetByLabel("Nombre o alias")).ToBeEnabledAsync();
    }

    [Fact]
    public async Task Partner_onboarding_exposes_service_duration_and_pickup_interval_controls()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await Login(page, DevelopmentSeeder.PartnerOperatorEmail);
        await page.GotoAsync($"{fixture.BaseUrl}/admin/negocios/nuevo");
        await Assertions.Expect(page.Locator("[data-testid=campo-nombre]")).ToBeEnabledAsync();
        // El paso 1 exige identidad mínima antes de avanzar.
        await page.Locator("[data-testid=campo-nombre]").FillAsync("Negocio de comprobación");
        await page.Locator("[data-testid=campo-municipio]").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await page.Locator("[data-testid=campo-categoria]").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await page.Locator("[data-testid=campo-descripcion-breve]").FillAsync("Negocio de comprobación.");
        await page.Locator("[data-testid=continuar]").ClickAsync();
        await Assertions.Expect(page.GetByLabel("Duración del servicio en minutos")).ToBeEnabledAsync();
        await page.GetByLabel("Pedidos para recoger").CheckAsync();
        await Assertions.Expect(page.GetByLabel("Intervalo de recogida en minutos")).ToBeEnabledAsync();
        await Assertions.Expect(page.GetByLabel("Preparación mínima en minutos")).ToBeEnabledAsync();
        await Assertions.Expect(page.GetByLabel("Cupo por intervalo")).ToBeEnabledAsync();
    }

    [Fact]
    public async Task Three_demo_businesses_render_logo_cover_and_two_gallery_images_uploaded_from_the_interface()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var admin = await context.NewPageAsync();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        foreach (var (id, slug, name) in new[]
        {
            (DevelopmentSeeder.BellaBusinessId, "salon-bella-uraba", "Salón Bella Urabá"),
            (DevelopmentSeeder.CorteBusinessId, "barberia-el-corte", "Barbería El Corte"),
            (DevelopmentSeeder.SazonBusinessId, "restaurante-sazon-local", "Restaurante Sazón Local")
        })
        {
            await admin.GotoAsync($"{fixture.BaseUrl}/admin/negocios/{id}");
            await Assertions.Expect(admin.GetByRole(AriaRole.Heading, new() { Name = name })).ToBeVisibleAsync();
            var expectedImageCount = 0;
            foreach (var (kind, index) in new[] { ("Logo", 0), ("Cover", 0), ("Gallery", 1), ("Gallery", 2) })
            {
                await admin.GetByLabel("Tipo de imagen").SelectOptionAsync(kind);
                await admin.GetByLabel("Texto alternativo").FillAsync($"{name} imagen ficticia {kind} {index}");
                await admin.Locator("input[type=file]").SetInputFilesAsync(new FilePayload
                {
                    Name = $"{slug}-{kind}-{index}.png", MimeType = "image/png", Buffer = TinyPng
                });
                expectedImageCount++;
                await Assertions.Expect(admin.Locator("figure.business-card"))
                    .ToHaveCountAsync(expectedImageCount);
            }
            await admin.GotoAsync($"{fixture.BaseUrl}/negocios/{slug}");
            await Assertions.Expect(admin.GetByRole(AriaRole.Heading, new() { Name = name })).ToBeVisibleAsync();
            Assert.True(await admin.Locator("img").CountAsync() >= 4);
        }
    }

    private async Task Login(IPage page, string email)
    {
        await page.GotoAsync($"{fixture.BaseUrl}/Account/Login");
        await page.GetByLabel("Correo").FillAsync(email);
        await page.GetByLabel("Contraseña").FillAsync(DevelopmentSeeder.DemoPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Ingresar" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/panel"));
    }
}
