using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using UrabaConecta.Contracts;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.EndToEndTests;

[Collection(PublicSiteCollection.Name)]
public sealed class PickupOrderFormBindingTests(BrowserFixture fixture, Xunit.Abstractions.ITestOutputHelper output)
{
    private const string Slug = "restaurante-sazon-local";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("keyboard")]
    [InlineData("fill")]
    [InlineData("input-event")]
    [InlineData("change-event")]
    public async Task Visible_fields_survive_recomposition_and_reach_the_persisted_order(string mode)
    {
        using var http = new HttpClient { BaseAddress = new(fixture.BaseUrl) };
        var menu = (await http.GetFromJsonAsync<PickupMenuDto>($"/api/v1/public/businesses/{Slug}/menu", Json))!;
        var product = menu.Products.First(p => p.IsAvailable);
        var other = menu.Products.First(p => p.IsAvailable && p.Id != product.Id);
        await using var context = await fixture.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await context.NewPageAsync();
        page.PageError += (_, error) => output.WriteLine(error);
        var alias = $"Human J06 {mode}";
        const string phone = "3000000006";
        const string notes = "Prueba de binding J06, sin entrega real.";
        await page.GotoAsync($"{fixture.BaseUrl}/negocios/{Slug}");
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Auto", Exact = true }))
            .ToBeEnabledAsync(new() { Timeout = 30000 });
        await page.Locator($"a[href='/negocios/{Slug}/pedidos#producto-{product.Id}']").ClickAsync();
        var card = page.Locator($"[data-product-id='{product.Id}']");
        await Assertions.Expect(card.Locator(".product-choice")).ToBeEnabledAsync(new() { Timeout = 30000 });
        await card.Locator(".product-cta").ClickAsync();
        await card.GetByRole(AriaRole.Button, new() { Name = $"Agregar uno de {product.Name}" }).ClickAsync();
        await card.Locator(".product-cta").ClickAsync();
        await page.GetByLabel("Hora para recoger").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        var nameInput = page.GetByLabel("Nombre o alias");
        var phoneInput = page.GetByLabel("Celular", new() { Exact = true });
        var noteInput = page.GetByLabel("Nota general (opcional)");
        if (mode == "input-event")
        {
            await page.GetByLabel("Acepto el uso de estos datos").CheckAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Confirmar pedido", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByText("Escribe tu nombre o alias.", new() { Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("Escribe tu número de celular.", new() { Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByTestId("order-created")).ToHaveCountAsync(0);
            await Enter(phoneInput, "no-es-numero", mode);
            await Assertions.Expect(page.GetByText("Escribe un celular válido: sólo números, entre 7 y 15 dígitos.", new() { Exact = true })).ToBeVisibleAsync();
        }
        await Enter(nameInput, alias, mode);
        await Enter(phoneInput, phone, mode);
        await Enter(noteInput, notes, mode);
        output.WriteLine($"{mode}: DOM name={await nameInput.InputValueAsync()}, phone={await phoneInput.InputValueAsync()}, notes={await noteInput.InputValueAsync()}");
        // InputBase adds this only after EditContext.NotifyFieldChanged. This is checked before
        // relying on blur/change, so visible-only text cannot silently pass the regression test.
        await Assertions.Expect(nameInput).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("modified"));
        await Assertions.Expect(phoneInput).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("modified"));
        await Assertions.Expect(noteInput).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("modified"));
        await page.GetByLabel("Acepto el uso de estos datos").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Volver a productos" }).ClickAsync();
        await card.GetByRole(AriaRole.Button, new() { Name = $"Agregar uno de {product.Name}" }).ClickAsync();
        await card.GetByRole(AriaRole.Button, new() { Name = $"Quitar uno de {product.Name}" }).ClickAsync();
        await card.Locator(".product-deselect").ClickAsync();
        await page.Locator($"[data-product-id='{other.Id}'] .product-choice").ClickAsync();
        await card.Locator(".product-choice").ClickAsync();
        await card.Locator(".product-cta").ClickAsync();
        await Assertions.Expect(nameInput).ToHaveValueAsync(alias);
        await Assertions.Expect(phoneInput).ToHaveValueAsync(phone);
        await Assertions.Expect(noteInput).ToHaveValueAsync(notes);
        await Assertions.Expect(card.GetByTestId("quantity")).ToHaveTextAsync("2");
        output.WriteLine($"Before submit: DOM name={await nameInput.InputValueAsync()}, phone={await phoneInput.InputValueAsync()}; all fields modified; no validation messages.");
        await Assertions.Expect(page.Locator(".validation-message, .validation-errors li")).ToHaveCountAsync(0);
        await page.GetByRole(AriaRole.Button, new() { Name = "Confirmar pedido", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("order-created")).ToBeVisibleAsync(new() { Timeout = 30000 });
        var code = await page.GetByTestId("tracking-code").InnerTextAsync();
        Assert.True(code.Length >= 20);
        var response = await http.GetAsync($"/api/v1/public/orders/{code}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<PickupOrderTrackingDto>(Json))!;
        Assert.Equal(product.Id, order.Lines.Single().ProductId);
        Assert.Equal(2, order.Lines.Single().Quantity);
        Assert.Equal(product.ReferencePrice * 2, order.Total);
        Assert.Equal("Pending", order.Status);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.ConnectionString).Options);
        var stored = await db.PickupOrders.Include(o => o.Lines).SingleAsync(o => o.BusinessId == DevelopmentSeeder.SazonBusinessId && o.PublicOrderNumber == order.OrderNumber);
        Assert.Equal(product.Id, stored.Lines.Single().ProductId);
        Assert.Equal(2, stored.Lines.Single().Quantity);
        Assert.Equal(order.Total, stored.Total);
        // The real operational API decrypts the persisted fields; no direct-create API or mock.
        await using var operations = await fixture.Browser.NewContextAsync();
        var operatorPage = await operations.NewPageAsync();
        await operatorPage.GotoAsync($"{fixture.BaseUrl}/Account/Login");
        await operatorPage.GetByLabel("Correo").FillAsync(DevelopmentSeeder.SazonOrdersWorkerEmail);
        await operatorPage.GetByLabel("Contraseña").FillAsync(DevelopmentSeeder.DemoPassword);
        await operatorPage.GetByRole(AriaRole.Button, new() { Name = "Ingresar" }).ClickAsync();
        await operatorPage.WaitForURLAsync(url => url.Contains("/panel"));
        var boardResponse = await operations.APIRequest.GetAsync($"{fixture.BaseUrl}/api/v1/businesses/{DevelopmentSeeder.SazonBusinessId}/orders");
        Assert.Equal(200, boardResponse.Status);
        var board = JsonSerializer.Deserialize<PickupOrderBoardDto>(await boardResponse.TextAsync(), Json)!;
        var persisted = board.Items.Single(o => o.Id == stored.Id);
        Assert.Equal(alias, persisted.CustomerAlias);
        Assert.Equal(phone, persisted.Phone);
        Assert.Equal(notes, persisted.Notes);
        output.WriteLine($"UI submit → server use case → DB {stored.Id} → HTTP 200; alias/phone/notes and product/quantity/total match; code={code}.");
    }

    private static async Task Enter(ILocator input, string value, string mode)
    {
        if (mode == "keyboard") await input.PressSequentiallyAsync(value);
        else if (mode == "fill") await input.FillAsync(value);
        else if (mode == "input-event") await input.EvaluateAsync("(el, value) => { el.value = value; el.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertReplacementText', data: value })); }", value);
        else await input.EvaluateAsync("(el, value) => { el.value = value; el.dispatchEvent(new Event('change', { bubbles: true })); }", value);
    }
}
