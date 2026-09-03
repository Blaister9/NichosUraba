using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;
using UrabaConecta.Contracts;

namespace UrabaConecta.EndToEndTests;

/// <summary>Real UI → persisted PickupOrder; optional existing DEV showcase, never synthetic cards.</summary>
[Collection(PublicSiteCollection.Name)]
public sealed class ProductActionJourneyTests(BrowserFixture fixture, Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string BaseUrl => Environment.GetEnvironmentVariable("URABACONECTA_PRODUCT_ACTION_URL") ?? fixture.BaseUrl;
    private string Slug => Environment.GetEnvironmentVariable("URABACONECTA_PRODUCT_ACTION_SLUG") ?? "restaurante-sazon-local";
    private static string Artifacts => Path.Combine(FindRoot(), "artifacts", "j-motion-06");

    [Theory]
    [InlineData(1440, 1000, false, false)]
    [InlineData(1920, 1080, false, false)]
    [InlineData(390, 844, false, false)]
    [InlineData(360, 800, false, false)]
    [InlineData(390, 844, true, false)]
    [InlineData(360, 800, false, true)]
    public async Task Product_identity_survives_selection_quantity_composer_and_confirmation(int width, int height, bool reduce, bool saveData)
    {
        Assert.True(new Uri(BaseUrl).IsLoopback || new Uri(BaseUrl).Host == "dev.urabaconecta.com", "Only local or explicitly named DEV may receive test orders.");
        using var http = new HttpClient { BaseAddress = new(BaseUrl) };
        var menu = (await http.GetFromJsonAsync<PickupMenuDto>($"/api/v1/public/businesses/{Slug}/menu", Json))!;
        var products = menu.Products.Where(p => p.IsAvailable).Take(3).ToArray();
        Assert.NotEmpty(products);
        var product = products[0];
        await using var context = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = reduce ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
            ColorScheme = ColorScheme.Light, HasTouch = width < 760
        });
        if (saveData)
            await context.AddInitScriptAsync("Object.defineProperty(navigator, 'connection', { value: { saveData: true, addEventListener() {}, removeEventListener() {} } });");
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.AddInitScriptAsync(AuditScript);
        var prefix = $"{width}x{height}-{(reduce ? "reduce" : saveData ? "save-data" : "normal")}";
        Directory.CreateDirectory(Artifacts);
        await page.GotoAsync($"{BaseUrl}/negocios/{Slug}");
        await page.Locator($"a[href='/negocios/{Slug}/pedidos#producto-{product.Id}']").ClickAsync();
        var card = Card(page, product);
        await Expect(card.Locator(".product-choice")).ToBeEnabledAsync(new() { Timeout = 30000 });
        await Expect(card).ToHaveAttributeAsync("data-selected", "true");
        await Expect(card.Locator(".product-choice")).ToBeFocusedAsync();
        await page.EvaluateAsync("() => document.fonts.ready");
        if (!string.IsNullOrEmpty(product.ImageUrl))
            await page.WaitForFunctionAsync("id => { const img = document.querySelector(`[data-product-id='${id}'] img`); return img?.complete && img.naturalWidth > 0; }", product.Id.ToString());
        await Settle(page);
        await card.Locator(".product-deselect").ClickAsync();
        await Expect(card).ToHaveAttributeAsync("data-selected", "false");
        await Settle(page);
        await page.ScreenshotAsync(new() { Path = Path.Combine(Artifacts, $"{prefix}-resting.png") });
        await card.EvaluateAsync("el => { window.__productNode = el; window.__mediaNode = el.querySelector('.catalogo-foto'); window.__ctaNode = el.querySelector('.product-cta'); }");
        var before = await card.BoundingBoxAsync();
        await ResetMetrics(page);
        await card.GetByRole(AriaRole.Button, new() { Name = $"Elegir {product.Name}", Exact = true }).ClickAsync();
        await Expect(card.Locator(".product-choice")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(card.Locator(".product-state")).ToHaveTextAsync("Seleccionado");
        await Measure(page, prefix, "selection");
        var after = await card.BoundingBoxAsync();
        Assert.Equal(before!.Height, after!.Height);
        await ResetMetrics(page);
        await card.Locator(".product-cta").ClickAsync();
        await Expect(card.GetByTestId("quantity")).ToHaveTextAsync("1");
        await card.GetByRole(AriaRole.Button, new() { Name = $"Agregar uno de {product.Name}" }).ClickAsync();
        await Expect(card.GetByTestId("quantity")).ToHaveTextAsync("2");
        await Expect(card.Locator(".product-line-total")).ToContainTextAsync(Price(product.ReferencePrice * 2));
        await Expect(card.Locator(".product-cta")).ToHaveAccessibleNameAsync($"Continuar con {product.Name}");
        await Measure(page, prefix, "quantity-and-dock");
        Assert.True(await card.EvaluateAsync<bool>("el => el === window.__productNode && el.querySelector('.catalogo-foto') === window.__mediaNode && el.querySelector('.product-cta') === window.__ctaNode"));
        await page.ScreenshotAsync(new() { Path = Path.Combine(Artifacts, $"{prefix}-selected.png") });

        // A→B→C and repeated quantities use real events, never classes or a fake render.
        await ResetMetrics(page);
        foreach (var next in products)
            await Card(page, next).Locator(".product-choice").ClickAsync(new() { Force = true });
        var last = products[^1];
        await Expect(Card(page, last)).ToHaveAttributeAsync("data-selected", "true");
        Assert.Equal(1, await page.Locator("[data-product-id][data-selected=true]").CountAsync());
        await Expect(Card(page, last).Locator(".product-price")).ToContainTextAsync(Price(last.ReferencePrice));
        await Expect(Card(page, last).Locator(".product-cta")).ToHaveAccessibleNameAsync(last.Id == product.Id ? $"Continuar con {last.Name}" : $"Agregar {last.Name} al pedido");
        var add = card.GetByRole(AriaRole.Button, new() { Name = $"Agregar uno de {product.Name}" });
        for (var i = 0; i < 5; i++) await add.ClickAsync(new() { Force = true });
        await Expect(card.GetByTestId("quantity")).ToHaveTextAsync("7");
        var remove = card.GetByRole(AriaRole.Button, new() { Name = $"Quitar uno de {product.Name}" });
        for (var i = 0; i < 5; i++) await remove.ClickAsync(new() { Force = true });
        await Expect(card.GetByTestId("quantity")).ToHaveTextAsync("2");
        await Expect(card.Locator(".product-line-total")).ToContainTextAsync(Price(product.ReferencePrice * 2));
        await Measure(page, prefix, "rapid-selection-and-quantity");
        await ResetMetrics(page);
        await card.Locator(".product-choice").PressAsync("Escape");
        await Expect(card).ToHaveAttributeAsync("data-selected", "false");
        await Expect(card.Locator(".product-choice")).ToBeFocusedAsync();
        await Expect(card.GetByTestId("quantity")).ToHaveTextAsync("2");
        await Measure(page, prefix, "close");
        await card.Locator(".product-cta").ClickAsync();
        await Expect(page.Locator("#order-heading")).ToBeFocusedAsync();
        await Expect(page.Locator(".resumen-lineas li")).ToContainTextAsync(product.Name);
        await Expect(page.Locator(".total-pedido")).ToContainTextAsync(Price(product.ReferencePrice * 2));
        await page.GetByRole(AriaRole.Button, new() { Name = "Volver a productos" }).ClickAsync();
        await Expect(card.Locator(".product-choice")).ToBeFocusedAsync();
        await ResetMetrics(page);
        await card.Locator(".product-cta").ClickAsync();
        await Measure(page, prefix, "composer");
        await page.ScreenshotAsync(new() { Path = Path.Combine(Artifacts, $"{prefix}-composer.png") });
        await CheckAccessibility(page);
        await Idle(page, prefix);
        await page.GetByLabel("Hora para recoger").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await page.GetByLabel("Nombre o alias").FillAsync($"J-MOTION-06 {prefix}");
        await page.GetByLabel("Celular", new() { Exact = true }).FillAsync("3000000000");
        Assert.Equal("tel", await page.GetByLabel("Celular", new() { Exact = true }).GetAttributeAsync("inputmode"));
        await page.GetByLabel("Nota general (opcional)").FillAsync("Prueba técnica J-MOTION-06, sin entrega real.");
        await page.GetByLabel("Acepto el uso de estos datos").CheckAsync();
        // Virtual-keyboard-sized viewport: the confirm action still scrolls into view without a dock.
        if (width < 760)
        {
            await page.SetViewportSizeAsync(width, 480);
            await page.GetByRole(AriaRole.Button, new() { Name = "Confirmar pedido", Exact = true }).ScrollIntoViewIfNeededAsync();
            await AssertGeometry(page);
            await page.ScreenshotAsync(new() { Path = Path.Combine(Artifacts, $"{prefix}-keyboard-viewport.png") });
            await page.SetViewportSizeAsync(width, height);
        }
        await page.Locator(".resumen-lineas").EvaluateAsync("el => { window.__summaryNode = el; window.__lineNode = el.firstElementChild; window.__composerNode = el.closest('section'); }");
        await ResetMetrics(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Confirmar pedido", Exact = true }).ClickAsync();
        await Expect(page.GetByTestId("order-created")).ToBeVisibleAsync(new() { Timeout = 30000 });
        await Expect(page.Locator("#order-heading")).ToBeFocusedAsync();
        await Expect(page.GetByTestId("order-created")).ToContainTextAsync(product.Name);
        await Expect(page.GetByTestId("order-created")).ToContainTextAsync(Price(product.ReferencePrice * 2));
        var code = await page.GetByTestId("tracking-code").InnerTextAsync();
        var persisted = (await http.GetFromJsonAsync<PickupOrderTrackingDto>($"/api/v1/public/orders/{code}", Json))!;
        Assert.Equal(2, persisted.Lines.Single().Quantity);
        Assert.Equal(product.Id, persisted.Lines.Single().ProductId);
        Assert.Equal(product.ReferencePrice, persisted.Lines.Single().UnitPrice);
        Assert.Equal(product.ReferencePrice * 2, persisted.Total);
        Assert.Equal("Pending", persisted.Status);
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Seguir mi pedido" })).ToHaveAttributeAsync("href", $"/seguimiento/pedidos/{code}");
        await Expect(page.GetByTestId("activity-recovery-cta")).ToHaveAttributeAsync("href", "/seguimiento");
        Assert.True(await page.Locator(".resumen-lineas").EvaluateAsync<bool>("el => el === window.__summaryNode && el.firstElementChild === window.__lineNode && el.closest('section') === window.__composerNode"));
        await Measure(page, prefix, "confirmation");
        await page.ScreenshotAsync(new() { Path = Path.Combine(Artifacts, $"{prefix}-confirmation.png") });
        await CheckAccessibility(page);
        await Idle(page, prefix);
        if (reduce || saveData) Assert.Equal(0, await page.EvaluateAsync<int>("() => window.__audit.motion"));
        else Assert.True(await page.EvaluateAsync<int>("() => window.__audit.motion") > 0, "The real motion module must execute.");
        Assert.Empty(errors);
        output.WriteLine($"{prefix}: order #{persisted.OrderNumber}, {persisted.Total}, code={code}, identity/quantity/focus PASS");
        await page.GetByTestId("activity-recovery-cta").ClickAsync();
        await Expect(page.GetByText($"Pedido #{persisted.OrderNumber}", new() { Exact = false }).First).ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    private async Task Measure(IPage page, string prefix, string phase)
    {
        await Settle(page);
        var metric = await page.EvaluateAsync<JsonElement>("() => ({ cls: window.__audit.cls, allShifts: window.__audit.all, animations: document.querySelector('.pickup-journey').getAnimations({subtree:true}).filter(a=>a.playState==='running').length })");
        output.WriteLine($"{prefix}/{phase}: {metric}");
        File.AppendAllText(Path.Combine(Artifacts, "metrics.jsonl"), JsonSerializer.Serialize(new { prefix, phase, metric }) + Environment.NewLine);
        Assert.True(metric.GetProperty("cls").GetDouble() < .01, $"{phase}: {metric}");
        Assert.True(metric.GetProperty("allShifts").GetDouble() < .01, $"All shifts, including recent input, {phase}: {metric}");
        Assert.Equal(0, metric.GetProperty("animations").GetInt32());
        await AssertGeometry(page);
    }
    private async Task Idle(IPage page, string prefix)
    {
        await Settle(page);
        var idle = await page.EvaluateAsync<JsonElement>("""
            () => new Promise(resolve => {
                const root = document.querySelector('.pickup-journey');
                let mutations = 0;
                const observer = new MutationObserver(records => mutations += records.length);
                observer.observe(root, { subtree: true, attributes: true, childList: true, characterData: true });
                const before = window.__audit.frames;
                setTimeout(() => { observer.disconnect(); resolve({ mutations, frames: window.__audit.frames - before, animations: root.getAnimations({subtree:true}).filter(a => a.playState === 'running' || a.pending).length }); }, 1800);
            })
            """);
        output.WriteLine($"{prefix}/idle: {idle}");
        foreach (var field in new[] { "mutations", "frames", "animations" }) Assert.Equal(0, idle.GetProperty(field).GetInt32());
    }
    private static async Task CheckAccessibility(IPage page)
    {
        var issues = await page.EvaluateAsync<string[]>(ContrastAccessibilityTests.Medidor.Replace("document.querySelectorAll('body *')", "document.querySelectorAll('.pickup-journey *')"));
        Assert.Empty(issues);
        await Expect(page.Locator(".resumen-lineas")).ToHaveAccessibleNameAsync("Productos de tu pedido");
        Assert.Equal(0, await page.Locator("[data-action-value][aria-live]").CountAsync());
    }

    [Fact]
    public async Task No_media_dark_mode_zero_quantity_and_missing_motion_module_keep_real_actions()
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        { ViewportSize = new() { Width = 360, Height = 800 }, ColorScheme = ColorScheme.Dark });
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + "/negocios/restaurante-sazon-local/pedidos");
        var card = page.GetByTestId("product-card").First;
        await Expect(card.Locator(".product-cta")).ToBeEnabledAsync(new() { Timeout = 30000 });
        await Expect(card.Locator(".sin-foto")).ToBeVisibleAsync();
        await card.Locator(".product-cta").ClickAsync();
        await Expect(card.GetByTestId("quantity")).ToHaveTextAsync("1");
        await Settle(page);
        await CheckAccessibility(page);
        Directory.CreateDirectory(Artifacts);
        await page.ScreenshotAsync(new() { Path = Path.Combine(Artifacts, "360x800-dark-no-media.png") });
        await card.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Quitar uno de ") }).ClickAsync();
        await Expect(card.GetByTestId("quantity")).ToHaveTextAsync("0");
        await Expect(card.Locator(".product-cta")).ToContainTextAsync("Agregar al pedido");
        await Expect(page.Locator(".resumen-lineas li")).ToHaveCountAsync(0);
        await card.Locator(".product-deselect").ClickAsync();
        await Expect(card.Locator(".product-choice")).ToBeFocusedAsync();
        await Expect(card).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("en-carrito"));

        // Block only decoration; the real Blazor app/API must still add and navigate to the form.
        await page.RouteAsync("**/Pages/PickupOrdering*.js", route => route.AbortAsync());
        await page.ReloadAsync();
        await Expect(card.Locator(".product-cta")).ToBeEnabledAsync(new() { Timeout = 30000 });
        await card.Locator(".product-cta").ClickAsync();
        await Expect(card.GetByTestId("quantity")).ToHaveTextAsync("1");
        await card.Locator(".product-cta").ClickAsync();
        await page.WaitForURLAsync("**#tu-pedido");
        Assert.EndsWith("#tu-pedido", page.Url);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Confirmar pedido", Exact = true })).ToBeVisibleAsync();
    }
    private static async Task AssertGeometry(IPage page)
    {
        Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > innerWidth + 1"));
        Assert.True(await page.EvaluateAsync<bool>("() => [...document.querySelectorAll('.product-dock')].every(el => getComputedStyle(el).position !== 'fixed')"));
    }
    private static Task ResetMetrics(IPage page) => page.EvaluateAsync("() => { window.__audit.cls = 0; window.__audit.all = 0; }");
    private static Task Settle(IPage page) => page.WaitForTimeoutAsync(650);
    private static ILocator Card(IPage page, ProductDto product) => page.Locator($"[data-product-id='{product.Id}']");
    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
    private static string Price(decimal value) => value.ToString("N0", new System.Globalization.CultureInfo("es-CO"));
    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "UrabaConecta.slnx"))) dir = dir.Parent;
        return dir!.FullName;
    }
    private const string AuditScript = """
        window.__audit = { cls: 0, all: 0, motion: 0, frames: 0 };
        new PerformanceObserver(list => { for (const e of list.getEntries()) { window.__audit.all += e.value; if (!e.hadRecentInput) window.__audit.cls += e.value; } }).observe({ type: 'layout-shift', buffered: true });
        const realAnimate = Element.prototype.animate;
        Element.prototype.animate = function(...args) { if (this.closest('.pickup-journey')) window.__audit.motion++; return realAnimate.apply(this, args); };
        const realFrame = window.requestAnimationFrame;
        window.requestAnimationFrame = function(...args) { window.__audit.frames++; return realFrame.apply(this, args); };
        """;
}

