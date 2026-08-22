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

namespace UrabaConecta.IntegrationTests;

/// <summary>
/// Reloj que se puede mover a mano. Los reintentos esperan medias horas: sin poder adelantar el
/// tiempo, comprobar que un fallo pasajero se reintenta —y que al agotarse deja de intentarse—
/// exigiría una prueba que dure horas o una que no compruebe nada.
/// </summary>
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan amount) => now = now.Add(amount);
}

/// <summary>
/// Transporte Web Push con guion por endpoint: cada dispositivo puede responder algo distinto, que
/// es justo el caso que en producción confundía —un teléfono muerto y otro sano en el mismo negocio—.
/// </summary>
public sealed class ScriptedPushTransport : IWebPushTransport
{
    public enum Behaviour { Succeed, Gone, NotFound, ServerError, Timeout }

    private readonly ConcurrentDictionary<string, Behaviour> script = new();
    public ConcurrentQueue<(string Endpoint, string Title)> Delivered { get; } = new();
    public ConcurrentQueue<string> Attempts { get; } = new();

    /// <summary>Lo que se aplica a cualquier endpoint sin guion propio.</summary>
    public Behaviour Default { get; set; } = Behaviour.Succeed;

    public void Script(string endpoint, Behaviour behaviour) => script[endpoint] = behaviour;

    public void Reset()
    {
        script.Clear(); Default = Behaviour.Succeed;
        while (Delivered.TryDequeue(out _)) { }
        while (Attempts.TryDequeue(out _)) { }
    }

    public int AttemptsFor(string endpoint) => Attempts.Count(x => x == endpoint);
    public int DeliveredFor(string endpoint) => Delivered.Count(x => x.Endpoint == endpoint);

    public Task SendAsync(WebPushSubscription subscription, PushMessage message,
        CancellationToken cancellationToken = default)
    {
        Attempts.Enqueue(subscription.Endpoint);
        var behaviour = script.TryGetValue(subscription.Endpoint, out var scripted) ? scripted : Default;
        return behaviour switch
        {
            Behaviour.Gone => throw new PushDeliveryException(410, new HttpRequestException("Gone")),
            Behaviour.NotFound => throw new PushDeliveryException(404, new HttpRequestException("Not Found")),
            Behaviour.ServerError => throw new PushDeliveryException(503,
                new HttpRequestException("Service Unavailable")),
            Behaviour.Timeout => throw new HttpRequestException("El proveedor no respondió."),
            _ => Record(subscription, message)
        };
    }

    private Task Record(WebPushSubscription subscription, PushMessage message)
    {
        Delivered.Enqueue((subscription.Endpoint, message.Title));
        return Task.CompletedTask;
    }
}

public sealed class OutboxWebFactory : PostgresWebFactory
{
    public ScriptedPushTransport Transport { get; } = new();
    public TestClock Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("WebPush:Subject", "mailto:outbox@urabaconecta.test");
        builder.UseSetting("WebPush:PublicKey", "outbox-public-test-key");
        builder.UseSetting("WebPush:PrivateKey", "outbox-private-test-key");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWebPushTransport>();
            services.AddSingleton<IWebPushTransport>(Transport);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}

/// <summary>
/// Lo que estas pruebas fijan: un aviso es un hecho guardado, y la entrega a un teléfono es un
/// intento con su propia suerte. Se comprueban por separado a propósito, porque el defecto que
/// motivó todo esto era exactamente confundirlos: si el envío fallaba, no quedaba nada.
/// </summary>
public sealed class NotificationOutboxTests(OutboxWebFactory factory) : IClassFixture<OutboxWebFactory>
{
    private const string OrderBusinessSlug = "restaurante-sazon-local";
    private static readonly Guid OrderBusinessId = DevelopmentSeeder.SazonBusinessId;

    private static WebPushSubscriptionRequest Device(string endpoint) => new()
    {
        Endpoint = endpoint, Keys = new() { P256dh = "browser-public-key", Auth = "browser-auth-secret" }
    };

