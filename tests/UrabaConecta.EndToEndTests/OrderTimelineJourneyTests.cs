using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;
using UrabaConecta.Contracts;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.EndToEndTests;

/// <summary>
/// J-MOTION-07. Lo que se prueba aquí no es que el estado llegue —eso ya lo hacía—, sino que
/// llegue como un hito que se suma a una historia: el anterior sigue ahí, el nuevo queda como
/// actual, y el encabezado y la lista cuentan lo mismo.
///
/// El estado lo mueve el negocio de verdad, desde su panel y con su sesión: no se escribe una fila
/// ni se llama a la API por debajo. Es la única forma de comprobar que el aviso viaja por SignalR
/// hasta la pantalla de quien pidió.
/// </summary>
[Collection(PublicSiteCollection.Name)]
public sealed class OrderTimelineJourneyTests(BrowserFixture fixture, Xunit.Abstractions.ITestOutputHelper output)
{
    private const string Slug = "restaurante-sazon-local";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string Carpeta = Path.Combine(Raiz(), "artifacts", "j-motion-07");

    private static string Raiz()
    {
        var actual = new DirectoryInfo(AppContext.BaseDirectory);
        while (actual is not null && !File.Exists(Path.Combine(actual.FullName, "UrabaConecta.slnx")))
            actual = actual.Parent;
        return actual?.FullName ?? AppContext.BaseDirectory;
    }

    // =============================================================================================
    // El caso central: la historia crece de a un hito, y lo anterior no se va.
    // =============================================================================================
    [Theory]
    [InlineData(1440, 1000)]
    [InlineData(1920, 1080)]
    [InlineData(390, 844)]
    [InlineData(360, 800)]
    public async Task History_grows_one_milestone_at_a_time_while_the_business_advances_the_order(
        int width, int height)
    {
        var etiqueta = $"{width}x{height}";
        var (code, orderId, alias) = await PlaceOrderAsync($"J07 crece {etiqueta}");
        await using var consumer = await fixture.Browser.NewContextAsync(new()
        { ViewportSize = new() { Width = width, Height = height } });
        var page = await consumer.NewPageAsync();
        await AbrirSeguimiento(page, code);

        // Al abrir, la historia ya existe y no entra nada: un hito real, el de la creación.
        await Assertions.Expect(Hitos(page)).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("[data-timeline-new=true]")).ToHaveCountAsync(0);
        await Assertions.Expect(Actual(page).Locator(".hito-titulo strong")).ToHaveTextAsync("Pedido recibido");
        await Assertions.Expect(page.GetByTestId("timeline-next")).ToHaveTextAsync("Falta que el negocio confirme el pedido.");
        await Captura(page, $"{etiqueta}-01-recibido");

        await using var operador = await LoginOperatorAsync();
        var panel = await operador.NewPageAsync();

        var esperados = new (string Boton, string Estado, string Encabezado, string Sigue)[]
        {
            ("Aceptar", "Accepted", "Aceptado", "Falta que empiecen a prepararlo."),
            ("Preparar", "Preparing", "En preparación", "Falta que quede listo."),
            ("Listo", "ReadyForPickup", "Listo para recoger", "Falta que pases a recogerlo."),
            ("Entregado", "Delivered", "Entregado", "")
        };

