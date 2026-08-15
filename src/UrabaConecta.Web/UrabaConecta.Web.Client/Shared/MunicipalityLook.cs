namespace UrabaConecta.Web.Client.Shared;

/// <summary>Identidad visual local para que cada municipio sea un destino reconocible.</summary>
public static class MunicipalityLook
{
    public sealed record Look(string Icon, string Monogram, string Cue, string Tone);

    private static readonly Dictionary<string, Look> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["apartado"] = new("palmera", "A", "Centro de Urabá", "selva"),
        ["carepa"] = new("hoja", "C", "Verde y cercano", "coral"),
        ["chigorodo"] = new("montana", "CH", "Camino y montaña", "cielo"),
        ["turbo"] = new("ola", "T", "Mar y golfo", "mar"),
    };

    public static Look For(string? slug)
        => slug is not null && Known.TryGetValue(slug, out var look)
            ? look : new("ubicacion", "U", "Urabá", "selva");
}
