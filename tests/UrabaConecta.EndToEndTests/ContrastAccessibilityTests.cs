using Microsoft.Playwright;

namespace UrabaConecta.EndToEndTests;

/// <summary>
/// Contraste AA sobre lo que el navegador termina pintando, en los dos temas.
///
/// Existe porque el sistema de temas hizo que un mismo componente tenga dos versiones y sólo una
/// pueda mirarse a la vez: al implementarlo salieron 44 componentes por debajo de AA, y el peor no
/// era un gris de relleno sino el botón principal —4.28:1 en claro y 3.17:1 en oscuro—. Leer el CSS
/// no lo habría encontrado: el color de un texto depende de qué superficie acabe teniendo detrás,
/// y eso sólo se sabe con la página montada.
///
/// Mide cada nodo de texto visible contra el fondo real —componiendo las capas translúcidas que
/// tenga encima— y aplica el umbral que le toca por tamaño: 3:1 desde 24px, o desde 18.66px en
/// negrita, y 4.5:1 el resto. Lo que se monta sobre fotografía queda fuera: ahí el contraste lo da
/// el velo y no hay color calculado que medir.
///
/// Comparte aplicación con el resto de pruebas del sitio público, así que no añade ni un proceso ni
/// un contenedor de PostgreSQL. Para saltárselas en una vuelta rápida:
///   dotnet test tests/UrabaConecta.EndToEndTests --filter "categoria!=accesibilidad"
/// </summary>
[Collection(PublicSiteCollection.Name)]
[Trait("categoria", "accesibilidad")]
public sealed class ContrastAccessibilityTests(BrowserFixture fixture)
{
    /// <summary>
    /// Superficies prioritarias, con los negocios del sembrado de desarrollo: descubrimiento en sus
    /// tres pasos, ficha, catálogo, turnos, y las pantallas de cuenta y de texto largo, que son las
    /// que más fácilmente se quedan fuera de un rediseño.
    /// </summary>
    public static TheoryData<string> Rutas() =>
    [
        "/",
        "/?lugar=chigorodo",
        "/?lugar=chigorodo&busco=barberia",
        "/negocios/barberia-el-corte",
        "/negocios/restaurante-sazon-local/pedidos",
        "/negocios/barberia-el-corte/turnos",
        "/explorar",
        "/seguimiento",
        "/para-negocios",
        "/Account/Login",
        "/Account/AccessDenied",
        "/legal/politica-de-datos",
    ];

    [Theory]
    [MemberData(nameof(Rutas))]
    public async Task Every_priority_surface_meets_AA_in_both_themes(string ruta)
    {
        var fallos = new List<string>();
        foreach (var tema in new[] { ColorScheme.Light, ColorScheme.Dark })
        {
            await using var context = await fixture.Browser.NewContextAsync(new()
            {
                ViewportSize = new() { Width = 390, Height = 844 },
                ColorScheme = tema,
            });
            var page = await context.NewPageAsync();
            var respuesta = await page.GotoAsync(fixture.BaseUrl + ruta);
            Assert.NotNull(respuesta);
            Assert.True(respuesta!.Ok, $"{ruta} respondió {respuesta.Status}");

            // El prerenderizado ya trae el texto; esperar a la red inactiva sólo alargaría la
            // prueba sin cambiar ningún color.
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await page.EvaluateAsync("() => document.fonts.ready");

            foreach (var hallazgo in await page.EvaluateAsync<string[]>(Medidor))
                fallos.Add($"[{tema.ToString()!.ToLowerInvariant()}] {ruta}  {hallazgo}");
        }

        Assert.True(fallos.Count == 0,
            $"Componentes por debajo de AA:{Environment.NewLine}{string.Join(Environment.NewLine, fallos)}");
    }

    /// <summary>
    /// Los tres distintivos del feed dependen del estado en vivo del negocio, así que un recorrido
    /// por rutas puede no encontrarlos nunca: EN VIVO sólo se pinta con una fila abierta, y por eso
    /// su 3.02:1 sobrevivió a la primera auditoría y sólo apareció al medir la Demo.
    ///
    /// Aquí no se fabrica un dato: se monta el componente real sobre la hoja de estilos real y se
    /// mide. Lo que se fija es la pareja de colores que declara el CSS, que es justo lo que se rompe
    /// cuando alguien cambia un token.
    /// </summary>
    [Theory]
    [InlineData("live-badge")]
    [InlineData("sponsored-badge")]
    [InlineData("promotion-badge")]
    [InlineData("status")]
    [InlineData("tag")]
    public async Task Badges_that_depend_on_live_state_also_meet_AA(string clase)
    {
        foreach (var tema in new[] { ColorScheme.Light, ColorScheme.Dark })
        {
            await using var context = await fixture.Browser.NewContextAsync(new()
            {
                ViewportSize = new() { Width = 390, Height = 844 },
                ColorScheme = tema,
            });
            var page = await context.NewPageAsync();
            await page.GotoAsync(fixture.BaseUrl);
            await page.EvaluateAsync("() => document.fonts.ready");

            var razon = await page.EvaluateAsync<double>(MedidorDeDistintivo, clase);
            Assert.True(razon >= 4.5,
                $"[{tema.ToString()!.ToLowerInvariant()}] .{clase} da {razon:0.00}:1 y necesita 4.5:1.");
        }
    }