    private string Endpoint(string name) => $"https://push.example/outbox/{name}-{Guid.NewGuid():N}";

    // ---------------------------------------------------------------------------------------
    // La operación de negocio no depende del proveedor
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_push_outage_does_not_stop_the_order_and_the_notice_is_still_recorded()
    {
        await Clean();
        var endpoint = await RegisterOwnerDevice("apagon");
        // Todo lo que salga hacia el proveedor revienta, incluso sin código de estado.
        factory.Transport.Default = ScriptedPushTransport.Behaviour.Timeout;

        var order = await CreateOrder();
        Assert.True(order.OrderNumber > 0);

        var inbox = await OwnerInbox();
        var notice = Assert.Single(inbox.Items, x => x.Kind == nameof(NotificationKind.OrderPlaced));
        Assert.Equal($"/panel/{OrderBusinessId}/pedidos#order-", notice.DeepLink![..notice.DeepLink!.IndexOf('#')]
            + "#order-");
        Assert.False(notice.IsRead);
        Assert.True(inbox.UnreadCount >= 1);

        // Y el intento hacia el teléfono queda registrado como pendiente, no como perdido.
        await factory.DrainNotificationsAsync();
        Assert.True(factory.Transport.AttemptsFor(endpoint) >= 1);
        Assert.Equal(NotificationDeliveryStatus.Pending, await StatusOf(endpoint));
    }

    [Fact]
    public async Task Without_any_device_the_notice_is_still_recorded_and_nothing_stays_queued()
    {
        await Clean();
        await CreateOrder();

        var inbox = await OwnerInbox();
        Assert.Contains(inbox.Items, x => x.Kind == nameof(NotificationKind.OrderPlaced));

        await factory.DrainNotificationsAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Repartido, con cero entregas: no hay nada que reintentar eternamente.
        Assert.Empty(await db.NotificationDeliveries.Where(x => x.BusinessId == OrderBusinessId)
            .ToListAsync());
        Assert.False(await db.Notifications.AnyAsync(x => x.BusinessId == OrderBusinessId &&
            x.FannedOutAtUtc == null));
    }

    // ---------------------------------------------------------------------------------------
    // Dos dispositivos: uno muerto no puede llevarse al sano por delante
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_dead_device_is_retired_and_the_healthy_one_still_gets_the_notice()
    {
        await Clean();
        var healthy = await RegisterOwnerDevice("sano");
        var dead = await RegisterOwnerDevice("muerto");
        factory.Transport.Script(dead, ScriptedPushTransport.Behaviour.Gone);

        await CreateOrder();
        var report = await factory.DrainNotificationsAsync();

        Assert.Equal(1, factory.Transport.DeliveredFor(healthy));
        Assert.Equal(0, factory.Transport.DeliveredFor(dead));
        Assert.True(report.Sent >= 1 && report.Expired >= 1);

        Assert.Equal(NotificationDeliveryStatus.Sent, await StatusOf(healthy));
        Assert.Equal(NotificationDeliveryStatus.Expired, await StatusOf(dead));
        Assert.True(await IsActive(healthy));
        // 410 es el navegador diciendo que ese destino ya no existe: se retira.
        Assert.False(await IsActive(dead));
    }

    [Fact]
    public async Task A_not_found_endpoint_is_retired_exactly_like_a_gone_one()
    {
        await Clean();
        var missing = await RegisterOwnerDevice("inexistente");
        factory.Transport.Script(missing, ScriptedPushTransport.Behaviour.NotFound);

        await CreateOrder();
        await factory.DrainNotificationsAsync();

        Assert.Equal(NotificationDeliveryStatus.Expired, await StatusOf(missing));
        Assert.False(await IsActive(missing));
        // No se reintenta: el destino no va a volver.
        Assert.Equal(1, factory.Transport.AttemptsFor(missing));
    }

