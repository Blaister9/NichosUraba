using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace UrabaConecta.IntegrationTests;

/// <summary>
/// El coste de componer la Home. Vive en su propia clase porque el conteo depende de cuántos
/// negocios haya publicados, y las clases que crean negocios de prueba lo moverían: aquí el
/// conjunto de datos es el del sembrado y no cambia.
///
/// La Home encadena varias lecturas y no puede hacerlas a la vez: con InteractiveServer todo el
/// circuito comparte un AppDbContext y las lecturas públicas pasan además por una puerta que las
/// serializa. Lanzarlas en paralelo no las haría más rápidas y sí rompería el contexto. Lo único
/// que baja el tiempo, entonces, es que haya menos, y este techo es lo que impide que vuelvan.
///
/// Con el recorrido guiado la pantalla tiene tres momentos —lugar, categoría, resultados— y el techo
/// se comprueba en los tres: la promesa del cambio es que las tres direcciones se resuelven con la
/// misma lectura única, así que ninguna puede costar más que la Home de antes.
/// </summary>
public sealed class HomeCompositionTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    /// <summary>Sembrado de desarrollo: barbería en Chigorodó, belleza en Apartadó, comida en Carepa.</summary>
    private const string Barberia = "/?lugar=chigorodo&busco=barberia";
    private const string Belleza = "/?lugar=apartado&busco=belleza-cuidado-personal";
    private const string Comida = "/?lugar=carepa&busco=restaurante";

    [Theory]
    [InlineData("/")]
    [InlineData("/?lugar=chigorodo")]
    [InlineData(Barberia)]
    public async Task Composing_any_step_stays_within_its_statement_budget(string ruta)
    {
        using var client = factory.CreateClient();
        var counter = factory.Services.GetRequiredService<QueryCounter>();

        counter.Reset();
        var response = await client.GetAsync(ruta);
        var sentencias = counter.Count;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Eran 36 con la disponibilidad de citas preguntada día por día y la ficha de la tienda
        // pedida para no mirarla; 18 después de agrupar esas dos, porque la fila, la carta, las
        // franjas y los horarios seguían pidiéndose negocio por negocio. Con el feed resuelto en
        // una lectura son 6, y —esto es lo que importa— seis con cualquier número de negocios y en
        // cualquiera de los tres pasos. El escalado lo fija HomeFeedScalingTests.
        Assert.True(sentencias <= 8, $"Componer {ruta} costó {sentencias} sentencias.");
    }

    [Fact]
    public async Task The_first_step_asks_where_before_anything_else()
    {
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/");

        // La decisión principal viaja en el HTML, no espera al circuito: es lo que permite que la
        // primera visita sea tocable en la primera pintura.
        Assert.Contains("¿Dónde estás buscando?", html);
        foreach (var municipio in new[] { "Apartadó", "Carepa", "Chigorodó", "Turbo", "Todo Urabá" })
            Assert.Contains(municipio, html);
        // Y ninguna decisión más: en el paso 1 no hay resultados que interpretar.
        Assert.DoesNotContain("data-testid=\"feed-piece\"", html);
    }

    [Fact]
    public async Task The_second_step_offers_only_categories_backed_by_published_businesses()
    {
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/?lugar=chigorodo");

        Assert.Contains("¿Qué estás buscando?", html);
        // Chigorodó tiene la barbería del sembrado y nada más. Las categorías salen de los datos, así
        // que ofrecer "Comida" aquí sería inventar una vertical.
        Assert.Contains("busco=barberia", html);
        Assert.DoesNotContain("busco=restaurante", html);
    }

    [Theory]
    [InlineData(Barberia, "Tomar turno")]
    [InlineData(Belleza, "Ver horarios")]
    [InlineData(Comida, "Pedir")]
    public async Task Each_result_step_keeps_its_state_and_action(string ruta, string accion)
    {
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync(ruta);

        // Lo que se recortó fue lecturas y decisiones, no piezas: la fotografía, el estado y la
        // acción operativa de cada vertical siguen enteros donde corresponden.
        Assert.Contains("data-testid=\"feed-piece\"", html);
        Assert.Contains(accion, html);
    }

    [Fact]
    public async Task Capture_still_precedes_sponsored_inside_the_results()
    {
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync(Comida);

        // El patrocinado del sembrado es el negocio con pedidos —Sazón, en Carepa— así que aquí
        // coincide con el municipio y la categoría elegidos y le corresponde aparecer.
        Assert.True(html.IndexOf("¿Tienes un negocio en Urabá?", StringComparison.Ordinal)
            < html.IndexOf("Patrocinado", StringComparison.Ordinal),
            "La pieza de captación debe seguir apareciendo antes que la patrocinada.");
    }

    [Fact]
    public async Task A_sponsored_piece_never_appears_outside_its_place_and_category()
    {
        using var client = factory.CreateClient();

        // Mismo patrocinado, otra categoría y otro municipio: publicidad que no corresponde al
        // territorio ni a la intención no se inserta. Lo garantiza el filtro de resultados, no una
        // regla aparte.
        var barberia = await client.GetStringAsync(Barberia);
        Assert.DoesNotContain("Patrocinado", barberia);
        var belleza = await client.GetStringAsync(Belleza);
        Assert.DoesNotContain("Patrocinado", belleza);
    }

    /// <summary>
    /// La invariante del paso 2: toda categoría que se ofrece lleva a algo. Es lo que hace que el
    /// recorrido no pueda producir un callejón sin salida tocando, y se comprueba recorriendo lo que
    /// la pantalla realmente ofrece en cada municipio en vez de una lista escrita en la prueba.
    /// </summary>
    [Theory]
    [InlineData("apartado")]
    [InlineData("carepa")]
    [InlineData("chigorodo")]
    [InlineData("turbo")]
    [InlineData("uraba")]
    public async Task Every_offered_category_leads_to_at_least_one_result(string lugar)
    {
        using var client = factory.CreateClient();
        var categorias = await client.GetStringAsync($"/?lugar={lugar}");

        var ofrecidas = System.Text.RegularExpressions.Regex
            .Matches(categorias, @"busco=([a-z0-9-]+)")
            .Select(x => x.Groups[1].Value).Distinct().ToArray();

        // Turbo no tiene negocios publicados: no ofrece categorías y lo dice, que es lo correcto.
        if (ofrecidas.Length == 0)
        {
            Assert.Contains("Estamos sumando negocios", categorias);
            return;
        }

        foreach (var categoria in ofrecidas)
        {
            var resultados = await client.GetStringAsync($"/?lugar={lugar}&busco={categoria}");
            Assert.Contains("data-testid=\"feed-piece\"", resultados);
            Assert.DoesNotContain("Todavía no tenemos", resultados);
        }
    }

    [Fact]
    public async Task A_place_and_category_without_businesses_explains_itself_and_offers_a_way_out()
    {
        using var client = factory.CreateClient();

        // No se llega tocando —el paso 2 sólo ofrece categorías con negocios— pero sí compartiendo
        // o guardando una dirección. La pantalla dice qué falta y ofrece las dos salidas que existen.
        var html = await client.GetStringAsync("/?lugar=carepa&busco=barberia");

        Assert.Contains("Todavía no tenemos", html);
        Assert.Contains("Carepa", html);
        Assert.Contains("lugar=uraba&amp;busco=barberia", html);
    }
}
