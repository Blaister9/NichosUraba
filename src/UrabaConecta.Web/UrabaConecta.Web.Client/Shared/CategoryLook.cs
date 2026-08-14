namespace UrabaConecta.Web.Client.Shared;

/// <summary>
/// Cómo se ve una categoría: su icono y su familia de color.
/// </summary>
/// <remarks>
/// Vive en la interfaz y no en la base de datos a propósito. La taxonomía la administra la
/// plataforma y cambia poco; el tratamiento visual cambia cada vez que se rediseña una pantalla, y
/// atarlo a una columna obligaría a una migración para mover un trazo. Una categoría sin entrada no
/// se rompe: cae en el aspecto neutro y sigue siendo navegable, que es lo que permite dar de alta
/// una categoría nueva sin tocar código.
/// </remarks>
public static class CategoryLook
{
    public sealed record Look(string Icon, string Tone);

    private static readonly Dictionary<string, Look> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["barberia"] = new("tijeras", "barberia"),
        ["belleza-cuidado-personal"] = new("brillo", "belleza"),
        ["unas-y-belleza"] = new("brillo", "belleza"),
        ["maquillaje-y-cosmeticos"] = new("bolsa", "producto"),
        ["restaurante"] = new("plato", "comida"),
        ["mascotas"] = new("mascota", "mascotas"),
        ["salud-bienestar"] = new("salud", "salud"),
        ["servicios-profesionales"] = new("herramienta", "servicios"),
    };

    public static Look For(string? slug)
        => slug is not null && Known.TryGetValue(slug, out var look) ? look : new("negocio", "neutro");

    public static string Icon(string? slug) => For(slug).Icon;

    /// <summary>Se emite como atributo <c>data-tono</c>; el CSS decide el color desde ahí.</summary>
    public static string Tone(string? slug) => For(slug).Tone;
}
