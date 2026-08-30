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
    /// cedió su diálogo de instalación; <c>pending</c> la persona aceptó y todavía falta la señal
    /// real de instalación; <c>manual</c> sólo hay una instrucción manual compatible;
    /// <c>unavailable</c> no existe una acción de instalación verificable.
    /// </summary>
    public string Mode { get; init; } = "unavailable";

    /// <summary>La persona ya dijo "ahora no" y todavía corre el plazo de silencio.</summary>
    public bool Dismissed { get; init; }

    /// <summary>
    /// Esta pestaña ES la aplicación, comprobado ahora por el modo de presentación. Una señal
    /// <c>appinstalled</c> recién recibida también puede marcar la sesión como instalada sin
    /// cambiar este indicador.
    /// </summary>
    public bool RunningAsApp { get; init; }

    /// <summary>
    /// Ya transcurrió el momento de cortesía o hubo una interacción. Sólo gobierna la invitación;
    /// la ficha explícita de estado puede seguir informando desde el primer render interactivo.
    /// </summary>
    public bool Ready { get; init; }

    public string Platform { get; init; } = "";
    public string Browser { get; init; } = "";

    /// <summary>Nombre genérico del control que abre el camino manual, si existe.</summary>
    public string Menu { get; init; } = "";

    /// <summary>Instrucción mínima del dispositivo. Vacío cuando no hay camino manual conocido.</summary>
    public IReadOnlyList<string> Steps { get; init; } = [];

    public bool Installed => Mode == "installed";

    /// <summary>
    /// Hay algo honesto que mostrar: diálogo nativo, camino manual o instalación aún en curso.
    /// </summary>
    public bool CanOffer => Mode is "native" or "manual" or "pending";
}
