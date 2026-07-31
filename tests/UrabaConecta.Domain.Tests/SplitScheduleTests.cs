using UrabaConecta.Domain;

namespace UrabaConecta.Domain.Tests;

/// <summary>
/// Jornadas partidas. El ejemplo de referencia es 08:00–12:00 y 14:00–18:00 con un servicio de
/// 60 minutos: la pausa del mediodía no debe poder reservarse ni por delante ni por detrás.
/// </summary>
public sealed class SplitScheduleTests
{
    private static readonly TimeZoneInfo Bogota = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");
    private static readonly DateOnly Lunes = new(2026, 8, 3);
    private static readonly DateTimeOffset Antes = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly ScheduleInterval[] JornadaPartida =
    [
        new(new TimeOnly(8, 0), new TimeOnly(12, 0)),
        new(new TimeOnly(14, 0), new TimeOnly(18, 0)),
    ];

    private static IReadOnlyList<TimeOnly> HorasLocales(IEnumerable<ScheduleInterval> intervals, int duracion)
        => AppointmentSlotCalculator
            .Calculate(Lunes, intervals, duracion, Bogota, Antes, [], stepMinutes: 30)
            .Select(x => TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.Start, Bogota).DateTime))
            .ToList();

    [Fact]
    public void A_continuous_day_is_a_single_interval()
    {
        var normalized = BusinessSchedule.Normalize([new ScheduleInterval(new(8, 0), new(18, 0))]);
        Assert.Single(normalized);
        Assert.Equal(new TimeOnly(8, 0), normalized[0].OpensAt);
        Assert.Equal(new TimeOnly(18, 0), normalized[0].ClosesAt);
    }

    [Fact]
    public void A_day_accepts_two_intervals_and_orders_them()
    {
        var normalized = BusinessSchedule.Normalize(
            [new ScheduleInterval(new(14, 0), new(18, 0)), new ScheduleInterval(new(8, 0), new(12, 0))]);
        Assert.Equal(2, normalized.Count);
        Assert.Equal(new TimeOnly(8, 0), normalized[0].OpensAt);
        Assert.Equal(new TimeOnly(14, 0), normalized[1].OpensAt);
    }

    [Fact]
    public void Overlapping_intervals_are_rejected()
    {
        var error = Assert.Throws<DomainException>(() => BusinessSchedule.Normalize(
            [new ScheduleInterval(new(8, 0), new(13, 0)), new ScheduleInterval(new(12, 0), new(18, 0))]));
        Assert.Equal("OVERLAPPING_INTERVALS", error.Code);
    }

    [Fact]
    public void Contiguous_intervals_are_allowed()
    {
        // 12:00–14:00 pegado a 14:00–18:00 no se solapa: es una jornada legítima.
        var normalized = BusinessSchedule.Normalize(
            [new ScheduleInterval(new(8, 0), new(14, 0)), new ScheduleInterval(new(14, 0), new(18, 0))]);
        Assert.Equal(2, normalized.Count);
    }

    [Fact]
    public void An_interval_that_closes_before_it_opens_is_rejected()
    {
        var error = Assert.Throws<DomainException>(() => BusinessSchedule.Normalize(
            [new ScheduleInterval(new(18, 0), new(8, 0))]));
        Assert.Equal("INVALID_HOURS", error.Code);
    }

    [Fact]
    public void A_closed_day_has_no_intervals_and_offers_no_slots()
    {
        Assert.Empty(BusinessSchedule.Normalize([]));
        Assert.Empty(HorasLocales([], 60));
    }

    [Fact]
    public void More_intervals_than_allowed_are_rejected()
    {
        var demasiados = Enumerable.Range(0, BusinessSchedule.MaximumIntervalsPerDay + 1)
            .Select(i => new ScheduleInterval(new TimeOnly(i, 0), new TimeOnly(i, 30)));
        Assert.Equal("TOO_MANY_INTERVALS",
            Assert.Throws<DomainException>(() => BusinessSchedule.Normalize(demasiados)).Code);
    }

    [Fact]
    public void The_split_day_offers_exactly_the_expected_hours()
    {
        // El enunciado de la misión, comprobado literalmente.
        var horas = HorasLocales(JornadaPartida, 60);
        Assert.Equal(
            [new TimeOnly(8, 0), new TimeOnly(8, 30), new TimeOnly(9, 0), new TimeOnly(9, 30),
             new TimeOnly(10, 0), new TimeOnly(10, 30), new TimeOnly(11, 0),
             new TimeOnly(14, 0), new TimeOnly(14, 30), new TimeOnly(15, 0), new TimeOnly(15, 30),
             new TimeOnly(16, 0), new TimeOnly(16, 30), new TimeOnly(17, 0)],
            horas);
    }

    [Theory]
    [InlineData(11, 30)]   // terminaría a las 12:30, ya dentro de la pausa
    [InlineData(12, 0)]    // la pausa
    [InlineData(13, 0)]    // la pausa
    [InlineData(13, 30)]   // no cabe entera antes de las 14:00
    [InlineData(17, 30)]   // terminaría a las 18:30, después de cerrar
    public void An_appointment_that_does_not_fit_inside_one_interval_is_not_offered(int hora, int minuto)
    {
        Assert.DoesNotContain(new TimeOnly(hora, minuto), HorasLocales(JornadaPartida, 60));
    }

    [Theory]
    [InlineData(8, 0)]
    [InlineData(11, 0)]
    [InlineData(14, 0)]
    [InlineData(17, 0)]
    public void An_appointment_that_fits_inside_an_interval_is_offered(int hora, int minuto)
    {
        Assert.Contains(new TimeOnly(hora, minuto), HorasLocales(JornadaPartida, 60));
    }

    [Fact]
    public void An_appointment_may_not_span_the_pause_even_if_both_ends_are_open()
    {
        // Un servicio de 4 horas cabe en cada tramo justo, pero jamás a caballo de la pausa.
        var horas = HorasLocales(JornadaPartida, 240);
        Assert.Equal([new TimeOnly(8, 0), new TimeOnly(14, 0)], horas);
    }

    [Fact]
    public void Open_now_distinguishes_the_pause_from_the_end_of_the_day()
    {
        Assert.NotNull(BusinessSchedule.IntervalAt(JornadaPartida, new TimeOnly(9, 0)));
        Assert.NotNull(BusinessSchedule.IntervalAt(JornadaPartida, new TimeOnly(15, 0)));
        // A las 13:00 está en pausa: cerrado, pero con jornada por delante.
        Assert.Null(BusinessSchedule.IntervalAt(JornadaPartida, new TimeOnly(13, 0)));
        Assert.Equal(new TimeOnly(14, 0), BusinessSchedule.NextInterval(JornadaPartida, new TimeOnly(13, 0))!.Value.OpensAt);
        // A las 19:00 ya no queda nada: cerrado por hoy.
        Assert.Null(BusinessSchedule.IntervalAt(JornadaPartida, new TimeOnly(19, 0)));
        Assert.Null(BusinessSchedule.NextInterval(JornadaPartida, new TimeOnly(19, 0)));
    }

    [Fact]
    public void An_interval_may_not_wrap_past_midnight()
    {
        // TimeOnly.AddHours da la vuelta al día en silencio, así que un tramo construido a ciegas
        // podía quedar como 23:00–01:00. El dominio lo rechaza porque el cierre no es posterior.
        Assert.Equal("INVALID_HOURS", Assert.Throws<DomainException>(() => BusinessSchedule.Normalize(
            [new ScheduleInterval(new(23, 0), new TimeOnly(23, 0).AddHours(2))])).Code);
    }

    [Fact]
    public void A_service_that_does_not_fit_in_any_interval_yields_no_slots()
    {
        // Cinco horas no caben en ningún tramo de cuatro, ni sumando los dos.
        Assert.Empty(HorasLocales(JornadaPartida, 300));
    }

    [Fact]
    public void Occupied_time_still_removes_slots_inside_an_interval()
    {
        var ocupado = new DateTimeOffset(2026, 8, 3, 15, 0, 0, TimeSpan.Zero);   // 10:00 en Bogotá
        var horas = AppointmentSlotCalculator
            .Calculate(Lunes, JornadaPartida, 60, Bogota, Antes,
                [(ocupado, ocupado.AddMinutes(60))], stepMinutes: 60)
            .Select(x => TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.Start, Bogota).DateTime))
            .ToList();
        Assert.DoesNotContain(new TimeOnly(10, 0), horas);
        Assert.Contains(new TimeOnly(9, 0), horas);
        Assert.Contains(new TimeOnly(14, 0), horas);
    }
}
