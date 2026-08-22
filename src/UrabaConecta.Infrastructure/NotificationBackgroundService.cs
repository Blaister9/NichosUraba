using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UrabaConecta.Application;

namespace UrabaConecta.Infrastructure;

/// <summary>
/// El bucle que saca los avisos del buzón. Todo lo interesante ocurre en
/// <see cref="INotificationDispatcher"/>; esto sólo decide cuándo llamarlo y se asegura de que una
/// pasada que reviente no se lleve el proceso por delante.
///
/// El sondeo es la garantía: aunque se pierdan todos los golpecitos en memoria —un reinicio, un
/// despliegue— lo que quedó pendiente en la base se encuentra en la siguiente vuelta.
/// </summary>
public sealed class NotificationBackgroundService(IServiceScopeFactory scopes,
    INotificationSignal signal, IOptions<NotificationOptions> options,
    ILogger<NotificationBackgroundService> logger) : BackgroundService
{
    private readonly NotificationOptions settings = options.Value;
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.BackgroundWorkerEnabled)
        {
            logger.LogWarning("El trabajador de avisos está apagado por configuración.");
            return;
        }
        var nextPrune = DateTimeOffset.UtcNow + PruneInterval;
        while (!stoppingToken.IsCancellationRequested)
        {
            var worked = false;
            try
            {
                using var scope = scopes.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
                var report = await dispatcher.RunOnceAsync(stoppingToken);
                worked = report.Total > 0;
                if (worked)
                    logger.LogInformation(
                        "Buzón de avisos: {FannedOut} repartidos, {Sent} entregados, {Retried} reprogramados, " +
                        "{Expired} caducados, {Abandoned} abandonados.",
                        report.FannedOut, report.Sent, report.Retried, report.Expired, report.Abandoned);

                if (DateTimeOffset.UtcNow >= nextPrune)
                {
                    var removed = await dispatcher.PruneAsync(stoppingToken);
                    nextPrune = DateTimeOffset.UtcNow + PruneInterval;
                    if (removed > 0) logger.LogInformation("Buzón podado: {Removed} filas.", removed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Una pasada fallida no puede terminar el servicio: lo pendiente sigue pendiente y
                // la siguiente vuelta lo vuelve a intentar.
                logger.LogError(ex, "Falló una pasada del buzón de avisos.");
            }

            var wait = TimeSpan.FromSeconds(worked
                ? Math.Max(1, settings.BusyPollSeconds)
                : Math.Max(1, settings.IdlePollSeconds));
            try { await signal.WaitAsync(wait, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
