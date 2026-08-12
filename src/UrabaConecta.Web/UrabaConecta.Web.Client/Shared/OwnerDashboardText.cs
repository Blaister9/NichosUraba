using System.Globalization;

namespace UrabaConecta.Web.Client.Shared;

/// <summary>
/// El texto del resumen operativo. Vive aparte de la vista para que la pantalla no calcule nada:
/// las cifras vienen resueltas del servidor y aquí sólo se les pone nombre y hora legibles.
///
/// El resto del panel todavía muestra horas en UTC con un desfase escrito a mano; esa pasada global
/// es de otra fase. Esta superficie es nueva, así que nace ya con la hora del negocio.
/// </summary>
public static class OwnerDashboardText
{
    private static readonly CultureInfo Colombia = new("es-CO");

    /// <summary>
    /// La hora en el reloj del negocio. Se usa la zona que el servidor dice haber usado para contar
    /// el día, no la del navegador ni una constante: un negocio en otra zona mostraría una hora que
    /// no coincide con sus propias métricas.
    /// </summary>
    public static string Time(DateTimeOffset instant, string timeZoneId)
        => TimeZoneInfo.ConvertTime(instant, Zone(timeZoneId)).ToString("h:mm tt", Colombia);

    private static TimeZoneInfo Zone(string timeZoneId)
    {
        // La zona ya viene resuelta por el servidor; este respaldo sólo cubre que el proceso que
        // pinta no reconozca el identificador, y nunca debe tumbar la pantalla entera.
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (TimeZoneNotFoundException) { return Bogota; }
        catch (InvalidTimeZoneException) { return Bogota; }
    }

    private static readonly TimeZoneInfo Bogota = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");
}
