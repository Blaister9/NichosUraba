using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UrabaConecta.Application;
using UrabaConecta.Domain;

namespace UrabaConecta.Infrastructure.Persistence;

/// <summary>
/// Cuelga fotografías de las fichas ficticias del catálogo local.
/// </summary>
/// <remarks>
/// Sólo en Development y sólo desde <c>private/demo-assets</c>, que no viaja dentro de la imagen de
/// contenedor: en Demo las imágenes las sube una persona por la interfaz, que es justamente el
/// camino que hay que poder demostrar. Aquí el objetivo es distinto —que quien abre el proyecto en
/// local vea el producto con contenido en lugar de una lista de texto— y por eso el fallo es
/// tolerable: si falta una imagen la ficha usa su respaldo dibujado y sigue siendo usable.
///
/// Es idempotente: una fila del catálogo que ya tiene fotografía vigente no se toca, así que
/// arrancar dos veces no duplica objetos en el almacenamiento.
/// </remarks>
public static class DemoCatalogImages
{
    /// <summary>
    /// Qué archivo va con qué fila. Cada fotografía se usa una sola vez: repetir la misma imagen en
    /// dos tarjetas de la misma lista es lo que delata un catálogo de relleno. Las filas que quedan
    /// sin archivo son deliberadas —enseñan el respaldo y el estado real de un negocio que todavía
    /// no subió todo— y no un olvido.
    /// </summary>
    private static readonly (Guid Target, bool IsService, string File, string AltText)[] Assignments =
    [
        (Guid.Parse("10000000-0000-0000-0000-000000000001"), true, "bella-gallery-2.jpg",
            "Corte femenino en Salón Bella Urabá (imagen ficticia)"),
        (Guid.Parse("10000000-0000-0000-0000-000000000002"), true, "bella-gallery-1.jpg",
            "Cepillado en Salón Bella Urabá (imagen ficticia)"),
        (Guid.Parse("70000000-0000-0000-0000-000000000006"), false, "sazon-gallery-1.jpg",
            "Bandeja del día en Restaurante Sazón Local (imagen ficticia)"),
    ];

    public static async Task SeedCatalogImagesAsync(this IServiceProvider services, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment()) return;
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("UrabaConecta.DemoCatalogImages");
        try
        {
            var root = LocateAssets(environment.ContentRootPath);
            if (root is null) return;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
            var processor = scope.ServiceProvider.GetRequiredService<IImageProcessor>();
            var now = DateTimeOffset.UtcNow;

            foreach (var (target, isService, file, altText) in Assignments)
            {
                var path = Path.Combine(root, file);
                if (!File.Exists(path)) continue;
                // La fila tiene que existir: el sembrado principal pudo no haberla creado si la base
                // ya traía datos propios, y colgar una foto de un identificador ausente fallaría al
                // guardar por la clave foránea.
                var exists = isService
                    ? await db.Services.AnyAsync(x => x.Id == target)
                    : await db.Products.AnyAsync(x => x.Id == target);
                if (!exists) continue;
                if (await db.BusinessImages.AnyAsync(x => !x.IsDeleted &&
                        (isService ? x.ServiceId == target : x.ProductId == target))) continue;

                var businessId = isService
                    ? await db.Services.Where(x => x.Id == target).Select(x => x.BusinessId).SingleAsync()
                    : await db.Products.Where(x => x.Id == target).Select(x => x.BusinessId).SingleAsync();
                var kind = isService ? BusinessImageKind.Service : BusinessImageKind.Product;
                // Mismo procesamiento que una carga real: firma binaria, reescalado y metadatos
                // fuera. Sembrar por un atajo dejaría en local imágenes que la interfaz no habría
                // aceptado.
                var normalized = processor.Normalize(await File.ReadAllBytesAsync(path), kind);
                var key = $"businesses/{businessId:N}/{kind.ToString().ToLowerInvariant()}/{Guid.NewGuid():N}{normalized.Extension}";
                await storage.PutAsync(key, normalized.Content, normalized.ContentType, CancellationToken.None);
                db.BusinessImages.Add(new BusinessImage(Guid.NewGuid(), businessId, kind, key,
                    normalized.ContentType, normalized.Width, normalized.Height,
                    normalized.Content.LongLength, altText, 0, now,
                    isService ? target : null, isService ? null : target));
            }
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<IPublicDirectoryCache>().Invalidate();
        }
        catch (Exception ex)
        {
            // Esto corre antes de que la aplicación escuche. Un fallo aquí no puede dejar el
            // arranque en 502 por una fotografía de demostración.
            logger.LogError(ex, "No se pudieron sembrar las fotografías del catálogo local. " +
                                "La aplicación arranca igual y las fichas usan su respaldo.");
        }
    }

    /// <summary>
    /// Los activos viven fuera del proyecto web, así que se sube por los directorios padres hasta
    /// dar con la carpeta. Devuelve null si no está: es lo normal en cualquier copia que no tenga
    /// el material privado, y no es un error.
    /// </summary>
    private static string? LocateAssets(string contentRoot)
    {
        for (var directory = new DirectoryInfo(contentRoot); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "private", "demo-assets");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }
}
