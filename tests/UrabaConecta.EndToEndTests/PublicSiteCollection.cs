namespace UrabaConecta.EndToEndTests;

/// <summary>
/// Las pruebas del sitio público comparten una sola aplicación.
/// </summary>
/// <remarks>
/// Cada clase con <c>IClassFixture&lt;BrowserFixture&gt;</c> levanta su propio proceso y su propio
/// contenedor de PostgreSQL. Con la paralelización desactivada eso ya es lento, y en una máquina
/// cargada llega a ser algo peor: al añadir una clase más, el circuito de otra pantalla tardaba más
/// de treinta segundos en conectar y una prueba sin relación con el cambio empezaba a fallar.
///
/// Estas dos clases sólo necesitan el sitio público con el sembrado de desarrollo, así que comparten
/// instancia: una aplicación menos por ejecución y ninguna prueba nueva a costa de otra.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class PublicSiteCollection : ICollectionFixture<BrowserFixture>
{
    public const string Name = "sitio-publico";
}
