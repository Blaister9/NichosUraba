using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UrabaConecta.Application;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure;
using UrabaConecta.Infrastructure.Persistence;
using UrabaConecta.Infrastructure.Security;

namespace UrabaConecta.IntegrationTests;

public sealed class PushWebFactory : PostgresWebFactory
{
    public RecordingPushTransport Transport { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("WebPush:Subject", "mailto:demo@urabaconecta.test");
        builder.UseSetting("WebPush:PublicKey", "public-test-key");
        builder.UseSetting("WebPush:PrivateKey", "private-test-key");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWebPushTransport>();
            services.AddSingleton<IWebPushTransport>(Transport);
        });
    }
}

public sealed record RecordedPush(Guid BusinessId, PushAudience Audience, Guid? EntityId, PushMessage Message);

public sealed class RecordingPushTransport : IWebPushTransport
{
    public ConcurrentQueue<RecordedPush> Sent { get; } = new();
    public int? FailureStatusCode { get; set; }

    public Task SendAsync(WebPushSubscription subscription, PushMessage message,
        CancellationToken cancellationToken = default)
    {
        if (FailureStatusCode is { } status)
            throw new PushDeliveryException(status, new HttpRequestException("Fallo simulado."));
        Sent.Enqueue(new(subscription.BusinessId, subscription.Audience, subscription.EntityId, message));
        return Task.CompletedTask;
    }

    public void Reset() { while (Sent.TryDequeue(out _)) { } FailureStatusCode = null; }
}

public sealed class PushNotificationApiTests(PushWebFactory factory) : IClassFixture<PushWebFactory>
{
    private static WebPushSubscriptionRequest Subscription(string suffix) => new()
    {
        Endpoint = $"https://push.example/{suffix}-{Guid.NewGuid():N}",
        Keys = new() { P256dh = "browser-public-key", Auth = "browser-auth-secret" }
    };

    [Fact]
    public async Task Configuration_exposes_only_the_public_vapid_key()
    {
        using var client = factory.CreateClient();
        var config = await client.GetFromJsonAsync<PushConfigurationDto>("/api/v1/public/push/config");
        Assert.True(config!.Enabled);
        Assert.Equal("public-test-key", config.PublicKey);
        var json = await client.GetStringAsync("/api/v1/public/push/config");
        Assert.DoesNotContain("private-test-key", json);
    }

    [Fact]
    public async Task Pwa_manifest_service_worker_and_registration_are_served()
    {
        using var client = factory.CreateClient();
        var home = await client.GetStringAsync("/");
        Assert.Contains("rel=\"manifest\" href=\"/manifest.webmanifest\"", home);
        Assert.Contains("/pwa.js", home);
        var manifest = await client.GetStringAsync("/manifest.webmanifest");
        Assert.Contains("\"display\": \"standalone\"", manifest);
        Assert.Contains("\"purpose\": \"any maskable\"", manifest);
        var worker = await client.GetStringAsync("/sw.js");
        Assert.Contains("addEventListener('push'", worker);
        Assert.Contains("addEventListener('notificationclick'", worker);
    }

