using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

/// <summary>
/// Las tres pantallas de operación, ya renderizadas. Se comprueba lo que ve quien atiende: de qué
/// establecimiento es lo que tiene delante, estados en español y horas legibles.
///
/// Las afirmaciones de "no aparece el enum" se hacen sobre el texto entre etiquetas y no sobre el
/// HTML entero, porque el valor que viaja en un &lt;option value="Pending"&gt; sigue siendo —y debe
/// seguir siendo— el nombre del enum.
/// </summary>
public sealed class OperationalSurfacesTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    private QueryCounter Counter => factory.Services.GetRequiredService<QueryCounter>();

    private async Task<string> SurfaceAsync(string email, string route)
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        await PlatformAdministrationApiTests.Login(client, email);
        var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Se decodifica porque Razor escapa los valores dinámicos: el nombre del negocio llega como
        // "Sal&#xF3;n" y las afirmaciones tienen que hablar del texto que la persona lee.
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Ningún nombre de enum puede llegar como texto a la pantalla.</summary>
    private static void NoRawStatuses(string html, params string[] statuses)
    {
        foreach (var status in statuses)
        {
            Assert.DoesNotContain($">{status}<", html);
            Assert.DoesNotContain($"> {status} <", html);
        }
        Assert.DoesNotContain("UTC", html);
    }

    // ------------------------------------------------------------------ citas

    [Fact]
    public async Task The_appointment_panel_says_which_business_it_belongs_to()
    {
        var html = await SurfaceAsync(DevelopmentSeeder.BellaOwnerEmail,
            $"/panel/{DevelopmentSeeder.BellaBusinessId}/citas");

        Assert.Contains("data-testid=\"business-name\"", html);
        Assert.Contains("Salón Bella Urabá", html);
        // Ni identificadores ni direcciones técnicas en el encabezado.
        Assert.DoesNotContain($">{DevelopmentSeeder.BellaBusinessId}<", html);
        Assert.DoesNotContain(">salon-bella-uraba<", html);
    }

    [Fact]
    public async Task The_appointment_filter_offers_spanish_states_while_still_sending_the_enum()
    {
        var html = await SurfaceAsync(DevelopmentSeeder.BellaOwnerEmail,
            $"/panel/{DevelopmentSeeder.BellaBusinessId}/citas");

        Assert.Contains("Todos los estados", html);
        foreach (var etiqueta in new[]
                 { ">Pendiente<", ">Confirmada<", ">Rechazada<", ">Cancelada<", ">Completada<", ">No asistió<" })
            Assert.Contains(etiqueta, html);
        // El valor sigue siendo el del dominio: la traducción es de pantalla, no del filtro.
        Assert.Contains("value=\"NoShow\"", html);
        NoRawStatuses(html, "Pending", "Confirmed", "Rejected", "Cancelled", "Completed", "NoShow");
    }

    [Fact]
    public async Task An_appointment_shows_a_human_status_and_a_local_hour()
    {
        var html = await SurfaceAsync(DevelopmentSeeder.BellaOwnerEmail,
            $"/panel/{DevelopmentSeeder.BellaBusinessId}/citas");

        Assert.Contains("data-testid=\"appointment-status\"", html);
        Assert.Contains("data-testid=\"appointment-start\"", html);
        // La demostración deja una cita completada; su estado se lee en español.
        Assert.Contains(">Completada<", html);
        // Nada de marcas de máquina: ni ISO, ni la hora en UTC.
        Assert.DoesNotContain("UTC", html);
        Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}T\d{2}:", html);
    }

    [Fact]
    public async Task The_appointment_actions_are_written_for_a_person()
    {
        var html = await SurfaceAsync(DevelopmentSeeder.BellaOwnerEmail,
            $"/panel/{DevelopmentSeeder.BellaBusinessId}/citas");
        foreach (var ingles in new[] { ">Confirm<", ">Reject<", ">Complete<", ">Cancel<", ">NoShow<" })
            Assert.DoesNotContain(ingles, html);
    }

    // ------------------------------------------------------------------ turnos

    [Fact]
    public async Task The_queue_board_keeps_the_business_name_and_speaks_spanish()
    {
        await WaitingTicketAsync(DevelopmentSeeder.CorteBusinessId);
        var html = await SurfaceAsync(DevelopmentSeeder.CorteOwnerEmail,
            $"/panel/{DevelopmentSeeder.CorteBusinessId}/turnos");

        Assert.Contains("data-testid=\"business-name\"", html);
        Assert.Contains("Barbería El Corte", html);
        Assert.Contains("data-testid=\"ticket-status\"", html);
        Assert.Contains(">En espera<", html);
        // "Te estamos llamando" le habla al cliente; en el tablero del negocio el turno está "Llamado".
        Assert.DoesNotContain("Te estamos llamando", html);
        NoRawStatuses(html, "Waiting", "Called", "InService", "Skipped");
        // La jornada conserva su propio vocabulario, que describe otra cosa.
        Assert.Contains("Abierta", html);
    }

    // ------------------------------------------------------------------ pedidos

    [Fact]
    public async Task The_orders_board_says_which_business_it_belongs_to_and_speaks_spanish()
    {
        var html = await SurfaceAsync(DevelopmentSeeder.SazonOwnerEmail,
            $"/panel/{DevelopmentSeeder.SazonBusinessId}/pedidos");

        Assert.Contains("data-testid=\"business-name\"", html);
        Assert.Contains("Restaurante Sazón Local", html);
        Assert.Contains("data-testid=\"order-status\"", html);
        Assert.Contains(">Entregado<", html);
        NoRawStatuses(html, "Pending", "Accepted", "Preparing", "ReadyForPickup", "Delivered");
        Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}T\d{2}:", html);
    }

    // ------------------------------------------------------------------ coste

    [Fact]
    public async Task Naming_the_business_does_not_cost_a_second_trip_per_screen()
    {
        // Citas y turnos ya resolvían el negocio, así que el nombre y la zona son gratis. Pedidos no
        // lo resolvía: cuesta una lectura por pantalla —no por pedido— y ése es el techo que se fija.
        using var bella = factory.CreateClient(new() { AllowAutoRedirect = false });
        await PlatformAdministrationApiTests.Login(bella, DevelopmentSeeder.BellaOwnerEmail);
        Counter.Reset();
        _ = await bella.GetAsync($"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/appointments");
        var citas = Counter.Count;

        using var sazon = factory.CreateClient(new() { AllowAutoRedirect = false });
        await PlatformAdministrationApiTests.Login(sazon, DevelopmentSeeder.SazonOwnerEmail);
        Counter.Reset();
        _ = await sazon.GetAsync($"/api/v1/businesses/{DevelopmentSeeder.SazonBusinessId}/orders");
        var pedidos = Counter.Count;

        Assert.True(citas <= 7, $"El listado de citas costó {citas} sentencias.");
        Assert.True(pedidos <= 6, $"El listado de pedidos costó {pedidos} sentencias.");
    }

    // ------------------------------------------------------------------ aislamiento

    [Fact]
    public async Task The_name_of_a_business_is_not_served_to_someone_outside_it()
    {
        // Añadir nombre y zona a la respuesta no puede convertirla en una forma de leer metadatos
        // de un establecimiento ajeno.
        using var otro = factory.CreateClient(new() { AllowAutoRedirect = false });
        await PlatformAdministrationApiTests.Login(otro, DevelopmentSeeder.OtherOwnerEmail);

        foreach (var ruta in new[]
                 {
                     $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/appointments",
                     $"/api/v1/businesses/{DevelopmentSeeder.SazonBusinessId}/orders"
                 })
        {
            var response = await otro.GetAsync(ruta);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.DoesNotContain("Salón Bella Urabá", await response.Content.ReadAsStringAsync());
        }
    }

    /// <summary>Un turno en espera, para que el tablero tenga una fila que mostrar.</summary>
    private async Task WaitingTicketAsync(Guid businessId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(
            db.QueueSessions.Where(x => x.BusinessId == businessId).OrderByDescending(x => x.OpenedAtUtc));
        var numero = 500 + Random.Shared.Next(1, 400);
        // Sin alias: el alias real viaja cifrado, y sembrar aquí un texto plano haría que la pantalla
        // reventara al intentar descifrarlo.
        db.Add(new QueueTicket(Guid.NewGuid(), businessId, session.Id, numero,
            Guid.NewGuid().ToString("N"), null, QueueTicketSource.WalkIn, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }
}
