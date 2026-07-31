using System.Net;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

/// <summary>
/// La pantalla de Personal devolvía 500 y terminaba el circuito. Estas pruebas la piden como la
/// pide un navegador: si el prerenderizado lanza, la respuesta deja de ser 200 y fallan.
/// </summary>
public sealed class BusinessStaffPageTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    private HttpClient Client() => factory.CreateClient(new() { AllowAutoRedirect = false });

    [Fact]
    public async Task The_staff_page_renders_without_a_server_error()
    {
        using var client = Client();
        await PlatformAdministrationApiTests.Login(client, DevelopmentSeeder.BellaOwnerEmail);
        var response = await client.GetAsync(
            $"/panel/{DevelopmentSeeder.BellaBusinessId}/configuracion/personal");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // El prerenderizado tiene que traer el formulario, no una página de error.
        Assert.Contains("Personal", body);
        Assert.DoesNotContain("An error occurred while processing your request", body);
    }

    [Fact]
    public async Task The_staff_page_renders_for_a_business_without_any_staff()
    {
        // El Corte no tiene citas ni perfiles operativos: es el caso de listas vacías.
        using var client = Client();
        await PlatformAdministrationApiTests.Login(client, DevelopmentSeeder.CorteOwnerEmail);
        var response = await client.GetAsync(
            $"/panel/{DevelopmentSeeder.CorteBusinessId}/configuracion/personal");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("An error occurred while processing your request", body);
    }

    [Fact]
    public async Task The_staff_page_is_not_reachable_for_another_business()
    {
        using var client = Client();
        await PlatformAdministrationApiTests.Login(client, DevelopmentSeeder.CorteOwnerEmail);
        // Aislamiento entre negocios: el prerenderizado no debe filtrar el personal de Bella.
        var response = await client.GetAsync(
            $"/panel/{DevelopmentSeeder.BellaBusinessId}/configuracion/personal");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("An error occurred while processing your request", body);
        // La página se dibuja, pero con el aviso de acceso, no con datos ajenos.
        Assert.Contains("No tiene acceso a este establecimiento", body);
    }
}
