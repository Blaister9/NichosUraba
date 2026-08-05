using System.Globalization;
using System.Text.RegularExpressions;

namespace UrabaConecta.Domain;

public enum DepositType { None, FixedAmount, Percentage }

/// <summary>
/// Estado del adelanto de una cita. UrabáConecta no mueve dinero: sólo registra en qué punto del
/// acuerdo manual entre cliente y negocio está el comprobante.
/// </summary>
public enum DepositStatus { NotRequired, Pending, Reported, Verified, Rejected }

/// <summary>Quién provocó un cambio de estado del adelanto, para poder auditarlo.</summary>
public enum DepositActorKind { Customer, Business, PlatformAdmin }

/// <summary>
/// Política de adelanto de un servicio. Es un valor validado: si existe, es coherente. Concentrar
/// aquí las reglas evita que la configuración, la creación de la cita y la ficha pública decidan
/// cosas distintas sobre el mismo adelanto.
/// </summary>
public sealed partial record DepositPolicy(bool RequiresDeposit, DepositType Type, decimal Value,
    string Instructions, string WhatsAppNumber)
{
    public const int MaximumInstructionsLength = 400;

    /// <summary>Un servicio que no cobra adelanto. Es el valor por omisión y el de la migración.</summary>
    public static DepositPolicy None { get; } = new(false, DepositType.None, 0, "", "");

    /// <summary>
    /// Construye la política validada contra el precio del servicio, porque un valor fijo sólo
    /// tiene sentido en relación con ese precio.
    /// </summary>
    public static DepositPolicy Create(bool requiresDeposit, DepositType type, decimal value,
        string? instructions, string? whatsAppNumber, decimal servicePrice)
    {
        // Sin adelanto no se conserva configuración residual: es exactamente la política vacía.
        if (!requiresDeposit || type == DepositType.None)
        {
            if (requiresDeposit)
                throw new DomainException("DEPOSIT_TYPE_REQUIRED",
                    "Elija si el adelanto es un valor fijo o un porcentaje.");
            return None;
        }
        if (value < 0)
            throw new DomainException("INVALID_DEPOSIT_VALUE", "El adelanto no puede ser negativo.");
        if (value == 0)
            throw new DomainException("INVALID_DEPOSIT_VALUE", "El adelanto debe ser mayor que cero.");
        if (type == DepositType.FixedAmount && value > servicePrice)
            throw new DomainException("DEPOSIT_ABOVE_PRICE",
                "El adelanto no puede superar el precio del servicio.");
        if (type == DepositType.Percentage && value > 100)
            throw new DomainException("INVALID_DEPOSIT_PERCENTAGE",
                "El porcentaje debe estar entre 1 y 100.");
        var cleanInstructions = instructions?.Trim() ?? "";
        if (cleanInstructions.Length == 0)
            throw new DomainException("DEPOSIT_INSTRUCTIONS_REQUIRED",
                "Escriba cómo debe hacerse el adelanto.");
        if (cleanInstructions.Length > MaximumInstructionsLength)
            throw new DomainException("DEPOSIT_INSTRUCTIONS_TOO_LONG",
                $"Las instrucciones admiten máximo {MaximumInstructionsLength} caracteres.");
        return new(true, type, value, cleanInstructions, WhatsAppNumbers.Normalize(whatsAppNumber));
    }

    /// <summary>
    /// El adelanto en pesos enteros. Un porcentaje del 50 % sobre $80.000 son $40.000; un valor
    /// fijo se respeta tal cual. Sin adelanto siempre es cero.
    /// </summary>
    public decimal CalculateFor(decimal servicePrice) => !RequiresDeposit ? 0m : Type switch
    {
        DepositType.FixedAmount => Money.ToWholePesos(Value),
        DepositType.Percentage => Money.ToWholePesos(servicePrice * Value / 100m),
        _ => 0m
    };
}

/// <summary>Redondeo del peso colombiano: no existen centavos en este flujo.</summary>
public static class Money
{
    public static decimal ToWholePesos(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);