    /// <summary>
    /// Recorre el árbol pintado y devuelve una línea por componente incumplidor, ya agrupada por
    /// clase: veinte filas de la misma lista son un solo problema que arreglar.
    /// </summary>
    private const string Medidor = """
        () => {
          const f = c => { c /= 255; return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); };
          const lum = p => 0.2126 * f(p[0]) + 0.7152 * f(p[1]) + 0.0722 * f(p[2]);
          const rgb = s => (s.match(/[\d.]+/g) || []).slice(0, 3).map(Number);
          const alfa = s => { const p = s.match(/[\d.]+/g) || []; return p.length > 3 ? Number(p[3]) : 1; };

          const porClase = new Map();
          for (const el of Array.from(document.querySelectorAll('body *'))) {
            const texto = Array.from(el.childNodes)
              .filter(n => n.nodeType === 3).map(n => n.textContent.trim()).join('');
            if (!texto) continue;
            const s = getComputedStyle(el);
            if (s.visibility === 'hidden' || s.display === 'none' || Number(s.opacity) < 0.15) continue;
            const caja = el.getBoundingClientRect();
            if (caja.width < 2 || caja.height < 2) continue;

            // Las capas translúcidas se componen en vez de saltarse: un encabezado al 88% sobre
            // papel es casi opaco, y tratarlo como papel daba falsos incumplimientos de 1:1.
            let fondo = null, nodo = el, sobreFoto = false;
            const capas = [];
            while (nodo && nodo !== document.documentElement) {
              const cs = getComputedStyle(nodo);
              if (cs.backgroundImage && cs.backgroundImage !== 'none') { sobreFoto = true; break; }
              const a = alfa(cs.backgroundColor);
              if (a > 0.001) {
                if (a >= 0.999) { fondo = rgb(cs.backgroundColor); break; }
                capas.push([rgb(cs.backgroundColor), a]);
              }
              nodo = nodo.parentElement;
            }
            if (sobreFoto || !fondo) continue;
            for (let i = capas.length - 1; i >= 0; i--) {
              const [c, a] = capas[i];
              fondo = fondo.map((x, j) => c[j] * a + x * (1 - a));
            }

            let frente = rgb(s.color);
            const a = alfa(s.color);
            if (a < 1) frente = frente.map((c, i) => c * a + fondo[i] * (1 - a));

            const l1 = lum(frente), l2 = lum(fondo);
            const razon = (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
            const px = parseFloat(s.fontSize);
            const grande = px >= 24 || (px >= 18.66 && Number(s.fontWeight) >= 700);
            const umbral = grande ? 3 : 4.5;
            if (razon >= umbral) continue;

            const clase = String(el.className || el.tagName).slice(0, 40);
            const anterior = porClase.get(clase);
            if (!anterior || anterior.razon > razon)
              porClase.set(clase, { razon, umbral, px: Math.round(px), texto: texto.slice(0, 40) });
          }

          return Array.from(porClase.entries()).map(([clase, p]) =>
            `${p.razon.toFixed(2)}:1 (min ${p.umbral}) ${p.px}px  ${clase}  "${p.texto}"`);
        }
        """;

    /// <summary>
    /// Monta un distintivo suelto con su clase real, lo mide contra su propio fondo y lo retira.
    /// Si la clase no declara fondo propio, se compone contra la superficie que tenga detrás.
    /// </summary>
    private const string MedidorDeDistintivo = """
        clase => {
          const f = c => { c /= 255; return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); };
          const lum = p => 0.2126 * f(p[0]) + 0.7152 * f(p[1]) + 0.0722 * f(p[2]);
          const rgb = s => (s.match(/[\d.]+/g) || []).slice(0, 3).map(Number);
          const alfa = s => { const p = s.match(/[\d.]+/g) || []; return p.length > 3 ? Number(p[3]) : 1; };

          const el = document.createElement('span');
          el.className = clase;
          el.textContent = 'EN VIVO';
          document.body.appendChild(el);
          const s = getComputedStyle(el);

          let fondo = null, nodo = el;
          const capas = [];
          while (nodo && nodo !== document.documentElement) {
            const cs = getComputedStyle(nodo);
            const a = alfa(cs.backgroundColor);
            if (a > 0.001) {
              if (a >= 0.999) { fondo = rgb(cs.backgroundColor); break; }
              capas.push([rgb(cs.backgroundColor), a]);
            }
            nodo = nodo.parentElement;
          }
          if (!fondo) fondo = [255, 255, 255];
          for (let i = capas.length - 1; i >= 0; i--) {
            const [c, a] = capas[i];
            fondo = fondo.map((x, j) => c[j] * a + x * (1 - a));
          }

          let frente = rgb(s.color);
          const a = alfa(s.color);
          if (a < 1) frente = frente.map((c, i) => c * a + fondo[i] * (1 - a));

          el.remove();
          const l1 = lum(frente), l2 = lum(fondo);
          return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
        }
        """;
}
