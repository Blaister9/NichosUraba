using UrabaConecta.Domain;

namespace UrabaConecta.Application;

/// <summary>
/// Provisiona únicamente infraestructura neutra. Horarios, servicios, personal y productos son
/// decisiones humanas y permanecen visibles como pendientes en el checklist.
/// </summary>
public sealed class BusinessCapabilityProvisioner(IPlatformAdministrationStore store)
    : IBusinessCapabilityProvisioner
{
    public async Task ProvisionAsync(Guid businessId,
        IReadOnlyCollection<BusinessModuleKind> newlyEnabledOperations,
        OrderFulfillmentMode fulfillmentMode, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (newlyEnabledOperations.Contains(BusinessModuleKind.VirtualQueues) &&
            await store.GetQueueDefinitionAsync(businessId, cancellationToken) is null)
            store.AddQueueDefinition(new QueueDefinition(Guid.NewGuid(), businessId, "Atención general", 20, 20,
                "Toma tu turno y consulta el avance.", true, now));

        if (newlyEnabledOperations.Contains(BusinessModuleKind.PickupOrders) &&
            await store.GetPickupSettingsAsync(businessId, cancellationToken) is null)
            store.AddPickupSettings(new PickupOrderSettings(Guid.NewGuid(), businessId, true,
                FulfillmentMessage(fulfillmentMode), 30, 15, 5, new TimeOnly(8, 0), new TimeOnly(18, 0)));
    }

    public static string FulfillmentMessage(OrderFulfillmentMode mode) => mode switch
    {
        OrderFulfillmentMode.PickupAtPublicLocation => "Haz tu pedido y recógelo en el establecimiento.",
        OrderFulfillmentMode.ExternalDelivery => "Haz tu pedido; la entrega se coordina por un servicio externo.",
        _ => "Haz tu pedido y coordina la entrega o recogida con el negocio."
    };
}
