using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UrabaConecta.Domain;

namespace UrabaConecta.Infrastructure.Persistence;

/// <summary>
/// El catálogo de categorías del piloto. No es un decorado: una categoría es taxonomía comercial
/// —lo que permite encontrar un negocio— y sin la fila no se puede dar de alta una odontología ni
/// una óptica sin que alguien entre a la base a crearla a mano.
///
/// Por eso corre en todos los ambientes, a diferencia del sembrado Demo. Y por eso sólo INSERTA lo
/// que falta: nunca renombra ni desactiva una categoría existente, que es lo que convertiría un
/// arranque rutinario en un cambio silencioso del directorio público. Una categoría sin negocios
/// publicados no aparece en las pantallas de descubrimiento, así que añadirlas antes de tiempo no
/// enseña categorías vacías a nadie.
/// </summary>
public static class PilotCategorySeeder
{
    private static readonly (Guid Id, string Slug, string Name)[] Catalog =
    [
        (Guid.Parse("ca7e0001-0000-4000-8000-000000000001"), "odontologia", "Odontología"),
        (Guid.Parse("ca7e0002-0000-4000-8000-000000000002"), "veterinarias", "Veterinarias"),
        (Guid.Parse("ca7e0003-0000-4000-8000-000000000003"), "spa-y-belleza", "Spa y belleza"),
        (Guid.Parse("ca7e0004-0000-4000-8000-000000000004"), "droguerias", "Droguerías"),
        (Guid.Parse("ca7e0005-0000-4000-8000-000000000005"), "opticas", "Ópticas")
    ];

    public static async Task SeedPilotCategoriesAsync(this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("UrabaConecta.PilotCategories");
        try
        {
            var existing = await db.Categories.AsNoTracking().Select(x => x.Slug)
                .ToListAsync(cancellationToken);
            var missing = Catalog.Where(x => !existing.Contains(x.Slug)).ToList();
            if (missing.Count == 0) return;
            foreach (var (id, slug, name) in missing) db.Categories.Add(new Category(id, slug, name));
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Categorías del piloto añadidas: {Slugs}.",
                string.Join(", ", missing.Select(x => x.Slug)));
        }
        catch (Exception ex)
        {
            // Una categoría que falta no puede impedir que arranque una aplicación con negocios
            // reales dentro. Queda registrado y el arranque sigue; lo que no habrá es la categoría.
            logger.LogError(ex, "No se pudieron sembrar las categorías del piloto.");
        }
    }
}
