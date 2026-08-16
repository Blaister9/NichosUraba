namespace UrabaConecta.Web.Client.Shared;

/// <summary>
/// Lo que el navegador nos deja saber sobre la instalación, tal como lo devuelve pwa.js.
///
/// Vive fuera de los componentes porque la invitación de instalar y la ficha de estado de la
/// cuenta leen exactamente lo mismo, y tenerlo dos veces era garantizar que una de las dos se
/// quedara atrás cuando cambiara el contrato de JavaScript.
/// </summary>
public sealed record AppInstallState
{
    /// <summary>
    /// <c>installed</c> ya está instalada o corriendo como aplicación; <c>native</c> el navegador
    /// nos cedió su diálogo de instalación; <c>manual</c> no lo cedió pero sí se puede instalar a
    /// mano; <c>unavailable</c> este navegador no instala nada.
    /// </summary>
    public string Mode { get; init; } = "unavailable";

    /// <summary>La persona ya dijo "ahora no" y todavía corre el plazo de silencio.</summary>
    public bool Dismissed { get; init; }

    public string Platform { get; init; } = "";
    public string Browser { get; init; } = "";

    /// <summary>Nombre del menú donde vive la opción, cuando hay que explicarlo a mano.</summary>
    public string Menu { get; init; } = "";

    /// <summary>Los pasos literales de ESTE navegador. Vacío cuando no hay camino manual.</summary>
    public IReadOnlyList<string> Steps { get; init; } = [];

    public bool Installed => Mode == "installed";

    /// <summary>Hay algo que ofrecer: o el diálogo nativo, o un camino manual que sí existe.</summary>
    public bool CanOffer => Mode is "native" or "manual";
}
