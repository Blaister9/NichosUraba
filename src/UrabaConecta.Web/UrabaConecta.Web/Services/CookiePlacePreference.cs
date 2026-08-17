using UrabaConecta.Web.Client.Services;

namespace UrabaConecta.Web.Services;

/// <summary>
/// Lee la preferencia de municipio de la petición que está siendo servida. Es la mitad servidora de
/// <see cref="IPlacePreference"/>: la escritura ocurre en el navegador, porque cuando la pantalla ya
/// respondió no queda encabezado donde poner una cookie.
/// </summary>
public sealed class CookiePlacePreference(IHttpContextAccessor accessor) : IPlacePreference
{
    public string? Read() => PlacePreference.Normalize(
        accessor.HttpContext?.Request.Cookies[PlacePreference.CookieName]);
}
