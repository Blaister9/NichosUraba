namespace UrabaConecta.Domain;

public static class AppointmentSlotCalculator
{
    public static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> Calculate(
        DateOnly date, TimeOnly opensAt, TimeOnly closesAt, int durationMinutes,
        TimeZoneInfo timeZone, DateTimeOffset nowUtc,
        IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> occupied, int stepMinutes = 15)
    {
        if (durationMinutes is < 5 or > 480 || closesAt <= opensAt)
            throw new DomainException("INVALID_AVAILABILITY", "La configuración de disponibilidad no es válida.");

        var localStart = date.ToDateTime(opensAt, DateTimeKind.Unspecified);
        var localClose = date.ToDateTime(closesAt, DateTimeKind.Unspecified);
        var result = new List<(DateTimeOffset, DateTimeOffset)>();
        for (var cursor = localStart; cursor.AddMinutes(durationMinutes) <= localClose; cursor = cursor.AddMinutes(stepMinutes))
        {
            var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(cursor, timeZone), TimeSpan.Zero);
            var end = start.AddMinutes(durationMinutes);
            if (start <= nowUtc) continue;
            if (occupied.Any(x => x.Start < end && x.End > start)) continue;
            result.Add((start, end));
        }
        return result;
    }
}
