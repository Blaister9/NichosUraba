using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Microsoft.AspNetCore.SignalR.Client;
using UrabaConecta.Contracts;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.EndToEndTests;

public sealed class QueueJourneyTests(BrowserFixture fixture, Xunit.Abstractions.ITestOutputHelper output) : IClassFixture<BrowserFixture>
{
    private readonly List<string> accessibilityFailures = [];
    [Fact]
    public async Task Seven_queue_scenarios_work_in_real_chromium()
    {
        await using var visitorContext = await MobileContext();
        var visitor = await visitorContext.NewPageAsync();

        // 1. Ahora -> Buscar -> perfil -> fila.
        await visitor.GotoAsync(fixture.BaseUrl);
        await visitor.GetByRole(AriaRole.Link, new() { Name = "Buscar", Exact = true }).First.ClickAsync();
        await visitor.WaitForURLAsync(url => url.Contains("/explorar"));
        await Expect(visitor.GetByRole(AriaRole.Heading, new() { Name = "Explorar negocios" }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(visitor.GetByRole(AriaRole.Button, new() { Name = "Buscar" }))
            .ToBeEnabledAsync(new() { Timeout = 30_000 });
        await visitor.GetByLabel("Qué buscas").FillAsync("El Corte");
        await visitor.GetByRole(AriaRole.Button, new() { Name = "Buscar" }).ClickAsync();
        await Expect(visitor.GetByText("Barbería El Corte")).ToBeVisibleAsync();
        await visitor.GotoAsync($"{fixture.BaseUrl}/negocios/barberia-el-corte");
        await Expect(visitor.GetByRole(AriaRole.Heading, new() { Name = "Barbería El Corte" })).ToBeVisibleAsync();
        await Expect(visitor.GetByRole(AriaRole.Link, new() { Name = "Tomar turno" }).First).ToBeVisibleAsync();
        await visitor.GotoAsync($"{fixture.BaseUrl}/negocios/barberia-el-corte/turnos");
        await Expect(visitor.GetByRole(AriaRole.Heading, new() { Name = "Atención general" })).ToBeVisibleAsync();
        await using var publicHub = new HubConnectionBuilder().WithUrl($"{fixture.BaseUrl}/hubs/queue").Build();
        var publicChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        publicHub.On("QueueChanged", () => publicChanged.TrySetResult());
        await publicHub.StartAsync();
        await publicHub.InvokeAsync("SubscribePublic", "barberia-el-corte");

        // 2. Toma y conserva su código individual.
        await visitor.GetByLabel("Alias corto (opcional)").FillAsync("E2E");
        // Desde V5 el turno público también exige aceptar el aviso de tratamiento de datos.
        await visitor.GetByLabel("Acepto que este alias se use para gestionar mi turno y anunciar el llamado.")
            .CheckAsync();
        await visitor.GetByRole(AriaRole.Button, new() { Name = "Tomar turno" }).ClickAsync();
        await Expect(visitor.GetByTestId("queue-created")).ToBeVisibleAsync();
        await publicChanged.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await visitor.GetByRole(AriaRole.Link, new() { Name = "Seguir mi turno" }).ClickAsync();
        // El tablero de la fila nombra la cifra en singular o en plural según cuánta gente hay:
        // "En espera" era una etiqueta de panel, no algo que alguien diga.
        await Expect(visitor.GetByText("Personas esperando", new() { Exact = true })
            .Or(visitor.GetByText("Persona esperando", new() { Exact = true })).First)
            .ToBeVisibleAsync();
        var trackingCode = visitor.Url.Split('/').Last();
        await using var ticketHub = new HubConnectionBuilder().WithUrl($"{fixture.BaseUrl}/hubs/queue").Build();
        var ticketChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ticketHub.On("TicketChanged", () => ticketChanged.TrySetResult());
        await ticketHub.StartAsync();
        await ticketHub.InvokeAsync("SubscribeTicket", trackingCode);

        // 3. Operador autorizado abre el panel móvil.
        await using var operatorContext = await MobileContext();
        var operations = await operatorContext.NewPageAsync();
        await Login(operations, DevelopmentSeeder.CorteQueueWorkerEmail);
        await operations.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.CorteBusinessId}/turnos");
        await Expect(operations.GetByRole(AriaRole.Heading, new() { Name = "Turnos virtuales" })).ToBeVisibleAsync();

        // 4. Llamado desde el panel operativo.
        await operations.GetByRole(AriaRole.Button, new() { Name = "Llamar siguiente" }).ClickAsync();
        // En el tablero del negocio el turno está "Llamado"; "Te estamos llamando" es la frase que lee
        // el cliente en su seguimiento, y en esta pantalla se leía como si el turno hablara solo.
        await Expect(operations.Locator(".queue-row").Filter(new() { HasTextString = "Llamado" })).ToBeVisibleAsync();
        await ticketChanged.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // 5. Atención y cierre de turno.
        await operations.GetByRole(AriaRole.Button, new() { Name = "Iniciar atención" }).ClickAsync();
        await Expect(operations.Locator(".queue-row").Filter(new() { HasTextString = "En atención" })).ToBeVisibleAsync();
        await operations.GetByRole(AriaRole.Button, new() { Name = "Completar" }).ClickAsync();
        await visitor.ReloadAsync();
        await Expect(visitor.GetByText("Atendido")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // 6. Pausa, reanudación y configuración sin cancelar historia.
        await operations.GetByRole(AriaRole.Button, new() { Name = "Pausar" }).ClickAsync();
        await Expect(operations.GetByText("Pausada")).ToBeVisibleAsync();
        await operations.GetByRole(AriaRole.Button, new() { Name = "Reanudar" }).ClickAsync();
        await Expect(operations.GetByRole(AriaRole.Link, new() { Name = "Configurar" })).ToBeVisibleAsync();
        await operations.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.CorteBusinessId}/configuracion/turnos");
        await Expect(operations.GetByRole(AriaRole.Heading, new() { Name = "Fila virtual" })).ToBeVisibleAsync();
        await operations.GetByLabel("Mensaje público").FillAsync("Mensaje E2E sin cancelar turnos.");
        await operations.GetByRole(AriaRole.Button, new() { Name = "Guardar configuración" }).ClickAsync();
        await Expect(operations.GetByText("Configuración guardada.")).ToBeVisibleAsync();

        // 7. Aislamiento y diseño a 360 px.
        await using var deniedContext = await MobileContext();
        var denied = await deniedContext.NewPageAsync();
        await Login(denied, DevelopmentSeeder.OtherOwnerEmail);
        await denied.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.CorteBusinessId}/turnos");
        await Expect(denied.GetByText("No tiene permiso para administrar turnos.")).ToBeVisibleAsync();
        Assert.False(await visitor.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"));
        Assert.False(await operations.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"));
    }

    /// <summary>
    /// J-MOTION-05 — ESTADO VIVO. La fila es la única pantalla del producto cuyo contenido cambia
    /// sin que nadie la toque, y hasta aquí ese cambio era una sustitución de texto: un 3 pasaba a
    /// ser un 2 y quien miraba de reojo no sabía si había avanzado o llevaba diez minutos igual.
    ///
    /// Esta prueba no pinta clases a mano: mueve la fila por donde se mueve de verdad —el operador
    /// llama, atiende y completa desde su panel— y comprueba en el teléfono del cliente que cada
    /// transición dice qué cambió, hacia dónde, y qué significa. Recorre las cuatro que existen:
    /// espera 3 → 2 → 1, "eres el siguiente" y "es tu turno".
    /// </summary>
    [Theory]
    [InlineData(1440, 1000)]
    [InlineData(1920, 1080)]
    [InlineData(390, 844)]
    [InlineData(360, 800)]
    public async Task Live_state_delta_narrates_the_real_queue_transitions(int width, int height)
    {
        await using var operatorContext = await MobileContext();
        var operations = await operatorContext.NewPageAsync();
        await Login(operations, DevelopmentSeeder.CorteQueueWorkerEmail);
        await operations.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.CorteBusinessId}/turnos");
        await Expect(operations.GetByRole(AriaRole.Heading, new() { Name = "Turnos virtuales" })).ToBeVisibleAsync();
        // La jornada puede llegar cerrada o pausada según qué prueba corrió antes: el recorrido
        // empieza siempre en el mismo sitio y no a merced del orden.
        var abrir = operations.GetByRole(AriaRole.Button, new() { Name = "Abrir jornada" });
        if (await abrir.CountAsync() > 0) await abrir.ClickAsync();
        var reanudar = operations.GetByRole(AriaRole.Button, new() { Name = "Reanudar" });
        if (await reanudar.CountAsync() > 0) await reanudar.ClickAsync();
        await Expect(operations.GetByRole(AriaRole.Button, new() { Name = "Pausar" })).ToBeVisibleAsync();

        // Tres personas delante, puestas por donde se ponen de verdad: el mostrador.
        for (var i = 1; i <= 3; i++)
        {
            await operations.GetByLabel("Alias presencial opcional").FillAsync($"Delta {i}");
            await operations.GetByRole(AriaRole.Button, new() { Name = "Agregar presencial" }).ClickAsync();
            await Expect(operations.Locator(".queue-row").Filter(new() { HasTextString = $"Delta {i}" }))
                .ToBeVisibleAsync();
        }

        // El teléfono en la mano de quien espera: 390 px es donde ocurre este recorrido.
        await using var visitorContext = await fixture.Browser.NewContextAsync(new()
        { ViewportSize = new() { Width = width, Height = height } });
        var visitor = await visitorContext.NewPageAsync();
        await visitor.GotoAsync($"{fixture.BaseUrl}/negocios/barberia-el-corte/turnos");
        await Expect(visitor.GetByRole(AriaRole.Heading, new() { Name = "Atención general" })).ToBeVisibleAsync();

        // CONTINUIDAD DEL CTA: pedir el turno y tenerlo ocupan el mismo hueco, y ese hueco cambia
        // de propósito una sola vez.
        var union = visitor.Locator("[data-live-swap]");
        await Capture(visitor, "tomar", width, height);
        var unionBefore = await union.BoundingBoxAsync();
        var scrollBefore = await visitor.EvaluateAsync<float>("scrollY");
        var unionNode = await union.ElementHandleAsync();
        await Expect(union).ToHaveAttributeAsync("data-live-clave", "tomar");
        await visitor.GetByLabel("Alias corto (opcional)").FillAsync("Delta");
        await visitor.GetByLabel("Acepto que este alias se use para gestionar mi turno y anunciar el llamado.")
            .CheckAsync();
        await visitor.GetByRole(AriaRole.Button, new() { Name = "Tomar turno" }).ClickAsync();
        await Expect(visitor.GetByTestId("queue-created")).ToBeVisibleAsync();
        await Expect(union).ToHaveAttributeAsync("data-live-clave", "confirmado");
        await Expect(union).ToHaveAttributeAsync("data-uc-live-n", "1");
        Assert.True(await unionNode!.EvaluateAsync<bool>("e => e.isConnected"));
        await visitor.WaitForTimeoutAsync(800);
        var unionAfter = await union.BoundingBoxAsync();
        Assert.Equal(unionBefore!.Y + scrollBefore, unionAfter!.Y + await visitor.EvaluateAsync<float>("scrollY"), 1);
        Assert.Equal(unionBefore.Height, unionAfter.Height, 1);
        await Capture(visitor, "confirmado", width, height);

        await visitor.GetByRole(AriaRole.Link, new() { Name = "Seguir mi turno" }).ClickAsync();
        var trackingCode = visitor.Url.Split('/').Last();
        var region = visitor.Locator("[data-live-state='turno']");
        var delante = visitor.Locator("[data-live-value][data-live-delta]");
        var estado = visitor.Locator(".mi-turno-estado");
        await Expect(region).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(region).ToHaveAttributeAsync("data-live-etapa", "espera");
        await Expect(delante).ToHaveTextAsync("3");
        await Expect(estado).ToHaveAttributeAsync("aria-live", "polite");
        await Expect(operations.GetByText("Delta", new() { Exact = true })).ToBeVisibleAsync();
        await Capture(visitor, "espera", width, height);
        var stableCard = await region.ElementHandleAsync();

        // A partir de aquí se mide el desplazamiento acumulado: reemplazar una cifra o una frase no
        // puede mover lo que hay debajo.
        await visitor.EvaluateAsync(@"() => {
            window.__cls = 0;
            new PerformanceObserver(lista => {
                for (const entrada of lista.getEntries()) if (!entrada.hadRecentInput) window.__cls += entrada.value;
            }).observe({ type: 'layout-shift', buffered: false });
        }");

        // 3 → 2 → 1. Cada vuelta es un llamado, una atención y un cierre reales: la posición sólo
        // baja cuando alguien de delante termina, que es exactamente lo que cuenta el dominio.
        for (var restantes = 2; restantes >= 0; restantes--)
        {
            await RecordState($"before complete: expected ahead {restantes + 1}", visitor, trackingCode);
            await AttendOne(operations);
            await RecordState($"after complete: expected ahead {restantes}", visitor, trackingCode);
            await Expect(delante).ToHaveTextAsync(restantes.ToString(), new() { Timeout = 5_000 });
            await Expect(delante).ToHaveAttributeAsync("data-uc-live-sentido", "baja");
            // Una noticia, un anuncio: ni se pierde un cambio ni se cuenta dos veces el mismo.
            await Expect(delante).ToHaveAttributeAsync("data-uc-live-n", (3 - restantes).ToString());
        }

        // ERES EL SIGUIENTE — todavía se espera, así que la etapa se eleva pero no interrumpe.
        await Expect(region).ToHaveAttributeAsync("data-live-etapa", "siguiente");
        await Expect(visitor.GetByRole(AriaRole.Heading, new() { Name = "Eres el siguiente" })).ToBeVisibleAsync();
        await Expect(estado).ToHaveAttributeAsync("aria-live", "polite");
        await Expect(region).ToHaveAttributeAsync("data-uc-live-n", "1");
        await Capture(visitor, "siguiente", width, height);

        // ES TU TURNO — el final del recorrido: interrumpe, y es lo único que interrumpe.
        await operations.GetByRole(AriaRole.Button, new() { Name = "Llamar siguiente" }).ClickAsync();
        await Expect(region).ToHaveAttributeAsync("data-live-etapa", "llamado", new() { Timeout = 5_000 });
        await Expect(visitor.GetByRole(AriaRole.Heading, new() { Name = "¡Es tu turno!" })).ToBeVisibleAsync();
        await Expect(estado).ToHaveAttributeAsync("aria-live", "assertive");
        await Expect(region).ToHaveAttributeAsync("data-uc-live-n", "2");
        // La salida de la fila deja de existir, pero su hueco no: la pantalla no da un tirón justo
        // en el momento en que hay que levantarse.
        await Expect(visitor.GetByRole(AriaRole.Button, new() { Name = "Cancelar mi turno" })).ToHaveCountAsync(0);
        Assert.True(await visitor.Locator(".fila-accion")
            .EvaluateAsync<double>("e => e.getBoundingClientRect().height") > 0);
        Assert.True(await stableCard!.EvaluateAsync<bool>("e => e.isConnected"));
        await Capture(visitor, "llamado", width, height);

        // QUIETO DESPUÉS. Nada sigue latiendo, nada quedó a medias y ningún adorno sobrevivió a su
        // animación: quien vuelve a mirar el teléfono ve un estado, no una función del tiempo.
        await visitor.WaitForTimeoutAsync(1_500);
        Assert.Equal(0, await region.EvaluateAsync<int>(
            "e => e.getAnimations({ subtree: true }).filter(a => a.playState === 'running').length"));
        Assert.Equal(0, await region.EvaluateAsync<int>(
            "e => e.getAnimations({ subtree: true }).filter(a => a.effect?.getTiming().iterations === Infinity).length"));
        Assert.Equal(0, await region.EvaluateAsync<int>(
            "e => e.querySelectorAll('[data-uc-live-anim],[data-uc-live-antes],[data-uc-live-delta],[data-uc-live-paso]').length"));
        await Expect(region).ToHaveAttributeAsync("data-uc-live-n", "2");
        var cls = await visitor.EvaluateAsync<double>("() => window.__cls");
        output.WriteLine($"{width}x{height}: CLS={cls}");
        Assert.True(cls < 0.01, $"CLS={cls}");
        await AssertIdle(visitor);
        Assert.False(await visitor.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > window.innerWidth"));

        // MOVIMIENTO REDUCIDO. La misma pantalla, sin un solo viaje: el estado sigue llegando, la
        // etapa sigue cambiando de superficie y "es tu turno" sigue destacando estando quieto.
        await using var quietContext = await fixture.Browser.NewContextAsync(new()
        { ViewportSize = new() { Width = 390, Height = 844 }, ReducedMotion = ReducedMotion.Reduce });
        var quiet = await quietContext.NewPageAsync();
        await quiet.GotoAsync($"{fixture.BaseUrl}/seguimiento/turnos/{trackingCode}");
        var quietRegion = quiet.Locator("[data-live-state='turno']");
        await Expect(quietRegion).ToHaveAttributeAsync("data-live-etapa", "llamado", new() { Timeout = 30_000 });
        await Expect(quiet.GetByRole(AriaRole.Heading, new() { Name = "¡Es tu turno!" })).ToBeVisibleAsync();
        await Expect(quietRegion).ToHaveCSSAsync("background-color", "rgb(255, 91, 71)");
        await Expect(quiet.GetByTestId("push-prompt")).ToBeVisibleAsync();

        await operations.GetByRole(AriaRole.Button, new() { Name = "Iniciar atención" }).ClickAsync();
        await Expect(quietRegion).ToHaveAttributeAsync("data-live-etapa", "atencion", new() { Timeout = 20_000 });
        await Expect(quiet.GetByRole(AriaRole.Heading, new() { Name = "Te están atendiendo" })).ToBeVisibleAsync();
        await Expect(quietRegion).ToHaveAttributeAsync("data-uc-live-n", "1");
        Assert.Equal(0, await quietRegion.EvaluateAsync<int>(
            "e => e.getAnimations({ subtree: true }).filter(a => a.playState === 'running').length"));
        Assert.Equal(0, await quietRegion.EvaluateAsync<int>(
            "e => e.querySelectorAll('[data-uc-live-anim],[data-uc-live-antes],[data-uc-live-paso]').length"));

        // La fila queda como estaba: ninguna prueba de esta clase hereda turnos de otra.
        await operations.GetByRole(AriaRole.Button, new() { Name = "Completar" }).ClickAsync();
        await Expect(region).ToHaveAttributeAsync("data-live-etapa", "cerrado");
        await Expect(visitor.GetByRole(AriaRole.Heading, new() { Name = "Atendido", Exact = true })).ToBeVisibleAsync();
        await Capture(visitor, "atendido", width, height);
        await Expect(operations.GetByRole(AriaRole.Button, new() { Name = "No hay nadie esperando" })
            .Or(operations.GetByRole(AriaRole.Button, new() { Name = "Llamar siguiente" }))).ToBeVisibleAsync();
        Assert.True(accessibilityFailures.Count == 0, string.Join(Environment.NewLine, accessibilityFailures));
    }

    /// <summary>
    /// Tres cambios seguidos más rápidos que la animación de uno. Lo que se comprueba es que la
    /// pantalla no acaba mostrando un valor viejo porque una animación quedó pendiente: manda el
    /// último estado, siempre, y las animaciones no se acumulan una detrás de otra.
    /// </summary>
    [Fact]
    public async Task Rapid_updates_never_leave_a_stale_value_on_screen()
    {
        using var api = new HttpClient { BaseAddress = new Uri(fixture.BaseUrl) };
        using var legal = await api.GetAsync("/api/v1/public/legal");
        legal.EnsureSuccessStatusCode();
        using var legalJson = System.Text.Json.JsonDocument.Parse(await legal.Content.ReadAsStringAsync());
        var policy = legalJson.RootElement.GetProperty("policyVersion").GetString()!;

        await using var context = await PhoneContext();
        var board = await context.NewPageAsync();
        await board.GotoAsync($"{fixture.BaseUrl}/negocios/barberia-el-corte/turnos");
        await Expect(board.GetByRole(AriaRole.Heading, new() { Name = "Atención general" })).ToBeVisibleAsync();
        // La segunda cifra del tablero es la que cuenta gente esperando.
        var esperando = board.Locator(".fila-cifras [data-live-value]").Nth(1);
        await Expect(esperando).ToBeVisibleAsync();
        var partida = int.Parse(await esperando.InnerTextAsync());
        await board.EvaluateAsync("""
            () => {
              window.__realChanges = [];
              const value = document.querySelectorAll('.fila-cifras [data-live-value]')[1];
              new MutationObserver(() => window.__realChanges.push({ value: value.textContent, at: performance.now() }))
                .observe(value, { attributes: true, attributeFilter: ['data-uc-live-visto'] });
            }
            """);

        // La ráfaga viene del backend real —tres turnos públicos, uno detrás de otro— y no de un
        // temporizador que finge movimiento.
        var codes = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            using var joined = await api.PostAsJsonAsync("/api/v1/public/businesses/barberia-el-corte/queue/tickets",
                new CreateQueueTicketRequest { Alias = $"Rafaga {i}", ConsentAccepted = true, ConsentNoticeVersion = policy });
            joined.EnsureSuccessStatusCode();
            using var body = System.Text.Json.JsonDocument.Parse(await joined.Content.ReadAsStringAsync());
            codes.Add(body.RootElement.GetProperty("trackingCode").GetString()!);
            await Expect(esperando).ToHaveTextAsync((partida + i + 1).ToString());
        }

        var esperado = (partida + 3).ToString();
        await Expect(esperando).ToHaveTextAsync(esperado, new() { Timeout = 20_000 });
        // Lo que la pantalla dice y lo que el motor cree que dice son lo mismo: no hay un valor
        // esperando su turno en ninguna cola.
        Assert.Equal(esperado, await esperando.GetAttributeAsync("data-uc-live-visto"));
        Assert.True(int.Parse(await esperando.GetAttributeAsync("data-uc-live-n") ?? "0") <= 3);
        Assert.True(await esperando.EvaluateAsync<int>(
            "e => e.getAnimations().filter(a => a.playState === 'running').length") <= 1);
        Assert.Equal("sube", await esperando.GetAttributeAsync("data-uc-live-sentido"));
        output.WriteLine("Real burst: " + await board.EvaluateAsync<string>("JSON.stringify(window.__realChanges)"));

        await board.WaitForTimeoutAsync(1_200);
        Assert.Equal(esperado, await esperando.InnerTextAsync());
        Assert.Null(await esperando.GetAttributeAsync("data-uc-live-antes"));
        Assert.Equal(0, await esperando.EvaluateAsync<int>(
            "e => e.getAnimations().filter(a => a.playState === 'running').length"));

        // La fila vuelve a como estaba, y de paso la bajada se cuenta igual que la subida.
        foreach (var code in codes)
        {
            using var tracked = await api.GetAsync($"/api/v1/public/queue/tickets/{code}");
            tracked.EnsureSuccessStatusCode();
            using var body = System.Text.Json.JsonDocument.Parse(await tracked.Content.ReadAsStringAsync());
            using var cancelled = await api.PostAsJsonAsync($"/api/v1/public/queue/tickets/{code}/cancel",
                new QueueSessionCommandRequest { Version = body.RootElement.GetProperty("version").GetInt64() });
            cancelled.EnsureSuccessStatusCode();
        }
        await RecordState("after cancelling burst", board);
        await Expect(esperando).ToHaveTextAsync(partida.ToString(), new() { Timeout = 20_000 });
        Assert.Equal("baja", await esperando.GetAttributeAsync("data-uc-live-sentido"));
        await AssertIdle(board);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Quiet_preferences_keep_real_queue_updates_instant(bool saveData)
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        { ViewportSize = new() { Width = 390, Height = 844 },
            ReducedMotion = saveData ? ReducedMotion.NoPreference : ReducedMotion.Reduce });
        if (saveData) await context.AddInitScriptAsync(
            "Object.defineProperty(navigator.connection, 'saveData', {get: () => true});");
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{fixture.BaseUrl}/negocios/barberia-el-corte/turnos");
        var count = page.Locator(".fila-cifras [data-live-value]").Nth(1);
        await Expect(count).ToBeVisibleAsync();
        var before = int.Parse(await count.InnerTextAsync());
        using var api = new HttpClient { BaseAddress = new Uri(fixture.BaseUrl) };
        var legal = (await api.GetFromJsonAsync<LegalInfoDto>("/api/v1/public/legal"))!;
        using var response = await api.PostAsJsonAsync("/api/v1/public/businesses/barberia-el-corte/queue/tickets",
            new CreateQueueTicketRequest { Alias = "Quiet", ConsentAccepted = true, ConsentNoticeVersion = legal.PolicyVersion });
        response.EnsureSuccessStatusCode();
        var ticket = (await response.Content.ReadFromJsonAsync<QueueTicketCreatedDto>())!;
        await Expect(count).ToHaveTextAsync((before + 1).ToString());
        Assert.Null(await count.GetAttributeAsync("data-uc-live-anim"));
        Assert.Null(await count.GetAttributeAsync("data-uc-live-antes"));
        Assert.Equal(0, await page.Locator("[data-live-state='fila']").EvaluateAsync<int>(
            "e => e.getAnimations({subtree:true}).filter(a => a.playState === 'running').length"));
        using var cancel = await api.PostAsJsonAsync($"/api/v1/public/queue/tickets/{ticket.TrackingCode}/cancel",
            new QueueSessionCommandRequest { Version = 0 });
        cancel.EnsureSuccessStatusCode();
        await Expect(count).ToHaveTextAsync(before.ToString());
        await AssertIdle(page);
    }

