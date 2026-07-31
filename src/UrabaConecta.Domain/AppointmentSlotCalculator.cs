namespace UrabaConecta.Domain;

public static class AppointmentSlotCalculator
{
    /// <summary>
    /// Calcula las horas disponibles de un día a partir de sus tramos de atención. Una cita debe
    /// empezar y terminar dentro del MISMO tramo: con 08:00–12:00 y 14:00–18:00 y un servicio de
    /// 60 minutos, las 11:30 no valen porque terminarían a las 12:30, ya en la pausa.
    /// </summary>
    public static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> Calculate(
        DateOnly date, IEnumerable<ScheduleInterval> intervals, int durationMinutes,
        TimeZoneInfo timeZone, DateTimeOffset nowUtc,
        IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> occupied, int stepMinutes = 15)
    {
        if (durationMinutes is < 5 or > 480)
            throw new DomainException("INVALID_AVAILABILITY", "La configuración de disponibilidad no es válida.");
        var ordered = BusinessSchedule.Normalize(intervals);
        var occupiedList = occupied as IReadOnlyCollection<(DateTimeOffset Start, DateTimeOffset End)>
                           ?? occupied.ToList();

        var result = new List<(DateTimeOffset, DateTimeOffset)>();
        foreach (var interval in ordered)
        {
            // El recorrido se reinicia en cada tramo, de modo que la pausa no arrastra el paso y
            // la tarde vuelve a empezar en hora exacta.
            var localOpen = date.ToDateTime(interval.OpensAt, DateTimeKind.Unspecified);
            var localClose = date.ToDateTime(interval.ClosesAt, DateTimeKind.Unspecified);
            for (var cursor = localOpen; cursor.AddMinutes(durationMinutes) <= localClose;
                 cursor = cursor.AddMinutes(stepMinutes))
            {
                var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(cursor, timeZone), TimeSpan.Zero);
                var end = start.AddMinutes(durationMinutes);
                if (start <= nowUtc) continue;
                var choca = false;
                foreach (var busy in occupiedList)
                    if (busy.Start < end && busy.End > start) { choca = true; break; }
                if (choca) continue;
                result.Add((start, end));
            }
        }
        return result.OrderBy(x => x.Item1).ToList();
    }

    /// <summary>Sobrecarga de un solo tramo, para jornadas continuas.</summary>
    public static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> Calculate(
        DateOnly date, TimeOnly opensAt, TimeOnly closesAt, int durationMinutes,
        TimeZoneInfo timeZone, DateTimeOffset nowUtc,
        IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> occupied, int stepMinutes = 15)
        => Calculate(date, [new ScheduleInterval(opensAt, closesAt)], durationMinutes, timeZone, nowUtc,
            occupied, stepMinutes);
}
