using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Persistence;
using UrabaConecta.Infrastructure.Security;

namespace UrabaConecta.IntegrationTests;

public sealed partial class QueueApiTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Long_lived_consumer_reads_external_cancellations_without_stale_tracked_entities()
    {
        using var writer = Client();
        await using var circuit = factory.Services.CreateAsyncScope();
        var consumer = circuit.ServiceProvider.GetRequiredService<IUrabaConectaApi>();
        const string slug = "barberia-el-corte";
        var before = (await consumer.GetPublicQueueAsync(slug))!;
        async Task<QueueTicketCreatedDto> Join(string alias)
        {
            using var response = await writer.PostAsJsonAsync($"/api/v1/public/businesses/{slug}/queue/tickets",
                new CreateQueueTicketRequest { Alias = alias, ConsentAccepted = true,
                    ConsentNoticeVersion = ConsentPolicyProvider.FallbackVersion });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<QueueTicketCreatedDto>())!;
        }
        var a = await Join("Live A");
        var b = await Join("Live B");
        var initial = (await consumer.GetQueueTicketAsync(b.TrackingCode))!;
        Assert.Equal(before.WaitingCount + 2, (await consumer.GetPublicQueueAsync(slug))!.WaitingCount);

        using var cancelA = await writer.PostAsJsonAsync($"/api/v1/public/queue/tickets/{a.TrackingCode}/cancel",
            new QueueSessionCommandRequest { Version = 0 });
        cancelA.EnsureSuccessStatusCode();
        Assert.Equal(initial.PeopleAhead - 1, (await consumer.GetQueueTicketAsync(b.TrackingCode))!.PeopleAhead);
        Assert.Equal(before.WaitingCount + 1, (await consumer.GetPublicQueueAsync(slug))!.WaitingCount);

        using var cancelB = await writer.PostAsJsonAsync($"/api/v1/public/queue/tickets/{b.TrackingCode}/cancel",
            new QueueSessionCommandRequest { Version = 0 });
        cancelB.EnsureSuccessStatusCode();
        var final = (await consumer.GetQueueTicketAsync(b.TrackingCode))!;
        Assert.Equal("Cancelled", final.Status);
        Assert.False(final.CanCancel);
        Assert.Equal(1, final.Version);
        Assert.Equal(before.WaitingCount, (await consumer.GetPublicQueueAsync(slug))!.WaitingCount);
    }

    [Fact]
    public async Task Public_business_exposes_queue_and_ticket_code_is_never_persisted_plain()
    {
        using var client = Client();
        var profile = await client.GetFromJsonAsync<BusinessProfileDto>("/api/v1/public/businesses/barberia-el-corte", Json);
        Assert.True(profile!.HasVirtualQueue);
        var status = await client.GetFromJsonAsync<QueuePublicStatusDto>(
            "/api/v1/public/businesses/barberia-el-corte/queue", Json);
        Assert.True(status!.CanJoin);
        var response = await client.PostAsJsonAsync("/api/v1/public/businesses/barberia-el-corte/queue/tickets",
            new CreateQueueTicketRequest { Alias = "Ana", ConsentAccepted = true, ConsentNoticeVersion = ConsentPolicyProvider.FallbackVersion }, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<QueueTicketCreatedDto>(Json))!;
        Assert.True(Convert.FromBase64String(created.TrackingCode.Replace('-', '+').Replace('_', '/') + "==").Length >= 16);
        var tracked = await client.GetFromJsonAsync<QueueTicketTrackingDto>(
            $"/api/v1/public/queue/tickets/{created.TrackingCode}", Json);
        Assert.Equal(created.Number, tracked!.Number);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.QueueTickets.SingleAsync(x => x.Number == created.Number);
        Assert.NotEqual(created.TrackingCode, stored.PublicCodeHash);
        Assert.DoesNotContain("Ana", stored.ProtectedAlias ?? "");
    }

    [Fact]
    public async Task Concurrent_public_creation_has_unique_contiguous_numbers()
    {
        using var client = Client();
        var operations = Enumerable.Range(0, 6).Select(i => client.PostAsJsonAsync(
            "/api/v1/public/businesses/barberia-el-corte/queue/tickets",
            new CreateQueueTicketRequest { Alias = $"P{i}", ConsentAccepted = true, ConsentNoticeVersion = ConsentPolicyProvider.FallbackVersion }, Json));
        var responses = await Task.WhenAll(operations);
        Assert.All(responses, x => Assert.Equal(HttpStatusCode.Created, x.StatusCode));
        var tickets = await Task.WhenAll(responses.Select(x => x.Content.ReadFromJsonAsync<QueueTicketCreatedDto>(Json)));
        var numbers = tickets.Select(x => x!.Number).Order().ToArray();
        Assert.Equal(numbers.Length, numbers.Distinct().Count());
        Assert.Equal(numbers.First() + numbers.Length - 1, numbers.Last());
    }

    [Fact]
    public async Task Queue_permission_is_isolated_and_revoked_immediately()
    {
        using var allowed = Client(); using var denied = Client(); using var other = Client(); using var owner = Client();
        await Login(allowed, DevelopmentSeeder.CorteQueueWorkerEmail);
        await Login(denied, DevelopmentSeeder.CorteNoPermissionEmail);
        await Login(other, DevelopmentSeeder.OtherOwnerEmail);
        await Login(owner, DevelopmentSeeder.CorteOwnerEmail);
        Assert.Equal(HttpStatusCode.OK, (await allowed.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.CorteBusinessId}/queue")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await denied.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.CorteBusinessId}/queue")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.CorteBusinessId}/queue")).StatusCode);
        var members = await owner.GetFromJsonAsync<BusinessMemberListDto>(
            $"/api/v1/businesses/{DevelopmentSeeder.CorteBusinessId}/memberships", Json);
        var worker = members!.Items.Single(x => x.Email == DevelopmentSeeder.CorteQueueWorkerEmail);
        var changed = await owner.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.CorteBusinessId}/memberships/{worker.Id}/permissions",
            new UpdateMemberPermissionsRequest { Version = worker.Version, CanManageQueues = false }, Json);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await allowed.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.CorteBusinessId}/queue")).StatusCode);
    }

    [Fact]
    public async Task Closing_with_active_tickets_and_cross_business_ticket_action_are_rejected()
    {
        using var owner = Client(); using var other = Client();
        await Login(owner, DevelopmentSeeder.CorteOwnerEmail);
        await Login(other, DevelopmentSeeder.OtherOwnerEmail);
        var admin = await owner.GetFromJsonAsync<QueueAdminDto>(
            $"/api/v1/businesses/{DevelopmentSeeder.CorteBusinessId}/queue", Json);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.CorteBusinessId}/queue/close",
            new QueueSessionCommandRequest { Version = admin!.SessionVersion!.Value }, Json)).StatusCode);
        var ticket = admin.Tickets.First();
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.CorteBusinessId}/queue/tickets/{ticket.Id}/cancel",
            new QueueTicketCommandRequest { TicketVersion = ticket.Version, SessionVersion = admin.SessionVersion.Value }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.CorteBusinessId}/queue/tickets/{Guid.NewGuid()}/cancel",
            new QueueTicketCommandRequest { SessionVersion = admin.SessionVersion.Value }, Json)).StatusCode);
    }

    [Fact]
    public async Task Invalid_tracking_code_reveals_no_ticket()
    {
        using var client = Client();
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/v1/public/queue/tickets/not-a-real-code")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_call_next_changes_only_one_ticket_for_the_observed_version()
    {
        using var visitor = Client();
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.Created, (await visitor.PostAsJsonAsync(
                "/api/v1/public/businesses/barberia-el-corte/queue/tickets",
                new CreateQueueTicketRequest { Alias = $"Concurrencia {i}", ConsentAccepted = true, ConsentNoticeVersion = ConsentPolicyProvider.FallbackVersion }, Json)).StatusCode);
        using var first = Client(); using var second = Client();
        await Login(first, DevelopmentSeeder.CorteOwnerEmail);
        await Login(second, DevelopmentSeeder.CorteOwnerEmail);
        var before = await first.GetFromJsonAsync<QueueAdminDto>(
            $"/api/v1/businesses/{DevelopmentSeeder.CorteBusinessId}/queue", Json);
        var command = new QueueSessionCommandRequest { Version = before!.SessionVersion!.Value };
        var responses = await Task.WhenAll(
            first.PostAsJsonAsync($"/api/v1/businesses/{DevelopmentSeeder.CorteBusinessId}/queue/call-next", command, Json),
            second.PostAsJsonAsync($"/api/v1/businesses/{DevelopmentSeeder.CorteBusinessId}/queue/call-next", command, Json));
        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        var after = await first.GetFromJsonAsync<QueueAdminDto>(
            $"/api/v1/businesses/{DevelopmentSeeder.CorteBusinessId}/queue", Json);
        Assert.Equal(1, after!.Tickets.Count(x => x.Status == "Called"));
    }

    private HttpClient Client() => factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
    private static async Task Login(HttpClient client, string email)
    {
        var html = await client.GetStringAsync("/Account/Login");
        var token = AntiforgeryRegex().Match(html).Groups[1].Value.Replace("&quot;", "\"").Replace("&amp;", "&");
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["_handler"] = "login",
            ["Input.Email"] = email, ["Input.Password"] = DevelopmentSeeder.DemoPassword, ["Input.RememberMe"] = "false"
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryRegex();
}
