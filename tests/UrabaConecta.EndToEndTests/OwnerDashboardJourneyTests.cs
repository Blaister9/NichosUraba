using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.EndToEndTests;

/// <summary>
/// El panel visto por quien opera. Lo que se comprueba aquí no son las cifras —de eso responden las
/// pruebas contra PostgreSQL— sino que cada negocio vea únicamente su operación, que la acción
/// principal lleve a donde dice, y que la pantalla siga siendo usable en un teléfono.
///
/// Antes de esto, /panel era una fila de botones idénticos: no decía en ningún momento cómo iba el día.
/// </summary>
public sealed class OwnerDashboardJourneyTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    [Fact]
    public async Task The_salon_sees_only_its_appointments_and_reaches_them_from_the_panel()
    {
        var page = await PanelAsync(DevelopmentSeeder.BellaOwnerEmail);
        var tarjeta = Card(page, DevelopmentSeeder.BellaBusinessId);

        await Expect(tarjeta).ToBeVisibleAsync();
        await Expect(tarjeta.Locator("[data-testid=appointments-summary]")).ToBeVisibleAsync();
        // Un salón sin turnos ni pedidos no puede ver esas operaciones.
        await Expect(tarjeta.Locator("[data-testid=queues-summary]")).ToHaveCountAsync(0);
        await Expect(tarjeta.Locator("[data-testid=orders-summary]")).ToHaveCountAsync(0);

        // La acción principal lleva a la operación, no a la configuración.
        var operar = tarjeta.Locator("[data-testid=primary-operation-action]");
        await Expect(operar).ToHaveTextAsync("Administrar citas");
        await operar.ClickAsync();
        await page.WaitForURLAsync(url => url.Contains($"/panel/{DevelopmentSeeder.BellaBusinessId}/citas"));

        // Y la configuración sigue estando, un escalón por debajo.
        await page.GoBackAsync();
        await Expect(Card(page, DevelopmentSeeder.BellaBusinessId)
            .Locator("[data-testid=business-configuration-action]")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_barbershop_sees_only_its_queue()
    {
        var page = await PanelAsync(DevelopmentSeeder.CorteOwnerEmail);
        var tarjeta = Card(page, DevelopmentSeeder.CorteBusinessId);

        await Expect(tarjeta.Locator("[data-testid=queues-summary]")).ToBeVisibleAsync();
        await Expect(tarjeta.Locator("[data-testid=appointments-summary]")).ToHaveCountAsync(0);
        await Expect(tarjeta.Locator("[data-testid=orders-summary]")).ToHaveCountAsync(0);

        var operar = tarjeta.Locator("[data-testid=primary-operation-action]");
        await Expect(operar).ToHaveTextAsync("Operar turnos");
        await operar.ClickAsync();
        await page.WaitForURLAsync(url => url.Contains($"/panel/{DevelopmentSeeder.CorteBusinessId}/turnos"));
    }

    [Fact]
    public async Task The_restaurant_sees_only_its_orders()
    {
        var page = await PanelAsync(DevelopmentSeeder.SazonOwnerEmail);
        var tarjeta = Card(page, DevelopmentSeeder.SazonBusinessId);

        await Expect(tarjeta.Locator("[data-testid=orders-summary]")).ToBeVisibleAsync();
        await Expect(tarjeta.Locator("[data-testid=appointments-summary]")).ToHaveCountAsync(0);
        await Expect(tarjeta.Locator("[data-testid=queues-summary]")).ToHaveCountAsync(0);

        var operar = tarjeta.Locator("[data-testid=primary-operation-action]");
        await Expect(operar).ToHaveTextAsync("Operar pedidos");
        await operar.ClickAsync();
        await page.WaitForURLAsync(url => url.Contains($"/panel/{DevelopmentSeeder.SazonBusinessId}/pedidos"));
    }

    [Fact]
    public async Task A_partner_operator_who_also_operates_a_business_keeps_both_things_on_the_same_screen()
    {
        // Este caso ya se rompió una vez: al ganar el panel de socia se perdió el de propietaria.
        // La misma persona puede dar de alta negocios Y operar uno donde tiene membresía.
        await GrantOwnershipAsync(DevelopmentSeeder.PartnerOperatorEmail, DevelopmentSeeder.SazonBusinessId);
        var page = await PanelAsync(DevelopmentSeeder.PartnerOperatorEmail);

        await Expect(page.Locator("[data-testid=crear-negocio]").First).ToBeVisibleAsync();
        await Expect(Card(page, DevelopmentSeeder.SazonBusinessId)).ToBeVisibleAsync();
        await Expect(Card(page, DevelopmentSeeder.SazonBusinessId)
            .Locator("[data-testid=orders-summary]")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_panel_fits_a_phone_without_sideways_scrolling()
    {
        var page = await PanelAsync(DevelopmentSeeder.CorteOwnerEmail, width: 375, height: 812);
        await Expect(Card(page, DevelopmentSeeder.CorteBusinessId)).ToBeVisibleAsync();

        Assert.False(await page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > document.documentElement.clientWidth + 1"));
        // La acción principal sigue alcanzable sin buscarla.
        await Expect(Card(page, DevelopmentSeeder.CorteBusinessId)
            .Locator("[data-testid=primary-operation-action]")).ToBeVisibleAsync();
    }

    // ------------------------------------------------------------------ apoyos

    private static ILocator Card(IPage page, Guid businessId)
        => page.Locator($"[data-testid=business-dashboard][data-business-id='{businessId}']");

    private async Task<IPage> PanelAsync(string email, int width = 1366, int height = 768)
    {
        var context = await fixture.Browser.NewContextAsync(
            new() { ViewportSize = new() { Width = width, Height = height } });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{fixture.BaseUrl}/Account/Login");
        await page.GetByLabel("Correo").FillAsync(email);
        await page.GetByLabel("Contraseña").FillAsync(DevelopmentSeeder.DemoPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Ingresar" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/panel") || url.Contains("/Account/ChangeTemporaryPassword"));
        await page.GotoAsync($"{fixture.BaseUrl}/panel");
        return page;
    }

    /// <summary>
    /// El sembrado no da membresías a la socia, así que este escenario se monta añadiéndola. Se hace
    /// contra la base porque la aplicación corre en otro proceso.
    /// </summary>
    private async Task GrantOwnershipAsync(string email, Guid businessId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.ConnectionString).Options;
        await using var db = new AppDbContext(options);
        var userId = await db.Users.Where(x => x.Email == email).Select(x => x.Id).SingleAsync();
        if (await db.BusinessMemberships.AnyAsync(x => x.UserId == userId && x.BusinessId == businessId)) return;
        db.BusinessMemberships.Add(new BusinessMembership(Guid.NewGuid(), businessId, userId,
            MembershipRole.Owner));
        await db.SaveChangesAsync();
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
