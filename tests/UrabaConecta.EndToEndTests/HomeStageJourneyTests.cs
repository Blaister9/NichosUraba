using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace UrabaConecta.EndToEndTests;

/// <summary>
/// La escena de Home: la media focal manda, el municipio la transforma sin sacar a nadie del marco
/// y los negocios son escenas de esa misma media. Lo que se comprueba aquí no es el aspecto —eso lo
/// mira una persona— sino que las mecánicas aprobadas existen y siguen funcionando en las dos
/// pantallas reales del piloto y con el movimiento desactivado.
/// </summary>
[Collection(PublicSiteCollection.Name)]
public sealed class HomeStageJourneyTests(BrowserFixture fixture)
{
    private const string DiscoveryUrl = "/?lugar=uraba&busco=belleza-cuidado-personal";

    [Fact]
    public async Task Scene_and_municipality_handoff_work_at_390_by_844()
    {
        await using var context = await Canvas(390, 844);
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + DiscoveryUrl);
        await WaitForStage(page);

        var scenes = page.Locator("[data-stage-scene]");
        await Expect(scenes).ToHaveCountAsync(4);
        await Expect(page.Locator("[data-stage-counter]")).ToHaveTextAsync("1 / 4");
        await Expect(scenes.First).ToHaveClassAsync(new Regex("is-active"));
        Assert.False(await HasHorizontalOverflow(page));

        // La escena empieza sobre el pliegue y domina la pantalla: es la mecánica de media focal.
        var stage = await page.Locator("[data-stage-media]").BoundingBoxAsync();
        Assert.NotNull(stage);
        Assert.InRange(stage!.X, 15.5, 16.5);
        Assert.InRange(stage.Y, 278, 288);
        Assert.InRange(stage.Width, 357.5, 358.5);
        Assert.InRange(stage.Height, 332.5, 334.5);

        // Cambiar de negocio reescribe el contexto accionable, no sólo la foto.
        await page.Locator("[data-stage-next]").ClickAsync();
        await Expect(page.Locator("[data-stage-counter]")).ToHaveTextAsync("2 / 4");
        await Expect(scenes.Nth(1)).ToHaveClassAsync(new Regex("is-active"));
        var action = page.Locator("[data-stage-action]");
        var href = await action.GetAttributeAsync("href");
        Assert.Contains("/citas?serviceId=", href);

        // Entrar a un negocio y volver conserva la escena que se estaba mirando.
        await action.ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/citas?serviceId=", StringComparison.Ordinal));
        await page.GoBackAsync();
        await Expect(page.Locator("[data-stage-counter]")).ToHaveTextAsync("2 / 4");

        // El municipio transforma la escena conservando la categoría, y el foco viaja al titular.
        var apartado = page.Locator("[data-stage-place][href*='lugar=apartado']");
        await apartado.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForURLAsync(url => url.Contains("lugar=apartado", StringComparison.Ordinal)
            && url.Contains("busco=belleza-cuidado-personal", StringComparison.Ordinal));
        await Expect(page.Locator("[data-stage-place][aria-current=location]")).ToHaveTextAsync("Apartadó");
        await Expect(page.Locator("[data-stage-title]")).ToHaveTextAsync("Apartadó, ahora");
        await Expect(page.Locator("[data-stage-stamp]")).ToHaveTextAsync("Apartadó");
        await Expect(page.Locator("[data-stage-counter]")).ToHaveTextAsync("1 / 4");
        Assert.Equal("stage-title", await page.EvaluateAsync<string>("() => document.activeElement?.className"));
    }

    [Fact]
    public async Task Scene_fits_the_360_by_800_canvas_and_answers_the_keyboard()
    {
        await using var context = await Canvas(360, 800);
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + DiscoveryUrl);
        await WaitForStage(page);

        Assert.False(await HasHorizontalOverflow(page));
        var stage = await page.Locator("[data-stage-media]").BoundingBoxAsync();
        Assert.NotNull(stage);
        Assert.InRange(stage!.X, 15.5, 16.5);
        Assert.InRange(stage.X + stage.Width, 343.5, 344.5);
        Assert.InRange(stage.Y, 276, 284);
        Assert.InRange(stage.Height, 291.5, 294.5);

        // La acción de la escena sigue estando sobre el pliegue: mirar sin poder actuar no sirve.
        var action = await page.Locator("[data-stage-action]").BoundingBoxAsync();
        Assert.NotNull(action);
        Assert.True(action!.Y + action.Height < 800, $"La acción quedó bajo el pliegue: {action.Y + action.Height}");

        await page.Locator("[data-stage-media]").FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Expect(page.Locator("[data-stage-counter]")).ToHaveTextAsync("2 / 4");
    }

    [Fact]
    public async Task Reduced_motion_removes_the_transition_but_keeps_the_scene_switchable()
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + DiscoveryUrl);
        await WaitForStage(page);

        var motion = await page.EvaluateAsync<string[]>("""
            () => {
                const image = document.querySelector('.stage-image.is-current');
                const media = document.querySelector('[data-stage-media]');
                return [
                    getComputedStyle(image).transitionDuration,
                    getComputedStyle(media).transitionDuration,
                    getComputedStyle(image).transform
                ];
            }
            """);
        Assert.Equal(["0s", "0s", "none"], motion);

        await page.Locator("[data-stage-next]").ClickAsync();
        await Expect(page.Locator("[data-stage-counter]")).ToHaveTextAsync("2 / 4");
        await Expect(page.Locator("[data-stage-name]")).ToContainTextAsync("Cepillado");
    }

    /// <summary>
    /// La captación acompaña a los resultados en su sitio editorial pero no es un negocio: ni entra
    /// en el contador ni se navega como una escena. El patrocinado sí es una escena y va marcado.
    /// </summary>
    [Fact]
    public async Task Capture_travels_with_the_scenes_without_being_counted_as_one()
    {
        await using var context = await Canvas(390, 844);
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + "/?lugar=carepa&busco=restaurante");
        await WaitForStage(page);

        await Expect(page.Locator("[data-testid=business-capture]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-stage-counter]")).ToHaveTextAsync("1 / 2");
        await Expect(page.Locator("[data-stage-scene]")).ToHaveCountAsync(2);
        Assert.Equal(0, await page.Locator("[data-testid=business-capture][data-stage-scene]").CountAsync());

        // Dentro de la misma secuencia desplazable y antes de la pieza patrocinada.
        var order = await page.EvaluateAsync<string[]>("""
            () => [...document.querySelector('[data-stage-list]').children]
                .map(node => node.dataset.stageScene !== undefined
                    ? `negocio${node.querySelector('.scene-tag') ? '-patrocinado' : ''}`
                    : 'captacion')
            """);
        Assert.Contains("captacion", order);
        Assert.True(Array.IndexOf(order, "captacion") < Array.IndexOf(order, "negocio-patrocinado"),
            $"La captación debe seguir apareciendo antes que la patrocinada: {string.Join(", ", order)}");
    }

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
