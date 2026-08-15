using UrabaConecta.Contracts;

namespace UrabaConecta.Web.Client.Shared;

/// <summary>
/// Curaduría comercial mínima. Por ahora se administra deliberadamente en código: permite ordenar
/// y rotular espacios destacados sin inventar planes, cobros ni un CMS antes de tener ventas.
/// </summary>
public static class PromotionCatalog
{
    public sealed record Placement(
        string BusinessSlug,
        int Priority,
        string Label,
        string Eyebrow,
        string Headline,
        string Description);

    private static readonly IReadOnlyList<Placement> Placements =
    [
        new(
            "brio-nativo-barberia-demo",
            100,
            "Destacado",
            "Fila virtual abierta",
            "Tu corte sin espera",
            "Entra a la fila desde el celular y llega cuando se acerque tu turno."),
        new(
            "lumina-coral-beauty-demo",
            80,
            "Destacado",
            "Recoge en Carepa",
            "Belleza para llevar",
            "Elige tus productos favoritos y prepara el pedido desde el celular."),
    ];

    public static Placement? For(string? businessSlug)
        => Placements.FirstOrDefault(x => x.BusinessSlug.Equals(businessSlug, StringComparison.OrdinalIgnoreCase));

    public static bool IsFeatured(BusinessCardDto business) => For(business.Slug) is not null;

    public static IReadOnlyList<BusinessCardDto> Order(IReadOnlyList<BusinessCardDto> businesses)
        => businesses.OrderByDescending(x => For(x.Slug)?.Priority ?? 0).ThenBy(x => x.Name).ToArray();
}
