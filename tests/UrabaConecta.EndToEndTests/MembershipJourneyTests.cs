using Microsoft.Playwright;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.EndToEndTests;

public sealed class MembershipJourneyTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    [Fact]
    public async Task Owner_assigns_configuration_permission_from_mobile()
    {
        await using var ownerContext = await MobileContext();
        var owner = await ownerContext.NewPageAsync();
        await Login(owner, DevelopmentSeeder.BellaOwnerEmail, DevelopmentSeeder.DemoPassword);
        await OpenTeam(owner, DevelopmentSeeder.BellaBusinessId);
        var created = await CreateMember(owner, appointments: true, configuration: false, members: false);
        var card = MemberCard(owner, created.Email);
        await card.GetByLabel("Configurar negocio").CheckAsync();
        AcceptDialogs(owner);
        await card.GetByRole(AriaRole.Button, new() { Name = "Guardar permisos" }).ClickAsync();
        await Expect(owner.GetByText($"Permisos de {created.Name} actualizados.")).ToBeVisibleAsync();
        Assert.False(await owner.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"));

        await using var memberContext = await MobileContext();
        var member = await memberContext.NewPageAsync();
        await Login(member, created.Email, created.Password);
        await member.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.BellaBusinessId}/configuracion");
        await Expect(member.GetByRole(AriaRole.Heading, new() { Name = "Configuración del negocio" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task Deactivation_revokes_access_on_the_next_request()
    {
        await using var ownerContext = await MobileContext();
        var owner = await ownerContext.NewPageAsync();
        await Login(owner, DevelopmentSeeder.BellaOwnerEmail, DevelopmentSeeder.DemoPassword);
        await OpenTeam(owner, DevelopmentSeeder.BellaBusinessId);
        var created = await CreateMember(owner, appointments: true, configuration: true, members: true);

        await using var memberContext = await MobileContext();
        var member = await memberContext.NewPageAsync();
        await Login(member, created.Email, created.Password);
        await member.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.BellaBusinessId}/citas");
        await Expect(member.GetByRole(AriaRole.Heading, new() { Name = "Citas", Exact = true })).ToBeVisibleAsync();

        AcceptDialogs(owner);
        await MemberCard(owner, created.Email).GetByRole(AriaRole.Button, new() { Name = "Desactivar acceso" }).ClickAsync();
        await Expect(owner.GetByText($"Acceso de {created.Name} desactivado.")).ToBeVisibleAsync();
        await member.ReloadAsync();
        await Expect(member.GetByText("No tiene acceso a este establecimiento.")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Last_owner_action_is_blocked_with_specific_message()
    {
        await using var context = await MobileContext();
        var page = await context.NewPageAsync();
        await Login(page, DevelopmentSeeder.BellaOwnerEmail, DevelopmentSeeder.DemoPassword);
        await OpenTeam(page, DevelopmentSeeder.BellaBusinessId);
        AcceptDialogs(page);
        var owner = MemberCard(page, DevelopmentSeeder.BellaOwnerEmail);
        await owner.GetByRole(AriaRole.Button, new() { Name = "Desactivar acceso" }).ClickAsync();
        await Expect(page.GetByText("El establecimiento debe conservar al menos una persona propietaria activa."))
            .ToBeVisibleAsync();
        await owner.GetByRole(AriaRole.Button, new() { Name = "Retirar propiedad" }).ClickAsync();
        await Expect(page.GetByText("El establecimiento debe conservar al menos una persona propietaria activa."))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task Ownership_transfers_and_former_owner_keeps_only_selected_permissions()
    {
        await using var originalContext = await MobileContext();
        var original = await originalContext.NewPageAsync();
        await Login(original, DevelopmentSeeder.BellaOwnerEmail, DevelopmentSeeder.DemoPassword);
        await OpenTeam(original, DevelopmentSeeder.BellaBusinessId);
        var successor = await CreateMember(original, appointments: true, configuration: false, members: false);
        AcceptDialogs(original);
        await MemberCard(original, successor.Email).GetByRole(AriaRole.Button, new() { Name = "Hacer propietaria" })
            .ClickAsync();
        await Expect(original.GetByText($"{successor.Name} ahora es propietaria.")).ToBeVisibleAsync();

        var originalCard = MemberCard(original, DevelopmentSeeder.BellaOwnerEmail);
        await originalCard.GetByLabel("Configurar negocio").UncheckAsync();
        await originalCard.GetByLabel("Administrar equipo").UncheckAsync();
        await originalCard.GetByRole(AriaRole.Button, new() { Name = "Retirar propiedad" }).ClickAsync();
        await Expect(original.GetByText("Propietaria Bella dejó de ser propietaria.")).ToBeVisibleAsync();
        await original.GotoAsync($"{fixture.BaseUrl}/panel");
        await Expect(original.GetByRole(AriaRole.Link, new() { Name = "Administrar citas" })).ToBeVisibleAsync();
        await Expect(original.GetByRole(AriaRole.Link, new() { Name = "Equipo y permisos" })).ToHaveCountAsync(0);

        await using var successorContext = await MobileContext();
        var successorPage = await successorContext.NewPageAsync();
        await Login(successorPage, successor.Email, successor.Password);
        await OpenTeam(successorPage, DevelopmentSeeder.BellaBusinessId);
        await Expect(successorPage.GetByRole(AriaRole.Heading, new() { Name = "Equipo y permisos" })).ToBeVisibleAsync();

        AcceptDialogs(successorPage);
        await MemberCard(successorPage, DevelopmentSeeder.BellaOwnerEmail)
            .GetByRole(AriaRole.Button, new() { Name = "Hacer propietaria" }).ClickAsync();
        await Expect(successorPage.GetByText("Propietaria Bella ahora es propietaria.")).ToBeVisibleAsync();
        await MemberCard(successorPage, successor.Email)
            .GetByRole(AriaRole.Button, new() { Name = "Desactivar acceso" }).ClickAsync();
        await Expect(successorPage.GetByText($"Acceso de {successor.Name} desactivado.")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Second_business_owner_cannot_open_bella_team_or_member_detail()
    {
        await using var context = await MobileContext();
        var page = await context.NewPageAsync();
        await Login(page, DevelopmentSeeder.OtherOwnerEmail, DevelopmentSeeder.DemoPassword);
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.BellaBusinessId}/equipo");
        await Expect(page.GetByText("No tiene acceso a este establecimiento.")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-member-id]")).ToHaveCountAsync(0);
        await page.GotoAsync(
            $"{fixture.BaseUrl}/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/memberships/{Guid.NewGuid()}");
        Assert.Contains("403", await page.Locator("body").InnerTextAsync());
    }

    private async Task<IBrowserContext> MobileContext() => await fixture.Browser.NewContextAsync(new()
    {
        ViewportSize = new() { Width = 360, Height = 800 }
    });

    private async Task OpenTeam(IPage page, Guid businessId)
    {
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{businessId}/equipo");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Equipo y permisos" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(page.GetByText("Cargando equipo…")).ToHaveCountAsync(0, new() { Timeout = 15_000 });
    }

    private async Task<CreatedMember> CreateMember(IPage page, bool appointments, bool configuration, bool members)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"Persona {suffix}";
        var email = $"persona-{suffix}@demo.local";
        var form = page.GetByTestId("create-member-form");
        await Expect(form.GetByRole(AriaRole.Button, new() { Name = "Crear y vincular" }))
            .ToBeEnabledAsync(new() { Timeout = 15_000 });
        await form.GetByLabel("Nombre visible").FillAsync(name);
        await form.GetByLabel("Correo ficticio").FillAsync(email);
        if (appointments) await form.GetByLabel("Administrar citas").CheckAsync();
        if (configuration) await form.GetByLabel("Configurar negocio").CheckAsync();
        if (members) await form.GetByLabel("Administrar equipo").CheckAsync();
        await form.GetByLabel("Correo ficticio").BlurAsync();
        await form.GetByRole(AriaRole.Button, new() { Name = "Crear y vincular" }).ClickAsync();
        var secret = page.Locator(".temporary-secret code");
        await Expect(secret).ToBeVisibleAsync();
        var password = (await secret.InnerTextAsync()).Trim();
        await Expect(MemberCard(page, email)).ToBeVisibleAsync();
        return new(name, email, password);
    }

    private static ILocator MemberCard(IPage page, string text)
        => page.Locator("[data-member-id]").Filter(new() { HasTextString = text });

    private async Task Login(IPage page, string email, string password)
    {
        await page.GotoAsync($"{fixture.BaseUrl}/Account/Login");
        await page.GetByLabel("Correo").FillAsync(email);
        await page.GetByLabel("Contraseña").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Ingresar" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/panel"));
    }

    private static void AcceptDialogs(IPage page)
        => page.Dialog += async (_, dialog) => await dialog.AcceptAsync();

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
    private sealed record CreatedMember(string Name, string Email, string Password);
}
