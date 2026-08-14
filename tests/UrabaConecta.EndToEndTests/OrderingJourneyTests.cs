using System.Text.RegularExpressions;
using Microsoft.Playwright;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.EndToEndTests;

public sealed class OrderingJourneyTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    [Fact]
    public async Task Eight_pickup_order_scenarios_work_in_real_mobile_chromium()
    {
        await using var visitorContext = await MobileContext();
        var visitor = await visitorContext.NewPageAsync();

        // 1. Directorio -> restaurante -> pedidos.
        await visitor.GotoAsync($"{fixture.BaseUrl}/negocios/restaurante-sazon-local");
        await Expect(visitor.GetByRole(AriaRole.Heading, new() { Name = "Restaurante Sazón Local" })).ToBeVisibleAsync();
        // La ficha de un negocio de pedidos ya enseña sus primeros productos, así que el enlace al
        // menú completo dice "Ver todo"; "Ver menú" sólo queda cuando todavía no hay catálogo.
        await Expect(visitor.GetByRole(AriaRole.Link, new() { Name = "Ver todo" })
            .Or(visitor.GetByRole(AriaRole.Link, new() { Name = "Ver menú" })).First).ToBeVisibleAsync();
        await visitor.GotoAsync($"{fixture.BaseUrl}/negocios/restaurante-sazon-local/pedidos");
        await Expect(visitor.GetByRole(AriaRole.Heading, new() { Name = "Pedido para recoger" })).ToBeVisibleAsync();

        // 2. Menú por categorías y carrito recalculado.
        await Expect(visitor.GetByRole(AriaRole.Heading, new() { Name = "Hamburguesas" })).ToBeVisibleAsync();
        // El contador dejó de ser dos botones rotulados "+" y "−": ahora cada uno dice de qué
        // producto habla, así que el nombre accesible es "Agregar uno de …".
        await visitor.Locator("[data-testid=product-card]").First
            .GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Agregar uno de ") }).ClickAsync();
        // El resumen concuerda en número: "1 unidades" era una cadena armada, no una frase.
        await Expect(visitor.GetByText("1 unidad").First).ToBeVisibleAsync();

        // 3. Datos mínimos, consentimiento y franja.
        await visitor.GetByLabel("Hora para recoger").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await visitor.GetByLabel("Nombre o alias").FillAsync("Pedido E2E");
        await visitor.GetByLabel("Celular").FillAsync("3001234567");
        await visitor.GetByLabel("Acepto el uso de estos datos").CheckAsync();
        await visitor.GetByRole(AriaRole.Button, new() { Name = "Confirmar pedido" }).ClickAsync();
        await Expect(visitor.GetByTestId("order-created")).ToBeVisibleAsync();

        // 4. Seguimiento individual.
        await visitor.GetByRole(AriaRole.Link, new() { Name = "Seguir mi pedido" }).ClickAsync();
        await Expect(visitor.GetByTestId("order-tracking")).ToBeVisibleAsync();
        await Expect(visitor.GetByText("Pendiente", new() { Exact = true })).ToBeVisibleAsync();

        // 5. Operador autorizado ve PII y acepta.
        await using var operatorContext = await MobileContext();
        var operations = await operatorContext.NewPageAsync();
        await Login(operations, DevelopmentSeeder.SazonOrdersWorkerEmail);
        await operations.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.SazonBusinessId}/pedidos");
        var orderCard = operations.Locator("[data-testid=admin-order]").Filter(new() { HasTextString = "Pedido E2E" });
        await Expect(orderCard).ToBeVisibleAsync();
        await orderCard.GetByRole(AriaRole.Button, new() { Name = "Aceptar" }).ClickAsync();
        await Expect(orderCard.GetByText("Aceptado")).ToBeVisibleAsync();

        // 6. Flujo operativo completo.
        await orderCard.GetByRole(AriaRole.Button, new() { Name = "Preparar" }).ClickAsync();
        await orderCard.GetByRole(AriaRole.Button, new() { Name = "Listo" }).ClickAsync();
        await orderCard.GetByRole(AriaRole.Button, new() { Name = "Entregado" }).ClickAsync();
        await Expect(orderCard.GetByText("Entregado")).ToBeVisibleAsync();

        // 7. Configuración y catálogo disponibles solo al rol correspondiente.
        await using var ownerContext = await MobileContext();
        var owner = await ownerContext.NewPageAsync();
        await Login(owner, DevelopmentSeeder.SazonOwnerEmail);
        await owner.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.SazonBusinessId}/configuracion/pedidos");
        await Expect(owner.GetByRole(AriaRole.Heading, new() { Name = "Pedidos y catálogo" })).ToBeVisibleAsync();
        await Expect(owner.Locator("input[value='Hamburguesa especial']")).ToBeVisibleAsync();

        // 8. Aislamiento y composición móvil sin desbordamiento.
        await using var deniedContext = await MobileContext();
        var denied = await deniedContext.NewPageAsync();
        await Login(denied, DevelopmentSeeder.SazonNoPermissionEmail);
        await denied.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.SazonBusinessId}/pedidos");
        await Expect(denied.GetByText("No tiene permiso para administrar pedidos.")).ToBeVisibleAsync();
        Assert.False(await visitor.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"));
        Assert.False(await operations.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"));
        Assert.False(await owner.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"));
    }

    private Task<IBrowserContext> MobileContext() => fixture.Browser.NewContextAsync(new()
    { ViewportSize = new() { Width = 360, Height = 800 } });
    private async Task Login(IPage page, string email)
    {
        await page.GotoAsync($"{fixture.BaseUrl}/Account/Login");
        await page.GetByLabel("Correo").FillAsync(email);
        await page.GetByLabel("Contraseña").FillAsync(DevelopmentSeeder.DemoPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Ingresar" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/panel"));
    }
    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
