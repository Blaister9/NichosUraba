using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UrabaConecta.Infrastructure.Persistence;

/// <summary>
/// Resultado del intento de migración del arranque. Es un singleton porque la readiness lo
/// consulta en cada sondeo: mientras el esquema no esté al día la instancia no debe recibir
/// tráfico, aunque el proceso siga vivo.
/// </summary>
public sealed class DatabaseMigrationState
{
    public bool Attempted { get; private set; }
    public bool Succeeded { get; private set; }
    /// <summary>Tipo de excepción, nunca el mensaje: puede contener la cadena de conexión.</summary>
    public string? FailureKind { get; private set; }
    public IReadOnlyList<string> Applied { get; private set; } = [];

    public void RecordSuccess(IReadOnlyList<string> applied)
    {
        Attempted = true;
        Succeeded = true;
        Applied = applied;
    }

    public void RecordFailure(Exception exception)
    {
        Attempted = true;
        Succeeded = false;
        FailureKind = exception.GetType().Name;
    }

    public void RecordSkipped()
    {
        Attempted = true;
        Succeeded = true;
    }
}

public static class DatabaseMigrator
{
    /// <summary>
    /// Aplica las migraciones pendientes antes de servir la primera petición, en todo ambiente.
    /// Hasta aquí sólo migraba el sembrado de Demo, de modo que Production arrancaba contra un
    /// esquema que nadie había creado.
    ///
    /// Un fallo no derriba el proceso a propósito: se registra y la readiness pasa a fallar. Así
    /// Railway conserva el despliegue anterior sirviendo en lugar de entrar en un ciclo de
    /// reinicios, y el operador conserva /health/live para diagnosticar.
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services,
        IHostEnvironment environment, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var state = scope.ServiceProvider.GetRequiredService<DatabaseMigrationState>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("UrabaConecta.Migraciones");

        // Permite desplegar sin migrar cuando la migración se ejecuta aparte, con respaldo previo.
        // La readiness sigue exigiendo que no queden pendientes, así que apagarlo no deja pasar
        // una instancia con el esquema atrasado.
        if (!configuration.GetValue("Database:MigrateOnStartup", true))
        {
            logger.LogWarning("Migración de arranque desactivada por configuración en {Ambiente}.",
                environment.EnvironmentName);
            state.RecordSkipped();
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count == 0)
            {
                state.RecordSuccess([]);
                logger.LogInformation("Esquema al día en {Ambiente}: sin migraciones pendientes.",
                    environment.EnvironmentName);
                return;
            }
            logger.LogInformation("Aplicando {Cantidad} migración(es) en {Ambiente}: {Migraciones}.",
                pending.Count, environment.EnvironmentName, string.Join(", ", pending));
            await db.Database.MigrateAsync(cancellationToken);
            state.RecordSuccess(pending);
            logger.LogInformation("Migraciones aplicadas correctamente en {Ambiente}.",
                environment.EnvironmentName);
        }
        catch (Exception ex)
        {
            state.RecordFailure(ex);
            // El mensaje de Npgsql puede incluir la cadena de conexión; se registra el tipo y la
            // traza queda en el registro estructurado sin volcarse a la respuesta HTTP.
            logger.LogError(ex, "Falló la migración de arranque en {Ambiente}. La instancia queda " +
                                "marcada como no lista y no recibirá tráfico.", environment.EnvironmentName);
        }
    }
}

/// <summary>
/// Readiness del esquema. Falla si la migración de arranque falló o si todavía quedan
/// migraciones pendientes, que es el estado en el que la aplicación respondería con errores
/// de columna inexistente.
/// </summary>
public sealed class DatabaseMigrationHealthCheck(AppDbContext db, DatabaseMigrationState state)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (state.Attempted && !state.Succeeded)
            return HealthCheckResult.Unhealthy(
                $"La migración de arranque falló ({state.FailureKind}).");
        try
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            return pending.Count == 0
                ? HealthCheckResult.Healthy("Esquema al día.")
                : HealthCheckResult.Unhealthy($"Quedan {pending.Count} migración(es) pendiente(s).");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"No se pudo comprobar el esquema ({ex.GetType().Name}).");
        }
    }
}
