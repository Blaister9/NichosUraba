using System.Globalization;

namespace UrabaConecta.Web.Client.Shared;

/// <summary>
/// Las horas como las lee quien atiende: en el reloj de su negocio y en español.
///
/// Todo se guarda en UTC, que es lo correcto, pero la pantalla llegó a mostrarlo tal cual —"20:45
/// UTC"— o restando cinco horas a mano. Lo primero obliga a la persona a hacer la conversión de
/// cabeza; lo segundo funciona sólo mientras el negocio esté en Colombia y nadie cambie de huso.
/// Aquí se convierte con la zona que el servidor dice tener configurada para ESE negocio.
///
/// Nada de esto cambia cómo se persisten las marcas de tiempo.
/// </summary>
public static class BusinessDateTimeText
{
    private static readonly CultureInfo Colombia = new("es-CO");

    /// <summary>Sólo la hora: "3:45 p. m.".</summary>
    public static string Time(DateTimeOffset instant, string? timeZoneId)
        => Local(instant, timeZoneId).ToString("h:mm tt", Colombia);

    /// <summary>Fecha y hora: "12 ago 2026, 3:45 p. m.".</summary>
    public static string DateAndTime(DateTimeOffset instant, string? timeZoneId)
        => Local(instant, timeZoneId).ToString("d MMM yyyy, h:mm tt", Colombia);

    /// <summary>Sólo la fecha: "12 ago 2026".</summary>
    public static string Date(DateTimeOffset instant, string? timeZoneId)
        => Local(instant, timeZoneId).ToString("d MMM yyyy", Colombia);

    private static DateTimeOffset Local(DateTimeOffset instant, string? timeZoneId)
        => TimeZoneInfo.ConvertTime(instant, Zone(timeZoneId));

    /// <summary>
    /// La zona ya viene resuelta por el servidor, que es quien avisa cuando un negocio la tiene mal
    /// escrita. Este respaldo sólo cubre que el proceso que pinta no reconozca el identificador, y
    /// callado a propósito: son fila por fila, y avisar aquí llenaría el registro de ruido repetido
    /// sin añadir nada a lo que el panel ya reportó una vez.
    /// </summary>
    private static TimeZoneInfo Zone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return Bogota;
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (TimeZoneNotFoundException) { return Bogota; }
        catch (InvalidTimeZoneException) { return Bogota; }
    }

    private static readonly TimeZoneInfo Bogota = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");
}
