using UrabaConecta.Web.Client.Shared;

namespace UrabaConecta.Web.Client.Services;

/// <summary>
/// Dónde busca esta persona, recordado entre visitas.
/// </summary>
/// <remarks>
/// Hay una sola preferencia y vive en una cookie, no en <c>localStorage</c>. La diferencia no es de
/// gusto: el servidor puede leer la cookie mientras compone el HTML, así que la primera pintura ya
/// entrega el paso correcto. Con la preferencia en almacenamiento del navegador el servidor no la
/// conoce, el prerender tiene que enseñar el selector de municipio y la pantalla salta al conectar
/// el circuito. Ese salto es justamente lo que no queremos.
///
/// No identifica a nadie ni exige sesión: guarda uno de cinco valores conocidos y nada más.
/// </remarks>
public interface IPlacePreference
{
    /// <summary>El municipio recordado, o nulo si esta persona todavía no ha elegido.</summary>
    string? Read();
}

public static class PlacePreference
{
    /// <summary>
    /// El mismo nombre lo escribe <c>Pages/Home.razor.js</c>, que es quien puede escribir cookies
    /// desde una pantalla ya servida. Si cambia aquí, cambia allí.
    /// </summary>
    public const string CookieName = "uc_lugar";

    /// <summary>Elegir "todo Urabá" es una elección, y se distingue de no haber elegido.</summary>
    public const string Everywhere = "uraba";

    /// <summary>
    /// Lo que dice la dirección cuando alguien quiere volver a elegir municipio: <c>?lugar=cambiar</c>.
    /// <see cref="Normalize"/> lo rechaza como cualquier otro valor desconocido, y eso es exactamente
    /// lo que enseña el paso 1 aunque haya preferencia guardada.
    ///
    /// Fue <c>?lugar=</c> —vacío significaba "todavía no"— y el servidor lo entendía perfectamente,
    /// pero el router interactivo descarta los valores vacíos de la cadena de consulta: al navegar sin
    /// recargar el parámetro llegaba nulo, la cookie volvía a mandar y el selector no se abría nunca.
    /// Un valor con letras no se puede colapsar.
    /// </summary>
    public const string Change = "cambiar";

    /// <summary>
    /// Una preferencia sólo puede ser un municipio del catálogo o todo Urabá. Cualquier otra cosa
    /// —una cookie manipulada, un municipio retirado— se trata como si no hubiera elección, así que
    /// el valor recordado nunca puede llevar a una pantalla que no exista.
    /// </summary>
    public static string? Normalize(string? value) => value == Everywhere
        ? Everywhere
        : DiscoveryCatalog.MunicipalityBySlug(value)?.Slug;
}

/// <summary>
/// Sin petición HTTP no hay cookie que leer. Existe para que la pantalla resuelva su dependencia
/// también en WebAssembly, donde nadie recordó nada todavía.
/// </summary>
public sealed class UnknownPlacePreference : IPlacePreference
{
    public string? Read() => null;
}
