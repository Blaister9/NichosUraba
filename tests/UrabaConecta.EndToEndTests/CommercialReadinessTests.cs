using Microsoft.Playwright;
using System.Text.RegularExpressions;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.EndToEndTests;

public sealed class CommercialReadinessTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public async Task Discovery_exposes_four_municipalities_categories_and_reproducible_search_on_mobile()
    {
        await using var context = await fixture.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl);

        var municipalityLinks = page.Locator(".portal-municipalities .portal-municipality");
        await Assertions.Expect(municipalityLinks).ToHaveCountAsync(4);
        foreach (var name in new[] { "Apartadó", "Carepa", "Chigorodó", "Turbo" })
            await Assertions.Expect(municipalityLinks.GetByText(name, new() { Exact = true })).ToBeVisibleAsync();
        foreach (var box in await municipalityLinks.EvaluateAllAsync<double[][]>(
                     "items => items.map(x => { const r=x.getBoundingClientRect(); return [r.left,r.right]; })"))
        {
            Assert.True(box[0] >= 0, $"El municipio empieza fuera del viewport: {box[0]}");
            Assert.True(box[1] <= 390, $"El municipio termina fuera del viewport: {box[1]}");
        }
        await Assertions.Expect(page.Locator(".portal-category-grid .portal-category")).ToHaveCountAsync(3);
        await Assertions.Expect(page.GetByText("Pronto", new() { Exact = true })).ToHaveCountAsync(0);

        await municipalityLinks.GetByText("Apartadó", new() { Exact = true }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/municipios/apartado"));
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Apartadó" })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Link, new() { NameRegex = new Regex("Belleza") }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/categorias/belleza-cuidado-personal") &&
                                           url.Contains("municipio=apartado"));
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Belleza y estética en Apartadó" }))
            .ToBeVisibleAsync();

        await page.GotoAsync(fixture.BaseUrl);
        await page.GetByRole(AriaRole.Link, new() { Name = "Buscar negocios" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/explorar"));
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Buscar" })).ToBeEnabledAsync();
        await page.GetByLabel("Qué buscas").FillAsync("Manicure");
        await page.GetByRole(AriaRole.Button, new() { Name = "Buscar" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/explorar?q=Manicure"));
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Salón Bella Urabá" })).ToBeVisibleAsync();
    }

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
