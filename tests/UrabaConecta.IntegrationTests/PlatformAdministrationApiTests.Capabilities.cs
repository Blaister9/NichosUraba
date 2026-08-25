using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UrabaConecta.Contracts;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

public sealed partial class PlatformAdministrationApiTests
{
    [Fact]
    public async Task Capability_off_on_provisions_neutral_dependencies_once_and_marks_human_data_missing()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var catalog = (await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json))!;
        var request = NewRequest(catalog, $"toggle-{Guid.NewGuid():N}", saveAsDraft: true);
        request.Appointments = false; request.VirtualQueues = true; request.PickupOrders = false;
        var created = (await (await admin.PostAsJsonAsync("/api/v1/admin/businesses", request, Json))
            .Content.ReadFromJsonAsync<PlatformBusinessCreatedDto>(Json))!.Business;
        var ready = await CompleteChecklistAsync(admin, created, catalog);
        var active = (await (await admin.PostAsJsonAsync($"/api/v1/admin/businesses/{created.Id}/activate",
            new PlatformBusinessStateRequest { Version = ready.Version }, Json))
            .Content.ReadFromJsonAsync<PlatformBusinessDto>(Json))!;
        Assert.True(active.IsPublished);

        var enabledResponse = await admin.PutAsJsonAsync($"/api/v1/admin/businesses/{created.Id}/modules",
            new UpdatePlatformModulesRequest { VirtualQueues = true, PickupOrders = true,
                Version = active.Version }, Json);
        Assert.Equal(HttpStatusCode.OK, enabledResponse.StatusCode);
        var enabled = (await enabledResponse.Content.ReadFromJsonAsync<PlatformBusinessDto>(Json))!;
        Assert.Equal("PendingConfiguration", enabled.Status);
        Assert.Contains(enabled.Readiness, x => x.Key == "catalog-product" && !x.IsComplete);

        var disabled = (await (await admin.PutAsJsonAsync($"/api/v1/admin/businesses/{created.Id}/modules",
            new UpdatePlatformModulesRequest { VirtualQueues = true, Version = enabled.Version }, Json))
            .Content.ReadFromJsonAsync<PlatformBusinessDto>(Json))!;
        var enabledAgain = (await (await admin.PutAsJsonAsync($"/api/v1/admin/businesses/{created.Id}/modules",
            new UpdatePlatformModulesRequest { VirtualQueues = true, PickupOrders = true,
                Version = disabled.Version }, Json)).Content.ReadFromJsonAsync<PlatformBusinessDto>(Json))!;
        Assert.Contains("PickupOrders", enabledAgain.Modules);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.PickupOrderSettings.CountAsync(x => x.BusinessId == created.Id));
        Assert.Equal(1, await db.QueueDefinitions.CountAsync(x => x.BusinessId == created.Id));
        Assert.False(await db.Products.AnyAsync(x => x.BusinessId == created.Id));
        Assert.False(await db.Services.AnyAsync(x => x.BusinessId == created.Id));
    }

    [Fact]
    public async Task Capability_api_rejects_operations_that_contradict_their_material_dependencies()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var catalog = (await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json))!;
        var created = (await (await admin.PostAsJsonAsync("/api/v1/admin/businesses",
            NewRequest(catalog, $"graph-{Guid.NewGuid():N}", true), Json))
            .Content.ReadFromJsonAsync<PlatformBusinessCreatedDto>(Json))!.Business;

        var response = await admin.PutAsJsonAsync($"/api/v1/admin/businesses/{created.Id}/modules",
            new UpdatePlatformModulesRequest { PickupOrders = true, Products = false,
                Version = created.Version }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CAPABILITY_DEPENDENCY", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Private_virtual_seller_does_not_expose_address_or_claim_store_pickup()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var catalog = (await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json))!;
        var slug = $"virtual-{Guid.NewGuid():N}";
        var request = NewRequest(catalog, slug, true);
        request.Appointments = false; request.PickupOrders = true;
        request.LocationMode = "PrivatePhysical"; request.OrderFulfillmentMode = "Coordinated";
        request.Address = "Dirección privada que nunca debe salir";
        request.InitialProductCategory = "Catálogo"; request.InitialProductName = "Producto virtual";
        var created = (await (await admin.PostAsJsonAsync("/api/v1/admin/businesses", request, Json))
            .Content.ReadFromJsonAsync<PlatformBusinessCreatedDto>(Json))!.Business;
        Assert.Contains(created.Readiness, x => x.Key == "location" && !x.IsApplicable);
        Assert.Contains(created.Readiness, x => x.Key == "fulfillment" && x.IsComplete);

        var saved = (await (await admin.PutAsJsonAsync($"/api/v1/admin/businesses/{created.Id}/profile",
            new SaveBusinessProfileRequest
            {
                Name = created.Name, Slug = created.Slug, MunicipalityId = created.MunicipalityId,
                CategoryId = created.CategoryId, ShortDescription = created.ShortDescription,
                Description = created.Description, Address = request.Address, PublicPhone = "3000000000",
                LocationMode = "PrivatePhysical", OrderFulfillmentMode = "Coordinated", Version = created.Version
            }, Json)).Content.ReadFromJsonAsync<PlatformBusinessDto>(Json))!;
        foreach (var kind in new[] { "Logo", "Cover" })
            Assert.Equal(HttpStatusCode.Created,
                (await UploadImageAsync(admin, saved.Id, kind, "foto.png", "image/png", TinyPng())).StatusCode);
        var ready = (await admin.GetFromJsonAsync<PlatformBusinessDto>(
            $"/api/v1/admin/businesses/{saved.Id}", Json))!;
        var active = (await (await admin.PostAsJsonAsync($"/api/v1/admin/businesses/{saved.Id}/activate",
            new PlatformBusinessStateRequest { Version = ready.Version }, Json))
            .Content.ReadFromJsonAsync<PlatformBusinessDto>(Json))!;
        Assert.True(active.IsPublished);

        using var visitor = Client();
        var profile = (await visitor.GetFromJsonAsync<BusinessProfileDto>(
            $"/api/v1/public/businesses/{slug}", Json))!;
        Assert.Equal("", profile.Address);
        Assert.Null(profile.ReferencePoint);
        Assert.Null(profile.LocationUrl);
        Assert.Equal("PrivatePhysical", profile.LocationMode);
        var menu = (await visitor.GetFromJsonAsync<PickupMenuDto>(
            $"/api/v1/public/businesses/{slug}/menu", Json))!;
        Assert.Contains("coordina", menu.PublicMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("establecimiento", menu.PublicMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("PublicPhysical", "PickupAtPublicLocation", true, "Pedir para recoger", "Hora para recoger")]
    [InlineData("PrivatePhysical", "Coordinated", false, "Hacer pedido", "Franja preferida")]
    [InlineData("Virtual", "Coordinated", false, "Hacer pedido", "Franja preferida")]
    [InlineData("Virtual", "ExternalDelivery", false, "Hacer pedido", "Franja de entrega preferida")]
    public async Task Public_order_surfaces_follow_location_and_fulfillment_semantics(
        string locationMode, string fulfillmentMode, bool exposesAddress, string action, string slotLabel)
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var catalog = (await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json))!;
        var slug = $"fulfillment-{Guid.NewGuid():N}";
        const string secretAddress = "Carrera privada 91 # 82-73";
        var request = NewRequest(catalog, slug, true);
        request.Appointments = false;
        request.VirtualQueues = false;
        request.PickupOrders = true;
        request.LocationMode = locationMode;
        request.OrderFulfillmentMode = fulfillmentMode;
        request.Address = secretAddress;
        request.InitialProductCategory = "Catálogo";
        request.InitialProductName = "Producto de prueba";
        request.InitialProductPrice = 18000;
        var created = (await (await admin.PostAsJsonAsync("/api/v1/admin/businesses", request, Json))
            .Content.ReadFromJsonAsync<PlatformBusinessCreatedDto>(Json))!.Business;

        var saved = (await (await admin.PutAsJsonAsync($"/api/v1/admin/businesses/{created.Id}/profile",
            new SaveBusinessProfileRequest
            {
                Name = created.Name, Slug = created.Slug, MunicipalityId = created.MunicipalityId,
                CategoryId = created.CategoryId, ShortDescription = created.ShortDescription,
                Description = created.Description, Address = secretAddress, PublicPhone = "3000000000",
                LocationMode = locationMode, OrderFulfillmentMode = fulfillmentMode, Version = created.Version
            }, Json)).Content.ReadFromJsonAsync<PlatformBusinessDto>(Json))!;
        foreach (var kind in new[] { "Logo", "Cover" })
            Assert.Equal(HttpStatusCode.Created,
                (await UploadImageAsync(admin, saved.Id, kind, "foto.png", "image/png", TinyPng())).StatusCode);
        var ready = (await admin.GetFromJsonAsync<PlatformBusinessDto>(
            $"/api/v1/admin/businesses/{saved.Id}", Json))!;
        var activeResponse = await admin.PostAsJsonAsync($"/api/v1/admin/businesses/{saved.Id}/activate",
            new PlatformBusinessStateRequest { Version = ready.Version }, Json);
        Assert.Equal(HttpStatusCode.OK, activeResponse.StatusCode);

        using var visitor = Client();
        var profile = (await visitor.GetFromJsonAsync<BusinessProfileDto>(
            $"/api/v1/public/businesses/{slug}", Json))!;
        var menu = (await visitor.GetFromJsonAsync<PickupMenuDto>(
            $"/api/v1/public/businesses/{slug}/menu", Json))!;
        var profileHtml = await visitor.GetStringAsync($"/negocios/{slug}");
        var orderingHtml = await visitor.GetStringAsync($"/negocios/{slug}/pedidos");

        Assert.Equal(locationMode, profile.LocationMode);
        Assert.Equal(fulfillmentMode, profile.OrderFulfillmentMode);
        Assert.Equal(locationMode, menu.LocationMode);
        Assert.Equal(fulfillmentMode, menu.OrderFulfillmentMode);
        Assert.Contains(action, profileHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(slotLabel, orderingHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(exposesAddress ? secretAddress : "", profile.Address);
        Assert.Equal(exposesAddress ? secretAddress : "", menu.PublicAddress);
        if (exposesAddress)
        {
            Assert.Contains(secretAddress, profileHtml, StringComparison.Ordinal);
            Assert.Contains("Recogida en", orderingHtml, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.DoesNotContain(secretAddress, profileHtml, StringComparison.Ordinal);
            Assert.DoesNotContain(secretAddress, orderingHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("Pedir para recoger", profileHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Hora para recoger", orderingHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("paga en el establecimiento", orderingHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(", Apartadó", profileHtml, StringComparison.OrdinalIgnoreCase);
        }
        if (fulfillmentMode == "ExternalDelivery")
        {
            Assert.Contains("servicio externo", orderingHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("no ofrece seguimiento de mensajería",
                PublicOrderFulfillmentSemantics.For(fulfillmentMode, locationMode).ConfirmationText,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
