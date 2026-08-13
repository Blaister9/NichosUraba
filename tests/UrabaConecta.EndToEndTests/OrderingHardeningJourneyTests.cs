using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;
using UrabaConecta.Contracts;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.EndToEndTests;

[Collection("Ordering hardening")]
public sealed class OrderingHardeningJourneyTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    private const string Slug = "restaurante-sazon-local";
    private static readonly Guid ProductId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Frozen_price_remains_historical_and_new_order_uses_updated_price()
    {
        var original = await CurrentProduct();
        var changedPrice = original.ReferencePrice + 1_000;
        var alias = $"Precio {Guid.NewGuid():N}"[..15];
        var oldOrder = await CreateOrder(alias, ProductId);

        await using var ownerContext = await Mobile(390, 844);
        var owner = await ownerContext.NewPageAsync();
        await Login(owner, DevelopmentSeeder.SazonOwnerEmail);
        await ChangeProduct(owner, changedPrice, true);

        await using var publicContext = await Mobile(390, 844);
        var tracking = await publicContext.NewPageAsync();
        await tracking.GotoAsync($"{fixture.BaseUrl}/seguimiento/pedidos/{oldOrder.TrackingCode}");
        // El total del pedido dejó de llevar el prefijo "Total:" pegado a la cifra, así que en un
        // pedido de una sola línea el mismo importe aparece dos veces: como línea y como total.
        // Lo que este caso vigila es que el precio congelado siga siendo el histórico.
        await Expect(tracking.GetByText(original.ReferencePrice.ToString("C0", Colombia),
            new() { Exact = true }).First).ToBeVisibleAsync();
        Assert.True(await tracking.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));

        var newOrder = await CreateOrder($"Nuevo {Guid.NewGuid():N}"[..15], ProductId);
        Assert.Equal(changedPrice, newOrder.Total);
    }

    [Fact]
    public async Task Inactive_product_disappears_and_historical_order_survives_then_product_returns()
    {
        var historical = await CreateOrder($"Hist {Guid.NewGuid():N}"[..15], ProductId);
        var current = await CurrentProduct();
        await using var ownerContext = await Mobile(412, 915);
        var owner = await ownerContext.NewPageAsync();
        await Login(owner, DevelopmentSeeder.SazonOwnerEmail);

        await using var publicContext = await Mobile(412, 915);
        var menu = await publicContext.NewPageAsync();
        try
        {
            await ChangeProduct(owner, current.ReferencePrice, false);
            await menu.GotoAsync($"{fixture.BaseUrl}/negocios/{Slug}/pedidos");
            await Expect(menu.GetByText("Hamburguesa tradicional", new() { Exact = true })).Not.ToBeVisibleAsync();
            Assert.True(await menu.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
            await menu.GotoAsync($"{fixture.BaseUrl}/seguimiento/pedidos/{historical.TrackingCode}");
            await Expect(menu.GetByText("Hamburguesa tradicional")).ToBeVisibleAsync();
        }
        finally
        {
            await ChangeProduct(owner, current.ReferencePrice, true);
        }
        await menu.GotoAsync($"{fixture.BaseUrl}/negocios/{Slug}/pedidos");
        await Expect(menu.GetByText("Hamburguesa tradicional", new() { Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Full_slot_shows_specific_error_and_another_slot_accepts_order()
    {
        using var http = new HttpClient { BaseAddress = new Uri(fixture.BaseUrl) };
        var slots = await http.GetFromJsonAsync<PickupSlotListDto>(
            $"/api/v1/public/businesses/{Slug}/pickup-slots", Json);
        var slot = slots!.Slots.Last();
        var fills = Enumerable.Range(0, 5).Select(i => http.PostAsJsonAsync(
            $"/api/v1/public/businesses/{Slug}/orders",
            Request($"Cupo {i}", ProductId, slot.Start), Json));
        var filled = await Task.WhenAll(fills);
        Assert.All(filled, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));

        var rejected = await http.PostAsJsonAsync($"/api/v1/public/businesses/{Slug}/orders",
            Request("Sin cupo", ProductId, slot.Start), Json);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Contains("franja", problem.GetProperty("title").GetString(), StringComparison.OrdinalIgnoreCase);

        var alternative = slots.Slots.First(x => x.Start != slot.Start);
        var accepted = await http.PostAsJsonAsync($"/api/v1/public/businesses/{Slug}/orders",
            Request("Otro horario", ProductId, alternative.Start), Json);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
    }

    [Fact]
    public async Task Public_cancellation_is_rejected_after_preparation_and_status_does_not_change()
    {
        var alias = $"Tarde {Guid.NewGuid():N}"[..15];
        var created = await CreateOrder(alias, ProductId);

        await using var operationsContext = await Mobile(360, 800);
        var operations = await operationsContext.NewPageAsync();
        await Login(operations, DevelopmentSeeder.SazonOrdersWorkerEmail);
        await operations.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.SazonBusinessId}/pedidos");
        var card = operations.Locator("[data-testid=admin-order]").Filter(new() { HasTextString = alias });
        await card.GetByRole(AriaRole.Button, new() { Name = "Aceptar" }).ClickAsync();
        await card.GetByRole(AriaRole.Button, new() { Name = "Preparar" }).ClickAsync();
        await Expect(card.GetByText("En preparación")).ToBeVisibleAsync();
        Assert.True(await operations.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));

        using var http = new HttpClient { BaseAddress = new Uri(fixture.BaseUrl) };
        var tracking = await http.GetFromJsonAsync<PickupOrderTrackingDto>(
            $"/api/v1/public/orders/{created.TrackingCode}", Json);
        var cancellation = await http.PostAsJsonAsync($"/api/v1/public/orders/{created.TrackingCode}/cancel",
            new PickupOrderCommandRequest { Version = tracking!.Version }, Json);
        Assert.Equal(HttpStatusCode.Conflict, cancellation.StatusCode);
        var after = await http.GetFromJsonAsync<PickupOrderTrackingDto>(
            $"/api/v1/public/orders/{created.TrackingCode}", Json);
        Assert.Equal("Preparing", after!.Status);
    }

    private async Task<ProductDto> CurrentProduct()
    {
        using var http = new HttpClient { BaseAddress = new Uri(fixture.BaseUrl) };
        var menu = await http.GetFromJsonAsync<PickupMenuDto>($"/api/v1/public/businesses/{Slug}/menu", Json);
        return menu!.Products.Single(x => x.Id == ProductId);
    }

    private async Task<PickupOrderCreatedDto> CreateOrder(string alias, Guid productId)
    {
        using var http = new HttpClient { BaseAddress = new Uri(fixture.BaseUrl) };
        var slots = await http.GetFromJsonAsync<PickupSlotListDto>(
            $"/api/v1/public/businesses/{Slug}/pickup-slots", Json);
        var response = await http.PostAsJsonAsync($"/api/v1/public/businesses/{Slug}/orders",
            Request(alias, productId, slots!.Slots.First().Start), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PickupOrderCreatedDto>(Json))!;
    }

    private async Task ChangeProduct(IPage owner, decimal price, bool active)
    {
        await owner.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.SazonBusinessId}/configuracion/pedidos");
        var editor = owner.Locator($"[data-product-id='{ProductId}']");
        await Expect(editor).ToBeVisibleAsync();
        var productsJson = await owner.EvaluateAsync<string>(
            "async url => await (await fetch(url)).text()",
            $"/api/v1/businesses/{DevelopmentSeeder.SazonBusinessId}/products");
        var products = JsonSerializer.Deserialize<IReadOnlyList<ProductDto>>(productsJson, Json)!;
        var current = products.Single(x => x.Id == ProductId);
        var updateOk = await owner.EvaluateAsync<bool>(
            """async args => (await fetch(args.url, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify(args.body) })).ok""",
            new { url = $"/api/v1/businesses/{DevelopmentSeeder.SazonBusinessId}/products/{ProductId}",
                body = new SaveProductRequest
            {
                CategoryId = current.CategoryId, Name = current.Name, Description = current.Description,
                ReferencePrice = price, DisplayOrder = current.DisplayOrder, IsActive = active,
                Version = current.Version
            }});
        Assert.True(updateOk);

        using var http = new HttpClient { BaseAddress = new Uri(fixture.BaseUrl) };
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var menu = await http.GetFromJsonAsync<PickupMenuDto>(
                $"/api/v1/public/businesses/{Slug}/menu", Json);
            var product = menu!.Products.SingleOrDefault(x => x.Id == ProductId);
            if ((active && product?.ReferencePrice == price) || (!active && product is null))
                return;
            await Task.Delay(250);
        }
        Assert.Fail("El cambio de producto no se reflejó en el menú público.");
    }

    private static CreatePickupOrderRequest Request(string alias, Guid productId, DateTimeOffset start) => new()
    {
        CustomerAlias = alias, Phone = "3001234567", PickupStart = start,
        ConsentAccepted = true, ConsentNoticeVersion = "pilot-1",
        Lines = [new() { ProductId = productId, Quantity = 1 }]
    };
    private Task<IBrowserContext> Mobile(int width, int height) => fixture.Browser.NewContextAsync(new()
    { ViewportSize = new() { Width = width, Height = height } });
    private async Task Login(IPage page, string email)
    {
        await page.GotoAsync($"{fixture.BaseUrl}/Account/Login");
        await page.GetByLabel("Correo").FillAsync(email);
        await page.GetByLabel("Contraseña").FillAsync(DevelopmentSeeder.DemoPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Ingresar" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/panel"));
    }
    private static readonly System.Globalization.CultureInfo Colombia = new("es-CO");
    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}

[CollectionDefinition("Ordering hardening", DisableParallelization = true)]
public sealed class OrderingHardeningCollection;
