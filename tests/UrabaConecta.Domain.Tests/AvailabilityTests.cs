using UrabaConecta.Domain;

namespace UrabaConecta.Domain.Tests;

public sealed class AvailabilityTests
{
    private static readonly TimeZoneInfo Bogota = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");

    [Fact]
    public void Calculates_slots_inside_business_hours()
    {
        var slots = AppointmentSlotCalculator.Calculate(new DateOnly(2026, 8, 3), new TimeOnly(8, 0),
            new TimeOnly(10, 0), 60, Bogota, new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), []);
        Assert.Equal(5, slots.Count);
        Assert.All(slots, slot => Assert.Equal(60, (slot.End - slot.Start).TotalMinutes));
    }

    [Fact]
    public void Removes_every_slot_that_overlaps_an_active_appointment()
    {
        var occupied = new[] { (new DateTimeOffset(2026, 8, 3, 14, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 3, 15, 0, 0, TimeSpan.Zero)) };
        var slots = AppointmentSlotCalculator.Calculate(new DateOnly(2026, 8, 3), new TimeOnly(8, 0),
            new TimeOnly(11, 0), 60, Bogota, new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), occupied);
        Assert.DoesNotContain(slots, slot => slot.Start < occupied[0].Item2 && slot.End > occupied[0].Item1);
    }

    [Fact]
    public void Does_not_offer_past_slots()
    {
        var slots = AppointmentSlotCalculator.Calculate(new DateOnly(2026, 8, 3), new TimeOnly(8, 0),
            new TimeOnly(9, 0), 45, Bogota, new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero), []);
        Assert.Empty(slots);
    }

    [Fact]
    public void Invalid_or_out_of_order_hours_are_rejected()
    {
        Assert.Throws<DomainException>(() => AppointmentSlotCalculator.Calculate(new DateOnly(2026, 8, 3),
            new TimeOnly(10, 0), new TimeOnly(9, 0), 45, Bogota, DateTimeOffset.MinValue, []));
    }

    [Fact]
    public void Inactive_service_is_rejected()
    {
        var service = new Service(Guid.NewGuid(), Guid.NewGuid(), "Corte", 60, 35000);
        service.Update("Corte", 60, 35000, false);
        var error = Assert.Throws<DomainException>(service.EnsureActive);
        Assert.Equal("SERVICE_INACTIVE", error.Code);
    }
}