    /// <summary>
    /// Los iconos del manifiesto, con respaldo en mapa de bits.
    ///
    /// Chromium moderno decodifica SVG, pero la recomendación de la plataforma sigue siendo llevar
    /// PNG: un navegador de fabricante que no lo decodifique deja de ofrecer instalación sin decir
    /// por qué. Los dos formatos conviven y cubren los mismos propósitos, así que ninguna variante
    /// —incluida la enmascarable— se queda sin respaldo. Las medidas se leen de la cabecera del
    /// propio archivo: un PNG que dice 512 en el manifiesto y mide otra cosa es peor que no estar.
    /// </summary>
    [Fact]
    public async Task Manifest_icons_ship_both_vector_and_raster_for_every_purpose()
    {
        using var client = factory.CreateClient();
        var manifest = await client.GetStringAsync("/manifest.webmanifest");

        foreach (var icono in new[] { "icon-192.svg", "icon-192.png", "icon-512.svg", "icon-512.png" })
            Assert.Contains($"\"src\": \"/icons/{icono}\"", manifest);

        // El respaldo tiene que cubrir también el propósito enmascarable, no sólo el corriente.
        Assert.Contains("\"type\": \"image/png\"", manifest);
        Assert.Equal(2, Regex.Matches(manifest, "\"purpose\": \"any maskable\"").Count);
        Assert.Equal(2, Regex.Matches(manifest, "\"purpose\": \"any\"").Count);

        foreach (var (nombre, lado) in new[] { ("icon-192.png", 192u), ("icon-512.png", 512u) })
        {
            var respuesta = await client.GetAsync($"/icons/{nombre}");
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.Equal("image/png", respuesta.Content.Headers.ContentType?.MediaType);

            var bytes = await respuesta.Content.ReadAsByteArrayAsync();
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes[..8]);
            // La cabecera IHDR lleva el ancho y el alto reales, en 32 bits y orden de red.
            Assert.Equal(lado, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)));
            Assert.Equal(lado, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)));
        }

        // Los vectores siguen ahí: el PNG es respaldo, no reemplazo.
        foreach (var nombre in new[] { "icon-192.svg", "icon-512.svg" })
        {
            var respuesta = await client.GetAsync($"/icons/{nombre}");
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.Equal("image/svg+xml", respuesta.Content.Headers.ContentType?.MediaType);
        }
    }

    /// <summary>
    /// El guion de instalación tiene que llegar entero al navegador. Sin la captura de
    /// beforeinstallprompt no hay diálogo nativo que abrir desde nuestra invitación, y sin el
    /// camino manual la única salida vuelve a ser el menú del navegador, que es de donde venimos.
    /// </summary>
    [Fact]
    public async Task Install_script_captures_the_browser_offer_and_keeps_a_manual_route()
    {
        using var client = factory.CreateClient();
        var guion = await client.GetStringAsync("/pwa.js");

        Assert.Contains("addEventListener('beforeinstallprompt'", guion);
        Assert.Contains("addEventListener('appinstalled'", guion);
        Assert.Contains("display-mode: ", guion);
        // El clic se atiende en el DOM: Chrome exige que prompt() ocurra dentro del gesto.
        Assert.Contains("data-uraba-instalar", guion);
        Assert.Contains("urabaApp", guion);
        // Instrucciones literales de cada familia de navegador, no una frase genérica.
        Assert.Contains("Añadir a pantalla de inicio", guion);
        Assert.Contains("Instalar aplicación", guion);
        Assert.Contains("Añadir página a", guion);

        var avisos = await client.GetStringAsync("/push-notifications.js");
        // Pedir el permiso sin suscribir: es lo que usa la ficha de estado de la cuenta.
        Assert.Contains("requestPermission:", avisos);
    }

    /// <summary>
    /// La página con la ficha de estado tiene que llegar ENTERA a un render estático: se lee el
    /// cuerpo completo, no sólo el código de estado.
    ///
    /// Nace de un fallo real: los componentes de instalación escuchan a JavaScript y al desecharse
    /// pedían la baja sin mirar si alguna vez se habían inscrito. En un render estático esa llamada
    /// lanza, la excepción escapaba dentro del Dispose y el cuerpo se cortaba a medio escribir —con
    /// el 200 ya enviado—. Conviene decir qué NO cubre esta prueba: aquella caída sólo aparecía con
    /// los tres ensamblados corriendo a la vez, porque depende de si el árbol prerenderizado se
    /// desecha antes de que termine la respuesta. Aquí queda el humo; la carrera la sigue
    /// destapando la suite completa.
    /// </summary>
    [Fact]
    public async Task The_activity_page_renders_whole_without_asking_javascript_while_prerendering()
    {
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/seguimiento");

        Assert.Contains("data-testid=\"app-status\"", html);
        Assert.Contains("Notificaciones", html);
        Assert.Contains("</html>", html);
    }

    [Fact]
    public async Task Owner_subscription_is_tenant_scoped_and_another_owner_is_forbidden()
    {
        using var owner = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await PlatformAdministrationApiTests.Login(owner, DevelopmentSeeder.CorteOwnerEmail);
        var ownerSubscription = Subscription("owner");
        var response = await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.CorteBusinessId}/push-subscriptions", ownerSubscription);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var other = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await PlatformAdministrationApiTests.Login(other, DevelopmentSeeder.OtherOwnerEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.CorteBusinessId}/push-subscriptions",
            Subscription("intruder"))).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.WebPushSubscriptions.SingleAsync(x => x.Endpoint == ownerSubscription.Endpoint);
        Assert.NotNull(stored.UserId);
        Assert.True(stored.IsActive);
    }

    [Fact]
    public async Task Queue_client_registration_protects_the_deep_link_and_receives_confirmation()
    {
        await factory.DrainNotificationsAsync();
        factory.Transport.Reset();
        using var client = factory.CreateClient();
        var created = await JoinQueue(client, "Push cliente");
        var response = await client.PostAsJsonAsync(
            $"/api/v1/public/queue/tickets/{created.TrackingCode}/push-subscriptions", Subscription("queue"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await factory.DrainNotificationsAsync();
        Assert.Contains(factory.Transport.Sent, x => x.Audience == PushAudience.QueueTicket &&
            x.Message.Title == "Turno registrado");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.WebPushSubscriptions.OrderByDescending(x => x.CreatedAtUtc)
            .FirstAsync(x => x.Audience == PushAudience.QueueTicket);
        Assert.NotNull(stored.EntityId);
        Assert.DoesNotContain(created.TrackingCode, stored.ProtectedDeepLink ?? "");
    }

    [Fact]
    public async Task New_queue_ticket_is_delivered_only_to_that_business_subscriptions()
    {
        using var owner = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await PlatformAdministrationApiTests.Login(owner, DevelopmentSeeder.CorteOwnerEmail);
        await owner.PostAsJsonAsync($"/api/v1/businesses/{DevelopmentSeeder.CorteBusinessId}/push-subscriptions",
            Subscription("corte-owner"));
        await factory.DrainNotificationsAsync();
        factory.Transport.Reset();

        using var client = factory.CreateClient();
        await JoinQueue(client, "Aislamiento push");
        await factory.DrainNotificationsAsync();
        var sent = factory.Transport.Sent.Where(x => x.Message.Title == "Nuevo turno en la fila").ToList();
        Assert.NotEmpty(sent);
        Assert.All(sent, x => Assert.Equal(DevelopmentSeeder.CorteBusinessId, x.BusinessId));
    }

    [Fact]
    public async Task Appointment_and_order_cover_owner_client_transport_and_deep_links()
    {
        using var appointmentOwner = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        using var orderOwner = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        using var visitor = factory.CreateClient();
        await PlatformAdministrationApiTests.Login(appointmentOwner, DevelopmentSeeder.BellaOwnerEmail);
        await PlatformAdministrationApiTests.Login(orderOwner, DevelopmentSeeder.SazonOwnerEmail);
        await appointmentOwner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/push-subscriptions", Subscription("bella-owner"));
        await orderOwner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.SazonBusinessId}/push-subscriptions", Subscription("sazon-owner"));

        await factory.DrainNotificationsAsync();
        factory.Transport.Reset();
        var appointment = await CreateAppointment(visitor);
        var order = await CreateOrder(visitor);
        await factory.DrainNotificationsAsync();
        Assert.Contains(factory.Transport.Sent, x => x.Audience == PushAudience.Owner &&
            x.Message.Title == "Nueva cita" &&
            x.Message.Url.StartsWith($"/panel/{DevelopmentSeeder.BellaBusinessId}/citas#appointment-"));
        Assert.Contains(factory.Transport.Sent, x => x.Audience == PushAudience.Owner &&
            x.Message.Title == "Nuevo pedido" &&
            x.Message.Url.StartsWith($"/panel/{DevelopmentSeeder.SazonBusinessId}/pedidos#order-"));

        factory.Transport.Reset();
        Assert.Equal(HttpStatusCode.OK, (await visitor.PostAsJsonAsync(
            $"/api/v1/public/appointments/{appointment.TrackingCode}/push-subscriptions",
            Subscription("appointment-client"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await visitor.PostAsJsonAsync(
            $"/api/v1/public/orders/{order.TrackingCode}/push-subscriptions",
            Subscription("order-client"))).StatusCode);
        await factory.DrainNotificationsAsync();
        Assert.Contains(factory.Transport.Sent, x => x.Audience == PushAudience.Appointment &&
            x.Message.Url == $"/seguimiento/citas/{appointment.TrackingCode}");
        Assert.Contains(factory.Transport.Sent, x => x.Audience == PushAudience.PickupOrder &&
            x.Message.Url == $"/seguimiento/pedidos/{order.TrackingCode}");

        factory.Transport.Reset();
        var appointments = await appointmentOwner.GetFromJsonAsync<AppointmentBoardDto>(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/appointments");
        var storedAppointment = appointments!.Items.Where(x => x.Status == "Pending" &&
                x.Start.ToUniversalTime() == appointment.Start.ToUniversalTime())
            .OrderByDescending(x => x.CreatedAt).First();
        Assert.Equal(HttpStatusCode.OK, (await appointmentOwner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/appointments/{storedAppointment.Id}/status",
            new ChangeAppointmentStatusRequest { TargetStatus = "Confirmed" })).StatusCode);

        var board = await orderOwner.GetFromJsonAsync<PickupOrderBoardDto>(
            $"/api/v1/businesses/{DevelopmentSeeder.SazonBusinessId}/orders");
        var storedOrder = board!.Items.Single(x => x.OrderNumber == order.OrderNumber);
        storedOrder = await ChangeOrder(orderOwner, storedOrder, "accept");
        storedOrder = await ChangeOrder(orderOwner, storedOrder, "prepare");
        _ = await ChangeOrder(orderOwner, storedOrder, "ready");

        await factory.DrainNotificationsAsync();
        Assert.Contains(factory.Transport.Sent, x => x.Audience == PushAudience.Appointment &&
            x.Message.Title == "Cita confirmada" &&
            x.Message.Url == $"/seguimiento/citas/{appointment.TrackingCode}");
        Assert.Contains(factory.Transport.Sent, x => x.Audience == PushAudience.PickupOrder &&
            x.Message.Title == "Pedido listo para recoger" &&
            x.Message.Url == $"/seguimiento/pedidos/{order.TrackingCode}");
    }

    [Fact]
    public async Task Gone_subscription_is_deactivated_after_delivery_attempt()
    {
        await factory.DrainNotificationsAsync();
        factory.Transport.Reset();
        factory.Transport.FailureStatusCode = 410;
        using var client = factory.CreateClient();
        var created = await JoinQueue(client, "Endpoint vencido");
        var request = Subscription("gone");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/v1/public/queue/tickets/{created.TrackingCode}/push-subscriptions", request)).StatusCode);
        // Un 410 es el navegador diciendo que ese destino ya no existe: se desactiva en el primer
        // intento y no se reintenta, a diferencia de un fallo pasajero.
        var report = await factory.DrainNotificationsAsync();
        Assert.True(report.Expired >= 1);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.WebPushSubscriptions.SingleAsync(x => x.Endpoint == request.Endpoint);
        Assert.False(stored.IsActive);
        Assert.Equal(1, stored.FailureCount);
        factory.Transport.Reset();
    }

    [Fact]
    public async Task Unavailable_product_opt_in_receives_one_restock_push_with_product_deep_link()
    {
        using var owner = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        using var visitor = factory.CreateClient();
        await PlatformAdministrationApiTests.Login(owner, DevelopmentSeeder.SazonOwnerEmail);
        var products = await owner.GetFromJsonAsync<ProductDto[]>(
            $"/api/v1/businesses/{DevelopmentSeeder.SazonBusinessId}/products");
        var product = products!.First();

        product = await SaveProduct(owner, product, available: false);
        var menu = await visitor.GetFromJsonAsync<PickupMenuDto>(
            "/api/v1/public/businesses/restaurante-sazon-local/menu");
        Assert.Contains(menu!.Products, x => x.Id == product.Id && !x.IsAvailable);

        var subscription = Subscription("restock");
        Assert.Equal(HttpStatusCode.OK, (await visitor.PostAsJsonAsync(
            $"/api/v1/public/businesses/restaurante-sazon-local/products/{product.Id}/push-subscriptions",
            subscription)).StatusCode);
        await factory.DrainNotificationsAsync();
        factory.Transport.Reset();

        product = await SaveProduct(owner, product, available: true);
        await factory.DrainNotificationsAsync();
        var sent = Assert.Single(factory.Transport.Sent, x => x.Audience == PushAudience.ProductRestock);
        Assert.Equal($"Volvió {product.Name}", sent.Message.Title);
        Assert.Equal($"/negocios/restaurante-sazon-local/pedidos#producto-{product.Id}", sent.Message.Url);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.WebPushSubscriptions.SingleAsync(x => x.Endpoint == subscription.Endpoint);
        Assert.False(stored.IsActive);
    }

    [Fact]
    public async Task Followed_business_receives_only_its_promotion_and_frequency_cap_blocks_second_push()
    {
        using var owner = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        using var visitor = factory.CreateClient();
        await PlatformAdministrationApiTests.Login(owner, DevelopmentSeeder.SazonOwnerEmail);
        var follower = Subscription("follower");
        Assert.Equal(HttpStatusCode.OK, (await visitor.PostAsJsonAsync(
            "/api/v1/public/businesses/restaurante-sazon-local/followers/push-subscriptions", follower)).StatusCode);
        await factory.DrainNotificationsAsync();
        factory.Transport.Reset();

        var first = await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.SazonBusinessId}/promotions", Promotion("Almuerzo listo"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstResult = await first.Content.ReadFromJsonAsync<BusinessPromotionSaveResultDto>();
        Assert.True(firstResult!.PushSent);
        await factory.DrainNotificationsAsync();
        var sent = Assert.Single(factory.Transport.Sent, x => x.Audience == PushAudience.BusinessFollower);
        Assert.Equal("Almuerzo listo", sent.Message.Title);
        Assert.Equal("/negocios/restaurante-sazon-local/pedidos", sent.Message.Url);

        factory.Transport.Reset();
        var second = await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.SazonBusinessId}/promotions", Promotion("Segunda novedad"));
        var secondResult = await second.Content.ReadFromJsonAsync<BusinessPromotionSaveResultDto>();
        Assert.False(secondResult!.PushSent);
        Assert.NotNull(secondResult.NextPushAllowedAtUtc);
        await factory.DrainNotificationsAsync();
        Assert.DoesNotContain(factory.Transport.Sent, x => x.Audience == PushAudience.BusinessFollower);

        var publicPromotions = await visitor.GetFromJsonAsync<BusinessPromotionDto[]>("/api/v1/public/promotions");
        Assert.Contains(publicPromotions!, x => x.Headline == "Almuerzo listo" &&
            x.BusinessSlug == "restaurante-sazon-local");
    }

    private static SaveBusinessPromotionRequest Promotion(string headline) => new()
    {
        Headline = headline, Body = "Disponible para recoger hoy.", CtaLabel = "Pedir",
        DeepLink = "/negocios/restaurante-sazon-local/pedidos", StartsAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        EndsAtUtc = DateTimeOffset.UtcNow.AddDays(2), IsActive = true, NotifyFollowers = true
    };

    private static async Task<ProductDto> SaveProduct(HttpClient owner, ProductDto product, bool available)
    {
        var response = await owner.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.SazonBusinessId}/products/{product.Id}",
            new SaveProductRequest
            {
                CategoryId = product.CategoryId, Name = product.Name, Description = product.Description,
                ReferencePrice = product.ReferencePrice, DisplayOrder = product.DisplayOrder,
                IsActive = product.IsActive, IsAvailable = available, Version = product.Version
            });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductDto>())!;
    }

    private static async Task<QueueTicketCreatedDto> JoinQueue(HttpClient client, string alias)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/public/businesses/barberia-el-corte/queue/tickets",
            new CreateQueueTicketRequest { Alias = alias, ConsentAccepted = true,
                ConsentNoticeVersion = ConsentPolicyProvider.FallbackVersion });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<QueueTicketCreatedDto>())!;
    }

    private static async Task<AppointmentCreatedDto> CreateAppointment(HttpClient client)
    {
        var profile = await client.GetFromJsonAsync<BusinessProfileDto>(
            "/api/v1/public/businesses/salon-bella-uraba");
        var service = profile!.Services.First();
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        SlotListDto? available = null;
        for (var attempt = 0; attempt < 20 && (available is null || available.Slots.Count == 0); attempt++)
        {
            available = await client.GetFromJsonAsync<SlotListDto>(
                $"/api/v1/public/businesses/salon-bella-uraba/appointment-slots?serviceId={service.Id}&date={date:yyyy-MM-dd}");
            if (available is null || available.Slots.Count == 0) date = date.AddDays(1);
        }
        Assert.NotNull(available);
        Assert.NotEmpty(available!.Slots);
        var response = await client.PostAsJsonAsync("/api/v1/public/businesses/salon-bella-uraba/appointments",
            new CreateAppointmentRequest
            {
                ServiceId = service.Id, Start = available.Slots.Last().Start, CustomerAlias = "Push cita",
                Phone = "3001234567", ConsentAccepted = true, ConsentNoticeVersion = "pilot-1"
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AppointmentCreatedDto>())!;
    }

    private static async Task<PickupOrderCreatedDto> CreateOrder(HttpClient client)
    {
        var menu = await client.GetFromJsonAsync<PickupMenuDto>(
            "/api/v1/public/businesses/restaurante-sazon-local/menu");
        var slots = await client.GetFromJsonAsync<PickupSlotListDto>(
            "/api/v1/public/businesses/restaurante-sazon-local/pickup-slots");
        Assert.NotEmpty(slots!.Slots);
        var response = await client.PostAsJsonAsync(
            "/api/v1/public/businesses/restaurante-sazon-local/orders", new CreatePickupOrderRequest
            {
                CustomerAlias = "Push pedido", Phone = "3001234567", PickupStart = slots.Slots.Last().Start,
                ConsentAccepted = true, ConsentNoticeVersion = "pilot-1",
                Lines = [new() { ProductId = menu!.Products.First().Id, Quantity = 1 }]
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PickupOrderCreatedDto>())!;
    }

    private static async Task<PickupOrderAdminDto> ChangeOrder(HttpClient owner, PickupOrderAdminDto order,
        string action)
    {
        var response = await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.SazonBusinessId}/orders/{order.Id}/{action}",
            new PickupOrderCommandRequest { Version = order.Version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PickupOrderAdminDto>())!;
    }
}