    private async Task RecordState(string step, IPage page, string? code = null)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString).Options);
        var tickets = await db.QueueTickets.AsNoTracking()
            .Where(t => t.BusinessId == DevelopmentSeeder.CorteBusinessId).OrderBy(t => t.Number)
            .Select(t => new { t.Number, t.Status, t.Version }).ToListAsync();
        using var api = new HttpClient { BaseAddress = new Uri(fixture.BaseUrl) };
        var path = code is null ? "/api/v1/public/businesses/barberia-el-corte/queue"
            : $"/api/v1/public/queue/tickets/{code}";
        output.WriteLine($"{step}\nDB: {System.Text.Json.JsonSerializer.Serialize(tickets)}\nAPI: {await api.GetStringAsync(path)}\nRazor: {await page.Locator(".fila-tablero").First.InnerTextAsync()}");
    }

    /// <summary>Un turno completo desde el panel: llamar, atender y cerrar.</summary>
    private static async Task AttendOne(IPage operations)
    {
        await operations.GetByRole(AriaRole.Button, new() { Name = "Llamar siguiente" }).ClickAsync();
        await operations.GetByRole(AriaRole.Button, new() { Name = "Iniciar atención" }).ClickAsync();
        var serving = operations.Locator(".queue-row").Filter(new()
        { Has = operations.GetByRole(AriaRole.Button, new() { Name = "Completar", Exact = true }) });
        var id = await serving.GetAttributeAsync("id");
        await operations.GetByRole(AriaRole.Button, new() { Name = "Completar" }).ClickAsync();
        await Expect(operations.Locator($"#{id}")).ToHaveCountAsync(0);
    }

    private async Task Capture(IPage page, string stage, int width, int height)
    {
        await page.EvaluateAsync("() => document.fonts.ready");
        await page.WaitForTimeoutAsync(1_100);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "UrabaConecta.slnx"))) root = root.Parent;
        var directory = Path.Combine(root!.FullName, "artifacts", "j-motion-05");
        Directory.CreateDirectory(directory);
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{width}x{height}-{stage}.png"), FullPage = true });
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
        var failures = await page.EvaluateAsync<string[]>(ContrastAccessibilityTests.Medidor
            .Replace("document.querySelectorAll('body *')", "document.querySelectorAll('[data-live-state] *')"));
        accessibilityFailures.AddRange(failures.Select(f => $"{stage} {width}x{height}: {f}"));
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await page.WaitForTimeoutAsync(350);
        var darkFailures = await page.EvaluateAsync<string[]>(ContrastAccessibilityTests.Medidor
            .Replace("document.querySelectorAll('body *')", "document.querySelectorAll('[data-live-state] *')"));
        accessibilityFailures.AddRange(darkFailures.Select(f => $"dark {stage} {width}x{height}: {f}"));
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
    }

    private async Task AssertIdle(IPage page)
    {
        await page.WaitForTimeoutAsync(1_200);
        await page.EvaluateAsync("""
            () => {
              window.__idleMutations = 0; window.__idleRaf = 0;
              window.__originalRaf = window.requestAnimationFrame;
              window.requestAnimationFrame = cb => { window.__idleRaf++; return window.__originalRaf.call(window, cb); };
              window.__idleObserver = new MutationObserver(m => window.__idleMutations += m.length);
              document.querySelectorAll('[data-live-state]').forEach(e =>
                window.__idleObserver.observe(e, { subtree: true, childList: true, characterData: true, attributes: true }));
            }
            """);
        await page.WaitForTimeoutAsync(3_000);
        var readings = await page.EvaluateAsync<int[]>("""
            () => {
              window.__idleObserver.disconnect(); window.requestAnimationFrame = window.__originalRaf;
              const regions = [...document.querySelectorAll('[data-live-state]')];
              return [window.__idleMutations, window.__idleRaf,
                regions.flatMap(e => e.getAnimations({ subtree:true })).filter(a => a.playState === 'running').length,
                regions.flatMap(e => e.getAnimations({ subtree:true })).filter(a => a.effect?.getTiming().iterations === Infinity).length,
                document.querySelectorAll('[data-uc-live-anim],[data-uc-live-antes],[data-uc-live-delta],[data-uc-live-paso]').length];
            }
            """);
        output.WriteLine($"Idle mutations/rAF/running/infinite/transient: {string.Join('/', readings)}");
        Assert.All(readings, n => Assert.Equal(0, n));
    }

    private async Task<IBrowserContext> MobileContext() => await fixture.Browser.NewContextAsync(new()
    { ViewportSize = new() { Width = 360, Height = 800 } });
    /// <summary>El teléfono en la mano mientras se espera: donde ocurre de verdad este recorrido.</summary>
    private async Task<IBrowserContext> PhoneContext() => await fixture.Browser.NewContextAsync(new()
    { ViewportSize = new() { Width = 390, Height = 844 } });
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
