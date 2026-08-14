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

    private static async Task<QueueTicketCreatedDto> JoinQueue(HttpClient client, string alias)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/public/businesses/barberia-el-corte/queue/tickets",
            new CreateQueueTicketRequest { Alias = alias, ConsentAccepted = true,
                ConsentNoticeVersion = ConsentPolicyProvider.FallbackVersion });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<QueueTicketCreatedDto>())!;
    }
}
