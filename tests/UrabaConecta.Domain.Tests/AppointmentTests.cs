using UrabaConecta.Domain;

namespace UrabaConecta.Domain.Tests;

public sealed class AppointmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Pending_can_be_confirmed_and_completed()
    {
        var appointment = NewAppointment();
        appointment.ChangeStatus(AppointmentStatus.Confirmed, Now.AddMinutes(1));
        appointment.ChangeStatus(AppointmentStatus.Completed, Now.AddHours(2));
        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
    }

    [Theory]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    public void Pending_rejects_invalid_transitions(AppointmentStatus target)
    {
        var appointment = NewAppointment();
        var error = Assert.Throws<DomainException>(() => appointment.ChangeStatus(target, Now.AddMinutes(1)));
        Assert.Equal("INVALID_STATE_TRANSITION", error.Code);
    }

    [Theory]
    [InlineData(AppointmentStatus.Rejected)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    public void Terminal_status_cannot_change(AppointmentStatus terminal)
    {
        var appointment = NewAppointment();
        if (terminal is AppointmentStatus.Completed or AppointmentStatus.NoShow)
            appointment.ChangeStatus(AppointmentStatus.Confirmed, Now.AddMinutes(1));
        appointment.ChangeStatus(terminal, Now.AddMinutes(2));
        Assert.Throws<DomainException>(() => appointment.ChangeStatus(AppointmentStatus.Cancelled, Now.AddMinutes(3)));
    }

    [Fact]
    public void Appointment_end_is_start_plus_snapshot_duration()
    {
        var appointment = NewAppointment();
        Assert.Equal(60, (appointment.EndAtUtc - appointment.StartAtUtc).TotalMinutes);
    }

    [Fact]
    public void Appointment_in_the_past_is_rejected()
    {
        var error = Assert.Throws<DomainException>(() => NewAppointment(Now.AddMinutes(-1)));
        Assert.Equal("APPOINTMENT_IN_PAST", error.Code);
    }

    [Fact]
    public void Consent_is_required()
    {
        Assert.Throws<DomainException>(() => new Appointment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Now.AddHours(1), 60, "Corte", 35000, "alias", "phone", "1234", "",
            "hash", 1, Guid.Empty, Now));
    }

    private static Appointment NewAppointment(DateTimeOffset? start = null) => new(Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), start ?? Now.AddHours(1), 60, "Corte", 35000, "alias-protegido",
        "telefono-protegido", "1234", "", "hash", 1, Guid.NewGuid(), Now);
}
