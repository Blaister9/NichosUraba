using Microsoft.Playwright;

namespace UrabaConecta.EndToEndTests;

/// <summary>
/// La escena de Home y la ficha del negocio son el mismo objeto en dos superficies, así que abrir un
/// negocio es una transformación del contenedor y no un corte con adorno previo.
///
/// Lo que se comprueba aquí no es el aspecto de la transición —eso lo mira una persona— sino el
/// contrato que la hace posible y que un cambio distraído rompería sin que nada se queje: que las dos
/// pantallas comparten la identidad del negocio, que la navegación ya no se retiene, que cuando no
/// hay nada que continuar el corte es limpio en vez de un fantasma desvaneciéndose, y que ningún
/// documento se queda con un nombre de transición puesto.
/// </summary>
[Collection(PublicSiteCollection.Name)]
public sealed class SharedSceneJourneyTests(BrowserFixture fixture)
{
    private const string DiscoveryUrl = "/?lugar=uraba&busco=belleza-cuidado-personal";
    private const string TwoScenesUrl = "/?lugar=carepa&busco=restaurante";

    [Fact]
    public async Task The_scene_opens_the_business_and_both_surfaces_share_its_identity()
    {
        await using var context = await Canvas(390, 844, touch: true);
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + DiscoveryUrl);
        await WaitForStage(page);

        var slug = await ActiveSceneSlug(page);
        Assert.False(string.IsNullOrWhiteSpace(slug), "La escena activa no dice de qué negocio habla");
        await Expect(page.Locator("[data-testid=feed-piece]")).ToHaveAttributeAsync("data-escena-vt", slug);
        // La fotografía abre la ficha; el botón conserva su acción operativa, que se comprueba aparte.
        await Expect(page.Locator("[data-stage-open]")).ToHaveAttributeAsync("href", $"/negocios/{slug}");

        await WatchOldMechanism(page);
        await page.Locator("[data-stage-open]").TapAsync();
        await page.WaitForURLAsync($"**/negocios/{slug}");

