using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UrabaConecta.Application;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace UrabaConecta.IntegrationTests;

/// <summary>
/// Lo que esta prueba protege no es un tiempo, es una forma: el coste de la Home tiene que ser el
/// mismo con tres negocios que con cien. La base vive en otra región y cada ida y vuelta cuesta
/// unos 73 ms, así que una sentencia por negocio es lo que convierte una pantalla de un segundo en
/// una de diez sin que nadie cambie nada —sólo entran clientes—.
///
/// Se siembra por el contexto y no por la API administrativa porque publicar cien negocios por el
/// recorrido completo tardaría más que toda la suite, y lo que se está midiendo es la lectura.
/// </summary>
public sealed class HomeFeedScalingTests(PostgresWebFactory factory, ITestOutputHelper output)
    : IClassFixture<PostgresWebFactory>
{
    private QueryCounter Counter => factory.Services.GetRequiredService<QueryCounter>();

    [Fact]
    public async Task The_home_feed_costs_the_same_with_three_businesses_and_with_a_hundred()
    {
        using var client = factory.CreateClient();
        var medidas = new List<(int Negocios, int Sentencias, long Milisegundos, int Bytes)>();
        var sembrados = 0;

        foreach (var objetivo in new[] { 3, 10, 25, 50, 100 })
        {
            sembrados += await SeedAsync(objetivo - sembrados);
            factory.Services.GetRequiredService<IPublicDirectoryCache>().Invalidate();

            // Una pasada previa para que el plan y las conexiones no se cobren en la medida.
            _ = await client.GetStringAsync("/api/v1/public/home-feed");
            Counter.Reset();
            var reloj = System.Diagnostics.Stopwatch.StartNew();
            var cuerpo = await client.GetStringAsync("/api/v1/public/home-feed");
            reloj.Stop();
            var sentencias = Counter.Count;

            var feed = System.Text.Json.JsonSerializer.Deserialize<HomeFeedDto>(cuerpo,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            medidas.Add((feed.Businesses.Count, sentencias, reloj.ElapsedMilliseconds,
                System.Text.Encoding.UTF8.GetByteCount(cuerpo)));
            output.WriteLine($"  {feed.Businesses.Count,4} negocios -> {sentencias,2} sentencias, " +
                             $"{reloj.ElapsedMilliseconds,5} ms, {cuerpo.Length,7} caracteres");
        }

        var primera = medidas[0].Sentencias;
        foreach (var medida in medidas)
            Assert.True(medida.Sentencias == primera,
                $"Con {medida.Negocios} negocios la Home costó {medida.Sentencias} sentencias y con " +
                $"{medidas[0].Negocios} costaba {primera}: el coste volvió a crecer con el catálogo.");
        // El techo absoluto, por si algún día el conteo crece parejo en todos los tramos.
        Assert.True(primera <= 8, $"El feed costó {primera} sentencias.");
        // Las filas devueltas sí crecen: lo que no puede crecer es el número de viajes.
        Assert.True(medidas[^1].Bytes > medidas[0].Bytes,
            "Con cien negocios el feed debería pesar más que con tres; si no, no se sembró nada.");
    }

    /// <summary>
    /// Negocios de las tres verticales, en rotación, con todo lo que el feed llega a mirar: fila
    /// abierta con turnos en espera, servicios con personal y horario, y carta con franjas de
    /// recogida. Un negocio de relleno sin nada configurado no probaría nada.
    /// </summary>
    private async Task<int> SeedAsync(int cuantos)
    {
        if (cuantos <= 0) return 0;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var municipalityId = await db.Municipalities.Where(x => x.IsActive).Select(x => x.Id).FirstAsync();
        var categoryId = await db.Categories.Where(x => x.IsActive).Select(x => x.Id).FirstAsync();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < cuantos; i++)
        {
            var id = Guid.NewGuid();
            var sufijo = $"{id:N}"[..8];
            db.Add(new Business(id, $"escala-{sufijo}", $"Escala {sufijo}", municipalityId, categoryId,
                "Negocio sembrado para medir cómo escala el feed.", "Calle 1 # 1-1", "3000000000"));
            foreach (var day in Enum.GetValues<DayOfWeek>())
                db.BusinessHours.Add(new BusinessHour(Guid.NewGuid(), id, day, new(6, 0), new(22, 0)));

            switch (i % 3)
            {
                case 0:
                    db.Add(new BusinessModule(id, BusinessModuleKind.VirtualQueues, true, now));
                    var definition = new QueueDefinition(Guid.NewGuid(), id, "Atención general", 15, 30,
                        "Toma tu turno.", true);
                    var session = new QueueSession(Guid.NewGuid(), id, definition.Id, now);
                    db.AddRange(definition, session);
                    break;
                case 1:
                    db.Add(new BusinessModule(id, BusinessModuleKind.Appointments, true, now));
                    var service = new Service(Guid.NewGuid(), id, "Servicio de escala", 60, 50000m,
                        "Servicio sembrado.", 0);
                    var staff = new StaffMember(Guid.NewGuid(), id, "Profesional de escala");
                    db.AddRange(service, staff, new StaffService(id, staff.Id, service.Id));
                    break;
                default:
                    db.Add(new BusinessModule(id, BusinessModuleKind.PickupOrders, true, now));
                    var productCategory = new ProductCategory(Guid.NewGuid(), id, "Carta");
                    db.AddRange(productCategory,
                        new Product(Guid.NewGuid(), id, productCategory.Id, "Producto de escala",
                            "Producto sembrado.", 20000m),
                        new PickupOrderSettings(Guid.NewGuid(), id, true, "Pasa por tu pedido.",
                            30, 15, 3, new(8, 0), new(20, 0)));
                    break;
            }
        }
        await db.SaveChangesAsync();
        return cuantos;
    }
}
