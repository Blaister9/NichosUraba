using UrabaConecta.Contracts;

namespace UrabaConecta.Web.Client.Shared;

/// <summary>
/// El mapa comercial inicial de UrabáConecta. Estas entradas existen aunque todavía no haya un
/// negocio publicado detrás: en ese caso la interfaz las presenta como crecimiento próximo, sin
/// convertirlas en un enlace a una pantalla vacía.
/// </summary>
public static class DiscoveryCatalog
{
    public sealed record Municipality(string Slug, string Name);
    public sealed record Category(string Slug, string Name);
    public sealed record CategoryEntry(string Slug, string Name, int BusinessCount)
    {
        public bool IsAvailable => BusinessCount > 0;
    }

    public static readonly IReadOnlyList<Municipality> Municipalities =
    [
        new("apartado", "Apartadó"),
        new("carepa", "Carepa"),
        new("chigorodo", "Chigorodó"),
        new("turbo", "Turbo"),
    ];

    public static readonly IReadOnlyList<Category> Categories =
    [
        new("barberia", "Barberías"),
        new("belleza-cuidado-personal", "Belleza y estética"),
        new("maquillaje-y-cosmeticos", "Maquillaje y cosméticos"),
        new("restaurante", "Comida"),
        new("mascotas", "Mascotas"),
        new("salud-bienestar", "Salud y bienestar"),
        new("servicios-profesionales", "Servicios"),
    ];

    public static IReadOnlyList<CategoryEntry> WithCounts(IReadOnlyList<CategoryCardDto> published)
    {
        var counts = published.ToDictionary(x => x.Slug, StringComparer.OrdinalIgnoreCase);
        var result = Categories.Select(x => counts.TryGetValue(x.Slug, out var value)
                ? new CategoryEntry(x.Slug, x.Name, value.BusinessCount)
                : new CategoryEntry(x.Slug, x.Name, 0))
            .ToList();

        // Una categoría creada por la plataforma no espera un despliegue para aparecer.
        var known = Categories.Select(x => x.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        result.AddRange(published.Where(x => !known.Contains(x.Slug))
            .Select(x => new CategoryEntry(x.Slug, x.Name, x.BusinessCount)));
        return result;
    }

    public static Municipality? MunicipalityBySlug(string? slug)
        => Municipalities.FirstOrDefault(x => x.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
}