    // ---------------------------------------------------------------------------------------
    // Fallos pasajeros: se reintentan, y no cuestan el dispositivo
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_transient_failure_is_retried_later_and_never_retires_the_device()
    {
        await Clean();
        var endpoint = await RegisterOwnerDevice("pasajero");
        factory.Transport.Script(endpoint, ScriptedPushTransport.Behaviour.ServerError);

        await CreateOrder();
        var first = await factory.DrainNotificationsAsync();
        Assert.Equal(1, first.Retried);
        Assert.Equal(NotificationDeliveryStatus.Pending, await StatusOf(endpoint));
        Assert.True(await IsActive(endpoint), "un 503 del proveedor no dice nada del dispositivo");

        // Antes de que toque el siguiente intento, el trabajador no vuelve a molestar al proveedor.
        await factory.DrainNotificationsAsync();
        Assert.Equal(1, factory.Transport.AttemptsFor(endpoint));

        // Cuando llega la hora, se reintenta; y esta vez el proveedor responde.
        factory.Transport.Script(endpoint, ScriptedPushTransport.Behaviour.Succeed);
        factory.Clock.Advance(TimeSpan.FromSeconds(45));
        var second = await factory.DrainNotificationsAsync();

        Assert.Equal(1, second.Sent);
        Assert.Equal(2, factory.Transport.AttemptsFor(endpoint));
        Assert.Equal(1, factory.Transport.DeliveredFor(endpoint));
        Assert.Equal(NotificationDeliveryStatus.Sent, await StatusOf(endpoint));
    }

    [Fact]
    public async Task Retries_stop_at_the_last_attempt_and_the_notice_stays_in_the_inbox()
    {
        await Clean();
        var endpoint = await RegisterOwnerDevice("sin-suerte");
        factory.Transport.Script(endpoint, ScriptedPushTransport.Behaviour.ServerError);

        await CreateOrder();
        foreach (var wait in NotificationDelivery.Backoff)
        {
            factory.Clock.Advance(wait + TimeSpan.FromSeconds(1));
            await factory.DrainNotificationsAsync();
        }

        Assert.Equal(NotificationDelivery.MaximumAttempts, factory.Transport.AttemptsFor(endpoint));
        Assert.Equal(NotificationDeliveryStatus.Abandoned, await StatusOf(endpoint));

        // Se deja de intentar, pero no se deja de contar: el aviso sigue en la bandeja.
        var inbox = await OwnerInbox();
        Assert.Contains(inbox.Items, x => x.Kind == nameof(NotificationKind.OrderPlaced));

        // Y una pasada más no reabre el intento abandonado.
        factory.Clock.Advance(TimeSpan.FromDays(1));
        await factory.DrainNotificationsAsync();
        Assert.Equal(NotificationDelivery.MaximumAttempts, factory.Transport.AttemptsFor(endpoint));
    }

    // ---------------------------------------------------------------------------------------
    // Nada se envía dos veces, y nada se pierde por reiniciar
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Draining_twice_does_not_send_the_same_notice_twice()
    {
        await Clean();
        var endpoint = await RegisterOwnerDevice("una-vez");

        await CreateOrder();
        await factory.DrainNotificationsAsync();
        await factory.DrainNotificationsAsync();
        factory.Clock.Advance(TimeSpan.FromHours(6));
        await factory.DrainNotificationsAsync();

        Assert.Equal(1, factory.Transport.DeliveredFor(endpoint));
        Assert.Equal(1, factory.Transport.AttemptsFor(endpoint));
    }

