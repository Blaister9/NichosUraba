namespace UrabaConecta.Infrastructure.Persistence;

/// <summary>
/// Los negocios que sembramos nosotros. La lista es explícita y no una corazonada sobre el nombre
/// o el slug: la administración necesita poder decir "esto es un decorado" sin riesgo de etiquetar
/// como ficticio un negocio real que casualmente se llame parecido.
///
/// Sirve para informar, nunca para decidir qué se borra. Ningún camino de la aplicación usa esta
/// lista para escribir ni para reasignar nada.
/// </summary>
public static class DemoFixtures
{
    public static readonly IReadOnlySet<Guid> BusinessIds = new HashSet<Guid>
    {
        DemoShowcaseSeeder.BarberBusinessId,
        DemoShowcaseSeeder.BeautyBusinessId,
        DevelopmentSeeder.BellaBusinessId,
        DevelopmentSeeder.OtherBusinessId,
        DevelopmentSeeder.CorteBusinessId,
        DevelopmentSeeder.SazonBusinessId
    };

    public static bool IsFixture(Guid businessId) => BusinessIds.Contains(businessId);
}
