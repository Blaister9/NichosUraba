using UrabaConecta.Domain;

namespace UrabaConecta.Domain.Tests;

public sealed class OrderingTests
{
    private static readonly Guid BusinessId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Product_rejects_negative_price()
        => Assert.Equal("INVALID_PRODUCT", Assert.Throws<DomainException>(() =>
            new Product(Guid.NewGuid(), BusinessId, Guid.NewGuid(), "Arroz", null, -1)).Code);

    [Fact]
    public void Product_update_uses_optimistic_version()
    {
        var product = new Product(Guid.NewGuid(), BusinessId, Guid.NewGuid(), "Arroz", null, 100);
        product.Update(product.ProductCategoryId, "Arroz", null, 200, 0, true, 0);
        Assert.Equal("CONCURRENCY_CONFLICT", Assert.Throws<DomainException>(() =>
            product.Update(product.ProductCategoryId, "Otro", null, 300, 0, true, 0)).Code);
    }

    [Fact]
    public void Settings_allocate_consecutive_numbers()
    {
        var settings = Settings();
        Assert.Equal(1001, settings.AllocateNumber());
        Assert.Equal(1002, settings.AllocateNumber());
    }

    [Fact]
    public void Settings_reject_invalid_time_range()
        => Assert.Equal("INVALID_ORDER_SETTINGS", Assert.Throws<DomainException>(() =>
            new PickupOrderSettings(Guid.NewGuid(), BusinessId, true, null, 30, 15, 5,
                new TimeOnly(20, 0), new TimeOnly(11, 0))).Code);

    [Fact]
    public void Line_freezes_name_price_and_total()
    {
        var line = Line(2);
        Assert.Equal("Hamburguesa", line.ProductNameSnapshot);
        Assert.Equal(44000, line.LineTotal);
    }

    [Fact]
    public void Order_calculates_total_from_snapshots()
    {
        var order = Order();
        Assert.Equal(44000, order.Total);
        Assert.Equal(PickupOrderStatus.Pending, order.Status);
    }

    [Fact]
    public void Valid_operational_flow_reaches_delivered()
    {
        var order = Order();
        order.Transition(PickupOrderStatus.Accepted, Now.AddMinutes(1), 0);
        order.Transition(PickupOrderStatus.Preparing, Now.AddMinutes(2), 1);
        order.Transition(PickupOrderStatus.ReadyForPickup, Now.AddMinutes(3), 2);
        order.Transition(PickupOrderStatus.Delivered, Now.AddMinutes(4), 3);
        Assert.Equal(PickupOrderStatus.Delivered, order.Status);
    }

    [Fact]
    public void Invalid_transition_is_rejected()
        => Assert.Equal("INVALID_ORDER_TRANSITION", Assert.Throws<DomainException>(() =>
            Order().Transition(PickupOrderStatus.Delivered, Now, 0)).Code);

    [Fact]
    public void Cancellation_requires_reason()
        => Assert.Equal("ORDER_REASON_REQUIRED", Assert.Throws<DomainException>(() =>
            Order().Transition(PickupOrderStatus.Cancelled, Now, 0)).Code);

    [Fact]
    public void Orders_permission_is_effective_immediately()
    {
        var member = new BusinessMembership(Guid.NewGuid(), BusinessId, Guid.NewGuid(), MembershipRole.Worker);
        Assert.False(member.HasPermission(false, false, false, false, true));
        member.UpdatePermissions(false, false, false, false, true, Now, 0);
        Assert.True(member.HasPermission(false, false, false, false, true));
    }

    private static PickupOrderSettings Settings() => new(Guid.NewGuid(), BusinessId, true, null,
        30, 15, 5, new TimeOnly(11, 0), new TimeOnly(20, 0));
    private static PickupOrderLine Line(int quantity = 1) => new(Guid.NewGuid(), BusinessId, Guid.NewGuid(),
        Guid.NewGuid(), "Hamburguesa", 22000, quantity);
    private static PickupOrder Order()
    {
        var id = Guid.NewGuid();
        var line = new PickupOrderLine(Guid.NewGuid(), BusinessId, id, Guid.NewGuid(), "Hamburguesa", 22000, 2);
        return new PickupOrder(id, BusinessId, 1001, Now.AddHours(1), Now.AddHours(1).AddMinutes(15),
            "alias-protegido", "telefono-protegido", "1234", null, "hash", "pilot-1", Now, Now, [line]);
    }
}
