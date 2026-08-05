using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace UrabaConecta.IntegrationTests;

public sealed class PostgresWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
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
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", Externa ?? _postgres.GetConnectionString());
        builder.UseSetting("URABACONECTA_TRACKING_HMAC_KEY", "integration-test-hmac-key-at-least-32-bytes");
        // Toda la suite escribe desde la misma IP, así que el límite público de doce por minuto la
        // cortaba a mitad. Se sube igual que en las pruebas de navegador; el límite en sí se
        // comprueba en producción, no aquí.
        builder.UseSetting("RateLimits:PublicWritesPerMinute", "200");
        // Permite afirmar cuántas sentencias cuesta una petición, que es la regresión que
        // provocaba catorce segundos en la consola administrativa.
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<QueryCounter>();
            services.AddSingleton<IInterceptor, CountingInterceptor>();
        });
    }

    public Task InitializeAsync() => Externa is not null ? Task.CompletedTask : _postgres.StartAsync();
    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (_contenedor.IsValueCreated) await _postgres.DisposeAsync();
    }
}
