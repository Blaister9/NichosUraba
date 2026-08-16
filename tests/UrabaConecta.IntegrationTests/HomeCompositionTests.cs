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
/// </summary>
public sealed class HomeCompositionTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    [Fact]
    public async Task Composing_the_feed_stays_within_its_statement_budget()
    {
        using var client = factory.CreateClient();
        var counter = factory.Services.GetRequiredService<QueryCounter>();

        counter.Reset();
        var response = await client.GetAsync("/");
        var sentencias = counter.Count;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        // El feed se prerenderiza en el servidor: sin piezas, el conteo no estaría midiendo nada.
        Assert.Contains("data-testid=\"feed-piece\"", html);
        // Eran 36 con la disponibilidad de citas preguntada día por día y la ficha de la tienda
        // pedida para no mirarla; 18 después de agrupar esas dos, porque la fila, la carta, las
        // franjas y los horarios seguían pidiéndose negocio por negocio. Con el feed resuelto en
        // una lectura son 6, y —esto es lo que importa— seis con cualquier número de negocios.
        // El escalado lo fija HomeFeedScalingTests; este techo protege la pantalla completa.
        Assert.True(sentencias <= 8, $"Componer la Home costó {sentencias} sentencias.");
    }

    [Fact]
    public async Task The_feed_keeps_its_pieces_and_their_order()
    {
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/");

        // Las tres verticales siguen apareciendo, y la captación seguida del patrocinado siguen
        // en su sitio: lo que se recortó fueron lecturas, no piezas.
        foreach (var esperado in new[] { "Tomar turno", "Ver horarios", "¿Tienes un negocio en Urabá?" })
            Assert.Contains(esperado, html);
        Assert.True(html.IndexOf("¿Tienes un negocio en Urabá?", StringComparison.Ordinal)
            < html.IndexOf("Patrocinado", StringComparison.Ordinal),
            "La pieza de captación debe seguir apareciendo antes que la patrocinada.");
    }

    [Fact]
    public async Task A_business_without_appointments_still_gets_its_pieces()
    {
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/");

        // La tienda ya no pide su ficha completa, y aun así conserva su pieza de producto y su
        // acción: era lo único que se quería comprobar al dejar de pedirla.
        Assert.Contains("Pedir", html);
    }
}
