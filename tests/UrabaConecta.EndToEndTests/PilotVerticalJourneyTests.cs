using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.EndToEndTests;

/// <summary>
/// Las cinco verticales del piloto sobre el mismo código.
///
/// Es la prueba de que UrabáConecta es una plataforma configurable y no cinco aplicaciones: la
/// odontología sólo agenda, la droguería sólo despacha, la veterinaria hace las tres cosas, y
/// ninguna de las tres necesitó una rama distinta. Lo que cambia entre ellas son filas de
/// capacidades, no condicionales por categoría.
///
/// Todas las clases con IClassFixture&lt;BrowserFixture&gt; levantan su propio proceso y su propio
/// PostgreSQL, así que las cinco verticales viven en UNA clase a propósito: cinco clases habrían
/// añadido cinco contenedores a una suite que ya va justa, y la lentitud acaba tumbando pruebas
/// ajenas.
/// </summary>
public sealed class PilotVerticalJourneyTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => PilotVerticalFixtures.EnsureAsync(fixture.ConnectionString);
    public Task DisposeAsync() => Task.CompletedTask;

    // =======================================================================================
    // Odontología: descubrir, agendar, ver la cita en el panel, cambiar estado y que el cliente
    // lo vea. Y que el aviso quede guardado aunque Push no entregue nada.
    // =======================================================================================

    [Fact]
    public async Task Dentistry_books_an_appointment_and_the_notice_survives_a_silent_push()
    {
        var vertical = PilotVerticalFixtures.Dentistry;
        await using var context = await MobileContext();
        var page = await context.NewPageAsync();

        // 1. Descubrir el negocio por su ficha pública.
        await page.GotoAsync($"{fixture.BaseUrl}/negocios/{vertical.Slug}");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = vertical.Name, Exact = true }))
            .ToBeVisibleAsync();

        // 2. Crear la cita.
        var code = await BookAppointment(page, vertical.Slug, "E2E Odontología");

        // 3. El negocio la ve en su agenda.
        await Login(page, PilotVerticalFixtures.OwnerEmail);
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{vertical.BusinessId}/citas");
        var card = page.Locator("[data-testid=appointment-card]")
            .Filter(new() { HasTextString = "E2E Odontología" });
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // 4. El aviso quedó guardado. En este ambiente ningún navegador se suscribió a Web Push, así
        //    que la bandeja es la ÚNICA vía por la que el negocio se entera: exactamente el caso que
        //    en producción dejaba un pedido sin avisar a nadie.
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{vertical.BusinessId}/avisos");
        await Expect(page.Locator("[data-testid=aviso][data-kind=AppointmentRequested]").First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // 5. Cambio de estado y el cliente lo consulta con su código.
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{vertical.BusinessId}/citas");
        card = page.Locator("[data-testid=appointment-card]").Filter(new() { HasTextString = "E2E Odontología" });
        await card.GetByRole(AriaRole.Button, new() { Name = "Confirmar" }).First.ClickAsync();
        await Expect(card.GetByText("Confirmada", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        await page.GotoAsync($"{fixture.BaseUrl}/seguimiento/citas/{code}");
        await Expect(page.GetByText("Confirmada", new() { Exact = true })).ToBeVisibleAsync();
        // Y la novedad queda escrita para el cliente, no sólo el estado actual.
        await Expect(page.Locator("[data-testid=tracking-update][data-kind=AppointmentConfirmed]"))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        Assert.False(await Overflows(page));
    }

    // =======================================================================================
    // Veterinaria: las tres operaciones a la vez, que es la combinación que obligaba a decidir si
    // esto era una plataforma o cinco.
    // =======================================================================================

    [Fact]
    public async Task Veterinary_runs_queue_and_orders_on_the_same_business()
    {
        var vertical = PilotVerticalFixtures.Veterinary;
        await using var context = await MobileContext();
        var page = await context.NewPageAsync();

        // 1. Turno público: la fila está abierta y admite gente.
        await page.GotoAsync($"{fixture.BaseUrl}/negocios/{vertical.Slug}/turnos");
        await page.GetByLabel("Alias corto (opcional)").FillAsync("E2E Vet");
        await page.GetByRole(AriaRole.Checkbox).CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Tomar turno" }).ClickAsync();
        await Expect(page.GetByTestId("queue-created")).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // 2. Y el mismo negocio recibe pedidos.
        var orderCode = await PlaceOrder(page, vertical.Slug, "E2E Vet pedido");

        // 3. El panel ofrece las tres operaciones, porque las tres están habilitadas aquí.
        await Login(page, PilotVerticalFixtures.OwnerEmail);
        await page.GotoAsync($"{fixture.BaseUrl}/panel");
        var card = page.Locator($"[data-testid=business-dashboard][data-business-id='{vertical.BusinessId}']");
        await Expect(card.GetByRole(AriaRole.Link, new() { Name = "Administrar citas" })).ToBeVisibleAsync();
        await Expect(card.GetByRole(AriaRole.Link, new() { Name = "Operar turnos" })).ToBeVisibleAsync();
        await Expect(card.GetByRole(AriaRole.Link, new() { Name = "Operar pedidos" })).ToBeVisibleAsync();

        // 4. El pedido avanza y el cliente ve el cambio con su código.
        await AdvanceOrder(page, vertical.BusinessId, "E2E Vet pedido", "Aceptar", "Aceptado");
        await page.GotoAsync($"{fixture.BaseUrl}/seguimiento/pedidos/{orderCode}");
        await Expect(page.GetByText("Aceptado", new() { Exact = true })).ToBeVisibleAsync();

        Assert.False(await Overflows(page));
    }

    // =======================================================================================
    // Spa y belleza: catálogo, pedido y el flujo operativo entero hasta "listo".
    // =======================================================================================

    [Fact]
    public async Task Spa_takes_an_order_through_the_whole_pickup_flow()
    {
        var vertical = PilotVerticalFixtures.Spa;
        await using var context = await MobileContext();
        var page = await context.NewPageAsync();

        var code = await PlaceOrder(page, vertical.Slug, "E2E Spa");
        await Login(page, PilotVerticalFixtures.OwnerEmail);

        // El negocio recibe el evento en su bandeja, sin depender de Push.
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{vertical.BusinessId}/avisos");
        await Expect(page.Locator("[data-testid=aviso][data-kind=OrderPlaced]").First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        await AdvanceOrder(page, vertical.BusinessId, "E2E Spa", "Aceptar", "Aceptado");
        await AdvanceOrder(page, vertical.BusinessId, "E2E Spa", "Preparar", "En preparación");
        await AdvanceOrder(page, vertical.BusinessId, "E2E Spa", "Listo", "Listo para recoger");

        await page.GotoAsync($"{fixture.BaseUrl}/seguimiento/pedidos/{code}");
        await Expect(page.GetByText("Listo para recoger").First).ToBeVisibleAsync();
        // Los tres cambios quedan escritos, no sólo el último.
        await Expect(page.Locator("[data-testid=tracking-update]")).ToHaveCountAsync(3,
            new() { Timeout = 20_000 });

        Assert.False(await Overflows(page));
    }

    // =======================================================================================
    // Droguería: sólo pedidos. Lo interesante es lo que NO aparece.
    // =======================================================================================

    [Fact]
    public async Task Pharmacy_only_offers_pickup_and_never_shows_appointment_sections()
    {
        var vertical = PilotVerticalFixtures.Pharmacy;
        await using var context = await MobileContext();
        var page = await context.NewPageAsync();

        var code = await PlaceOrder(page, vertical.Slug, "E2E Droguería");
        await Login(page, PilotVerticalFixtures.OwnerEmail);

        // La configuración de una droguería no ofrece servicios, ni personal, ni turnos: no le
        // sirven de nada y el servidor los rechazaría igual.
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{vertical.BusinessId}/configuracion");
        await Expect(page.GetByTestId("config-pedidos")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(page.GetByTestId("config-servicios")).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("config-personal")).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("config-turnos")).ToHaveCountAsync(0);

        // Y ocultar la tarjeta no es la única defensa: la dirección directa tampoco pasa.
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{vertical.BusinessId}/configuracion/servicios");
        await Expect(page.GetByText("Este establecimiento no tiene esa función habilitada."))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // La operación de recogida sí funciona de punta a punta.
        await AdvanceOrder(page, vertical.BusinessId, "E2E Droguería", "Aceptar", "Aceptado");
        await AdvanceOrder(page, vertical.BusinessId, "E2E Droguería", "Preparar", "En preparación");
        await AdvanceOrder(page, vertical.BusinessId, "E2E Droguería", "Listo", "Listo para recoger");
        await AdvanceOrder(page, vertical.BusinessId, "E2E Droguería", "Entregado", "Entregado");

        await page.GotoAsync($"{fixture.BaseUrl}/seguimiento/pedidos/{code}");
        await Expect(page.GetByText("Entregado").First).ToBeVisibleAsync();

        Assert.False(await Overflows(page));
    }

    // =======================================================================================
    // Óptica: citas y catálogo conviviendo, que es la combinación de la que salió el modelo.
    // =======================================================================================

    [Fact]
    public async Task Optics_offers_both_appointments_and_catalogue_and_no_queue()
    {
        var vertical = PilotVerticalFixtures.Optics;
        await using var context = await MobileContext();
        var page = await context.NewPageAsync();

        var appointmentCode = await BookAppointment(page, vertical.Slug, "E2E Óptica");
        var orderCode = await PlaceOrder(page, vertical.Slug, "E2E Óptica pedido");

        await Login(page, PilotVerticalFixtures.OwnerEmail);
        await page.GotoAsync($"{fixture.BaseUrl}/panel");
        var card = page.Locator($"[data-testid=business-dashboard][data-business-id='{vertical.BusinessId}']");
        await Expect(card.GetByRole(AriaRole.Link, new() { Name = "Administrar citas" })).ToBeVisibleAsync();
        await Expect(card.GetByRole(AriaRole.Link, new() { Name = "Operar pedidos" })).ToBeVisibleAsync();
        // Sin fila habilitada, la acción de turnos no existe.
        await Expect(card.GetByRole(AriaRole.Link, new() { Name = "Operar turnos" })).ToHaveCountAsync(0);

        await page.GotoAsync($"{fixture.BaseUrl}/seguimiento/citas/{appointmentCode}");
        await Expect(page.GetByText("Pendiente").First).ToBeVisibleAsync();
        await page.GotoAsync($"{fixture.BaseUrl}/seguimiento/pedidos/{orderCode}");
        await Expect(page.GetByText("Pendiente", new() { Exact = true })).ToBeVisibleAsync();

        Assert.False(await Overflows(page));
    }

    // =======================================================================================
    // La bandeja es de quien tiene membresía, y de nadie más.
    // =======================================================================================

    [Fact]
    public async Task The_inbox_of_a_vertical_is_closed_to_another_owner()
    {
        var vertical = PilotVerticalFixtures.Dentistry;
        await using var context = await MobileContext();
        var page = await context.NewPageAsync();
        await Login(page, DevelopmentSeeder.OtherOwnerEmail);
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{vertical.BusinessId}/avisos");
        await Expect(page.GetByTestId("avisos-error")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(page.Locator("[data-testid=aviso]")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Las capacidades derivadas se guardan resueltas al dar de alta. Se comprueba contra la base
    /// porque es la fuente de verdad que la pantalla lee, no un detalle de presentación.
    /// </summary>
    [Fact]
    public async Task Each_vertical_stores_the_capabilities_it_actually_needs()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString).Options;
        await using var db = new AppDbContext(options);

        foreach (var vertical in PilotVerticalFixtures.All)
        {
            var rows = await db.BusinessModules.AsNoTracking()
                .Where(x => x.BusinessId == vertical.BusinessId).ToListAsync();
            var capabilities = BusinessCapabilities.Resolve(rows);

            Assert.Equal(vertical.Appointments, capabilities.Contains(BusinessModuleKind.Appointments));
            Assert.Equal(vertical.Queues, capabilities.Contains(BusinessModuleKind.VirtualQueues));
            Assert.Equal(vertical.Orders, capabilities.Contains(BusinessModuleKind.PickupOrders));
            // Servicios y personal viven de las citas; los productos, de los pedidos.
            Assert.Equal(vertical.Appointments, capabilities.Contains(BusinessModuleKind.Services));
            Assert.Equal(vertical.Appointments, capabilities.Contains(BusinessModuleKind.Staff));
            Assert.Equal(vertical.Orders, capabilities.Contains(BusinessModuleKind.Products));
        }
    }

    // =======================================================================================
    // Composición: se usa desde un teléfono, y desde uno estrecho
    // =======================================================================================

    /// <summary>
    /// La bandeja en 360, en 390 y en escritorio. Se comprueban tres cosas que se rompen solas al
    /// añadir contenido: que la página no se pueda desplazar en horizontal, que las acciones
    /// principales sigan siendo tocables —44 px es el mínimo del sistema visual— y que ninguna
    /// quede fuera de la pantalla.
    /// </summary>
    [Theory]
    [InlineData(360, 740)]
    [InlineData(390, 844)]
    [InlineData(1366, 768)]
    public async Task The_inbox_composes_without_overflow_and_keeps_its_actions_reachable(int width, int height)
    {
        var vertical = PilotVerticalFixtures.Spa;
        await using var context = await fixture.Browser.NewContextAsync(new()
        { ViewportSize = new() { Width = width, Height = height } });
        var page = await context.NewPageAsync();
        await PlaceOrder(page, vertical.Slug, $"E2E Composición {width}");
        await Login(page, PilotVerticalFixtures.OwnerEmail);
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{vertical.BusinessId}/avisos");
        await Expect(page.Locator("[data-testid=aviso]").First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        Assert.False(await Overflows(page), $"la bandeja se desborda a {width} px");

        // La conexión en vivo puede reconectar una vez al arrancar y disparar una recarga de la
        // lista justo cuando se mide: la visibilidad ya confirmada no garantiza que el nodo siga
        // siendo el mismo un instante después. Se reintenta la medición en vez de asumir que un
        // solo intento alcanza; es la pantalla la que se comprueba, no la carrera con el circuito.
        var action = page.Locator("[data-testid=aviso]").First
            .GetByRole(AriaRole.Button, new() { Name = "Ver" });
        var box = await StableBoundingBox(action);
        Assert.True(box.Height >= 44, $"la acción mide {box.Height} px de alto a {width} px");
        Assert.True(box.X >= 0 && box.X + box.Width <= width + 1,
            $"la acción se sale de la pantalla a {width} px");

        // El filtro también se toca: en 360 px es donde una fila de botones se sale primero.
        var filter = page.GetByTestId("avisos-filtro-no-leidos");
        Assert.True((await StableBoundingBox(filter)).Height >= 44);
    }

    /// <summary>
    /// La primera lectura tras confirmar visibilidad puede caer justo en un repintado del circuito
    /// y devolver nulo aunque el elemento exista un instante antes y después. Se reintenta un par de
    /// veces en vez de tratar ese instante como el estado real de la pantalla.
    /// </summary>
    private static async Task<LocatorBoundingBoxResult> StableBoundingBox(ILocator locator)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Expect(locator).ToBeVisibleAsync(new() { Timeout = 20_000 });
            if (await locator.BoundingBoxAsync() is { } box) return box;
            await locator.Page.WaitForTimeoutAsync(200);
        }
        throw new InvalidOperationException("El elemento nunca dejó de repintarse lo suficiente para medirlo.");
    }

    // ------------------------------------------------------------------ apoyos

    private async Task<string> BookAppointment(IPage page, string slug, string alias)
    {
        await page.GotoAsync($"{fixture.BaseUrl}/negocios/{slug}/citas");
        await page.GetByRole(AriaRole.Button, new() { Name = "Ver horas disponibles" }).ClickAsync();
        await page.Locator("button.slot").First.WaitForAsync(new() { Timeout = 20_000 });
        await page.Locator("button.slot").First.ClickAsync();
        await page.GetByLabel("Nombre o alias").FillAsync(alias);
        await page.GetByLabel("Teléfono").FillAsync("3004567890");
        await page.GetByRole(AriaRole.Checkbox).CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Enviar solicitud" }).ClickAsync();
        await Expect(page.GetByText("Solicitud enviada.")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        return (await page.GetByTestId("tracking-code").InnerTextAsync()).Trim();
    }

    private async Task<string> PlaceOrder(IPage page, string slug, string alias)
    {
        await page.GotoAsync($"{fixture.BaseUrl}/negocios/{slug}/pedidos");
        await page.Locator("[data-testid=product-card]").First
            .GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Agregar uno de ") })
            .ClickAsync();
        await page.GetByLabel("Hora para recoger").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await page.GetByLabel("Nombre o alias").FillAsync(alias);
        await page.GetByLabel("Celular").FillAsync("3001234567");
        await page.GetByLabel("Acepto el uso de estos datos").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Confirmar pedido" }).ClickAsync();
        await Expect(page.GetByTestId("order-created")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await page.GetByRole(AriaRole.Link, new() { Name = "Seguir mi pedido" }).ClickAsync();
        await Expect(page.GetByTestId("order-tracking")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        return page.Url.Split('/').Last().Split('?')[0];
    }

    private async Task AdvanceOrder(IPage page, Guid businessId, string alias, string action, string expected)
    {
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{businessId}/pedidos");
        var card = page.Locator("[data-testid=admin-order]").Filter(new() { HasTextString = alias });
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await card.GetByRole(AriaRole.Button, new() { Name = action }).First.ClickAsync();
        await Expect(card.GetByText(expected).First).ToBeVisibleAsync(new() { Timeout = 20_000 });
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

    /// <summary>El teléfono no debe poder desplazarse en horizontal en ninguna de las pantallas.</summary>
    private static Task<bool> Overflows(IPage page)
        => page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth");

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
