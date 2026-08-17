using Microsoft.Playwright;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.EndToEndTests;

[Collection(PublicSiteCollection.Name)]
public sealed class CommercialReadinessTests(BrowserFixture fixture)
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public async Task Guided_results_open_with_content_and_survive_a_visit_to_a_business_on_mobile()
    {
        await using var context = await fixture.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await context.NewPageAsync();
        // Los resultados son el tercer paso del recorrido, y llegar por dirección es una de las dos
        // formas legítimas de estar aquí; la otra —tocando— la cubre GuidedDiscoveryJourneyTests.
        await page.GotoAsync($"{fixture.BaseUrl}/?lugar=uraba&busco=belleza-cuidado-personal");

        await Assertions.Expect(page.Locator("[data-testid=live-feed] [data-testid=feed-piece]").First)
            .ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".portal-municipalities, .portal-category-grid")).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByText("Pronto", new() { Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("[data-testid=feed-piece]").First).ToContainTextAsync("Bella");
        // La geometría se mide con la pantalla ya viva. Antes de que el circuito conecte, Blazor
        // sustituye el DOM prerenderizado y un elemento medido a mitad de ese cambio no tiene caja:
        // la aserción de maquetación no diría nada sobre la maquetación. El buscador habilitado es la
        // señal de que esa sustitución terminó.
        await Assertions.Expect(page.Locator("[data-testid=results-search]"))
            .ToBeEnabledAsync(new() { Timeout = 30_000 });
        var firstPiece = await page.Locator("[data-testid=feed-piece]").First.BoundingBoxAsync();
        Assert.NotNull(firstPiece);
        // Sigue empezando arriba, aunque encima haya contexto y búsqueda en vez de una cinta de
        // filtros: el contenido no puede caer bajo el pliegue.
        Assert.True(firstPiece!.Y < 240, $"El contenido empieza demasiado abajo: {firstPiece.Y}");
        Assert.True(firstPiece.Height > 480, $"La primera pieza no domina el viewport: {firstPiece.Height}");

        await page.EvaluateAsync("window.scrollTo(0, 260)");
        await page.Locator(".feed-business-link").First.ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/negocios/", StringComparison.Ordinal));
        var savedScroll = int.Parse(await page.EvaluateAsync<string>("() => sessionStorage.getItem('urabaAhoraScroll')"));
        await page.GoBackAsync();
        var returnReady = false;
        for (var attempt = 0; attempt < 60 && !returnReady; attempt++)
        {
            returnReady = await page.EvaluateAsync<bool>("() => sessionStorage.getItem('urabaAhoraReturn') === null && Boolean(document.querySelector('[data-testid=feed-piece]'))");
            if (!returnReady) await Task.Delay(50);
        }
        var returnDebug = await page.EvaluateAsync<string>("() => JSON.stringify({ url: location.href, returning: sessionStorage.getItem('urabaAhoraReturn'), stored: sessionStorage.getItem('urabaAhoraScroll'), cookie: document.cookie, pieces: document.querySelectorAll('[data-testid=feed-piece]').length, home: Boolean(document.querySelector('.ahora-home')), state: document.querySelector('.quiet-feed,.ahora-error,.guided-boot')?.textContent })");
        var logTail = string.Join(Environment.NewLine, fixture.RecentLog.Split(Environment.NewLine).TakeLast(40));
        Assert.True(returnReady, $"La Home no restauró el feed después de volver desde el negocio: {returnDebug}\n{logTail}");
        // El contexto no se restaura desde almacenamiento efímero: viaja en la dirección, así que
        // volver de una ficha no puede perderlo.
        await Assertions.Expect(page.Locator("[data-testid=place-chip]")).ToContainTextAsync("Todo Urabá");
        await Assertions.Expect(page.Locator("[data-testid=category-chip]")).ToContainTextAsync("Belleza");
        var restoredScroll = await page.EvaluateAsync<int>("window.scrollY");
        Assert.InRange(Math.Abs(restoredScroll - savedScroll), 0, 12);

        var bottomNav = page.Locator(".nav-inferior a");
        await Assertions.Expect(bottomNav).ToHaveCountAsync(3);
        await Assertions.Expect(page.Locator(".nav-inferior")).ToContainTextAsync("Ahora");
        await Assertions.Expect(page.Locator(".nav-inferior")).ToContainTextAsync("Buscar");
        await Assertions.Expect(page.Locator(".nav-inferior")).ToContainTextAsync("Mi actividad");

        await page.GotoAsync(fixture.BaseUrl);
        await page.GetByRole(AriaRole.Link, new() { Name = "Buscar" }).First.ClickAsync();
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
