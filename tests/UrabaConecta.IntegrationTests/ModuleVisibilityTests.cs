using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using UrabaConecta.Contracts;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

/// <summary>
/// Regla que fijan estas pruebas: mostrar una operación exige el módulo habilitado en ESE negocio
/// y además el permiso de la persona. Antes bastaba el permiso, así que una propietaria veía citas,
/// turnos y pedidos aunque su establecimiento sólo tuviera uno de los tres, y una URL directa
/// llegaba al módulo no habilitado.
/// </summary>
public sealed class ModuleVisibilityTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private HttpClient Client() => factory.CreateClient(new() { AllowAutoRedirect = false });

    private async Task<MyBusinessDto> MineAsync(string email, Guid businessId)
    {
        using var client = Client();
        await PlatformAdministrationApiTests.Login(client, email);
        var mine = await client.GetFromJsonAsync<List<MyBusinessDto>>("/api/v1/businesses/mine", Json);
        return mine!.Single(x => x.Id == businessId);
    }

    [Fact]
    public async Task Bella_offers_appointments_and_never_queues_or_orders()
    {
        var bella = await MineAsync(DevelopmentSeeder.BellaOwnerEmail, DevelopmentSeeder.BellaBusinessId);
        Assert.True(bella.HasAppointments);
        Assert.False(bella.HasVirtualQueues);
        Assert.False(bella.HasPickupOrders);
        Assert.True(bella.ShowAppointments);
        Assert.False(bella.ShowQueues);
        Assert.False(bella.ShowOrders);
        // Configuración y equipo no dependen de módulos y deben seguir disponibles.
        Assert.True(bella.CanManageConfiguration);
        Assert.True(bella.CanManageMembers);
    }

    [Fact]
    public async Task Corte_offers_queues_and_never_appointments_or_orders()
    {
        var corte = await MineAsync(DevelopmentSeeder.CorteOwnerEmail, DevelopmentSeeder.CorteBusinessId);
        Assert.True(corte.HasVirtualQueues);
        Assert.False(corte.HasAppointments);
        Assert.False(corte.HasPickupOrders);
        Assert.True(corte.ShowQueues);
        Assert.False(corte.ShowAppointments);
        Assert.False(corte.ShowOrders);
        Assert.True(corte.CanManageConfiguration);
        Assert.True(corte.CanManageMembers);
    }

    [Fact]
    public async Task Sazon_offers_orders_and_never_appointments_or_queues()
    {
        var sazon = await MineAsync(DevelopmentSeeder.SazonOwnerEmail, DevelopmentSeeder.SazonBusinessId);
        Assert.True(sazon.HasPickupOrders);
        Assert.False(sazon.HasAppointments);
        Assert.False(sazon.HasVirtualQueues);
        Assert.True(sazon.ShowOrders);
        Assert.False(sazon.ShowAppointments);
        Assert.False(sazon.ShowQueues);
        Assert.True(sazon.CanManageConfiguration);
        Assert.True(sazon.CanManageMembers);
    }

    [Fact]
    public async Task A_multi_module_business_offers_exactly_the_modules_it_has_enabled()
    {
        using var admin = Client();
        await PlatformAdministrationApiTests.Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var catalog = (await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json))!;
        var created = (await (await admin.PostAsJsonAsync("/api/v1/admin/businesses",
            new CreatePlatformBusinessRequest
            {
                Name = $"Multi {Guid.NewGuid():N}"[..18], Slug = $"multi-{Guid.NewGuid():N}",
                MunicipalityId = catalog.Municipalities[0].Id, CategoryId = catalog.Categories[0].Id,
                ShortDescription = "Negocio ficticio con dos funciones.",
                Description = "Negocio ficticio con dos funciones activas.",
                // Dos módulos sí, uno no: el listado debe reflejar exactamente eso.
                Appointments = true, PickupOrders = true, VirtualQueues = false,
                ExistingOwnerEmail = DevelopmentSeeder.BellaOwnerEmail, SaveAsDraft = true
            }, Json)).Content.ReadFromJsonAsync<PlatformBusinessCreatedDto>(Json))!.Business;

        var mine = await MineAsync(DevelopmentSeeder.BellaOwnerEmail, created.Id);
        Assert.True(mine.HasAppointments);
        Assert.True(mine.HasPickupOrders);
        Assert.False(mine.HasVirtualQueues);
        Assert.True(mine.ShowAppointments);
        Assert.True(mine.ShowOrders);
        Assert.False(mine.ShowQueues);
    }

    [Fact]
    public async Task A_member_without_permission_does_not_get_the_action_even_with_the_module_enabled()
    {
        // Corte sí tiene turnos habilitados, pero esta cuenta no tiene el permiso.
        var sinPermiso = await MineAsync(DevelopmentSeeder.CorteNoPermissionEmail, DevelopmentSeeder.CorteBusinessId);
        Assert.True(sinPermiso.HasVirtualQueues);
        Assert.False(sinPermiso.CanManageQueues);
        Assert.False(sinPermiso.ShowQueues);

        var sinPedidos = await MineAsync(DevelopmentSeeder.SazonNoPermissionEmail, DevelopmentSeeder.SazonBusinessId);
        Assert.True(sinPedidos.HasPickupOrders);
        Assert.False(sinPedidos.CanManageOrders);
        Assert.False(sinPedidos.ShowOrders);
    }

    [Fact]
    public async Task A_direct_url_to_a_disabled_module_is_rejected_with_403()
    {
        using var bella = Client();
        await PlatformAdministrationApiTests.Login(bella, DevelopmentSeeder.BellaOwnerEmail);
        // Bella es propietaria, así que tiene todos los permisos: lo que falta es el módulo.
        foreach (var path in new[]
                 {
                     $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/queue",
                     $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/orders",
                     $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/products",
                     $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/order-settings",
                 })
            Assert.Equal(HttpStatusCode.Forbidden, (await bella.GetAsync(path)).StatusCode);

        using var corte = Client();
        await PlatformAdministrationApiTests.Login(corte, DevelopmentSeeder.CorteOwnerEmail);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await corte.GetAsync($"/api/v1/businesses/{DevelopmentSeeder.CorteBusinessId}/appointments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await corte.GetAsync($"/api/v1/businesses/{DevelopmentSeeder.CorteBusinessId}/orders")).StatusCode);

        using var sazon = Client();
        await PlatformAdministrationApiTests.Login(sazon, DevelopmentSeeder.SazonOwnerEmail);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await sazon.GetAsync($"/api/v1/businesses/{DevelopmentSeeder.SazonBusinessId}/appointments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await sazon.GetAsync($"/api/v1/businesses/{DevelopmentSeeder.SazonBusinessId}/queue")).StatusCode);
    }

    [Fact]
    public async Task The_enabled_module_keeps_working_for_whoever_has_the_permission()
    {
        // El filtrado no debe cerrar el módulo que sí corresponde a cada negocio.
        using var bella = Client();
        await PlatformAdministrationApiTests.Login(bella, DevelopmentSeeder.BellaOwnerEmail);
        Assert.Equal(HttpStatusCode.OK,
            (await bella.GetAsync($"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/appointments")).StatusCode);

        using var corte = Client();
        await PlatformAdministrationApiTests.Login(corte, DevelopmentSeeder.CorteOwnerEmail);
        Assert.Equal(HttpStatusCode.OK,
            (await corte.GetAsync($"/api/v1/businesses/{DevelopmentSeeder.CorteBusinessId}/queue")).StatusCode);

        using var sazon = Client();
        await PlatformAdministrationApiTests.Login(sazon, DevelopmentSeeder.SazonOwnerEmail);
        Assert.Equal(HttpStatusCode.OK,
            (await sazon.GetAsync($"/api/v1/businesses/{DevelopmentSeeder.SazonBusinessId}/orders")).StatusCode);
    }

    [Fact]
    public async Task An_archived_business_disappears_from_my_establishments()
    {
        using var admin = Client();
        await PlatformAdministrationApiTests.Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var catalog = (await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json))!;
        var created = (await (await admin.PostAsJsonAsync("/api/v1/admin/businesses",
            new CreatePlatformBusinessRequest
            {
                Name = $"Residuo {Guid.NewGuid():N}"[..18], Slug = $"residuo-{Guid.NewGuid():N}",
                MunicipalityId = catalog.Municipalities[0].Id, CategoryId = catalog.Categories[0].Id,
                ShortDescription = "Piloto ficticio que se archiva.",
                Description = "Piloto ficticio que después se archiva.",
                Appointments = true, ExistingOwnerEmail = DevelopmentSeeder.BellaOwnerEmail, SaveAsDraft = true
            }, Json)).Content.ReadFromJsonAsync<PlatformBusinessCreatedDto>(Json))!.Business;

        using var owner = Client();
        await PlatformAdministrationApiTests.Login(owner, DevelopmentSeeder.BellaOwnerEmail);
        var before = await owner.GetFromJsonAsync<List<MyBusinessDto>>("/api/v1/businesses/mine", Json);
        Assert.Contains(before!, x => x.Id == created.Id);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync(
            $"/api/v1/admin/businesses/{created.Id}/archive",
            new PlatformBusinessStateRequest { Version = created.Version }, Json)).StatusCode);

        // La membresía sigue existiendo, pero un negocio archivado ya no se opera: no debe
        // aparecer entre los establecimientos de la persona.
        var after = await owner.GetFromJsonAsync<List<MyBusinessDto>>("/api/v1/businesses/mine", Json);
        Assert.DoesNotContain(after!, x => x.Id == created.Id);
        Assert.Contains(after!, x => x.Id == DevelopmentSeeder.BellaBusinessId);
    }

    [Fact]
    public async Task The_bella_owner_only_receives_the_businesses_she_operates()
    {
        using var owner = Client();
        await PlatformAdministrationApiTests.Login(owner, DevelopmentSeeder.BellaOwnerEmail);
        var mine = (await owner.GetFromJsonAsync<List<MyBusinessDto>>("/api/v1/businesses/mine", Json))!;
        Assert.Contains(mine, x => x.Id == DevelopmentSeeder.BellaBusinessId);
        // Nunca el establecimiento de otra persona.
        Assert.DoesNotContain(mine, x => x.Id == DevelopmentSeeder.OtherBusinessId);
        Assert.DoesNotContain(mine, x => x.Id == DevelopmentSeeder.CorteBusinessId);
        Assert.DoesNotContain(mine, x => x.Id == DevelopmentSeeder.SazonBusinessId);
        // Y ninguno archivado.
        Assert.DoesNotContain(mine, x => x.BusinessStatus == "Archived");
    }
}
