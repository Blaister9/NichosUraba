using System.Text.RegularExpressions;
using Microsoft.Playwright;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.EndToEndTests;

/// <summary>
/// El pedido de dos productos recorrido como lo recorre una clienta en el teléfono: llenando el
/// carrito y pulsando "Continuar", que es el único camino visible cuando la carta es más larga que
/// la pantalla.
///
/// <para>
/// <see cref="OrderingJourneyTests"/> ya cubría el resto del vertical, pero agregaba un solo
/// producto y llegaba al formulario con <c>GetByLabel</c>, que desplaza la página por su cuenta. Así
/// nunca pulsaba "Continuar" ni sumaba una segunda línea, y el fallo real —el enlace de fragmento se
/// resolvía contra &lt;base href="/"&gt;, echaba a la Home y se llevaba el carrito— pasó entero por
/// debajo de la suite. Esta prueba existe para que ese camino no vuelva a quedar sin mirar.
/// </para>
/// </summary>
public sealed class OrderingTwoProductJourneyTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    private const string Menu = "/negocios/restaurante-sazon-local/pedidos";
    private const string Alias = "Pedido Dos Productos";

    [Fact]
    public async Task Two_product_order_survives_the_continue_shortcut_and_reaches_the_owner()
    {
        await using var visitorContext = await MobileContext();
        var visitor = await visitorContext.NewPageAsync();
        await visitor.GotoAsync($"{fixture.BaseUrl}{Menu}");
        await Expect(visitor.GetByRole(AriaRole.Heading, new() { Name = "Pedido para recoger" })).ToBeVisibleAsync();

        // 1. Dos productos distintos, uno de ellos repetido: el carrito tiene que sumar por línea.
        await Add(visitor, "Hamburguesa tradicional", 2);
        await Add(visitor, "Papas fritas", 1);
        await Expect(Line(visitor, "Hamburguesa tradicional")).ToHaveTextAsync(new Regex(@"^2 ×"));
        await Expect(Line(visitor, "Papas fritas")).ToHaveTextAsync(new Regex(@"^1 ×"));
        // 2*18.000 + 8.000 = 44.000, y el encabezado del carrito cuenta unidades, no líneas.
        await Expect(visitor.Locator(".total-pedido")).ToContainTextAsync("44.000");
        await Expect(visitor.Locator(".carrito-flotante")).ToContainTextAsync("3 unidades");

        // 2. Los atajos de categoría son enlaces de fragmento: saltan dentro de la carta, no navegan.
        await visitor.Locator(".chip-row a").Filter(new() { HasTextString = "Bebidas" }).ClickAsync();
        await visitor.WaitForTimeoutAsync(500);
        Assert.EndsWith(Menu, new Uri(visitor.Url).AbsolutePath, StringComparison.Ordinal);
        await Expect(visitor.Locator(".total-pedido")).ToContainTextAsync("44.000");

        // 3. "Continuar" es el atajo al formulario. Tiene que dejar a la clienta en la misma página
        //    y con lo que llevaba: si navega de verdad, el circuito se rehace y el carrito se pierde.
        await visitor.Locator(".carrito-flotante a").Filter(new() { HasTextString = "Continuar" }).ClickAsync();
        await visitor.WaitForTimeoutAsync(500);
        Assert.EndsWith(Menu, new Uri(visitor.Url).AbsolutePath, StringComparison.Ordinal);
        await Expect(visitor.Locator(".resumen-lineas li")).ToHaveCountAsync(2);
        await Expect(visitor.Locator(".total-pedido")).ToContainTextAsync("44.000");

        // 4. Checkout con los datos mínimos.
        await visitor.GetByLabel("Hora para recoger").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await visitor.GetByLabel("Nombre o alias").FillAsync(Alias);
        await visitor.GetByLabel("Celular").FillAsync("3001234567");
        await visitor.GetByLabel("Acepto el uso de estos datos").CheckAsync();
        await visitor.GetByRole(AriaRole.Button, new() { Name = "Confirmar pedido" }).ClickAsync();
        await Expect(visitor.GetByTestId("order-created")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(visitor.GetByTestId("order-created")).ToContainTextAsync("44.000");

        // 5. El seguimiento repite las dos líneas y el total, que es lo que la clienta va a comparar
        //    cuando llegue al local a pagar.
        await visitor.GetByRole(AriaRole.Link, new() { Name = "Seguir mi pedido" }).ClickAsync();
        await Expect(visitor.GetByTestId("order-tracking")).ToBeVisibleAsync();
        var trackingUrl = visitor.Url;
        await Expect(visitor.GetByText("2 × Hamburguesa tradicional")).ToBeVisibleAsync();
        await Expect(visitor.GetByText("1 × Papas fritas")).ToBeVisibleAsync();
        await Expect(visitor.GetByText("Pendiente", new() { Exact = true })).ToBeVisibleAsync();

        // 6. El negocio lo recibe con las dos líneas y sus cantidades.
        await using var ownerContext = await MobileContext();
        var owner = await ownerContext.NewPageAsync();
        await Login(owner, DevelopmentSeeder.SazonOrdersWorkerEmail);
        await owner.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.SazonBusinessId}/pedidos");
        var card = owner.Locator("[data-testid=admin-order]").Filter(new() { HasTextString = Alias });
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(card).ToContainTextAsync("2 × Hamburguesa tradicional");
        await Expect(card).ToContainTextAsync("1 × Papas fritas");
        await Expect(card.GetByTestId("order-status")).ToHaveTextAsync("Pendiente");

        // 7. Todos los estados que el producto ya tiene, y la clienta viendo cada uno.
        foreach (var (action, expected) in new[]
                 { ("Aceptar", "Aceptado"), ("Preparar", "En preparación"),
                   ("Listo", "Listo para recoger"), ("Entregado", "Entregado") })
        {
            await card.GetByRole(AriaRole.Button, new() { Name = action, Exact = true }).ClickAsync();
            await Expect(card.GetByTestId("order-status")).ToHaveTextAsync(expected, new() { Timeout = 15_000 });
            await visitor.GotoAsync(trackingUrl);
            await Expect(visitor.GetByText(expected, new() { Exact = true }))
                .ToBeVisibleAsync(new() { Timeout = 15_000 });
        }

        // 8. Un solo pedido: ni el doble envío ni el refresco del seguimiento crean otro.
        await visitor.ReloadAsync();
        await Expect(visitor.GetByTestId("order-tracking")).ToBeVisibleAsync();
        await owner.ReloadAsync();
        await Expect(owner.Locator("[data-testid=admin-order]")
            .Filter(new() { HasTextString = Alias })).ToHaveCountAsync(1);
        Assert.False(await visitor.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > window.innerWidth"));
    }

    /// <summary>
    /// Lo que se lee en el vertical de pedidos, en español. Las franjas y el seguimiento salían con
    /// la cultura del servidor ("Sun 16 Aug", "Sunday, 16 August 2026") mientras el dinero sí usaba
    /// es-CO, y la validación mostraba el texto que arma ASP.NET con el nombre de la propiedad.
    /// </summary>
    [Fact]
    public async Task The_ordering_screens_read_in_spanish()
    {
        await using var context = await MobileContext();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{fixture.BaseUrl}{Menu}");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Pedido para recoger" })).ToBeVisibleAsync();

        // Las franjas: días y meses abreviados en español, y nunca los ingleses.
        var franjas = await page.GetByLabel("Hora para recoger").Locator("option").AllTextContentsAsync();
        var ofrecidas = franjas.Skip(1).ToArray();
        Assert.NotEmpty(ofrecidas);
        Assert.All(ofrecidas, texto => Assert.DoesNotMatch(
            new Regex(@"\b(Mon|Tue|Wed|Thu|Fri|Sat|Sun|Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\b"), texto));
        Assert.Contains(ofrecidas, texto => Regex.IsMatch(texto, @"\b(lun|mar|mié|jue|vie|sáb|dom)\b"));

        // La validación le habla a quien pide, no nombra la propiedad del contrato.
        await Add(page, "Hamburguesa tradicional", 1);
        await page.GetByLabel("Hora para recoger").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Confirmar pedido" }).ClickAsync();
        await Expect(page.GetByText("Escribe tu nombre o alias.")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(page.GetByText("Escribe tu número de celular.")).ToBeVisibleAsync();
        await Expect(page.GetByText("Debes aceptar el tratamiento de datos para continuar.")).ToBeVisibleAsync();
        var validacion = await page.Locator(".validation-message").AllTextContentsAsync();
        Assert.All(validacion, texto => Assert.DoesNotContain("field", texto, StringComparison.OrdinalIgnoreCase));

        // Y el seguimiento, que es la pantalla que se guarda, con la fecha en español.
        await page.GetByLabel("Nombre o alias").FillAsync("Pedido Español");
        await page.GetByLabel("Celular").FillAsync("3001234567");
        await page.GetByLabel("Acepto el uso de estos datos").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Confirmar pedido" }).ClickAsync();
        await Expect(page.GetByTestId("order-created")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await page.GetByRole(AriaRole.Link, new() { Name = "Seguir mi pedido" }).ClickAsync();
        await Expect(page.GetByTestId("order-tracking")).ToBeVisibleAsync();
        var recoges = await page.Locator(".lead").First.InnerTextAsync();
        Assert.Matches(new Regex(@"(lunes|martes|miércoles|jueves|viernes|sábado|domingo)"), recoges);
        Assert.DoesNotMatch(new Regex(@"\b(Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)\b"), recoges);
    }

    private static ILocator Line(IPage page, string product) =>
        page.Locator(".resumen-lineas li").Filter(new() { HasTextString = product });

    private static async Task Add(IPage page, string product, int times)
    {
        var card = page.Locator("[data-testid=product-card]").Filter(new() { HasTextString = product }).First;
        var button = card.GetByRole(AriaRole.Button, new() { NameRegex = new Regex($"^Agregar uno de {product}$") });
        await Assertions.Expect(button).ToBeEnabledAsync(new() { Timeout = 30_000 });
        for (var i = 0; i < times; i++) await button.ClickAsync();
    }

    private Task<IBrowserContext> MobileContext() => fixture.Browser.NewContextAsync(new()
    { ViewportSize = new() { Width = 390, Height = 844 } });

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
