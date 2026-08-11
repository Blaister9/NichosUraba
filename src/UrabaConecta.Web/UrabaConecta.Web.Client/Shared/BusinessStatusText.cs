namespace UrabaConecta.Web.Client.Shared;

/// <summary>
/// Vocabulario en español de los estados del negocio. El servidor los transporta con el nombre
/// del enum ("Draft", "PendingReview"); ninguno de esos nombres debe llegar a la pantalla.
///
/// Vive aquí, y no dentro de una página, porque el panel de la socia y la ficha de preparación
/// deben decir exactamente lo mismo: dos traducciones distintas del mismo estado se leen como
/// dos estados distintos.
/// </summary>
public static class BusinessStatusText
{
    public const string Draft = "Draft";
    public const string PendingConfiguration = "PendingConfiguration";
    public const string PendingReview = "PendingReview";
    public const string Active = "Active";
    public const string Suspended = "Suspended";
    public const string Archived = "Archived";

    public static string Label(string? status) => status switch
    {
        Draft => "Borrador",
        PendingConfiguration => "Configuración pendiente",
        PendingReview => "En revisión",
        Active => "Publicado",
        Suspended => "Suspendido",
        Archived => "Archivado",
        // Un estado nuevo sin traducir no debe filtrar su nombre técnico a la pantalla.
        _ => "Sin estado"
    };

    /// <summary>Qué significa el estado para quien lo lee, en una línea.</summary>
    public static string Description(string? status) => status switch
    {
        Draft => "Todavía no es visible para nadie.",
        PendingConfiguration => "Falta completar información antes de poder publicarlo.",
        PendingReview => "Enviado a la administración. Espera respuesta.",
        Active => "Visible en el directorio y recibiendo clientes.",
        Suspended => "Fuera del directorio. El historial se conserva.",
        Archived => "Cerrado. Solo lectura.",
        _ => ""
    };

    /// <summary>Sufijo de la clase CSS de la píldora de estado, reutilizando la paleta existente.</summary>
    public static string CssModifier(string? status) => status switch
    {
        Active => "confirmed",
        PendingReview => "pending",
        Suspended or Archived => "cancelled",
        _ => "draft"
    };

    /// <summary>Un negocio en estos estados todavía está en manos de la socia.</summary>
    public static bool IsInPreparation(string? status)
        => status is Draft or PendingConfiguration;
}
