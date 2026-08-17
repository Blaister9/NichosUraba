using Microsoft.Playwright;

namespace UrabaConecta.EndToEndTests;

/// <summary>
/// El recorrido guiado en un navegador real: dónde buscas, qué buscas, qué puedes hacer ahora.
///
/// El sembrado de desarrollo reparte una vertical por municipio, que es justo lo que hace falta para
/// comprobar que las categorías salen de los datos y no de una lista escrita a mano: Chigorodó tiene
/// la barbería, Apartadó la belleza, Carepa la comida y Turbo un negocio sin módulos.
/// </summary>
[Collection(PublicSiteCollection.Name)]
public sealed class GuidedDiscoveryJourneyTests(BrowserFixture fixture)
{
    private const string PlaceCookie = "uc_lugar";

    [Fact]
    public async Task First_visit_asks_only_where_and_the_decision_fits_the_first_viewport()
    {
        await using var context = await MobileContext();
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl);

        await Expect(page.Locator("[data-testid=guided-place]")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "¿Dónde estás buscando?" }))
            .ToBeVisibleAsync();

        // Una sola decisión: no hay resultados que interpretar ni categorías compitiendo con ella.
        await Expect(page.Locator("[data-testid=feed-piece]")).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-testid=guided-category]")).ToHaveCountAsync(0);
        await Expect(page.Locator(".portal-municipalities, .portal-category-grid")).ToHaveCountAsync(0);
        await Expect(page.GetByText("Pronto", new() { Exact = true })).ToHaveCountAsync(0);

        // Los cuatro municipios y Todo Urabá están nombrados; sólo son tocables los que tienen
        // negocios publicados. En el sembrado Turbo no los tiene, así que se anuncia en vez de
        // llevar a una pantalla vacía.
        foreach (var name in new[] { "Apartadó", "Carepa", "Chigorodó", "Turbo", "Todo Urabá" })
            await Expect(page.GetByText(name, new() { Exact = true }).First).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid=place-soon]")).ToHaveCountAsync(1);
        await Expect(page.Locator("[data-testid=place-soon]")).ToContainTextAsync("Turbo");

        // Todas las opciones dentro del primer viewport, con la primera muy arriba: es lo que permite
        // saber qué tocar sin leer nada más.
        var options = page.Locator("[data-testid=place-option]");
        await Expect(options).ToHaveCountAsync(4);
        var first = await options.First.BoundingBoxAsync();
        Assert.NotNull(first);
        Assert.True(first!.Y < 260, $"La primera opción empieza demasiado abajo: {first.Y}");
        var last = await options.Last.BoundingBoxAsync();
        Assert.NotNull(last);
        Assert.True(last!.Y + last.Height < 844, $"La última opción cae fuera del viewport: {last.Y + last.Height}");

        // Ubicación: existe como acción secundaria y no se pide nada al cargar.
        await Expect(page.Locator("[data-testid=use-location]")).ToBeVisibleAsync();
        // Y todavía no se ha guardado ninguna preferencia: nadie ha elegido.
        Assert.DoesNotContain(await context.CookiesAsync(), x => x.Name == PlaceCookie);
    }

    [Fact]
    public async Task Choosing_a_place_shows_only_the_categories_that_place_really_has()
    {
        await using var context = await MobileContext();
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl);

        await page.Locator("[data-testid=place-option]").Filter(new() { HasTextString = "Chigorodó" }).ClickAsync();

        await Expect(page.Locator("[data-testid=guided-category]")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "¿Qué estás buscando?" })).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid=place-chip]")).ToContainTextAsync("Chigorodó");
        // La barbería del sembrado está en Chigorodó y nada más: ofrecer "Restaurante" aquí sería
        // inventar una vertical que no existe en este municipio.
        await Expect(page.Locator("[data-testid=category-option]")).ToHaveCountAsync(1);
        await Expect(page.Locator("[data-testid=category-option]")).ToContainTextAsync("Barbería");
    }

    [Fact]
    public async Task Choosing_a_category_shows_only_compatible_results_with_state_and_action()
    {
        await using var context = await MobileContext();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{fixture.BaseUrl}/?lugar=chigorodo");

        await page.Locator("[data-testid=category-option]").First.ClickAsync();

        await Expect(page.Locator("[data-testid=guided-results]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid=place-chip]")).ToContainTextAsync("Chigorodó");
        await Expect(page.Locator("[data-testid=category-chip]")).ToContainTextAsync("Barbería");
        await Expect(page.Locator("[data-testid=feed-piece]").First).ToBeVisibleAsync();
        // Estado y acción, que es el diferencial que el recorrido no debía perder.
        await Expect(page.Locator("[data-testid=feed-piece]").First).ToContainTextAsync("Barbería El Corte");
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Tomar turno" }).First).ToBeVisibleAsync();

        // Nada de otra categoría se cuela: el negocio de belleza y el de comida están en el mismo feed.
        var results = page.Locator("[data-testid=live-feed]");
        await Expect(results).Not.ToContainTextAsync("Salón Bella Urabá");
        await Expect(results).Not.ToContainTextAsync("Restaurante Sazón Local");
    }

    [Fact]
    public async Task Changing_place_keeps_the_intent_and_changes_the_results()
    {
        await using var context = await MobileContext();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{fixture.BaseUrl}/?lugar=chigorodo&busco=barberia");
        await Expect(page.Locator("[data-testid=feed-piece]").First).ToContainTextAsync("Barbería El Corte");

        // Cambiar de municipio no obliga a repetir qué se buscaba.
        await page.Locator("[data-testid=place-chip]").ClickAsync();
        await Expect(page.Locator("[data-testid=guided-place]")).ToBeVisibleAsync();
        await page.Locator("[data-testid=place-option]").Filter(new() { HasTextString = "Apartadó" }).ClickAsync();

        // En Apartadó no hay barberías: vacío que explica y ofrece las dos salidas que existen.
        await Expect(page.Locator("[data-testid=guided-empty]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid=guided-empty]")).ToContainTextAsync("Todavía no tenemos");
        await Expect(page.Locator("[data-testid=feed-piece]")).ToHaveCountAsync(0);

        await page.GetByRole(AriaRole.Link, new() { Name = "Ver en todo Urabá" }).ClickAsync();
        await Expect(page.Locator("[data-testid=feed-piece]").First).ToContainTextAsync("Barbería El Corte");
        await Expect(page.Locator("[data-testid=place-chip]")).ToContainTextAsync("Todo Urabá");
    }

    [Fact]
    public async Task A_remembered_place_skips_the_first_step_without_a_flash()
    {
        await using var context = await MobileContext();
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl);
        await page.Locator("[data-testid=place-option]").Filter(new() { HasTextString = "Carepa" }).ClickAsync();
        await Expect(page.Locator("[data-testid=guided-category]")).ToBeVisibleAsync();

        // La elección quedó guardada en una sola preferencia, y en la cookie: es lo que el servidor
        // puede leer mientras compone el HTML.
        var cookies = await context.CookiesAsync();
        Assert.Contains(cookies, x => x.Name == PlaceCookie && x.Value == "carepa");

        // Visita siguiente: la dirección desnuda no vuelve a preguntar el municipio.
        await page.GotoAsync(fixture.BaseUrl);
        await Expect(page.Locator("[data-testid=guided-category]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid=place-chip]")).ToContainTextAsync("Carepa");

        // Y no hay salto: con JavaScript apagado no existe circuito que pueda corregir la pantalla,
        // así que si el paso 2 aparece aquí es porque lo entregó el servidor en la primera pintura.
        // Éste es el ensayo que falla si la preferencia vuelve a vivir sólo en el navegador.
        await using var noScript = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 390, Height = 844 }, JavaScriptEnabled = false
        });
        await noScript.AddCookiesAsync([new Cookie
        {
            Name = PlaceCookie, Value = "carepa", Url = fixture.BaseUrl
        }]);
        var served = await noScript.NewPageAsync();
        await served.GotoAsync(fixture.BaseUrl);
        await Expect(served.Locator("[data-testid=guided-category]")).ToBeVisibleAsync();
        await Expect(served.Locator("[data-testid=guided-place]")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task The_url_wins_over_the_remembered_place_and_can_be_shared()
    {
        await using var context = await MobileContext();
        await context.AddCookiesAsync([new Cookie { Name = PlaceCookie, Value = "carepa", Url = fixture.BaseUrl }]);
        var page = await context.NewPageAsync();

        // Precedencia: dirección sobre cookie. Un enlace compartido enseña lo que dice, no lo que
        // esta persona eligió otro día.
        await page.GotoAsync($"{fixture.BaseUrl}/?lugar=turbo");
        await Expect(page.Locator("[data-testid=place-chip]")).ToContainTextAsync("Turbo");
    }

    [Fact]
    public async Task The_place_can_always_be_changed_again_and_back_undoes_one_decision()
    {
        await using var context = await MobileContext();
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl);
        await page.Locator("[data-testid=place-option]").Filter(new() { HasTextString = "Carepa" }).ClickAsync();
        await page.Locator("[data-testid=category-option]").First.ClickAsync();
        await Expect(page.Locator("[data-testid=guided-results]")).ToBeVisibleAsync();

        // Atrás deshace exactamente una decisión, porque cada avance es una dirección.
        await page.GoBackAsync();
        await Expect(page.Locator("[data-testid=guided-category]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid=place-chip]")).ToContainTextAsync("Carepa");

        // El selector de lugar es transitorio: se abre desde el contexto y atrás vuelve a donde
        // estabas, sin dejar una entrada de historial que no lleve a ninguna parte.
        await page.Locator("[data-testid=place-chip]").ClickAsync();
        await Expect(page.Locator("[data-testid=guided-place]")).ToBeVisibleAsync();
        await page.GoBackAsync();
        await Expect(page.Locator("[data-testid=guided-category]")).ToBeVisibleAsync();

        // Y el municipio se puede cambiar tantas veces como haga falta.
        await page.Locator("[data-testid=place-chip]").ClickAsync();
        await page.Locator("[data-testid=place-option]").Filter(new() { HasTextString = "Chigorodó" }).ClickAsync();
        await Expect(page.Locator("[data-testid=place-chip]")).ToContainTextAsync("Chigorodó");
        await Expect(page.Locator("[data-testid=category-option]")).ToContainTextAsync("Barbería");
    }

    [Fact]
    public async Task Search_inside_the_results_filters_and_offers_the_full_directory()
    {
        await using var context = await MobileContext();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{fixture.BaseUrl}/?lugar=uraba&busco=belleza-cuidado-personal");

        var search = page.Locator("[data-testid=results-search]");
        await Expect(search).ToBeEnabledAsync(new() { Timeout = 30_000 });
        // Sin escribir nada ya hay resultados: la búsqueda acelera, no es la puerta de entrada.
        await Expect(page.Locator("[data-testid=feed-piece]").First).ToBeVisibleAsync();

        await search.FillAsync("Manicure");
        await Expect(page.Locator("[data-testid=feed-piece]").First).ToContainTextAsync("Manicure");

        // Lo que no está aquí no se inventa: se ofrece el directorio completo, que ya existe.
        await search.FillAsync("bicicleta");
        await Expect(page.Locator("[data-testid=guided-empty]")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Buscar en todo Urabá" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/explorar?q=bicicleta", StringComparison.Ordinal));
    }

    private async Task<IBrowserContext> MobileContext() => await fixture.Browser.NewContextAsync(new()
    { ViewportSize = new() { Width = 390, Height = 844 } });

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
