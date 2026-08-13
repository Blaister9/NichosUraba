using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UrabaConecta.Contracts;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

public sealed partial class OrderingApiTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Public_menu_creates_and_tracks_order_without_persisting_plain_pii()
    {
        using var client = Client();
        var menu = await client.GetFromJsonAsync<PickupMenuDto>(
            "/api/v1/public/businesses/restaurante-sazon-local/menu", Json);
        Assert.Equal(3, menu!.Categories.Count); Assert.Equal(5, menu.Products.Count);
        var created = await Create(client, "Ana", menu.Products[0].Id);
        var tracked = await client.GetFromJsonAsync<PickupOrderTrackingDto>(
            $"/api/v1/public/orders/{created.TrackingCode}", Json);
        Assert.Equal(created.OrderNumber, tracked!.OrderNumber);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.PickupOrders.SingleAsync(x => x.PublicOrderNumber == created.OrderNumber);
        Assert.DoesNotContain("Ana", stored.ProtectedCustomerAlias);
        Assert.NotEqual(created.TrackingCode, stored.PublicCodeHash);
    }

    [Fact]
    public async Task Concurrent_capacity_allows_only_configured_maximum()
    {
        using var client = Client();
        var menu = await client.GetFromJsonAsync<PickupMenuDto>(
            "/api/v1/public/businesses/restaurante-sazon-local/menu", Json);
        var slots = await client.GetFromJsonAsync<PickupSlotListDto>(
            "/api/v1/public/businesses/restaurante-sazon-local/pickup-slots", Json);
        var slot = slots!.Slots.Last();
        var calls = Enumerable.Range(0, 6).Select(i => client.PostAsJsonAsync(
            "/api/v1/public/businesses/restaurante-sazon-local/orders",
            Request($"C{i}", menu!.Products[0].Id, slot.Start), Json));
        var responses = await Task.WhenAll(calls);
        Assert.Equal(5, responses.Count(x => x.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Frozen_price_survives_catalog_price_change()
    {
        using var client = Client();
        var menu = await client.GetFromJsonAsync<PickupMenuDto>(
            "/api/v1/public/businesses/restaurante-sazon-local/menu", Json);
        var product = menu!.Products[0];
        var created = await Create(client, "Precio", product.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = await db.Products.SingleAsync(x => x.Id == product.Id);
        entity.Update(entity.ProductCategoryId, entity.Name, entity.Description, product.ReferencePrice + 9000,
            entity.DisplayOrder, true, entity.Version);
        await db.SaveChangesAsync();
        var tracked = await client.GetFromJsonAsync<PickupOrderTrackingDto>(
            $"/api/v1/public/orders/{created.TrackingCode}", Json);
        Assert.Equal(product.ReferencePrice, tracked!.Lines[0].UnitPrice);
    }

    [Fact]
    public async Task Orders_permission_and_business_isolation_are_enforced()
    {
        using var allowed = Client(); using var denied = Client(); using var other = Client();
        await Login(allowed, DevelopmentSeeder.SazonOrdersWorkerEmail);
        await Login(denied, DevelopmentSeeder.SazonNoPermissionEmail);
        await Login(other, DevelopmentSeeder.OtherOwnerEmail);
        var url = $"/api/v1/businesses/{DevelopmentSeeder.SazonBusinessId}/orders";
        Assert.Equal(HttpStatusCode.OK, (await allowed.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await denied.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Stale_version_and_invalid_transition_return_conflict()
    {
        using var publicClient = Client(); using var owner = Client();
        var menu = await publicClient.GetFromJsonAsync<PickupMenuDto>(
            "/api/v1/public/businesses/restaurante-sazon-local/menu", Json);
        var created = await Create(publicClient, "Estado", menu!.Products[0].Id);
        await Login(owner, DevelopmentSeeder.SazonOwnerEmail);
        var board = await owner.GetFromJsonAsync<PickupOrderBoardDto>(
            $"/api/v1/businesses/{DevelopmentSeeder.SazonBusinessId}/orders", Json);
        var order = board!.Items.Single(x => x.OrderNumber == created.OrderNumber);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.SazonBusinessId}/orders/{order.Id}/deliver",
            new PickupOrderCommandRequest { Version = order.Version }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.SazonBusinessId}/orders/{order.Id}/accept",
            new PickupOrderCommandRequest { Version = order.Version }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.SazonBusinessId}/orders/{order.Id}/prepare",
            new PickupOrderCommandRequest { Version = order.Version }, Json)).StatusCode);
    }

    private async Task<PickupOrderCreatedDto> Create(HttpClient client, string alias, Guid productId)
    {
        var slots = await client.GetFromJsonAsync<PickupSlotListDto>(
            "/api/v1/public/businesses/restaurante-sazon-local/pickup-slots", Json);
        var response = await client.PostAsJsonAsync("/api/v1/public/businesses/restaurante-sazon-local/orders",
            Request(alias, productId, slots!.Slots.First().Start), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PickupOrderCreatedDto>(Json))!;
    }
    private static CreatePickupOrderRequest Request(string alias, Guid productId, DateTimeOffset slot) => new()
    {
        CustomerAlias = alias, Phone = "3001234567", PickupStart = slot, ConsentAccepted = true,
        ConsentNoticeVersion = "pilot-1", Lines = [new() { ProductId = productId, Quantity = 1 }]
    };
    private HttpClient Client() => factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
    private static async Task Login(HttpClient client, string email)
    {
        var html = await client.GetStringAsync("/Account/Login");
        var token = AntiforgeryRegex().Match(html).Groups[1].Value.Replace("&quot;", "\"").Replace("&amp;", "&");
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["_handler"] = "login",
            ["Input.Email"] = email, ["Input.Password"] = DevelopmentSeeder.DemoPassword
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryRegex();
}
