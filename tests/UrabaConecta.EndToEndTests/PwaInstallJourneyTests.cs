using System.Text.RegularExpressions;
using Microsoft.Playwright;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.EndToEndTests;

/// <summary>
/// Comprueba la experiencia de instalación que ve una persona en un teléfono: oferta nativa,
/// alternativa manual mínima, descarte persistente y ausencia de insistencia si ya está instalada.
/// </summary>
public sealed class PwaInstallJourneyTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    private const string AgenteMovil =
        "Mozilla/5.0 (Linux; Android 13; Mobile) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/120.0.0.0 Mobile Safari/537.36";

    /// <summary>
    /// Sin oferta programática no aparece un botón que finja abrir el instalador. Sólo queda la
    /// instrucción corta del menú que el dispositivo sí permite usar.
    /// </summary>
    [Fact]
    public async Task Mobile_browser_without_native_offer_shows_only_the_manual_route()
    {
        await using var context = await AndroidContext();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{fixture.BaseUrl}/seguimiento");

        var estado = page.GetByTestId("app-status");
        await Expect(estado).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(page.GetByTestId("app-status-app-valor")).ToHaveTextAsync("Instalar");

        await page.GetByTestId("app-status-install").ClickAsync();
        var pasos = page.GetByTestId("app-status-steps");
        await Expect(pasos).ToBeVisibleAsync();
        await Expect(page.GetByTestId("install-native")).ToHaveCountAsync(0);
        await Expect(pasos).ToContainTextAsync("Instalar aplicación");
        await Expect(pasos).ToContainTextAsync("pantalla de inicio");
        await Expect(pasos.Locator("li")).ToHaveCountAsync(1);

        // Nada de esto puede empujar la pantalla a lo ancho en un teléfono.
        Assert.False(await page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > window.innerWidth"));
    }

    /// <summary>
    /// Con la oferta del navegador disponible, el botón abre el diálogo nativo. Se comprueba que
    /// prompt() se llama de verdad: es la parte que se rompe si el clic viaja al servidor y
    /// vuelve fuera del gesto de la persona.
    /// </summary>
    [Fact]
    public async Task Native_offer_is_captured_and_the_button_opens_the_browser_dialog()
    {
        await using var context = await AndroidContext();
        await context.AddInitScriptAsync(GuionOfertaNativa);
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl);

        var invitacion = page.GetByTestId("install-invite");
        await Expect(invitacion).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(invitacion).ToContainTextAsync("Instalar UrabáConecta");
        await Expect(invitacion).ToContainTextAsync("Ten tus pedidos y seguimiento a la mano.");
        var boton = invitacion.GetByTestId("install-native");
        await Expect(boton).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(boton).ToHaveTextAsync("Instalar UrabáConecta");
        Assert.Equal("native", await page.EvaluateAsync<string>("urabaApp.install.state().mode"));

        await boton.ClickAsync();
        await Expect(invitacion).ToHaveCountAsync(0);
        Assert.True(await page.EvaluateAsync<bool>("window.__dialogoAbierto === true"));
    }

    /// <summary>
    /// Corriendo de verdad como aplicación —el navegador lo dice por display-mode— no hay nada que
    /// ofrecer: ya está dentro. Éste es el único caso en que el botón desaparece.
    /// </summary>
    [Fact]
    public async Task Running_as_the_installed_app_hides_the_call_to_action()
    {
        await using var context = await AndroidContext();
        await context.AddInitScriptAsync(GuionModoApp);
        var page = await context.NewPageAsync();

        await page.GotoAsync(fixture.BaseUrl);
        await Expect(page.GetByTestId("install-invite")).ToHaveCountAsync(0);
        await page.GotoAsync($"{fixture.BaseUrl}/seguimiento");
        await Expect(page.GetByTestId("app-status-app-valor")).ToHaveTextAsync("Instalada",
            new() { Timeout = 30_000 });
        Assert.True(await page.EvaluateAsync<bool>("urabaApp.install.state().runningAsApp"));
        await Expect(page.GetByTestId("app-status-install")).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("install-invite")).ToHaveCountAsync(0);

    }

    /// <summary>
    /// Una instalación aceptada se recuerda también al volver desde una pestaña: no se repite la
    /// invitación ni queda otro CTA de instalación en Mi actividad.
    /// </summary>
    [Fact]
    public async Task A_remembered_install_hides_redundant_installation_calls_to_action()
    {
        await using var context = await AndroidContext();
        await context.AddInitScriptAsync(
            "try { localStorage.setItem('urabaAppInstalada', '1'); } catch {}");
        var page = await context.NewPageAsync();

        await page.GotoAsync(fixture.BaseUrl);
        await Expect(page.GetByTestId("install-invite")).ToHaveCountAsync(0);
        await page.GotoAsync($"{fixture.BaseUrl}/seguimiento");
        await Expect(page.GetByTestId("app-status-app-valor")).ToHaveTextAsync("Instalada",
            new() { Timeout = 30_000 });
        await Expect(page.GetByTestId("app-status-install")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// El "ahora no" silencia la invitación durante catorce días y tampoco reaparece como otro CTA
    /// al navegar inmediatamente a Mi actividad.
    /// </summary>
    [Fact]
    public async Task Dismissal_silences_installation_across_immediate_navigation()
    {
        await using var context = await AndroidContext();
        await context.AddInitScriptAsync(GuionOfertaNativa);
        var page = await context.NewPageAsync();

        await page.GotoAsync(fixture.BaseUrl);
        var invitacion = page.GetByTestId("install-invite");
        await Expect(invitacion).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await invitacion.GetByTestId("install-dismiss").ClickAsync();
        Assert.True(await page.EvaluateAsync<bool>("urabaApp.install.state().dismissed"));
        await Expect(page.GetByTestId("install-invite")).ToHaveCountAsync(0);

        await page.GotoAsync($"{fixture.BaseUrl}/seguimiento");
        await Expect(page.GetByTestId("app-status")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(page.GetByTestId("app-status-install")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Visibilidad física, no sólo presencia en el DOM: el control tiene que medir algo, verse y no
    /// estar tapado, en la pantalla de un teléfono y sin haber hecho ninguna operación antes.
    /// </summary>
    [Fact]
    public async Task The_install_control_is_actually_visible_on_a_phone()
    {
        await using var context = await AndroidContext();
        await context.AddInitScriptAsync(GuionOfertaNativa);
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl);
        var boton = page.GetByTestId("install-native");
        await Expect(boton).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var medida = await boton.EvaluateAsync<Medida>("""
            boton => {
              const c = getComputedStyle(boton), r = boton.getBoundingClientRect();
              const centro = document.elementFromPoint(
                Math.round(r.left + r.width / 2), Math.round(r.top + r.height / 2));
              return {
                ancho: r.width, alto: r.height, opacidad: parseFloat(c.opacity),
                visibilidad: c.visibility, presentacion: c.display,
                color: c.color, fondo: c.backgroundColor,
                propio: boton === centro || boton.contains(centro)
              };
            }
            """);
        // Objetivo táctil real, no un rastro de 1 px.
        Assert.True(medida.Ancho >= 88, $"ancho {medida.Ancho}");
        Assert.True(medida.Alto >= 36, $"alto {medida.Alto}");
        Assert.Equal(1d, medida.Opacidad);
        Assert.Equal("visible", medida.Visibilidad);
        Assert.NotEqual("none", medida.Presentacion);
        // Texto SELVA sobre fondo SELVA: el fallo que no se ve en una captura de DOM.
        Assert.NotEqual(medida.Color, medida.Fondo);
        Assert.True(medida.Propio, "algo tapa el botón en el punto donde se toca");
    }

    private sealed record Medida
    {
        public double Ancho { get; init; }
        public double Alto { get; init; }
        public double Opacidad { get; init; }
        public string Visibilidad { get; init; } = "";
        public string Presentacion { get; init; } = "";
        public string Color { get; init; } = "";
        public string Fondo { get; init; } = "";
        public bool Propio { get; init; }
    }

    /// <summary>
    /// Quien opera el negocio recibe la invitación destacada y sin salida de escape: para esa
    /// persona la aplicación instalada con avisos es la herramienta, no un extra.
    /// </summary>
    [Fact]
    public async Task Owner_gets_a_prominent_invitation_that_cannot_be_postponed()
    {
        await using var context = await AndroidContext();
        var page = await context.NewPageAsync();
        await Login(page, DevelopmentSeeder.CorteOwnerEmail);

        var invitacion = page.GetByTestId("install-invite");
        await Expect(invitacion).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(invitacion).ToContainTextAsync("Instalar UrabáConecta");
        await Expect(invitacion).ToHaveClassAsync(new Regex("is-destacada"));
        await Expect(invitacion.GetByTestId("install-dismiss")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Al cliente se le sugiere después de tomar el turno —no al entrar— y puede posponerlo. El
    /// "ahora no" tiene que sobrevivir a una recarga o no es una decisión, es un parpadeo.
    /// </summary>
    [Fact]
    public async Task Client_invitation_appears_after_taking_a_turn_and_can_be_postponed()
    {
        await using var context = await AndroidContext();
        var page = await context.NewPageAsync();

        // Al entrar, sin haber hecho nada, no hay invitación: eso es lo que separa "contextual"
        // de "ventana emergente al primer segundo".
        await page.GotoAsync($"{fixture.BaseUrl}/negocios/barberia-el-corte/turnos");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Atención general" }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(page.GetByTestId("install-invite")).ToHaveCountAsync(0);

        await page.GetByLabel("Alias corto (opcional)").FillAsync("PWA");
        await page.GetByLabel("Acepto que este alias se use para gestionar mi turno y anunciar el llamado.")
            .CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Tomar turno" }).ClickAsync();
        await Expect(page.GetByTestId("queue-created")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Seguir mi turno" }).ClickAsync();

        var invitacion = page.GetByTestId("install-invite");
        await Expect(invitacion).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(invitacion).ToContainTextAsync("Instalar UrabáConecta");
        await Expect(invitacion).ToContainTextAsync("Ten tus pedidos y seguimiento a la mano");

        var seguimiento = page.Url;
        await invitacion.GetByTestId("install-dismiss").ClickAsync();
        await Expect(invitacion).ToHaveCountAsync(0);

        await page.GotoAsync(seguimiento);
        await Expect(page.GetByRole(AriaRole.Heading).First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(page.GetByTestId("install-invite")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Permiso concedido y permiso bloqueado. Lo que importa del segundo es que no queda ningún
    /// botón insistiendo y que se dice, con todas las letras, que no se pierde nada.
    /// </summary>
    [Fact]
    public async Task Notification_permission_is_reported_without_asking_and_without_insisting()
    {
        await using var concedido = await AndroidContext();
        await concedido.AddInitScriptAsync(GuionPermiso("granted"));
        var otorgada = await concedido.NewPageAsync();
        await otorgada.GotoAsync($"{fixture.BaseUrl}/seguimiento");
        // EVIDENCIA PRE-FIX QUE REPRODUCE: permission=granted se mostraba como "Activas" aunque
        // no hubiera PushSubscription persistida para ninguna operación.
        await Expect(otorgada.GetByTestId("app-status-push-valor")).ToHaveTextAsync("Permitidas",
            new() { Timeout = 30_000 });
        await Expect(otorgada.GetByTestId("app-status-push-enable")).ToHaveCountAsync(0);

        await using var bloqueado = await AndroidContext();
        await bloqueado.AddInitScriptAsync(GuionPermiso("denied"));
        var denegada = await bloqueado.NewPageAsync();
        await denegada.GotoAsync($"{fixture.BaseUrl}/seguimiento");
        await Expect(denegada.GetByTestId("app-status-push-valor")).ToHaveTextAsync("Bloqueadas",
            new() { Timeout = 30_000 });
        await Expect(denegada.GetByTestId("app-status-push-enable")).ToHaveCountAsync(0);
        await Expect(denegada.GetByTestId("app-status-push")).ToContainTextAsync("Nada deja de funcionar");

        // Y en la pantalla de un negocio, donde el aviso sí se ofrece, tampoco queda botón.
        await denegada.GotoAsync($"{fixture.BaseUrl}/negocios/barberia-el-corte");
        await Expect(denegada.GetByTestId("push-blocked").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(denegada.GetByTestId("push-toggle")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Nadie pide el permiso del navegador por abrir una página. El diálogo sólo puede aparecer
    /// después de que la persona toque algo.
    /// </summary>
    [Fact]
    public async Task Permission_is_never_requested_on_load()
    {
        await using var context = await AndroidContext();
        await context.AddInitScriptAsync(GuionEspiaDePermiso);
        var page = await context.NewPageAsync();

        foreach (var ruta in new[] { "/", "/seguimiento", "/negocios/barberia-el-corte",
                                     "/negocios/barberia-el-corte/turnos" })
        {
            await page.GotoAsync($"{fixture.BaseUrl}{ruta}");
            await Expect(page.GetByRole(AriaRole.Heading).First)
                .ToBeVisibleAsync(new() { Timeout = 30_000 });
            Assert.Equal(0, await page.EvaluateAsync<int>("window.__vecesQuePidioPermiso ?? 0"));
        }
    }

    /* ------------------------------------------------------------------------------------- */

    /// <summary>
    /// Falsifica la oferta del navegador. Se dispara dentro de "load" para que pwa.js ya tenga su
    /// escucha puesta, que es exactamente el orden en el que ocurre en Chrome de verdad.
    /// </summary>
    private const string GuionOfertaNativa = """
        window.__dialogoAbierto = false;
        window.addEventListener('load', () => {
          const evento = new Event('beforeinstallprompt');
          evento.prompt = () => { window.__dialogoAbierto = true; };
          evento.userChoice = Promise.resolve({ outcome: 'accepted' });
          window.dispatchEvent(evento);
        });
        """;

    /// <summary>
    /// Finge que la pestaña ES la aplicación. Playwright no emula display-mode, así que se
    /// interviene matchMedia y se deja intacto todo lo demás.
    /// </summary>
    private const string GuionModoApp = """
        const originalMatchMedia = window.matchMedia.bind(window);
        window.matchMedia = consulta => /display-mode:\s*standalone/.test(consulta)
          ? { matches: true, media: consulta, onchange: null,
              addEventListener() {}, removeEventListener() {},
              addListener() {}, removeListener() {}, dispatchEvent() { return false; } }
          : originalMatchMedia(consulta);
        """;

    /// <summary>
    /// Fija el permiso del navegador. Hay que falsificarlo en los dos sentidos: Chromium sin
    /// interfaz responde "denied" a las notificaciones haga lo que haga el permiso concedido del
    /// contexto, así que conceder por la vía de Playwright no probaría nada. Lo que se comprueba
    /// aquí es nuestra traducción del permiso a lo que se lee en pantalla.
    /// </summary>
    private static string GuionPermiso(string valor) => $$"""
        try {
          Object.defineProperty(Notification, 'permission',
            { get: () => '{{valor}}', configurable: true });
          Notification.requestPermission = () => Promise.resolve('{{valor}}');
        } catch {}
        """;

    private const string GuionEspiaDePermiso = """
        window.__vecesQuePidioPermiso = 0;
        const original = Notification.requestPermission.bind(Notification);
        Notification.requestPermission = (...args) => {
          window.__vecesQuePidioPermiso++;
          return original(...args);
        };
        """;

    private async Task<IBrowserContext> AndroidContext() =>
        await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 360, Height = 800 },
            UserAgent = AgenteMovil
        });

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
