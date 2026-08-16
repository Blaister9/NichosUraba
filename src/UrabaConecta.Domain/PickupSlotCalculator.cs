namespace UrabaConecta.Domain;

/// <summary>
/// Las franjas de recogida que existirían en un rango de días, antes de mirar cuántas están
/// ocupadas. Vive aquí porque la pantalla de pedidos y el feed de la Home tienen que ofrecer
/// exactamente las mismas: si una calculara sus horas por su cuenta, la Home podría anunciar
/// "recoges hoy 3:15 pm" y la pantalla de pedidos no tener esa franja.
/// </summary>
public static class PickupSlotCalculator
{
    /// <param name="earliestUtc">
    /// El primer instante admisible, ya con la preparación mínima sumada. Las franjas anteriores no
    /// se generan: no se puede recoger algo que todavía no da tiempo de preparar.
    /// </param>
    public static IReadOnlyList<DateTimeOffset> Candidates(IEnumerable<DateOnly> dates,
        IEnumerable<(DayOfWeek Day, TimeOnly OpensAt, TimeOnly ClosesAt)> hours,
        TimeOnly receivesFrom, TimeOnly receivesUntil, int slotIntervalMinutes,
        TimeZoneInfo zone, DateTimeOffset earliestUtc)
    {
        var byDay = hours.ToLookup(x => x.Day);
        var candidates = new List<DateTimeOffset>();
        foreach (var day in dates)
        {
            // Un día puede tener varios tramos. Se recorre cada uno por separado, así que entre
            // 14:00 y 17:00 —la pausa— no se genera ninguna franja.
            var intervals = BusinessSchedule.Normalize(byDay[day.DayOfWeek]
                .Select(x => new ScheduleInterval(x.OpensAt, x.ClosesAt)));
            foreach (var interval in intervals)
            {
                var from = interval.OpensAt > receivesFrom ? interval.OpensAt : receivesFrom;
                var until = interval.ClosesAt < receivesUntil ? interval.ClosesAt : receivesUntil;
                if (until <= from) continue;
                for (var local = day.ToDateTime(from);
                     local.AddMinutes(slotIntervalMinutes) <= day.ToDateTime(until);
                     local = local.AddMinutes(slotIntervalMinutes))
                {
                    var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
                        DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone));
                    if (start < earliestUtc) continue;
                    candidates.Add(start);
                }
            }
        }
        return candidates;
    }
}