    [Fact]
    public async Task A_pending_delivery_survives_a_worker_that_never_ran()
    {
        await Clean();
        var endpoint = await RegisterOwnerDevice("reinicio");
        factory.Transport.Default = ScriptedPushTransport.Behaviour.Timeout;

        await CreateOrder();
        // Primera pasada: se reparte y falla. Es el estado en el que un despliegue puede pillar al
        // proceso, y lo que importa es que la fila quede escrita y no en memoria de nadie.
        await factory.DrainNotificationsAsync();
        Assert.Equal(NotificationDeliveryStatus.Pending, await StatusOf(endpoint));

        // "Reinicio": un ámbito nuevo, sin ningún estado heredado, retoma lo pendiente.
        factory.Transport.Default = ScriptedPushTransport.Behaviour.Succeed;
        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
            var report = await dispatcher.RunOnceAsync();
            Assert.Equal(1, report.Sent);
        }
        Assert.Equal(NotificationDeliveryStatus.Sent, await StatusOf(endpoint));
    }

    [Fact]
    public async Task The_same_business_fact_is_recorded_once_even_if_the_command_is_repeated()
    {
        await Clean();
        var order = await CreateOrder();
        using var owner = await OwnerClient();
        var board = await owner.GetFromJsonAsync<PickupOrderBoardDto>(
            $"/api/v1/businesses/{OrderBusinessId}/orders");
        var stored = board!.Items.Single(x => x.OrderNumber == order.OrderNumber);

        var accepted = await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{OrderBusinessId}/orders/{stored.Id}/accept",
            new PickupOrderCommandRequest { Version = stored.Version });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        // Segundo clic con la misma versión: el pedido lo rechaza y el aviso no se duplica.
        var repeated = await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{OrderBusinessId}/orders/{stored.Id}/accept",
            new PickupOrderCommandRequest { Version = stored.Version });
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Notifications.CountAsync(x => x.EntityId == stored.Id &&
            x.Kind == NotificationKind.OrderAccepted));
    }

    // ---------------------------------------------------------------------------------------
    // El cliente también conserva su historia
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task The_customer_can_read_every_status_change_even_with_push_broken()
    {
        await Clean();
        factory.Transport.Default = ScriptedPushTransport.Behaviour.Timeout;
        var order = await CreateOrder();
        using var owner = await OwnerClient();
        var board = await owner.GetFromJsonAsync<PickupOrderBoardDto>(
            $"/api/v1/businesses/{OrderBusinessId}/orders");
        var stored = board!.Items.Single(x => x.OrderNumber == order.OrderNumber);
        stored = await Advance(owner, stored, "accept");
        stored = await Advance(owner, stored, "prepare");
        _ = await Advance(owner, stored, "ready");

        using var visitor = factory.CreateClient();
        var updates = await visitor.GetFromJsonAsync<List<NotificationDto>>(
            $"/api/v1/public/orders/{order.TrackingCode}/notifications");

        Assert.Contains(updates!, x => x.Kind == nameof(NotificationKind.OrderAccepted));
        Assert.Contains(updates!, x => x.Kind == nameof(NotificationKind.OrderPreparing));
        Assert.Contains(updates!, x => x.Kind == nameof(NotificationKind.OrderReady));
        // El enlace del cliente no se guarda: su código es la credencial y viaja en la suscripción.
        Assert.All(updates!, x => Assert.Null(x.DeepLink));
    }

    [Fact]
    public async Task An_unknown_tracking_code_reveals_nothing()
    {
        using var visitor = factory.CreateClient();
        foreach (var code in new[] { "no-existe", new string('a', 40) })
        {
            var response = await visitor.GetAsync($"/api/v1/public/orders/{code}/notifications");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty((await response.Content.ReadFromJsonAsync<List<NotificationDto>>())!);
        }
    }

    [Fact]
    public async Task The_inbox_of_a_business_is_closed_to_someone_from_another_business()
    {
        using var stranger = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await PlatformAdministrationApiTests.Login(stranger, DevelopmentSeeder.OtherOwnerEmail);
        var response = await stranger.GetAsync($"/api/v1/businesses/{OrderBusinessId}/notifications");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var counts = await stranger.GetFromJsonAsync<List<NotificationCountDto>>(
            "/api/v1/businesses/notifications/unread");
        Assert.DoesNotContain(counts!, x => x.BusinessId == OrderBusinessId);
    }

    [Fact]
    public async Task Reading_a_notice_clears_it_for_the_business()
    {
        await Clean();
        await CreateOrder();
        using var owner = await OwnerClient();
        var before = await owner.GetFromJsonAsync<NotificationPageDto>(
            $"/api/v1/businesses/{OrderBusinessId}/notifications");
        Assert.True(before!.UnreadCount > 0);

        var response = await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{OrderBusinessId}/notifications/read", new MarkNotificationsReadRequest());
        var after = await response.Content.ReadFromJsonAsync<NotificationPageDto>();
        Assert.Equal(0, after!.UnreadCount);
        Assert.All(after.Items, x => Assert.True(x.IsRead));
    }

    // ---------------------------------------------------------------------------------------
    // Andamiaje
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Deja el buzón y los dispositivos del negocio como estaban antes de la suite. Sin esto, un
    /// intento pendiente de otra prueba se colaría en la siguiente pasada y afirmaríamos sobre él.
    /// </summary>
    private async Task Clean()
    {
        await factory.DrainNotificationsAsync(8);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.NotificationDeliveries.Where(x => x.BusinessId == OrderBusinessId).ExecuteDeleteAsync();
        await db.Notifications.Where(x => x.BusinessId == OrderBusinessId).ExecuteDeleteAsync();
        await db.WebPushSubscriptions.Where(x => x.BusinessId == OrderBusinessId).ExecuteDeleteAsync();
        factory.Transport.Reset();
    }

    private async Task<HttpClient> OwnerClient()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await PlatformAdministrationApiTests.Login(client, DevelopmentSeeder.SazonOwnerEmail);
        return client;
    }

    private async Task<string> RegisterOwnerDevice(string name)
    {
        var endpoint = Endpoint(name);
        using var owner = await OwnerClient();
        var response = await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{OrderBusinessId}/push-subscriptions", Device(endpoint));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return endpoint;
    }

    private async Task<NotificationPageDto> OwnerInbox()
    {
        using var owner = await OwnerClient();
        return (await owner.GetFromJsonAsync<NotificationPageDto>(
            $"/api/v1/businesses/{OrderBusinessId}/notifications"))!;
    }

    private async Task<NotificationDeliveryStatus> StatusOf(string endpoint)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscriptionId = await db.WebPushSubscriptions.AsNoTracking()
            .Where(x => x.Endpoint == endpoint).Select(x => x.Id).SingleAsync();
        return await db.NotificationDeliveries.AsNoTracking()
            .Where(x => x.SubscriptionId == subscriptionId)
            .OrderByDescending(x => x.CreatedAtUtc).Select(x => x.Status).FirstAsync();
    }

    private async Task<bool> IsActive(string endpoint)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.WebPushSubscriptions.AsNoTracking()
            .Where(x => x.Endpoint == endpoint).Select(x => x.IsActive).SingleAsync();
    }

    private static async Task<PickupOrderAdminDto> Advance(HttpClient owner, PickupOrderAdminDto order,
        string action)
    {
        var response = await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{OrderBusinessId}/orders/{order.Id}/{action}",
            new PickupOrderCommandRequest { Version = order.Version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PickupOrderAdminDto>())!;
    }

    private async Task<PickupOrderCreatedDto> CreateOrder()
    {
        using var visitor = factory.CreateClient();
        var menu = await visitor.GetFromJsonAsync<PickupMenuDto>(
            $"/api/v1/public/businesses/{OrderBusinessSlug}/menu");
        var slots = await visitor.GetFromJsonAsync<PickupSlotListDto>(
            $"/api/v1/public/businesses/{OrderBusinessSlug}/pickup-slots");
        Assert.NotEmpty(slots!.Slots);
        var response = await visitor.PostAsJsonAsync(
            $"/api/v1/public/businesses/{OrderBusinessSlug}/orders", new CreatePickupOrderRequest
            {
                CustomerAlias = "Buzón", Phone = "3001234567", PickupStart = slots.Slots.Last().Start,
                ConsentAccepted = true, ConsentNoticeVersion = "pilot-1",
                Lines = [new() { ProductId = menu!.Products.First().Id, Quantity = 1 }]
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PickupOrderCreatedDto>())!;
    }
}
