using UrabaConecta.Domain;

namespace UrabaConecta.Domain.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void Service_rejects_invalid_duration_and_negative_price()
    {
        Assert.Equal("INVALID_DURATION", Assert.Throws<DomainException>(() =>
            new Service(Guid.NewGuid(), Guid.NewGuid(), "Corte", 0, 1)).Code);
        Assert.Equal("INVALID_PRICE", Assert.Throws<DomainException>(() =>
            new Service(Guid.NewGuid(), Guid.NewGuid(), "Corte", 30, -1)).Code);
    }

    [Fact]
    public void Service_activation_changes_do_not_change_identity()
    {
        var id = Guid.NewGuid();
        var service = new Service(id, Guid.NewGuid(), "Corte", 30, 10000, "Breve", 2);
        service.Update("Corte", 30, 10000, false, "Breve", 2, 0);
        Assert.False(service.IsActive);
        Assert.Equal(id, service.Id);
        service.Update("Corte", 30, 10000, true, "Breve", 2, 1);
        Assert.True(service.IsActive);
        Assert.Equal(2, service.Version);
    }

    [Fact]
    public void Service_detects_stale_version()
    {
        var service = new Service(Guid.NewGuid(), Guid.NewGuid(), "Corte", 30, 10000);
        service.Update("Corte", 30, 10000, true, expectedVersion: 0);
        var error = Assert.Throws<DomainException>(() =>
            service.Update("Cambio perdido", 30, 10000, true, expectedVersion: 0));
        Assert.Equal("CONCURRENCY_CONFLICT", error.Code);
    }

    [Fact]
    public void Business_hours_reject_equal_or_reversed_interval()
    {
        Assert.Throws<DomainException>(() => new BusinessHour(Guid.NewGuid(), Guid.NewGuid(),
            DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(9, 0)));
        Assert.Throws<DomainException>(() => new BusinessHour(Guid.NewGuid(), Guid.NewGuid(),
            DayOfWeek.Monday, new TimeOnly(10, 0), new TimeOnly(9, 0)));
    }

    [Fact]
    public void Exceptions_validate_intervals_and_full_day()
    {
        Assert.Throws<DomainException>(() => new AvailabilityException(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), new DateOnly(2026, 8, 1), AvailabilityExceptionType.ClosedInterval,
            new TimeOnly(12, 0), new TimeOnly(11, 0)));
        var closed = new AvailabilityException(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 8, 1), AvailabilityExceptionType.ClosedAllDay);
        Assert.True(closed.IsUnavailable);
        Assert.Null(closed.OpensAt);
    }

    [Fact]
    public void Staff_can_be_removed_from_future_availability_without_deletion()
    {
        var id = Guid.NewGuid();
        var staff = new StaffMember(id, Guid.NewGuid(), "Profesional");
        staff.Update("Profesional", true, false, 0);
        Assert.False(staff.ParticipatesInAvailability);
        Assert.True(staff.IsActive);
        Assert.Equal(id, staff.Id);
    }
}
