using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using UrabaConecta.Application;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

/// <summary>
/// No es sellada para que una prueba pueda añadir lo suyo —un reloj detenido, por ejemplo— sin
/// levantar un segundo PostgreSQL ni duplicar el conteo de sentencias.
/// </summary>
public class PostgresWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    /// PostgreSQL de las pruebas. Por omisión se levanta un contenedor, que es lo que corre en
    /// integración continua. Con URABACONECTA_TEST_PG se apunta a una base ya existente, para
    /// poder ejecutar la suite en una máquina donde Docker no arranca; el contenedor entonces ni
    /// siquiera se construye, porque el constructor de Testcontainers ya exige el demonio.
    /// </summary>
    private static readonly string? Externa = Environment.GetEnvironmentVariable("URABACONECTA_TEST_PG");
    private readonly Lazy<PostgreSqlContainer> _contenedor = new(() => new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("urabaconecta_tests").WithUsername("tests").WithPassword("tests-only-password").Build());
    private PostgreSqlContainer _postgres => _contenedor.Value;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connectionString = Externa ?? _postgres.GetConnectionString();
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
        builder.UseSetting("URABACONECTA_TRACKING_HMAC_KEY", "integration-test-hmac-key-at-least-32-bytes");
        // Toda la suite escribe desde la misma IP, así que el límite público de doce por minuto la
        // cortaba a mitad. Se sube igual que en las pruebas de navegador; el límite en sí se
        // comprueba en producción, no aquí.
        builder.UseSetting("RateLimits:PublicWritesPerMinute", "200");
        // El buzón de avisos se ejecuta a mano en las pruebas. Con el bucle de fondo encendido, una
        // entrega de una prueba anterior podía llegar después del Reset del transporte y afirmar
        // cosas de otra prueba; aquí interesa la certeza, no la concurrencia.
        builder.UseSetting("Notifications:BackgroundWorkerEnabled", "false");
        // Permite afirmar cuántas sentencias cuesta una petición, que es la regresión que
        // provocaba catorce segundos en la consola administrativa.
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<QueryCounter>();
            services.AddSingleton<IInterceptor, CountingInterceptor>();
            // Registrar el interceptor en el contenedor NO basta: EF sólo los aplica si están
            // enlazados a las opciones del contexto. Sin este reenlace el contador se quedaba en
            // cero y toda afirmación de coste pasaba sola —"0 == 0"—, que es exactamente el modo en
            // que una prueba de rendimiento deja de proteger nada sin que nadie se entere.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.AddDbContext<AppDbContext>((provider, options) =>
                options.UseNpgsql(connectionString).AddInterceptors(provider.GetServices<IInterceptor>()));
        });
    }

    /// <summary>
    /// Vacía el buzón: reparte lo pendiente y agota los intentos que ya tocaban. Devuelve el
    /// resumen acumulado para poder afirmar cuántas entregas salieron y cuántas se reprogramaron.
    ///
    /// Se ejecuta en bucle porque una pasada reparte y otra entrega: son fases distintas y la
    /// primera crea el trabajo de la segunda.
    /// </summary>
    public async Task<NotificationDispatchReport> DrainNotificationsAsync(int passes = 4)
    {
        var total = NotificationDispatchReport.Empty;
        for (var pass = 0; pass < passes; pass++)
        {
            await using var scope = Services.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
            var report = await dispatcher.RunOnceAsync();
            total = new(total.FannedOut + report.FannedOut, total.Attempted + report.Attempted,
                total.Sent + report.Sent, total.Retried + report.Retried, total.Expired + report.Expired,
                total.Abandoned + report.Abandoned, total.Skipped + report.Skipped);
            if (report.Total == 0) break;
        }
        return total;
    }

    public Task InitializeAsync() => Externa is not null ? Task.CompletedTask : _postgres.StartAsync();
    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (_contenedor.IsValueCreated) await _postgres.DisposeAsync();
    }
}
