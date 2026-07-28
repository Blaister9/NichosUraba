using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using UrabaConecta.Application;
using UrabaConecta.Contracts;

namespace UrabaConecta.Infrastructure.Persistence;

public sealed class PlatformHealthProvider(
    AppDbContext db,
    IObjectStorage storage,
    IHostEnvironment environment,
    IConfiguration configuration) : IPlatformHealthProvider
{
    public async Task<PlatformHealthDto> GetAsync(CancellationToken cancellationToken)
    {
        var database = await CheckDatabaseAsync(cancellationToken);
        var objectStorage = await storage.CheckHealthAsync(cancellationToken);
        var keysPath = configuration["DataProtection:KeysPath"];
        var dataProtection = string.IsNullOrWhiteSpace(keysPath)
            ? "En memoria (las cookies no sobreviven a un reinicio)"
            : Directory.Exists(keysPath)
                ? $"Persistente en {keysPath}" +
                  (string.IsNullOrWhiteSpace(configuration["DataProtection:CertificateBase64"])
                      ? " (sin cifrado adicional en reposo)"
                      : " (cifradas con certificado X.509)")
                : $"No existe la ruta {keysPath}";

        return new(
            environment.EnvironmentName,
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "desconocida",
            configuration["Deployment:Commit"] ?? "desconocido",
            DateTimeOffset.TryParse(configuration["Deployment:DeployedAtUtc"], out var deployed) ? deployed : null,
            database, objectStorage.IsHealthy ? $"Disponible — {objectStorage.Detail}" : $"Error — {objectStorage.Detail}",
            storage.Provider, dataProtection,
            configuration.GetValue<bool>("DemoSeed:Enabled"));
    }

    private async Task<string> CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken);
            return pending.Any()
                ? $"Conectada — {pending.Count()} migración(es) pendiente(s)"
                : "Conectada — sin migraciones pendientes";
        }
        catch (Exception ex)
        {
            return $"Error — {ex.GetType().Name}";
        }
    }
}
