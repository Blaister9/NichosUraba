using System.Text.RegularExpressions;
using Microsoft.Playwright;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.EndToEndTests;

/// <summary>
/// La pregunta que estas pruebas contestan no es "¿es instalable?" sino "¿una persona que abre
/// UrabáConecta en Android descubre cómo instalarla sin conocer el menú del navegador?".
///
/// Por eso todo corre con el agente de un Honor real y se comprueba el texto que se lee en
/// pantalla, no la existencia del manifiesto. Los dos caminos del navegador se provocan a
/// propósito: con beforeinstallprompt disponible y sin él, que es el caso que motivó el trabajo.
/// </summary>
public sealed class PwaInstallJourneyTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    /// <summary>Un Honor con Chrome, que es el dispositivo donde se detectó el problema.</summary>
    private const string AgenteAndroid =
        "Mozilla/5.0 (Linux; Android 13; HONOR X9a) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/120.0.0.0 Mobile Safari/537.36";

    /// <summary>
    /// Chrome Android sin oferta del navegador: el camino manual tiene que estar escrito, y con
    /// las palabras que aparecen en ESE menú.
    /// </summary>
    [Fact]
    public async Task Chrome_android_without_native_offer_shows_the_manual_route()
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
        // Las dos palabras que Chrome usa en Android, porque el rótulo cambia según la versión.
        await Expect(pasos).ToContainTextAsync("Instalar aplicación");
        await Expect(pasos).ToContainTextAsync("pantalla de inicio");

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
        await page.GotoAsync($"{fixture.BaseUrl}/seguimiento");

        var boton = page.GetByTestId("app-status-install");
        await Expect(boton).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(boton).ToHaveTextAsync("Instalar");
        Assert.Equal("native", await page.EvaluateAsync<string>("urabaApp.install.state().mode"));

        await boton.ClickAsync();
        await Expect(page.GetByTestId("app-status-app-valor")).ToHaveTextAsync("Instalada");
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

        await page.GotoAsync($"{fixture.BaseUrl}/seguimiento");
        await Expect(page.GetByTestId("app-status-app-valor")).ToHaveTextAsync("Instalada",
            new() { Timeout = 30_000 });
        Assert.True(await page.EvaluateAsync<bool>("urabaApp.install.state().runningAsApp"));
        await Expect(page.GetByTestId("app-status-install")).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("install-invite")).ToHaveCountAsync(0);

        await Login(page, DevelopmentSeeder.CorteOwnerEmail);
        await Expect(page.GetByTestId("app-status-app-valor")).ToHaveTextAsync("Instalada",
            new() { Timeout = 30_000 });
        await Expect(page.GetByTestId("install-invite")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Recordar una instalación no es estar dentro de la aplicación. La marca de localStorage
    /// sobrevive a desinstalar, y antes bastaba para borrar el botón del DOM y dejar la pantalla
    /// sin ninguna salida. El rótulo puede decir "Instalada"; el camino tiene que seguir ahí.
    /// </summary>
    [Fact]
    public async Task A_remembered_install_still_offers_the_manual_route_in_a_browser_tab()
    {
        await using var context = await AndroidContext();
        await context.AddInitScriptAsync(
            "try { localStorage.setItem('urabaAppInstalada', '1'); } catch {}");
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{fixture.BaseUrl}/seguimiento");
        await Expect(page.GetByTestId("app-status")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        Assert.False(await page.EvaluateAsync<bool>("urabaApp.install.state().runningAsApp"));

        var boton = page.GetByTestId("app-status-install");
        await Expect(boton).ToBeVisibleAsync();
        await Expect(boton).ToHaveTextAsync("Cómo instalarla");
        await boton.ClickAsync();
        await Expect(page.GetByTestId("app-status-steps")).ToContainTextAsync("Instalar aplicación");
    }

    /// <summary>
    /// La causa del fallo en el Honor: Chrome de Android pone referrer "android-app://" a cualquier
    /// enlace abierto desde otra aplicación —WhatsApp, el correo—, y lo tomábamos por "ya está
    /// instalada". Quien llegaba a la Demo desde un mensaje no veía ningún botón.
    /// </summary>
    [Fact]
    public async Task A_link_opened_from_another_android_app_still_offers_installation()
    {
        await using var context = await AndroidContext();
        await context.AddInitScriptAsync(GuionReferrerDeApp);
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{fixture.BaseUrl}/seguimiento");
        await Expect(page.GetByTestId("app-status")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        Assert.Equal("android-app://com.whatsapp/",
            await page.EvaluateAsync<string>("document.referrer"));
        Assert.Equal("manual", await page.EvaluateAsync<string>("urabaApp.install.state().mode"));
        await Expect(page.GetByTestId("app-status-app-valor")).ToHaveTextAsync("Instalar");
        await Expect(page.GetByTestId("app-status-install")).ToHaveTextAsync("Cómo instalarla");
    }

    /// <summary>
    /// El "ahora no" silencia la invitación contextual durante catorce días. La ficha permanente de
    /// Mi actividad no se toca: es donde se va a mirar el estado a propósito.
    /// </summary>
    [Fact]
    public async Task Dismissal_silences_the_invitation_but_never_the_status_card()
    {
        await using var context = await AndroidContext();
        await context.AddInitScriptAsync(
            "try { localStorage.setItem('urabaInstalarDescartada', String(Date.now())); } catch {}");
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{fixture.BaseUrl}/seguimiento");
        await Expect(page.GetByTestId("app-status")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        Assert.True(await page.EvaluateAsync<bool>("urabaApp.install.state().dismissed"));
        await Expect(page.GetByTestId("install-invite")).ToHaveCountAsync(0);

        // Lo que no puede pasar: que el descarte se lleve por delante la ficha y su CTA.
        await Expect(page.GetByTestId("app-status-app-valor")).ToHaveTextAsync("Instalar");
        await Expect(page.GetByTestId("app-status-install")).ToHaveTextAsync("Cómo instalarla");
        await Expect(page.GetByTestId("app-status-install")).ToBeVisibleAsync();
    }

    /// <summary>
    /// Visibilidad física, no sólo presencia en el DOM: el control tiene que medir algo, verse y no
    /// estar tapado, en la pantalla de un teléfono y sin haber hecho ninguna operación antes.
    /// </summary>
    [Fact]
    public async Task The_install_control_is_actually_visible_on_a_phone()
    {
        await using var context = await AndroidContext();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{fixture.BaseUrl}/seguimiento");
        var boton = page.GetByTestId("app-status-install");
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
        await Expect(invitacion).ToContainTextAsync("Ten tus turnos, citas y pedidos a mano");

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
    /// Llegar desde otra aplicación de Android. Es lo que hace Chrome con cualquier enlace tocado
    /// dentro de WhatsApp o del correo, y no distingue de un TWA por el referrer.
    /// </summary>
    private const string GuionReferrerDeApp = """
        try {
          Object.defineProperty(document, 'referrer',
            { get: () => 'android-app://com.whatsapp/', configurable: true });
        } catch {}
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
            UserAgent = AgenteAndroid
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
