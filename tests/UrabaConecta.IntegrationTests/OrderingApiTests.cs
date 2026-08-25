using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;
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
        // Seis desde que el catálogo ficticio incluye "Bandeja del día", el único producto local con
        // fotografía real: hacía falta para poder juzgar cómo se ve una carta con imágenes.
        Assert.Equal(3, menu!.Categories.Count); Assert.Equal(6, menu.Products.Count);
        var created = await Create(client, "Ana", menu.Products[0].Id);
        var tracked = await client.GetFromJsonAsync<PickupOrderTrackingDto>(
            $"/api/v1/public/orders/{created.TrackingCode}", Json);
        Assert.Equal(created.OrderNumber, tracked!.OrderNumber);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.PickupOrders.SingleAsync(x => x.BusinessId == DevelopmentSeeder.SazonBusinessId &&
            x.PublicOrderNumber == created.OrderNumber);
        Assert.DoesNotContain("Ana", stored.ProtectedCustomerAlias);
        Assert.NotEqual(created.TrackingCode, stored.PublicCodeHash);
        Assert.True(await db.ConsentReceipts.AnyAsync(x => x.PickupOrderId == stored.Id));
        Assert.True(await db.Notifications.AnyAsync(x => x.BusinessId == stored.BusinessId &&
            x.EntityId == stored.Id && x.Kind == NotificationKind.OrderPlaced));
    }

    [Fact]
    public async Task A_rejected_order_leaves_no_partial_rows_or_consumed_number()
    {
        using var client = Client();
        var slot = (await client.GetFromJsonAsync<PickupSlotListDto>(
            "/api/v1/public/businesses/restaurante-sazon-local/pickup-slots", Json))!.Slots.First().Start;
        int ordersBefore, consentsBefore, notificationsBefore, nextNumberBefore;
        await using (var beforeScope = factory.Services.CreateAsyncScope())
        {
            var before = beforeScope.ServiceProvider.GetRequiredService<AppDbContext>();
            ordersBefore = await before.PickupOrders.CountAsync(x => x.BusinessId == DevelopmentSeeder.SazonBusinessId);
            consentsBefore = await before.ConsentReceipts.CountAsync(x => x.BusinessId == DevelopmentSeeder.SazonBusinessId &&
                x.PickupOrderId != null);
            notificationsBefore = await before.Notifications.CountAsync(x => x.BusinessId == DevelopmentSeeder.SazonBusinessId &&
                x.Kind == NotificationKind.OrderPlaced);
            nextNumberBefore = (await before.PickupOrderSettings.SingleAsync(
                x => x.BusinessId == DevelopmentSeeder.SazonBusinessId)).NextOrderNumber;
        }

        var response = await client.PostAsJsonAsync(
            "/api/v1/public/businesses/restaurante-sazon-local/orders",
            Request("Pedido inválido", Guid.NewGuid(), slot), Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var afterScope = factory.Services.CreateAsyncScope();
        var after = afterScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(ordersBefore,
            await after.PickupOrders.CountAsync(x => x.BusinessId == DevelopmentSeeder.SazonBusinessId));
        Assert.Equal(consentsBefore, await after.ConsentReceipts.CountAsync(
            x => x.BusinessId == DevelopmentSeeder.SazonBusinessId && x.PickupOrderId != null));
        Assert.Equal(notificationsBefore, await after.Notifications.CountAsync(
            x => x.BusinessId == DevelopmentSeeder.SazonBusinessId && x.Kind == NotificationKind.OrderPlaced));
        Assert.Equal(nextNumberBefore, (await after.PickupOrderSettings.SingleAsync(
            x => x.BusinessId == DevelopmentSeeder.SazonBusinessId)).NextOrderNumber);
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
            entity.DisplayOrder, true, true, entity.Version);
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

    /// <summary>
    /// La lectura agrupada que arma la disponibilidad tiene que excluir exactamente los mismos
    /// estados que excluía el COUNT por franja. Se comprueba contra PostgreSQL y no con una tienda
    /// falsa porque lo que puede desviarse es el WHERE, y ese WHERE sólo existe en la consulta real.
    /// </summary>
    [Theory]
    [InlineData(PickupOrderStatus.Cancelled)]
    [InlineData(PickupOrderStatus.Rejected)]
    [InlineData(PickupOrderStatus.Delivered)]
    public async Task A_closed_order_stops_taking_up_room_in_its_slot(PickupOrderStatus cerrado)
    {
        using var client = Client();
        var menu = await client.GetFromJsonAsync<PickupMenuDto>(
            "/api/v1/public/businesses/restaurante-sazon-local/menu", Json);
        var libre = (await Franjas(client)).Last();

        var created = await CreateAt(client, $"Cierre {cerrado}", menu!.Products[0].Id, libre.Start);
        var ocupada = (await Franjas(client)).Single(x => x.Start == libre.Start);
        Assert.Equal(libre.RemainingCapacity - 1, ocupada.RemainingCapacity);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = await db.PickupOrders.SingleAsync(x => x.PublicOrderNumber == created.OrderNumber);
            // Se escribe el estado final directamente: lo que se está probando es el filtro de la
            // consulta, no el camino de transiciones, que ya tiene sus propias pruebas.
            db.Entry(order).Property(nameof(PickupOrder.Status)).CurrentValue = cerrado;
            await db.SaveChangesAsync();
        }

        var devuelta = (await Franjas(client)).Single(x => x.Start == libre.Start);
        Assert.Equal(libre.RemainingCapacity, devuelta.RemainingCapacity);
    }

    [Fact]
    public async Task Availability_for_a_whole_week_costs_the_same_as_a_single_day()
    {
        using var client = Client();
        var counter = factory.Services.GetRequiredService<QueryCounter>();

        counter.Reset();
        var unDia = await client.GetFromJsonAsync<PickupSlotListDto>(
            $"/api/v1/public/businesses/restaurante-sazon-local/pickup-slots?date={DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)):yyyy-MM-dd}",
            Json);
        var sentenciasUnDia = counter.Count;

        counter.Reset();
        var laSemana = await client.GetFromJsonAsync<PickupSlotListDto>(
            "/api/v1/public/businesses/restaurante-sazon-local/pickup-slots", Json);
        var sentenciasSemana = counter.Count;

        // Siete días devuelven muchas más franjas...
        Assert.True(laSemana!.Slots.Count > unDia!.Slots.Count * 3,
            $"La semana devolvió {laSemana.Slots.Count} franjas y el día {unDia.Slots.Count}.");
        // ...y cuestan lo mismo. Antes cada franja añadía su propio COUNT.
        Assert.Equal(sentenciasUnDia, sentenciasSemana);
        Assert.True(sentenciasSemana <= 4,
            $"La disponibilidad de la semana costó {sentenciasSemana} sentencias para {laSemana.Slots.Count} franjas.");
    }

    private async Task<IReadOnlyList<PickupSlotDto>> Franjas(HttpClient client)
        => (await client.GetFromJsonAsync<PickupSlotListDto>(
            "/api/v1/public/businesses/restaurante-sazon-local/pickup-slots", Json))!.Slots;

    private async Task<PickupOrderCreatedDto> CreateAt(HttpClient client, string alias, Guid productId,
        DateTimeOffset slot)
    {
        var response = await client.PostAsJsonAsync("/api/v1/public/businesses/restaurante-sazon-local/orders",
            Request(alias, productId, slot), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PickupOrderCreatedDto>(Json))!;
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
        CustomerAlias = alias, Phone = "3001234567",
        // Es el formato que reprodujo el fallo en Demo: el instante correcto expresado con la hora
        // local de Colombia. La aplicación debe normalizarlo antes de hablar con PostgreSQL.
        PickupStart = slot.ToOffset(TimeSpan.FromHours(-5)), ConsentAccepted = true,
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