        var previos = 1;
        foreach (var (boton, estado, encabezado, sigue) in esperados)
        {
            await IniciarDesplazamiento(page);
            await AvanzarAsync(panel, alias, boton);

            // Exactamente un hito más. Ni cero —el aviso llegó— ni dos —no se duplica—.
            await Assertions.Expect(Hitos(page)).ToHaveCountAsync(previos + 1, new() { Timeout = 20_000 });
            previos++;

            // El nuevo es el actual; el que era actual sigue en la lista, ahora como historia.
            var actual = Actual(page);
            await Assertions.Expect(actual).ToHaveAttributeAsync("data-status", estado);
            await Assertions.Expect(page.Locator("[data-timeline-state=actual]")).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator($"[data-status=Pending]")).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator("[data-status=Pending][data-timeline-state=historia]"))
                .ToHaveCountAsync(1);

            // El encabezado y la historia no pueden contar cosas distintas.
            await Assertions.Expect(page.Locator("[data-testid=order-tracking] .status")).ToHaveTextAsync(encabezado);
            if (sigue.Length > 0) await Assertions.Expect(page.GetByTestId("timeline-next")).ToHaveTextAsync(sigue);
            else await Assertions.Expect(page.GetByTestId("timeline-next")).ToHaveCountAsync(0);

            // Sólo el hito nuevo se anuncia, no la historia entera.
            var anuncio = await page.GetByTestId("timeline-announce").InnerTextAsync();
            Assert.StartsWith(encabezado == "Entregado" ? "Pedido entregado" : "Pedido", anuncio);
            Assert.DoesNotContain("Pedido recibido", anuncio);

            var cls = await LeerDesplazamiento(page);
            var quieto = await Reposo(page);
            output.WriteLine($"{etiqueta} → {estado}: cls={cls}, {quieto}");
            Assert.True(cls < 0.1, $"El hito nuevo movió la página: {cls}");
        }

        await Captura(page, $"{etiqueta}-02-entregado");
        // El cierre se distingue por forma y palabra, no sólo por color.
        await Assertions.Expect(Actual(page)).ToHaveAttributeAsync("data-timeline-outcome", "entregado");
        await Assertions.Expect(Actual(page).GetByText("Cierre")).ToBeVisibleAsync();
        Assert.Equal(5, await Hitos(page).CountAsync());
        Assert.False(await Desborda(page), "La historia no cabe a lo ancho.");
    }

    // =============================================================================================
    // Tres cambios seguidos: ninguno se pierde. Lo contrario de la fila virtual.
    // =============================================================================================
    [Fact]
    public async Task Three_fast_transitions_leave_the_three_milestones_in_the_real_order()
    {
        var (code, _, alias) = await PlaceOrderAsync("J07 rapido");
        await using var consumer = await fixture.Browser.NewContextAsync(new()
        { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await consumer.NewPageAsync();
        await AbrirSeguimiento(page, code);

        await using var operador = await LoginOperatorAsync();
        var panel = await operador.NewPageAsync();
        // Sin esperar a que la pantalla del cliente termine de pintar cada paso.
        await AvanzarAsync(panel, alias, "Aceptar");
        await AvanzarAsync(panel, alias, "Preparar");
        await AvanzarAsync(panel, alias, "Listo");

        await Assertions.Expect(Hitos(page)).ToHaveCountAsync(4, new() { Timeout = 25_000 });
        Assert.Equal(["Pending", "Accepted", "Preparing", "ReadyForPickup"], await Estados(page));
        await Assertions.Expect(Actual(page)).ToHaveAttributeAsync("data-status", "ReadyForPickup");
        await Assertions.Expect(page.Locator("[data-testid=order-tracking] .status"))
            .ToHaveTextAsync("Listo para recoger");
        output.WriteLine($"rápido: {string.Join(" → ", await Estados(page))}");
        Assert.Equal(0, (await Reposo(page)).Animaciones);
    }

    // =============================================================================================
    // Recargar y un segundo dispositivo reconstruyen la misma historia, sin duplicados.
    // =============================================================================================
    [Fact]
    public async Task A_refresh_and_a_second_device_rebuild_the_same_history()
    {
        var (code, _, alias) = await PlaceOrderAsync("J07 refresco");
        await using var consumer = await fixture.Browser.NewContextAsync();
        var page = await consumer.NewPageAsync();
        await AbrirSeguimiento(page, code);
        await using var operador = await LoginOperatorAsync();
        var panel = await operador.NewPageAsync();
        await AvanzarAsync(panel, alias, "Aceptar");
        await AvanzarAsync(panel, alias, "Preparar");
        await Assertions.Expect(Hitos(page)).ToHaveCountAsync(3, new() { Timeout = 20_000 });

        await page.ReloadAsync();
        await Assertions.Expect(page.GetByTestId("order-timeline")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Assertions.Expect(Hitos(page)).ToHaveCountAsync(3);
        Assert.Equal(["Pending", "Accepted", "Preparing"], await Estados(page));
        // Al hidratar no se estrena nada: la historia ya estaba escrita antes de abrirla.
        await Assertions.Expect(page.Locator("[data-timeline-new=true]")).ToHaveCountAsync(0);

        await using var segundo = await fixture.Browser.NewContextAsync(new()
        { ViewportSize = new() { Width = 360, Height = 800 } });
        var otro = await segundo.NewPageAsync();
        await AbrirSeguimiento(otro, code);
        Assert.Equal(await Estados(page), await Estados(otro));
        Assert.Equal(3, await Hitos(otro).CountAsync());
    }

    // =============================================================================================
    // Menos movimiento y ahorro de datos: la misma historia, sin un solo viaje.
    // =============================================================================================
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Quiet_preferences_keep_the_whole_history_without_travel(bool reduce, bool saveData)
    {
        var (code, _, alias) = await PlaceOrderAsync($"J07 quieto {(reduce ? "reduce" : "save")}");
        await using var consumer = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            ReducedMotion = reduce ? ReducedMotion.Reduce : ReducedMotion.NoPreference
        });
        if (saveData) await consumer.SetExtraHTTPHeadersAsync(new Dictionary<string, string> { ["Save-Data"] = "on" });
        var page = await consumer.NewPageAsync();
        if (saveData)
            await page.AddInitScriptAsync(
                "Object.defineProperty(navigator, 'connection', { value: { saveData: true }, configurable: true });");
        await AbrirSeguimiento(page, code);

        await using var operador = await LoginOperatorAsync();
        var panel = await operador.NewPageAsync();
        await AvanzarAsync(panel, alias, "Aceptar");

        await Assertions.Expect(Hitos(page)).ToHaveCountAsync(2, new() { Timeout = 20_000 });
        await Assertions.Expect(Actual(page)).ToHaveAttributeAsync("data-status", "Accepted");
        // Ni una animación: el hito llegó completo, con su marca y su hora.
        var quieto = await Reposo(page);
        Assert.Equal(0, quieto.Animaciones);
        output.WriteLine($"reduce={reduce} saveData={saveData}: {quieto}");
        await Captura(page, reduce ? "390x844-reduce" : "390x844-save-data");
    }

    // =============================================================================================
    // Una cancelación desde el enlace público no publica aviso. Aun así tiene que quedar escrita.
    // =============================================================================================
    [Fact]
    public async Task A_public_cancellation_closes_the_history_even_without_a_stored_update()
    {
        var (code, _, _) = await PlaceOrderAsync("J07 cancela");
        await using var consumer = await fixture.Browser.NewContextAsync(new()
        { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await consumer.NewPageAsync();
        await AbrirSeguimiento(page, code);
        await page.GetByRole(AriaRole.Button, new() { Name = "Cancelar pedido" }).ClickAsync();

        await Assertions.Expect(Hitos(page)).ToHaveCountAsync(2, new() { Timeout = 20_000 });
        var actual = Actual(page);
        await Assertions.Expect(actual).ToHaveAttributeAsync("data-status", "Cancelled");
        await Assertions.Expect(actual).ToHaveAttributeAsync("data-timeline-outcome", "cierre");
        await Assertions.Expect(actual.GetByText("Cierre")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("timeline-next")).ToHaveCountAsync(0);
        // El encabezado y el último hito siguen contando lo mismo.
        await Assertions.Expect(page.Locator("[data-testid=order-tracking] .status")).ToHaveTextAsync("Cancelado");
        await Captura(page, "390x844-cancelado");
    }

    // =============================================================================================
    // Accesibilidad: la historia se entiende sin movimiento y sin color.
    // =============================================================================================
    [Fact]
    public async Task The_history_is_a_real_list_with_readable_contrast()
    {
        var (code, _, alias) = await PlaceOrderAsync("J07 acceso");
        await using var consumer = await fixture.Browser.NewContextAsync(new()
        { ViewportSize = new() { Width = 1440, Height = 1000 } });
        var page = await consumer.NewPageAsync();
        await AbrirSeguimiento(page, code);
        await using var operador = await LoginOperatorAsync();
        var panel = await operador.NewPageAsync();
        await AvanzarAsync(panel, alias, "Aceptar");
        await Assertions.Expect(Hitos(page)).ToHaveCountAsync(2, new() { Timeout = 20_000 });

        // Estructura, no sólo pintura: una lista ordenada de elementos de lista.
        Assert.Equal(2, await page.Locator("[data-testid=order-timeline] ol.hitos > li").CountAsync());
        var anuncio = page.GetByTestId("timeline-announce");
        await Assertions.Expect(anuncio).ToHaveAttributeAsync("aria-live", "polite");
        // El estado actual se nombra con palabras, no sólo con posición o color.
        await Assertions.Expect(Actual(page).GetByText("Ahora")).ToBeVisibleAsync();

        var problemas = await page.EvaluateAsync<string[]>(
            ContrastAccessibilityTests.Medidor.Replace("document.querySelectorAll('body *')",
                "document.querySelectorAll('.linea-pedido, .linea-pedido *')"));
        Assert.Empty(problemas);
    }

    // =============================================================================================
    // Utilidades
    // =============================================================================================
    private static ILocator Hitos(IPage page) => page.Locator("[data-testid=order-timeline] .hito");
    private static ILocator Actual(IPage page) => page.Locator(".hito[data-timeline-state=actual]");

    private static async Task<string[]> Estados(IPage page) =>
        [.. await Hitos(page).EvaluateAllAsync<string[]>("nodes => nodes.map(n => n.dataset.status)")];

    private async Task AbrirSeguimiento(IPage page, string code)
    {
        await page.GotoAsync($"{fixture.BaseUrl}/seguimiento/pedidos/{code}");
        await Assertions.Expect(page.GetByTestId("order-timeline")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        // Sin esto el circuito puede no haber conectado todavía y el primer avance llegaría
        // sólo al recargar, que es justo lo que esta pantalla no debe necesitar.
        await Assertions.Expect(page.Locator("[data-testid=order-tracking] .status")).ToBeVisibleAsync();
        await page.WaitForTimeoutAsync(600);
    }

    private async Task<IBrowserContext> LoginOperatorAsync()
    {
        var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{fixture.BaseUrl}/Account/Login");
        await page.GetByLabel("Correo").FillAsync(DevelopmentSeeder.SazonOrdersWorkerEmail);
        await page.GetByLabel("Contraseña").FillAsync(DevelopmentSeeder.DemoPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Ingresar" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/panel"));
        await page.CloseAsync();
        return context;
    }

    private async Task AvanzarAsync(IPage panel, string alias, string boton)
    {
        await panel.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.SazonBusinessId}/pedidos");
        var fila = panel.Locator("[data-testid=admin-order]").Filter(new() { HasTextString = alias });
        await Assertions.Expect(fila).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await fila.GetByRole(AriaRole.Button, new() { Name = boton, Exact = true }).First.ClickAsync();
        await Assertions.Expect(fila.GetByRole(AriaRole.Button, new() { Name = boton, Exact = true }))
            .ToHaveCountAsync(0, new() { Timeout = 20_000 });
    }

    /// <summary>Empieza a contar los desplazamientos de la página antes de que llegue el hito.</summary>
    private static async Task IniciarDesplazamiento(IPage page) => await page.EvaluateAsync("""
        () => {
            window.__j07 = { cls: 0 };
            window.__j07.observer?.disconnect();
            const observer = new PerformanceObserver(list => {
                for (const entry of list.getEntries()) if (!entry.hadRecentInput) window.__j07.cls += entry.value;
            });
            observer.observe({ type: 'layout-shift', buffered: false });
            window.__j07.observer = observer;
        }
        """);

    private static Task<double> LeerDesplazamiento(IPage page) => page.EvaluateAsync<double>(
        "() => { window.__j07.observer.takeRecords(); return window.__j07.cls }");

    private sealed record Quietud(int Mutaciones, int Animaciones)
    {
        public override string ToString() => $"mutaciones={Mutaciones}, animaciones={Animaciones}";
    }

    /// <summary>Cuando se asienta no queda nada moviéndose ni cambiando solo.</summary>
    private static async Task<Quietud> Reposo(IPage page)
    {
        var medida = await page.EvaluateAsync<JsonElement>("""
            () => new Promise(resolve => {
                const raiz = document.querySelector('[data-testid=order-timeline]');
                let mutaciones = 0;
                const observer = new MutationObserver(rs => mutaciones += rs.length);
                observer.observe(raiz, { subtree: true, attributes: true, childList: true, characterData: true });
                setTimeout(() => {
                    observer.disconnect();
                    const vivas = raiz.getAnimations({ subtree: true })
                        .filter(a => a.playState === 'running' || a.pending).length;
                    resolve({ mutaciones, animaciones: vivas });
                }, 1500);
            })
            """);
        return new(medida.GetProperty("mutaciones").GetInt32(), medida.GetProperty("animaciones").GetInt32());
    }

    private static Task<bool> Desborda(IPage page)
        => page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth + 1");

    private static async Task Captura(IPage page, string nombre)
    {
        Directory.CreateDirectory(Carpeta);
        await page.ScreenshotAsync(new() { Path = Path.Combine(Carpeta, $"{nombre}.png"), FullPage = true });
    }

    /// <summary>
    /// El pedido se crea por la API pública, que es el mismo camino que usa la pantalla. Lo que
    /// esta suite prueba empieza después: cómo se cuenta lo que le va pasando.
    /// </summary>
    private async Task<(string Code, Guid OrderId, string Alias)> PlaceOrderAsync(string alias)
    {
        using var http = new HttpClient { BaseAddress = new(fixture.BaseUrl) };
        var menu = (await http.GetFromJsonAsync<PickupMenuDto>($"/api/v1/public/businesses/{Slug}/menu", Json))!;
        var product = menu.Products.First(p => p.IsAvailable);
        var slots = (await http.GetFromJsonAsync<PickupSlotListDto>($"/api/v1/public/businesses/{Slug}/pickup-slots", Json))!;
        var request = new CreatePickupOrderRequest
        {
            PickupStart = slots.Slots.First().Start,
            CustomerAlias = alias,
            Phone = "3000000007",
            Lines = [new() { ProductId = product.Id, Quantity = 2 }],
            ConsentAccepted = true
        };
        var response = await http.PostAsJsonAsync($"/api/v1/public/businesses/{Slug}/orders", request, Json);
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<PickupOrderCreatedDto>(Json))!;
        return (created.TrackingCode, Guid.Empty, alias);
    }
}