        var llegada = await Arrival(page);
        Assert.True(llegada.StartsWith("continua", StringComparison.Ordinal), $"La escena no continuó: {llegada}");
        await Expect(page.Locator(".ficha-hero")).ToHaveAttributeAsync("data-escena-vt", slug);
        Assert.Null(await page.EvaluateAsync<string?>("() => sessionStorage.getItem('e2e-apertura')"));
        await NamesStayUnique(page);
    }

    /// <summary>
    /// Elegir otro negocio antes de abrir tiene que mover la identidad con la escena. Si se quedara
    /// pegada a la primera, abrir la tercera escena intentaría continuar con la ficha equivocada.
    /// </summary>
    [Fact]
    public async Task Choosing_another_scene_moves_the_identity_before_opening()
    {
        await using var context = await Canvas(390, 844);
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + DiscoveryUrl);
        await WaitForStage(page);

        // Adelante y atrás deprisa: la identidad es la de la escena que queda, no la de las que pasaron.
        await page.Locator("[data-stage-next]").ClickAsync();
        await page.Locator("[data-stage-next]").ClickAsync();
        await page.Locator("[data-stage-prev]").ClickAsync();
        await Expect(page.Locator("[data-stage-counter]")).ToHaveTextAsync("2 / 4");

        var slug = await ActiveSceneSlug(page);
        await Expect(page.Locator("[data-testid=feed-piece]")).ToHaveAttributeAsync("data-escena-vt", slug);
        await Expect(page.Locator("[data-stage-open]")).ToHaveAttributeAsync("href", $"/negocios/{slug}");

        await page.Locator("[data-stage-open]").ClickAsync();
        await page.WaitForURLAsync($"**/negocios/{slug}");
        var llegada = await Arrival(page);
        Assert.True(llegada.StartsWith("continua", StringComparison.Ordinal), $"La escena no continuó: {llegada}");
        await Expect(page.Locator(".ficha-hero")).ToHaveAttributeAsync("data-escena-vt", slug);
    }

    /// <summary>
    /// La acción de la escena lleva a la fila, a los horarios o a la carta, y esas pantallas no
    /// enseñan el encabezado del negocio: no hay contenedor que continuar. Eso tiene que ser un corte
    /// limpio y no media transición —la instantánea de Home desvaneciéndose sola encima— que es
    /// justamente lo que ocurre si nadie descarta la transición al llegar.
    /// </summary>
    [Fact]
    public async Task An_operational_destination_cuts_clean_instead_of_faking_continuity()
    {
        await using var context = await Canvas(390, 844);
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + DiscoveryUrl);
        await WaitForStage(page);

        var action = page.Locator("[data-stage-action]");
        var destination = await action.GetAttributeAsync("href");
        Assert.NotNull(destination);
        await action.ClickAsync();
        await page.WaitForURLAsync(url => url.Contains(destination!, StringComparison.Ordinal));

        var llegada = await Arrival(page);
        Assert.True(llegada.StartsWith("corta", StringComparison.Ordinal), $"La entrada no fue limpia: {llegada}");
        Assert.Equal(0, await page.Locator("[data-escena-vt]").CountAsync());
        await NamesStayUnique(page);
    }

    /// <summary>Sin movimiento se llega igual, y se llega sin transformación ni espera inventada.</summary>
    [Fact]
    public async Task Reduced_motion_opens_the_profile_without_transforming_anything()
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + DiscoveryUrl);
        await WaitForStage(page);

        var slug = await ActiveSceneSlug(page);
        await WatchOldMechanism(page);
        await page.Locator("[data-stage-open]").ClickAsync();
        await page.WaitForURLAsync($"**/negocios/{slug}");

        var llegada = await Arrival(page);
        Assert.True(llegada.StartsWith("corta", StringComparison.Ordinal), $"La entrada no fue limpia: {llegada}");
        Assert.Null(await page.EvaluateAsync<string?>("() => sessionStorage.getItem('e2e-apertura')"));
        await NamesStayUnique(page);
    }

    /// <summary>
    /// En escritorio la composición es otra —el bloque accionable va montado sobre la fotografía, con
    /// su propio desenfoque— y la escena tiene que seguir siendo un único contenedor con un único
    /// nombre. Se abre con el teclado, que es el otro camino real hasta la ficha.
    /// </summary>
    [Fact]
    public async Task The_desktop_scene_opens_from_the_keyboard_with_a_single_identity()
    {
        await using var context = await Canvas(1440, 1000);
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + DiscoveryUrl);
        await WaitForStage(page);

        Assert.Equal(1, await page.Locator("[data-escena-vt]").CountAsync());
        await NamesStayUnique(page);

        var slug = await ActiveSceneSlug(page);
        await page.Locator("[data-stage-open]").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForURLAsync($"**/negocios/{slug}");

        var llegada = await Arrival(page);
        Assert.True(llegada.StartsWith("continua", StringComparison.Ordinal), $"La escena no continuó: {llegada}");
        Assert.Equal(1, await page.Locator("[data-escena-vt]").CountAsync());
        await NamesStayUnique(page);
    }

    /// <summary>
    /// Volver conserva la escena que se estaba mirando y no deja nada a medias: ni un nombre vivo, ni
    /// una escena que ya no responde. Dos toques seguidos sobre la fotografía son una navegación, no
    /// dos pantallas superpuestas.
    /// </summary>
    [Fact]
    public async Task Back_keeps_the_scene_and_leaves_no_corrupt_state()
    {
        await using var context = await Canvas(360, 800);
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + TwoScenesUrl);
        await WaitForStage(page);

        await page.Locator("[data-stage-next]").ClickAsync();
        await Expect(page.Locator("[data-stage-counter]")).ToHaveTextAsync("2 / 2");
        var slug = await ActiveSceneSlug(page);

        await page.Locator("[data-stage-open]").DblClickAsync();
        await page.WaitForURLAsync($"**/negocios/{slug}");
        await NamesStayUnique(page);

        await page.GoBackAsync();
        await WaitForStage(page);
        await Expect(page.Locator("[data-stage-counter]")).ToHaveTextAsync("2 / 2");
        Assert.Equal(1, await page.Locator("[data-escena-vt]").CountAsync());
        await NamesStayUnique(page);

        // La escena sigue viva después de volver: la continuidad no puede costar la interacción. Se
        // comprueba por teclado y no con el ratón porque un clic exige que el elemento esté quieto, y
        // eso depende de que el navegador esté dibujando; lo que se prueba aquí es la escena.
        await page.Locator("[data-stage-media]").FocusAsync();
        await page.Keyboard.PressAsync("ArrowLeft");
        await Expect(page.Locator("[data-stage-counter]")).ToHaveTextAsync("1 / 2");
    }

    private async Task<IBrowserContext> Canvas(int width, int height, bool touch = false) =>
        await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = height },
            HasTouch = touch
        });

    /// <summary>
    /// Cómo llegó esta pantalla —continua si el contenedor viajó, corta si no había nada que
    /// continuar— y por qué. Sin el porqué, un fallo aquí es indistinguible de un navegador que no
    /// sabe animar entre documentos, y son dos problemas muy distintos.
    /// </summary>
    private static async Task<string> Arrival(IPage page)
    {
        // La marca se escribe mientras se lee el <head>, así que ya está cuando la pantalla
        // responde. La espera corta es sólo para no confundir una carrera con un fallo real.
        try
        {
            await page.WaitForFunctionAsync("() => document.documentElement.dataset.escena !== undefined",
                null, new() { Timeout = 5_000 });
        }
        catch (PlaywrightException) { /* que lo cuente el diagnóstico, no un tiempo agotado */ }

        return await page.EvaluateAsync<string>("""
            () => [
                document.documentElement.dataset.escena,
                'pagereveal=' + ('onpagereveal' in window),
                'reduce=' + matchMedia('(prefers-reduced-motion: reduce)').matches,
                'ahorro=' + Boolean(navigator.connection?.saveData),
                'venia=' + sessionStorage.getItem('uc_escena_vt'),
                'aqui=' + (document.querySelector('[data-escena-vt]')?.dataset.escenaVt ?? ''),
                'visible=' + document.visibilityState
            ].join(' ')
            """);
    }

    private static async Task<string> ActiveSceneSlug(IPage page) =>
        await page.Locator("[data-stage-scene].is-active").GetAttributeAsync("data-scene-vt") ?? "";

    /// <summary>
    /// Deja constancia si el mecanismo antiguo —escalar la escena y retener la navegación 180 ms—
    /// vuelve a encenderse donde el navegador ya sabe animar entre documentos. Se guarda en
    /// sessionStorage porque el documento que lo encendería es justo el que desaparece al navegar.
    /// </summary>
    private static async Task WatchOldMechanism(IPage page) =>
        await page.EvaluateAsync("""
            () => {
                sessionStorage.removeItem('e2e-apertura');
                const media = document.querySelector('[data-stage-media]');
                new MutationObserver(() => {
                    if (media.classList.contains('is-opening')) sessionStorage.setItem('e2e-apertura', '1');
                }).observe(media, { attributes: true, attributeFilter: ['class'] });
            }
            """);

    /// <summary>
    /// El nombre de la transición existe donde hay algo que continuar y en un solo nodo: dos nodos con
    /// el mismo nombre invalidan la transición entera, y uno vivo donde no hay nada que continuar deja
    /// un contexto de apilamiento que nadie pidió. Se mira el estilo calculado y no el atributo, que es
    /// donde de verdad se decide.
    /// </summary>
    private static async Task NamesStayUnique(IPage page)
    {
        var estado = await page.EvaluateAsync<string>("""
            () => {
                // El navegador nombra la raíz por su cuenta —así es como se anima el fondo—, y eso
                // no lo pone este proyecto ni cuenta aquí.
                const nombrados = [...document.querySelectorAll('*')]
                    .filter(nodo => !['none', 'root'].includes(getComputedStyle(nodo).viewTransitionName));
                return nombrados.length + ':' + (document.documentElement.dataset.escena ?? '');
            }
            """);
        Assert.True(estado is "1:continua" or "0:corta", $"Nombres vivos y marca del documento: {estado}");
    }

    private static async Task WaitForStage(IPage page) =>
        await page.Locator("[data-stage-media][data-stage-ready=true]").WaitForAsync(new()
        {
            State = WaitForSelectorState.Attached,
            Timeout = 30_000
        });

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
