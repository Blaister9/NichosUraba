namespace UrabaConecta.Contracts;

/// <summary>
/// Semántica pública única de pedidos. Los componentes consumen este resultado en vez de decidir
/// por separado qué significa la franja, dónde se recibe el pedido o cómo se acuerda el pago.
/// </summary>
public sealed record PublicOrderFulfillmentCopy(
    string CapabilityLabel,
    string PrimaryAction,
    string PageTitle,
    string SectionTitle,
    string SectionDescription,
    string SlotLabel,
    string SlotHelp,
    string FulfillmentSummary,
    string PaymentNotice,
    string ConfirmationText,
    string TrackingTimePrefix,
    string TotalLabel,
    string CustomerInstructionsHeading,
    string AvailabilityPrefix,
    string ReadyStatusLabel,
    string PublicPlace,
    bool ShowStreetAddress);

public static class PublicOrderFulfillmentSemantics
{
    public const string PublicPhysical = "PublicPhysical";
    public const string PickupAtPublicLocation = "PickupAtPublicLocation";
    public const string Coordinated = "Coordinated";
    public const string ExternalDelivery = "ExternalDelivery";

    public static PublicOrderFulfillmentCopy For(string? fulfillmentMode, string? locationMode,
        string? publicAddress = null, string? municipality = null)
    {
        var publicPhysical = string.Equals(locationMode, PublicPhysical, StringComparison.Ordinal);
        var address = publicPhysical ? publicAddress?.Trim() ?? "" : "";
        var place = PublicPlace(address, municipality);
        var showStreetAddress = publicPhysical && address.Length > 0;

        return fulfillmentMode switch
        {
            Coordinated => new("Pedidos", "Hacer pedido", "Haz tu pedido", "Pedidos",
                "Arma tu pedido. La entrega o recogida se coordina directamente con el negocio.",
                "Franja preferida",
                "Elige una franja de referencia; el negocio confirmará los detalles contigo.",
                "Entrega o recogida coordinada con el negocio. El negocio confirmará los detalles.",
                "El pago se acuerda directamente con el negocio. UrabáConecta no procesa pagos.",
                "El negocio confirmará cómo recibirás el pedido y coordinará el pago contigo.",
                "Franja preferida", "Total del pedido", "Indicaciones del negocio",
                "franja preferida", "Pedido listo", place, showStreetAddress),
            ExternalDelivery => new("Pedidos", "Hacer pedido", "Haz tu pedido", "Pedidos",
                "Arma tu pedido. La entrega se coordina mediante un servicio externo.",
                "Franja de entrega preferida",
                "Elige una franja de referencia; el negocio confirmará la coordinación externa.",
                "Entrega mediante un servicio externo coordinado por el negocio.",
                "El pago y la entrega externa se coordinan directamente con el negocio. UrabáConecta no procesa pagos.",
                "El negocio confirmará los detalles de la entrega externa y el pago. UrabáConecta no ofrece seguimiento de mensajería.",
                "Franja de entrega preferida", "Total del pedido", "Indicaciones de entrega",
                "entrega preferida", "Pedido listo", place, showStreetAddress),
            _ => new("Pedidos", "Pedir para recoger", "Pedido para recoger", "Para recoger",
                "Arma tu pedido, elige a qué hora pasas y paga directamente con el negocio.",
                "Hora para recoger", "Elige la hora en que pasarás por el pedido.",
                place.Length > 0 ? $"Recogida en {place}." : "Recogida en la ubicación pública del negocio.",
                "El pago se realiza directamente con el negocio al recoger. UrabáConecta no procesa pagos.",
                "Pagas directamente con el negocio al recoger el pedido.",
                "Hora para recoger", "Total a pagar al recoger", "Antes de venir",
                "recoges", "Listo para recoger", place, showStreetAddress)
        };
    }

    private static string PublicPlace(string address, string? municipality)
    {
        var town = municipality?.Trim() ?? "";
        if (address.Length == 0) return town;
        if (town.Length == 0 || address.Contains(town, StringComparison.OrdinalIgnoreCase)) return address;
        return $"{address}, {town}";
    }
}
