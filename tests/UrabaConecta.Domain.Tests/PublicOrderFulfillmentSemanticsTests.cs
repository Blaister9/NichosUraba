using UrabaConecta.Contracts;

namespace UrabaConecta.Domain.Tests;

public sealed class PublicOrderFulfillmentSemanticsTests
{
    [Fact]
    public void Public_pickup_exposes_only_its_public_address_and_uses_pickup_copy()
    {
        var copy = PublicOrderFulfillmentSemantics.For("PickupAtPublicLocation", "PublicPhysical",
            "Calle 1 # 2-3", "Apartadó");

        Assert.True(copy.ShowStreetAddress);
        Assert.Equal("Calle 1 # 2-3, Apartadó", copy.PublicPlace);
        Assert.Equal("Pedir para recoger", copy.PrimaryAction);
        Assert.Equal("Hora para recoger", copy.SlotLabel);
    }

    [Theory]
    [InlineData("PrivatePhysical", "Coordinated", "Hacer pedido", "Franja preferida")]
    [InlineData("Virtual", "Coordinated", "Hacer pedido", "Franja preferida")]
    [InlineData("Virtual", "ExternalDelivery", "Hacer pedido", "Franja de entrega preferida")]
    public void Non_public_fulfillment_never_exposes_the_supplied_address(string location, string mode,
        string action, string slotLabel)
    {
        var copy = PublicOrderFulfillmentSemantics.For(mode, location,
            "SECRETO: carrera privada 99", "Apartadó");

        Assert.False(copy.ShowStreetAddress);
        Assert.Equal("Apartadó", copy.PublicPlace);
        Assert.DoesNotContain("SECRETO", string.Join(' ', copy.PublicPlace, copy.FulfillmentSummary,
            copy.PaymentNotice, copy.ConfirmationText));
        Assert.Equal(action, copy.PrimaryAction);
        Assert.Equal(slotLabel, copy.SlotLabel);
    }

    [Fact]
    public void External_delivery_does_not_invent_logistics()
    {
        var copy = PublicOrderFulfillmentSemantics.For("ExternalDelivery", "Virtual");
        var text = string.Join(' ', copy.SectionDescription, copy.FulfillmentSummary,
            copy.PaymentNotice, copy.ConfirmationText);

        Assert.Contains("servicio externo", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tarifa", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("transportadora", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tiempo estimado", text, StringComparison.OrdinalIgnoreCase);
    }
}
