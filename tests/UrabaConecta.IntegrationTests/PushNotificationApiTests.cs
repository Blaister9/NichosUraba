using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
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
        factory.Transport.Reset();
        using var client = factory.CreateClient();
        var created = await JoinQueue(client, "Push cliente");
        var response = await client.PostAsJsonAsync(
            $"/api/v1/public/queue/tickets/{created.TrackingCode}/push-subscriptions", Subscription("queue"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
        factory.Transport.Reset();

        using var client = factory.CreateClient();
        await JoinQueue(client, "Aislamiento push");
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

        factory.Transport.Reset();
        var appointment = await CreateAppointment(visitor);
        var order = await CreateOrder(visitor);
        Assert.Contains(factory.Transport.Sent, x => x.Audience == PushAudience.Owner &&
            x.Message.Title == "Nueva cita" &&
            x.Message.Url.StartsWith($"/panel/{DevelopmentSeeder.BellaBusinessId}/citas#appointment-"));
        Assert.Contains(factory.Transport.Sent, x => x.Audience == PushAudience.Owner &&
            x.Message.Title == "Nuevo pedido para recoger" &&
            x.Message.Url.StartsWith($"/panel/{DevelopmentSeeder.SazonBusinessId}/pedidos#order-"));

        factory.Transport.Reset();
        Assert.Equal(HttpStatusCode.OK, (await visitor.PostAsJsonAsync(
            $"/api/v1/public/appointments/{appointment.TrackingCode}/push-subscriptions",
            Subscription("appointment-client"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await visitor.PostAsJsonAsync(
            $"/api/v1/public/orders/{order.TrackingCode}/push-subscriptions",
            Subscription("order-client"))).StatusCode);
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
        factory.Transport.Reset();
        factory.Transport.FailureStatusCode = 410;
        using var client = factory.CreateClient();
        var created = await JoinQueue(client, "Endpoint vencido");
        var request = Subscription("gone");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/v1/public/queue/tickets/{created.TrackingCode}/push-subscriptions", request)).StatusCode);

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
        factory.Transport.Reset();

        product = await SaveProduct(owner, product, available: true);
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
        factory.Transport.Reset();

        var first = await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.SazonBusinessId}/promotions", Promotion("Almuerzo listo"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstResult = await first.Content.ReadFromJsonAsync<BusinessPromotionSaveResultDto>();
        Assert.True(firstResult!.PushSent);
        var sent = Assert.Single(factory.Transport.Sent, x => x.Audience == PushAudience.BusinessFollower);
        Assert.Equal("Almuerzo listo", sent.Message.Title);
        Assert.Equal("/negocios/restaurante-sazon-local/pedidos", sent.Message.Url);

        factory.Transport.Reset();
        var second = await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.SazonBusinessId}/promotions", Promotion("Segunda novedad"));
        var secondResult = await second.Content.ReadFromJsonAsync<BusinessPromotionSaveResultDto>();
        Assert.False(secondResult!.PushSent);
        Assert.NotNull(secondResult.NextPushAllowedAtUtc);
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
