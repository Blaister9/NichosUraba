using Microsoft.Playwright;

namespace UrabaConecta.EndToEndTests;

/// <summary>
/// Home no es un carrusel con una página debajo: es una secuencia. La cámara —media focal y bloque
/// accionable— se queda fija mientras los capítulos de cada negocio pasan por delante, y el recorrido
/// no sólo revela cosas: recompone la escena.
///
/// Lo que se comprueba aquí es que la secuencia existe y que es UN estado: que el capítulo que manda
/// y la fase en la que va gobiernan a la vez el recorte de la media, la identidad que viaja a la
/// ficha, la oferta que entra y el contador. Si esto pasa a ser seis mecanismos sueltos, alguna de
/// estas afirmaciones deja de ser cierta.
/// </summary>
[Collection(PublicSiteCollection.Name)]
public sealed class SceneChoreographyJourneyTests(BrowserFixture fixture)
{
    private const string DiscoveryUrl = "/?lugar=uraba&busco=belleza-cuidado-personal";

    /// <summary>
    /// Un capítulo por escena, y cada uno con lo que el feed sabe de verdad de ese negocio: servicios
    /// con su precio y su duración, la fila con sus cifras o el producto del día. Nada inventado.
    /// </summary>
    [Fact]
    public async Task The_vertical_sequence_carries_the_real_offer_of_each_business()
    {
        await using var context = await Canvas(390, 844);
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + DiscoveryUrl);
        await WaitForStage(page);

        await Expect(page.Locator(".stage-step")).ToHaveAttributeAsync("data-capitulos", "vivo");
        var escenas = await page.Locator("[data-stage-scene]").CountAsync();
        Assert.Equal(escenas, await page.Locator("[data-stage-chapter]").CountAsync());

