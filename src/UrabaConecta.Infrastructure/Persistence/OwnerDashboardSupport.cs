using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UrabaConecta.Application;

namespace UrabaConecta.Infrastructure.Persistence;

/// <summary>
/// Las zonas horarias de todos los negocios del panel en una sola consulta. Leerlas negocio a negocio
/// costaría una ida y vuelta por establecimiento antes siquiera de empezar a contar.
/// </summary>
public sealed class BusinessTimeZoneResolver(AppDbContext db) : IBusinessTimeZoneResolver
{
    public async Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(IReadOnlyCollection<Guid> businessIds,
        CancellationToken cancellationToken = default)
    {
        if (businessIds.Count == 0) return new Dictionary<Guid, string>();
        return await db.Businesses.AsNoTracking()
            .Where(x => businessIds.Contains(x.Id))
            .Select(x => new { x.Id, x.TimeZoneId })
            .ToDictionaryAsync(x => x.Id, x => x.TimeZoneId, cancellationToken);
    }
}

/// <summary>
/// Deja el aviso de zona horaria inválida en la traza estructurada. El panel no se detiene por esto,
/// así que sin registro el error sería invisible: el negocio mostraría el día de Bogotá y nadie
/// sabría que su configuración está mal escrita.
/// </summary>
public sealed class OwnerDashboardDiagnostics(ILogger<OwnerDashboardDiagnostics> logger)
    : IOwnerDashboardDiagnostics
{
    public void InvalidTimeZone(Guid businessId, string timeZoneId)
        => logger.LogWarning(
            "El negocio {NegocioId} tiene una zona horaria que no existe ({ZonaHoraria}); " +
            "su resumen se calcula con la de Bogotá.", businessId, timeZoneId);
}
