using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using UrabaConecta.Contracts;
using UrabaConecta.Infrastructure.Persistence;
using UrabaConecta.Web.Services;

namespace UrabaConecta.IntegrationTests;

public sealed partial class ObservabilityTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Every_response_carries_a_correlation_identifier()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/public/businesses");

        Assert.True(response.Headers.TryGetValues(RequestCorrelationMiddleware.HeaderName, out var values));
        Assert.False(string.IsNullOrWhiteSpace(values!.Single()));
    }

    [Fact]
    public async Task A_client_supplied_correlation_identifier_is_echoed_back()
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/public/businesses");
        request.Headers.Add(RequestCorrelationMiddleware.HeaderName, "soporte-2026-08-10-a1");

        var response = await client.SendAsync(request);

        Assert.Equal("soporte-2026-08-10-a1",
            response.Headers.GetValues(RequestCorrelationMiddleware.HeaderName).Single());
    }

    [Fact]
    public async Task A_forged_correlation_identifier_never_reaches_the_log()
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/public/businesses");
        // Un salto de línea permitiría inyectar una entrada falsa en el registro.
        request.Headers.TryAddWithoutValidation(RequestCorrelationMiddleware.HeaderName,
            "bueno\nFATAL: entrada falsificada");

        var response = await client.SendAsync(request);

        var devuelto = response.Headers.GetValues(RequestCorrelationMiddleware.HeaderName).Single();
        Assert.DoesNotContain("falsificada", devuelto);
        Assert.DoesNotContain('\n', devuelto);
    }

    [Fact]
    public async Task Readiness_covers_the_schema_and_liveness_stays_independent()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);

        // La readiness sólo puede estar en verde si la migración de arranque se ejecutó y no
        // quedaron pendientes: es la condición que impide publicar un esquema atrasado.
        var estado = factory.Services.GetRequiredService<DatabaseMigrationState>();
        Assert.True(estado.Attempted);
        Assert.True(estado.Succeeded);
    }

    [Fact]
    public async Task The_health_screen_reports_uptime_and_the_migration_outcome()
    {
        using var admin = factory.CreateClient(new() { AllowAutoRedirect = false });
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var health = (await admin.GetFromJsonAsync<PlatformHealthDto>("/api/v1/admin/health", Json))!;

        Assert.True(health.Uptime > TimeSpan.Zero);
        Assert.False(string.IsNullOrWhiteSpace(health.MigrationStatus));
        Assert.DoesNotContain("Falló", health.MigrationStatus);
    }

    private static async Task Login(HttpClient client, string email)
    {
        var html = await client.GetStringAsync("/Account/Login");
        var token = AntiforgeryRegex().Match(html).Groups[1].Value
            .Replace("&quot;", "\"").Replace("&amp;", "&");
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token, ["_handler"] = "login",
                ["Input.Email"] = email, ["Input.Password"] = DevelopmentSeeder.DemoPassword,
                ["Input.RememberMe"] = "false"
            }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryRegex();
}