        var oferta = await page.EvaluateAsync<string[]>("""
            () => [...document.querySelectorAll('[data-stage-chapter]')].map(capitulo => {
                const piezas = [...capitulo.querySelectorAll('.chapter-offer')];
                const foco = capitulo.querySelector('.chapter-offer.is-focus .offer-name')?.textContent ?? '';
                const nombre = capitulo.querySelector('.chapter-name').textContent.trim();
                const nombres = piezas.map(x => x.querySelector('.offer-name').textContent.trim());
                return [piezas.length, foco.trim(), piezas[0]?.querySelector('.offer-meta')?.textContent ?? '',
                    nombre, nombres.includes(nombre), capitulo.dataset.stageBusiness].join('|');
            })
            """);
        Assert.All(oferta, linea =>
        {
            var partes = linea.Split('|');
            Assert.True(int.Parse(partes[0]) > 0, $"Un capítulo sin nada que enseñar: {linea}");
            Assert.False(string.IsNullOrWhiteSpace(partes[2]), $"Una pieza sin dato real: {linea}");
            Assert.False(string.IsNullOrWhiteSpace(partes[5]), $"Un capítulo sin negocio: {linea}");
            if (bool.Parse(partes[4]))
                Assert.Equal(partes[3], partes[1]);
        });
        // La escena habla de una pieza concreta, así que el capítulo la señala en vez de repetir la
        // lista entera del negocio.
        Assert.Contains(oferta, linea => linea.Split('|')[1].Length > 0);
    }

    /// <summary>
    /// El recorrido cambia el capítulo y revela su oferta sin deformar la focal aprobada. La cámara
    /// se queda donde se la ve y el relevo de imagen ocurre dentro del mismo marco.
    /// </summary>
    [Fact]
    public async Task Scrolling_advances_the_scene_without_deforming_the_focal_frame()
    {
        await using var context = await Canvas(390, 844);
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + DiscoveryUrl);
        await WaitForStage(page);
        Assert.Equal("a", await Phase(page));

        var recorteAbierto = await Clip(page);
        await GoInto(page, 0, 0.30);
        await Expect(page.Locator(".stage-step")).ToHaveAttributeAsync("data-fase", "b");
        await Expect(page.Locator(".stage-step")).ToHaveAttributeAsync("data-active-phase", "b");
        await FinishAnimations(page);
        var recorteMedio = await Clip(page);
        var progresoMedio = await Progress(page);

        await GoInto(page, 0, 0.58);
        await Expect(page.Locator(".stage-step")).ToHaveAttributeAsync("data-fase", "c");
        await FinishAnimations(page);
        var recorteCerrado = await Clip(page);
        var progresoCerrado = await Progress(page);

        Assert.Equal(recorteAbierto, recorteMedio);
        Assert.Equal(recorteMedio, recorteCerrado);
        Assert.Equal("none", recorteCerrado);
        Assert.True(progresoMedio is > 0.20 and < 0.45, $"Progreso medio incoherente: {progresoMedio}");
        Assert.True(progresoCerrado is > 0.52 and < 0.68, $"Progreso final incoherente: {progresoCerrado}");

        // La cámara sigue fija bajo la cabecera mientras el capítulo pasa, y la oferta ya está dentro.
        var camara = await page.Locator("[data-stage-camera]").BoundingBoxAsync();
        Assert.NotNull(camara);
        Assert.True(camara!.Y < 120, $"La cámara dejó de sostener la escena: {camara.Y}");
        var visible = await page.EvaluateAsync<double>("""
            () => Number(getComputedStyle(
                document.querySelector('[data-stage-chapter="0"] .chapter-offer')).opacity)
            """);
        Assert.True(visible > 0.9, $"La oferta del capítulo activo no entró: {visible}");
    }

    /// <summary>
    /// El negocio siguiente no es la tarjeta siguiente: es el capítulo siguiente, y llega a la misma
    /// cámara. Con él viaja la identidad que la ficha tiene que repetir.
    /// </summary>
    [Fact]
    public async Task The_next_business_arrives_in_the_same_camera()
    {
        await using var context = await Canvas(390, 844);
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + DiscoveryUrl);
        await WaitForStage(page);
        await Expect(page.Locator("[data-stage-counter]")).ToHaveTextAsync("1 / 4");

        await GoInto(page, 1, 0.25);
        await Expect(page.Locator("[data-stage-chapter='1']")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-active"));
        await Expect(page.Locator("[data-stage-counter]")).ToHaveTextAsync("2 / 4");
        await Expect(page.Locator(".stage-step")).ToHaveAttributeAsync("data-active-chapter", "1");

        var coinciden = await page.EvaluateAsync<string>("""
            () => {
                const capitulo = document.querySelector('[data-stage-chapter="1"]');
                const escena = document.querySelectorAll('[data-stage-scene]')[1];
                return [
                    document.querySelector('[data-stage-name]').textContent.trim()
                        === capitulo.querySelector('.chapter-name').textContent.trim(),
                    document.querySelector('[data-escena-vt]').dataset.escenaVt === escena.dataset.sceneVt,
                    document.querySelector('[data-stage-action]').getAttribute('href') === escena.dataset.sceneUrl
                ].join(',');
            }
            """);
        Assert.Equal("true,true,true", coinciden);
        Assert.Equal(await page.Locator("[data-stage-scene].is-active").GetAttributeAsync("data-scene-vt"),
            await page.Locator(".stage-step").GetAttributeAsync("data-active-business"));
    }

    /// <summary>La secuencia desemboca donde la dejó J-MOTION-03A: el contenedor viaja a la ficha.</summary>
    [Fact]
    public async Task The_sequence_still_ends_in_the_shared_bounds_transition()
    {
        await using var context = await Canvas(390, 844);
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + DiscoveryUrl);
        await WaitForStage(page);

        await GoInto(page, 1, 0.25);
        await Expect(page.Locator("[data-stage-counter]")).ToHaveTextAsync("2 / 4");
        var slug = await page.Locator("[data-escena-vt]").GetAttributeAsync("data-escena-vt");

        await page.Locator("[data-stage-open]").ClickAsync();
        await page.WaitForURLAsync($"**/negocios/{slug}");
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-escena", "continua");
        await Expect(page.Locator(".ficha-hero")).ToHaveAttributeAsync("data-escena-vt", slug!);
    }

    /// <summary>Sin movimiento la secuencia sigue estando: lo que desaparece es el viaje, no el contenido.</summary>
    [Fact]
    public async Task Reduced_motion_keeps_the_sequence_and_removes_the_travel()
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + DiscoveryUrl);
        await WaitForStage(page);

        var quieto = await page.EvaluateAsync<string[]>("""
            () => {
                const oferta = document.querySelector('[data-stage-chapter="0"] .chapter-offer');
                const contexto = document.querySelector('[data-stage-context]');
                return [
                    getComputedStyle(oferta).opacity,
                    getComputedStyle(oferta).transitionDuration,
                    getComputedStyle(contexto).transitionDuration,
                    getComputedStyle(contexto).transform
                ];
            }
            """);
        Assert.Equal(["1", "0s", "0s", "none"], quieto);

        // Y la secuencia sigue avanzando: el capítulo siguiente sigue siendo alcanzable.
        await GoInto(page, 1, 0.25);
        await Expect(page.Locator("[data-stage-counter]")).ToHaveTextAsync("2 / 4");
    }

    /// <summary>
    /// En escritorio la escala es otra y el bloque accionable va montado sobre la fotografía, pero el
    /// recorrido es el mismo: cámara sostenida y capítulos pasando.
    /// </summary>
    [Fact]
    public async Task The_desktop_sequence_pins_the_camera_and_fits()
    {
        await using var context = await Canvas(1440, 1000);
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + DiscoveryUrl);
        await WaitForStage(page);
        Assert.False(await HasHorizontalOverflow(page));
        Assert.Equal("sticky", await Position(page));

        await GoInto(page, 1, 0.30);
        await Expect(page.Locator(".stage-step")).ToHaveAttributeAsync("data-fase", "b");
        await Expect(page.Locator("[data-stage-counter]")).ToHaveTextAsync("2 / 4");
        Assert.False(await HasHorizontalOverflow(page));

        var camara = await page.Locator("[data-stage-camera]").BoundingBoxAsync();
        Assert.NotNull(camara);
        Assert.True(camara!.Y < 160, $"La cámara no se quedó sosteniendo la escena: {camara.Y}");

        // Al acabar el track la cámara se libera estructuralmente: no queda fijada sobre herramientas
        // ni pie, y no hay ningún scrollTo continuo intentando retenerla.
        await page.EvaluateAsync("() => window.scrollTo(0, document.documentElement.scrollHeight)");
        await OneFrame(page);
        var released = await page.Locator("[data-stage-camera]").BoundingBoxAsync();
        Assert.NotNull(released);
        Assert.True(released!.Y < camara.Y - 80, $"La cámara no se liberó: {camara.Y} → {released.Y}");
    }

    /// <summary>En teléfono la cámara se compacta, pero la acción y el capítulo siguen siendo útiles.</summary>
    [Theory]
    [InlineData(390, 844)]
    [InlineData(360, 800)]
    public async Task Mobile_canvases_keep_the_action_and_release_without_overflow(int width, int height)
    {
        await using var context = await Canvas(width, height);
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + DiscoveryUrl);
        await WaitForStage(page);
        Assert.False(await HasHorizontalOverflow(page));

        await GoInto(page, 1, 0.58);
        await Expect(page.Locator(".stage-step")).ToHaveAttributeAsync("data-active-chapter", "1");
        await Expect(page.Locator(".stage-step")).ToHaveAttributeAsync("data-active-phase", "c");
        Assert.False(await HasHorizontalOverflow(page));

        var action = await page.Locator("[data-stage-chapter='1'] [data-stage-chapter-action]").BoundingBoxAsync();
        Assert.NotNull(action);
        Assert.True(action!.Y + action.Height > 0 && action.Y < height,
            $"La acción dejó de ser utilizable en {width}×{height}: y={action.Y}, h={action.Height}");

        // El laboratorio aprobado mantiene escala 1: la lente sólo admite el desplazamiento focal
        // sutil del gesto/scroll y la fotografía se encarga del fundido.
        var compactacion = await page.EvaluateAsync<double[]>("""
            () => {
                const media = document.querySelector('[data-stage-media]').getBoundingClientRect();
                const lente = document.querySelector('[data-stage-lens]');
                return [media.height, parseFloat(getComputedStyle(lente).transform.split(',')[0].replace('matrix(', ''))];
            }
            """);
        var altoAprobado = Math.Min(height * .5, 416);
        Assert.InRange(compactacion[0], altoAprobado - 1, altoAprobado + 1);
        Assert.InRange(compactacion[1], .999, 1.001);
    }

    /// <summary>
    /// Lleva el recorrido hasta un punto concreto del capítulo: el avance es lo que hay recorrido de
    /// su propio alto, así que se calcula con la misma regla que el guion, no con píxeles a ojo.
    /// </summary>
    private static async Task GoInto(IPage page, int chapter, double progress)
    {
        // La primera pasada lleva la cámara a sticky; la segunda usa ya esa geometría estable. No
        // se finge la escena: ambas son operaciones normales de scroll y el guion sólo las observa.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await page.EvaluateAsync("""
                datos => {
                    const paso = document.querySelector('.stage-step');
                    const capitulo = paso.querySelector(`[data-stage-chapter="${datos.chapter}"]`);
                    const caja = capitulo.getBoundingClientRect();
                    const linea = window.innerHeight * .24;
                    window.scrollTo(0, Math.round(window.scrollY + caja.top - linea
                        + caja.height * datos.progress));
                }
                """, new { chapter, progress });
            await OneFrame(page);
        }
    }

    private static async Task<string> Phase(IPage page) =>
        await page.Locator(".stage-step").GetAttributeAsync("data-fase") ?? "";

    private static async Task<string> Clip(IPage page) =>
        await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('[data-stage-media]')).clipPath");

    private static async Task<double> Progress(IPage page) =>
        double.Parse(await page.Locator(".stage-step").GetAttributeAsync("data-progress") ?? "0",
            System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<string> Position(IPage page) =>
        await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('[data-stage-camera]')).position");

    private static Task OneFrame(IPage page) => page.EvaluateAsync("() => new Promise(requestAnimationFrame)");

    /// <summary>Termina las transiciones del compositor para observar su estado final sin depender
    /// de la velocidad de la máquina que ejecuta Playwright.</summary>
    private static Task FinishAnimations(IPage page) => page.EvaluateAsync("""
        () => {
            document.getAnimations().forEach(animation => animation.finish());
            return new Promise(requestAnimationFrame);
        }
        """);

    private async Task<IBrowserContext> Canvas(int width, int height) =>
        await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = height }
        });

    private static async Task<bool> HasHorizontalOverflow(IPage page) =>
        await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > window.innerWidth + 1");

    private static async Task WaitForStage(IPage page) =>
        await page.Locator("[data-stage-media][data-stage-ready=true]").WaitForAsync(new()
        {
            State = WaitForSelectorState.Attached,
            Timeout = 30_000
        });

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
