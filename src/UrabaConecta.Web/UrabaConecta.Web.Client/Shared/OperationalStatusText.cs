namespace UrabaConecta.Web.Client.Shared;

/// <summary>
/// Vocabulario en español de los estados de la operación. El servidor los transporta con el nombre
/// del enum ("Pending", "ReadyForPickup"); ninguno de esos nombres debe llegar a la pantalla.
///
/// Vive aquí, y no dentro de cada página, porque citas, turnos y pedidos llegaron a tener tres
/// traducciones distintas del mismo concepto: la fila decía "Completado" y el listado de citas
/// "Completada" para cosas que la persona lee seguidas. Un solo lugar, una sola palabra.
///
/// No traduce hacia el otro lado a propósito: lo que viaja al servidor sigue siendo el nombre del
/// enum, así que ningún texto de pantalla puede cambiar una transición.
/// </summary>
public static class OperationalStatusText
{
    /// <summary>
    /// Lo que se muestra cuando aparece un estado que esta versión no conoce. Es preferible a
    /// devolver el nombre crudo: un enum inglés en pantalla es un defecto que nadie reporta porque
    /// parece intencional.
    /// </summary>
    public const string Unknown = "Estado desconocido";

    public static string Appointment(string? status) => status switch
    {
        "Pending" => "Pendiente",
        "Confirmed" => "Confirmada",
        "Rejected" => "Rechazada",
        "Cancelled" => "Cancelada",
        "Completed" => "Completada",
        "NoShow" => "No asistió",
        _ => Unknown
    };

    public static string Order(string? status) => status switch
    {
        "Pending" => "Pendiente",
        "Accepted" => "Aceptado",
        "Rejected" => "Rechazado",
        "Preparing" => "En preparación",
        "ReadyForPickup" => "Listo",
        "Delivered" => "Entregado",
        "Cancelled" => "Cancelado",
        _ => Unknown
    };

    /// <summary>
    /// El turno visto por quien atiende. "Called" es "Llamado" y no "Te estamos llamando": esa
    /// segunda frase le habla al cliente, y en el tablero del negocio se leía como si el turno
    /// estuviera hablándole a la operadora.
    /// </summary>
    public static string QueueTicket(string? status) => status switch
    {
        "Waiting" => "En espera",
        "Called" => "Llamado",
        "InService" => "En atención",
        "Completed" => "Atendido",
        "Skipped" => "Omitido",
        "Cancelled" => "Cancelado",
        _ => Unknown
    };
}