    /// <summary>Formato coloquial colombiano: "$40.000".</summary>
    public static string Display(decimal value)
        => value.ToString("C0", CultureInfo.GetCultureInfo("es-CO"));
}

/// <summary>
/// Números de WhatsApp para enlaces wa.me. No se integra ninguna API de Meta: el enlace abre la
/// conversación y es la persona quien envía el comprobante.
/// </summary>
public static partial class WhatsAppNumbers
{
    /// <summary>
    /// Deja sólo dígitos y exige código de país, porque wa.me no acepta ni "+" ni espacios y un
    /// número local no identifica a nadie fuera de su país.
    /// </summary>
    public static string Normalize(string? value)
    {
        var digits = NonDigits().Replace(value ?? "", "");
        if (digits.Length == 0)
            throw new DomainException("DEPOSIT_WHATSAPP_REQUIRED",
                "Indique el WhatsApp que recibirá los comprobantes.");
        if (digits.Length is < 11 or > 15 || digits[0] == '0')
            throw new DomainException("INVALID_DEPOSIT_WHATSAPP",
                "Escriba el número con código de país y sin signos, por ejemplo 573001234567.");
        return digits;
    }

    /// <summary>Enlace wa.me con el mensaje ya codificado. Nunca lleva "+" ni espacios.</summary>
    public static string BuildLink(string number, string message)
        => $"https://wa.me/{NonDigits().Replace(number, "")}?text={Uri.EscapeDataString(message)}";

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigits();
}

/// <summary>
/// El mensaje que el cliente envía con su comprobante. Lleva lo justo para que el negocio
/// identifique la cita: ni teléfono ni nombre del solicitante, que el negocio ya tiene.
/// </summary>
public static class DepositMessage
{
    public static string Build(string businessName, string serviceName, DateTimeOffset startUtc,
        string timeZoneId, string trackingCode, decimal depositAmount, decimal servicePrice)
    {
        var colombia = CultureInfo.GetCultureInfo("es-CO");
        var local = ToLocal(startUtc, timeZoneId);
        return $"""
            Hola, realicé el adelanto de mi cita.

            Negocio: {businessName}
            Servicio: {serviceName}
            Fecha: {local.ToString("dddd d 'de' MMMM 'de' yyyy", colombia)}
            Hora: {local.ToString("h:mm tt", colombia)}
            Código: {trackingCode}
            Valor del adelanto: {Money.Display(depositAmount)}
            Valor total: {Money.Display(servicePrice)}

            Adjunto el comprobante para su verificación.
            """;
    }

    private static DateTimeOffset ToLocal(DateTimeOffset value, string timeZoneId)
    {
        try { return TimeZoneInfo.ConvertTime(value, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)); }
        catch (TimeZoneNotFoundException) { return value; }
    }
}

/// <summary>
/// Rastro de cada cambio de estado del adelanto. Se guarda aparte de la cita porque la cita sólo
/// conserva el estado actual y aquí interesa quién lo movió y cuándo.
/// </summary>
public sealed class AppointmentDepositAudit : IBusinessOwned
{
    private AppointmentDepositAudit() { }
    public AppointmentDepositAudit(Guid id, Guid businessId, Guid appointmentId, DepositActorKind actorKind,
        Guid? actorUserId, DepositStatus previousStatus, DepositStatus newStatus, DateTimeOffset occurredAtUtc,
        string? reason = null)
    {
        (Id, BusinessId, AppointmentId, ActorKind, ActorUserId) =
            (id, businessId, appointmentId, actorKind, actorUserId);
        (PreviousStatus, NewStatus, OccurredAtUtc) = (previousStatus, newStatus, occurredAtUtc);
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid AppointmentId { get; private set; }
    public DepositActorKind ActorKind { get; private set; }
    /// <summary>Nulo cuando actúa el cliente: se identifica con su código, no con una cuenta.</summary>
    public Guid? ActorUserId { get; private set; }
    public DepositStatus PreviousStatus { get; private set; }
    public DepositStatus NewStatus { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string? Reason { get; private set; }
}
