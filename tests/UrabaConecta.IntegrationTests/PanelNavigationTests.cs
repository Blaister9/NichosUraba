using System.Net;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

/// <summary>
/// Al salir de "Mis establecimientos", que es un componente interactivo, la navegación mejorada
/// cambiaba la dirección pero dejaba en pantalla la misma página: quien pulsaba "Operar pedidos"
/// seguía viendo "Mis establecimientos" y la operación parecía rota. Los enlaces de acción se
/// excluyen de esa navegación para que la página destino se cargue completa.
/// </summary>
public sealed class PanelNavigationTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    private HttpClient Client() => factory.CreateClient(new() { AllowAutoRedirect = false });

    [Fact]
    public async Task The_establishment_actions_opt_out_of_enhanced_navigation()
    {
        using var client = Client();
        await PlatformAdministrationApiTests.Login(client, DevelopmentSeeder.SazonOwnerEmail);
        var body = await (await client.GetAsync("/panel")).Content.ReadAsStringAsync();
        Assert.Contains("data-enhance-nav=\"false\"", body);
        Assert.Contains("Operar pedidos", body);
    }

    [Theory]
    [InlineData("/panel/{0}/pedidos", "Pedidos")]
    [InlineData("/panel/{0}/configuracion", "Configuración")]
    public async Task The_destination_of_each_action_renders_its_own_page(string ruta, string titulo)
    {
        using var client = Client();
        await PlatformAdministrationApiTests.Login(client, DevelopmentSeeder.SazonOwnerEmail);
        var response = await client.GetAsync(string.Format(ruta, DevelopmentSeeder.SazonBusinessId));
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(titulo, body);
        // Si la página destino se quedara con el contenido del panel, este texto delataría el fallo.
        Assert.DoesNotContain("<h1>Mis establecimientos</h1>", body);
    }

    [Fact]
    public async Task The_orders_panel_prerenders_its_orders_instead_of_a_false_empty_state()
    {
        using var client = Client();
        await PlatformAdministrationApiTests.Login(client, DevelopmentSeeder.SazonOwnerEmail);
        var body = await (await client.GetAsync(
            $"/panel/{DevelopmentSeeder.SazonBusinessId}/pedidos")).Content.ReadAsStringAsync();
        // El seed deja pedidos: el vacío sólo debe aparecer cuando de verdad no hay ninguno.
        Assert.Contains("admin-order", body);
        Assert.DoesNotContain("No hay pedidos todavía", body);
    }
}
