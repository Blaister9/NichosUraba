namespace UrabaConecta.Domain;

/// <summary>Un tramo de atención dentro de un mismo día.</summary>
public readonly record struct ScheduleInterval(TimeOnly OpensAt, TimeOnly ClosesAt)
{
    public int DurationMinutes => (int)(ClosesAt - OpensAt).TotalMinutes;
    public bool Contains(TimeOnly moment) => moment >= OpensAt && moment < ClosesAt;
    /// <summary>Si un servicio que empieza en <paramref name="start"/> cabe entero en el tramo.</summary>
    public bool Fits(TimeOnly start, int durationMinutes)
        => start >= OpensAt && start.AddMinutes(durationMinutes) <= ClosesAt
           && start.AddMinutes(durationMinutes) > start;   // descarta el cruce de medianoche
}

/// <summary>
/// Reglas de una jornada. Se concentran aquí para que la configuración, las citas y los pedidos
/// decidan lo mismo: un día es una lista ordenada de tramos que no se solapan, y un día sin tramos
/// está cerrado.
/// </summary>
public static class BusinessSchedule
{
    public const int MaximumIntervalsPerDay = 6;

    /// <summary>
    /// Ordena y valida los tramos de un día. Los contiguos se permiten —12:00–14:00 seguido de
    /// 14:00–18:00 es una jornada legítima—; solapados no. No se admite cruzar la medianoche en
    /// esta versión, cosa que ya garantiza exigir cierre posterior a apertura.
    /// </summary>
    public static IReadOnlyList<ScheduleInterval> Normalize(IEnumerable<ScheduleInterval> intervals)
    {
        var ordered = intervals.OrderBy(x => x.OpensAt).ThenBy(x => x.ClosesAt).ToList();
        if (ordered.Count > MaximumIntervalsPerDay)
            throw new DomainException("TOO_MANY_INTERVALS",
                $"Un día admite máximo {MaximumIntervalsPerDay} intervalos.");
        foreach (var interval in ordered)
            if (interval.ClosesAt <= interval.OpensAt)
                throw new DomainException("INVALID_HOURS", "La hora de cierre debe ser posterior a la de apertura.");
        for (var i = 1; i < ordered.Count; i++)
            if (ordered[i].OpensAt < ordered[i - 1].ClosesAt)
                throw new DomainException("OVERLAPPING_INTERVALS",
                    "Los intervalos de un mismo día no pueden solaparse.");
        return ordered;
    }

    /// <summary>El tramo que contiene el momento indicado, si el negocio está atendiendo.</summary>
    public static ScheduleInterval? IntervalAt(IEnumerable<ScheduleInterval> intervals, TimeOnly moment)
    {
        foreach (var interval in intervals)
            if (interval.Contains(moment)) return interval;
        return null;
    }

    /// <summary>
    /// El siguiente tramo que abre después del momento indicado. Sirve para distinguir "cerrado
    /// temporalmente", cuando todavía queda jornada por delante, de "cerrado" por hoy.
    /// </summary>
    public static ScheduleInterval? NextInterval(IEnumerable<ScheduleInterval> intervals, TimeOnly moment)
    {
        ScheduleInterval? next = null;
        foreach (var interval in intervals)
            if (interval.OpensAt > moment && (next is null || interval.OpensAt < next.Value.OpensAt))
                next = interval;
        return next;
    }
}
