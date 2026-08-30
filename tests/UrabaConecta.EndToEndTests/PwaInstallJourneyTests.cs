using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.EndToEndTests;

/// <summary>
/// Comprueba la experiencia de instalación que ve una persona en teléfono y escritorio: oferta
/// nativa, alternativas manuales, descarte temporal y ausencia de falsos positivos de instalación.
/// </summary>
public sealed class PwaInstallJourneyTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    private const string AgenteMovil =
        "Mozilla/5.0 (Linux; Android 13; Mobile) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/120.0.0.0 Mobile Safari/537.36";

    private const string AgenteIos =
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_4 like Mac OS X) AppleWebKit/605.1.15 " +
        "(KHTML, like Gecko) Version/17.4 Mobile/15E148 Safari/604.1";

    /// <summary>
    /// La ventana de recuperación de pwa.js (ESPERA_INSTALACION_MS) son 12 s reales. Los tests la
    /// dejan correr de verdad en vez de falsificar el reloj: intervenir setTimeout en una página de
    /// Blazor Server también intervendría los latidos de SignalR, y entonces lo que se estaría
    /// midiendo sería el reloj falso y no la recuperación.
    /// </summary>
    private const float VentanaDeRecuperacion = 12_000;

    /// <summary>Tope para esperar el vencimiento, con margen sobre la ventana.</summary>
    private const float TopeDeRecuperacion = 20_000;

    /// <summary>Margen que se deja correr POR ENCIMA de la ventana para probar una ausencia.</summary>
    private const float MasAllaDeLaRecuperacion = VentanaDeRecuperacion + 4_000;

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
        await Expect(pasos.Locator("li")).ToHaveCountAsync(2);

        // Nada de esto puede empujar la pantalla a lo ancho en un teléfono.
        Assert.False(await page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > window.innerWidth"));
    }

    [Fact]
    public async Task Ios_shows_share_and_add_to_home_instructions_without_an_android_prompt()
    {
        await using var context = await IosContext();
        await context.AddInitScriptAsync(GuionIos);
        var page = await context.NewPageAsync();

        await page.GotoAsync(fixture.BaseUrl);
        var invitacion = page.GetByTestId("install-invite");
        await Expect(invitacion).ToBeVisibleAsync(new() { Timeout = 30_000 });
        Assert.Equal("ios", await invitacion.GetAttributeAsync("data-plataforma"));
        await Expect(invitacion.GetByTestId("install-native")).ToHaveCountAsync(0);
        await Expect(invitacion.GetByTestId("install-steps")).ToContainTextAsync("Compartir");
        await Expect(invitacion.GetByTestId("install-steps")).ToContainTextAsync("Añadir a pantalla de inicio");
        await Expect(invitacion).Not.ToContainTextAsync("Instalar aplicación");
    }

    [Fact]
    public async Task Desktop_chromium_without_native_offer_keeps_a_visible_manual_route()
    {
        await using var context = await DesktopContext();
        var page = await context.NewPageAsync();

        await page.GotoAsync(fixture.BaseUrl);
        var invitacion = page.GetByTestId("install-invite");
        await Expect(invitacion).ToBeVisibleAsync(new() { Timeout = 30_000 });
        Assert.Equal("desktop", await invitacion.GetAttributeAsync("data-plataforma"));
        await Expect(invitacion.GetByTestId("install-native")).ToHaveCountAsync(0);
        await invitacion.GetByTestId("install-manual").ClickAsync();
        await Expect(invitacion.GetByTestId("install-steps")).ToContainTextAsync("menú de este navegador");
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
        await Expect(invitacion).ToContainTextAsync("Lleva UrabáConecta contigo");
        await Expect(invitacion).ToContainTextAsync("entra más rápido a tus citas, pedidos y negocios");
        var boton = invitacion.GetByTestId("install-native");
        await Expect(boton).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(boton).ToHaveTextAsync("Instalar");
        Assert.Equal("native", await page.EvaluateAsync<string>("urabaApp.install.state().mode"));

        await boton.ClickAsync();
        Assert.True(await page.EvaluateAsync<bool>("window.__dialogoAbierto === true"));
        await Expect(invitacion.GetByTestId("install-pending")).ToBeVisibleAsync();
        Assert.Equal("pending", await page.EvaluateAsync<string>("urabaApp.install.state().mode"));
        Assert.False(await page.EvaluateAsync<bool>("urabaApp.install.state().runningAsApp"));

        await page.EvaluateAsync("window.dispatchEvent(new Event('appinstalled'))");
        await Expect(invitacion).ToHaveCountAsync(0);
        Assert.Equal("installed", await page.EvaluateAsync<string>("urabaApp.install.state().mode"));
    }

    [Fact]
    public async Task Desktop_native_offer_uses_the_same_real_browser_dialog()
    {
        await using var context = await DesktopContext();
        await context.AddInitScriptAsync(GuionOfertaNativa);
        var page = await context.NewPageAsync();

        await page.GotoAsync(fixture.BaseUrl);
        var invitacion = page.GetByTestId("install-invite");
        await Expect(invitacion).ToBeVisibleAsync(new() { Timeout = 30_000 });
        Assert.Equal("desktop", await invitacion.GetAttributeAsync("data-plataforma"));

        await invitacion.GetByTestId("install-native").ClickAsync();
        Assert.True(await page.EvaluateAsync<bool>("window.__dialogoAbierto === true"));
        await Expect(invitacion.GetByTestId("install-pending")).ToBeVisibleAsync();
    }

    /// <summary>
    /// DEFECTO AUDITADO: aceptar el diálogo dejaba "pending" sin salida. Si appinstalled no llega
    /// nunca —el sistema cancela la instalación, o el navegador no emite el evento— la persona se
    /// quedaba en "Terminando la instalación…" sin botón, sin "Ahora no" y sin más recurso que
    /// recargar. La recuperación vence sola, no declara una instalación que nadie confirmó, y
    /// devuelve una acción y una salida.
    /// </summary>
    [Fact]
    public async Task Accepting_the_dialog_without_appinstalled_stops_being_pending_on_its_own()
    {
        await using var context = await AndroidContext();
        await context.AddInitScriptAsync(GuionOfertaNativa);
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl);

        var invitacion = page.GetByTestId("install-invite");
        await Expect(invitacion).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await invitacion.GetByTestId("install-native").ClickAsync();
        await Expect(invitacion.GetByTestId("install-pending")).ToBeVisibleAsync();
        Assert.Equal("pending", await page.EvaluateAsync<string>("urabaApp.install.state().mode"));

        // Nadie emite appinstalled. Lo único que pasa es el tiempo.
        await Expect(invitacion.GetByTestId("install-pending"))
            .ToHaveCountAsync(0, new() { Timeout = TopeDeRecuperacion });

        // No se inventa una instalación que no ocurrió, ni un descarte que nadie pidió.
        Assert.Equal("manual", await page.EvaluateAsync<string>("urabaApp.install.state().mode"));
        Assert.False(await page.EvaluateAsync<bool>("urabaApp.install.state().runningAsApp"));
        Assert.False(await page.EvaluateAsync<bool>("urabaApp.install.state().dismissed"));

        // Y la persona recupera con qué actuar y con qué cerrar, sin recargar nada.
        await Expect(invitacion).ToBeVisibleAsync();
        await Expect(invitacion.GetByTestId("install-manual")).ToBeVisibleAsync();
        var descarte = invitacion.GetByTestId("install-dismiss");
        await Expect(descarte).ToBeVisibleAsync();
        await descarte.ClickAsync();
        await Expect(page.GetByTestId("install-invite")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// El otro lado del mismo defecto: cuando appinstalled SÍ llega, manda esa señal y no el
    /// temporizador. La instalación queda confirmada, la invitación se va, y la recuperación no
    /// puede volver más tarde a contradecir ninguna de las dos cosas.
    /// </summary>
    [Fact]
    public async Task Appinstalled_during_pending_confirms_and_cancels_the_recovery_timer()
    {
        await using var context = await AndroidContext();
        await context.AddInitScriptAsync(GuionOfertaNativa);
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl);

        var invitacion = page.GetByTestId("install-invite");
        await Expect(invitacion).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await invitacion.GetByTestId("install-native").ClickAsync();
        await Expect(invitacion.GetByTestId("install-pending")).ToBeVisibleAsync();

        await page.EvaluateAsync("window.dispatchEvent(new Event('appinstalled'))");
        Assert.Equal("installed", await page.EvaluateAsync<string>("urabaApp.install.state().mode"));
        await Expect(page.GetByTestId("install-invite")).ToHaveCountAsync(0);

        // Pasada la ventana de recuperación, nada resucita la card ni el estado pendiente: el
        // temporizador se canceló al confirmarse la instalación.
        await page.WaitForTimeoutAsync(MasAllaDeLaRecuperacion);
        Assert.Equal("installed", await page.EvaluateAsync<string>("urabaApp.install.state().mode"));
        await Expect(page.GetByTestId("install-invite")).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("install-pending")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Invitation_waits_for_the_courtesy_moment_instead_of_competing_with_first_paint()
    {
        await using var context = await AndroidContext();
        await context.AddInitScriptAsync(GuionOfertaNativa);
        var page = await context.NewPageAsync();

        await page.GotoAsync(fixture.BaseUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("window.urabaApp?.install?.state");
        Assert.False(await page.EvaluateAsync<bool>("urabaApp.install.state().ready"));
        await Expect(page.GetByTestId("install-invite")).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("install-invite")).ToBeVisibleAsync(new() { Timeout = 30_000 });
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
    /// La antigua marca persistente no puede seguir afirmando que la app está instalada. Esa marca
    /// también podía sobrevivir a una desinstalación o a un diálogo aceptado que nunca terminara.
    /// </summary>
    [Fact]
    public async Task A_legacy_remembered_install_does_not_create_a_permanent_false_positive()
    {
        await using var context = await AndroidContext();
        await context.AddInitScriptAsync(
            "try { localStorage.setItem('urabaAppInstalada', '1'); } catch {}");
        var page = await context.NewPageAsync();

        await page.GotoAsync(fixture.BaseUrl);
        await Expect(page.GetByTestId("install-invite")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await page.GotoAsync($"{fixture.BaseUrl}/seguimiento");
        await Expect(page.GetByTestId("app-status-app-valor")).ToHaveTextAsync("Instalar",
            new() { Timeout = 30_000 });
        await Expect(page.GetByTestId("app-status-install")).ToBeVisibleAsync();
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

    [Fact]
    public async Task Dismissal_expires_after_fourteen_days_and_the_invitation_can_return()
    {
        await using var context = await AndroidContext();
        await context.AddInitScriptAsync(
            "localStorage.setItem('urabaInstalarDescartada', String(Date.now() - 15 * 86400000));");
        var page = await context.NewPageAsync();

        await page.GotoAsync(fixture.BaseUrl);
        await Expect(page.GetByTestId("install-invite")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        Assert.False(await page.EvaluateAsync<bool>("urabaApp.install.state().dismissed"));
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
    /// DEFECTO AUDITADO: la Home es una pantalla de selva con texto papel, y la invitación trae SU
    /// propia superficie clara. Sin declarar tinta, la card heredaba el papel de la pantalla: el
    /// "Ahora no" acababa en rgb(255,253,249) sobre rgb(255,253,249) —contraste 1:1, invisible—.
    ///
    /// Se mide sobre estilos calculados por el navegador, no sobre lo que dice la hoja: el fondo de
    /// la card es un degradado, así que su backgroundColor calculado es transparente y una lectura
    /// ingenua vería la selva de detrás y daría el defecto por bueno. Por eso se resuelven las dos
    /// paradas del degradado y se componen sobre la superficie opaca que hay debajo.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_invitation_stays_legible_over_the_dark_home(bool temaOscuro)
    {
        await using var context = await AndroidContext(temaOscuro ? ColorScheme.Dark : ColorScheme.Light);
        await context.AddInitScriptAsync(GuionOfertaNativa);
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl);

        var invitacion = page.GetByTestId("install-invite");
        await Expect(invitacion).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var descarte = invitacion.GetByTestId("install-dismiss");
        await Expect(descarte).ToBeVisibleAsync();

        var medida = await invitacion.EvaluateAsync<Tintas>(SondaDeContraste);
        var pantalla = LeerTinta(medida.Pantalla);

        // El fallo exacto del informe: la misma tinta delante y detrás.
        Assert.NotEqual(medida.DescarteColor, medida.DescarteFondo);

        var descarteTexto = Sobre(LeerTinta(medida.DescarteColor), LeerTinta(medida.DescarteFondo));
        var descarteFondo = Sobre(LeerTinta(medida.DescarteFondo), pantalla);
        var razonDescarte = Contraste(descarteTexto, descarteFondo);
        Assert.True(razonDescarte >= 4.5,
            $"\"Ahora no\": {medida.DescarteColor} sobre {medida.DescarteFondo} = {razonDescarte:0.00}:1");

        // El texto principal se lee sobre las DOS paradas del degradado, no sólo sobre una.
        var titulo = LeerTinta(medida.TextoColor);
        foreach (var (parada, crudo) in new[] { ("inicio", medida.CardInicio), ("fin", medida.CardFin) })
        {
            var fondo = Sobre(LeerTinta(crudo), pantalla);
            var razon = Contraste(Sobre(titulo, fondo), fondo);
            Assert.True(razon >= 4.5,
                $"título sobre el degradado ({parada}): {medida.TextoColor} sobre {crudo} = {razon:0.00}:1");
        }
    }

    /// <summary>
    /// Resuelve las tintas que de verdad se pintan. Las paradas del degradado se leen inyectando una
    /// sonda dentro de la propia card: así var() se resuelve con las variables que hereda la card y
    /// el navegador devuelve rgb()/rgba() canónico en vez del texto del token.
    /// </summary>
    private const string SondaDeContraste = """
        invitacion => {
          const resolver = valor => {
            const sonda = document.createElement('span');
            sonda.style.backgroundColor = valor;
            invitacion.appendChild(sonda);
            const tinta = getComputedStyle(sonda).backgroundColor;
            sonda.remove();
            return tinta;
          };
          // Sólo el canal alfa decide, y sólo cuando el color lo trae: mirar el final del texto
          // confundiría un rgb(1, 2, 0) opaco con un transparente.
          const invisible = tinta => {
            const canales = (tinta.match(/[\d.]+/g) || []).map(Number);
            return canales.length > 3 && canales[3] === 0;
          };
          const opacoDetras = elemento => {
            for (let nodo = elemento; nodo; nodo = nodo.parentElement) {
              const tinta = getComputedStyle(nodo).backgroundColor;
              if (tinta && tinta !== 'transparent' && !invisible(tinta)) return tinta;
            }
            return getComputedStyle(document.documentElement).backgroundColor;
          };
          const titulo = invitacion.querySelector('strong');
          const descarte = invitacion.querySelector('[data-testid="install-dismiss"]');
          return {
            textoColor: getComputedStyle(titulo).color,
            descarteColor: getComputedStyle(descarte).color,
            descarteFondo: getComputedStyle(descarte).backgroundColor,
            cardInicio: resolver('var(--uc-green-wash)'),
            cardFin: resolver('var(--superficie-2)'),
            pantalla: opacoDetras(invitacion.parentElement)
          };
        }
        """;

    private sealed record Tintas
    {
        public string TextoColor { get; init; } = "";
        public string DescarteColor { get; init; } = "";
        public string DescarteFondo { get; init; } = "";
        public string CardInicio { get; init; } = "";
        public string CardFin { get; init; } = "";
        public string Pantalla { get; init; } = "";
    }

    private readonly record struct Tinta(double R, double G, double B, double A);

    private static Tinta LeerTinta(string css)
    {
        var canales = Regex.Matches(css, @"[\d.]+")
            .Select(coincidencia => double.Parse(coincidencia.Value, CultureInfo.InvariantCulture))
            .ToArray();
        Assert.True(canales.Length >= 3, $"no es un color legible: {css}");
        return new Tinta(canales[0], canales[1], canales[2], canales.Length > 3 ? canales[3] : 1d);
    }

    /// <summary>Compone una tinta translúcida sobre la que tiene detrás. Opaca, se devuelve igual.</summary>
    private static Tinta Sobre(Tinta frente, Tinta fondo) => frente.A >= 1d ? frente : new Tinta(
        frente.R * frente.A + fondo.R * (1d - frente.A),
        frente.G * frente.A + fondo.G * (1d - frente.A),
        frente.B * frente.A + fondo.B * (1d - frente.A),
        1d);

    /// <summary>Razón de contraste de WCAG 2.1. No es una suite completa: es la evidencia del fallo.</summary>
    private static double Contraste(Tinta texto, Tinta fondo)
    {
        static double Canal(double valor)
        {
            var v = valor / 255d;
            return v <= 0.03928d ? v / 12.92d : Math.Pow((v + 0.055d) / 1.055d, 2.4d);
        }

        static double Luminancia(Tinta t) =>
            0.2126d * Canal(t.R) + 0.7152d * Canal(t.G) + 0.0722d * Canal(t.B);

        var uno = Luminancia(texto) + 0.05d;
        var otro = Luminancia(fondo) + 0.05d;
        return uno > otro ? uno / otro : otro / uno;
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
        await Expect(invitacion).ToContainTextAsync("Lleva tu negocio contigo");
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
        await Expect(invitacion).ToContainTextAsync("Lleva UrabáConecta contigo");
        await Expect(invitacion).ToContainTextAsync("entra más rápido a tus citas, pedidos y negocios");

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

    private const string GuionIos = """
        try {
          Object.defineProperty(navigator, 'standalone',
            { value: false, configurable: true });
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

    private async Task<IBrowserContext> AndroidContext(ColorScheme? tema = null) =>
        await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 360, Height = 800 },
            UserAgent = AgenteMovil,
            ColorScheme = tema
        });

    private async Task<IBrowserContext> IosContext() =>
        await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            UserAgent = AgenteIos
        });

    private async Task<IBrowserContext> DesktopContext() =>
        await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1365, Height = 900 },
            UserAgent =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
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
