using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace UrabaConecta.IntegrationTests;

public sealed class PostgresWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("urabaconecta_tests").WithUsername("tests").WithPassword("tests-only-password").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
        builder.UseSetting("URABACONECTA_TRACKING_HMAC_KEY", "integration-test-hmac-key-at-least-32-bytes");
        // Permite afirmar cuántas sentencias cuesta una petición, que es la regresión que
        // provocaba catorce segundos en la consola administrativa.
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<QueryCounter>();
            services.AddSingleton<IInterceptor, CountingInterceptor>();
        });
    }

    public Task InitializeAsync() => _postgres.StartAsync();
    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
