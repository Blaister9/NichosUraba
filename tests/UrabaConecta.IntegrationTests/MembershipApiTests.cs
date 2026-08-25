using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Identity;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

public sealed partial class MembershipApiTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Member_list_enforces_authentication_permission_and_business_scope()
    {
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/memberships")).StatusCode);

        using var bellaOwner = Client();
        using var otherOwner = Client();
        using var worker = Client();
        await Login(bellaOwner, DevelopmentSeeder.BellaOwnerEmail);
        await Login(otherOwner, DevelopmentSeeder.OtherOwnerEmail);
        await Login(worker, DevelopmentSeeder.BellaWorkerEmail);

        var list = await bellaOwner.GetFromJsonAsync<BusinessMemberListDto>(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/memberships", Json);
        Assert.Contains(list!.Items, x => x.Email == DevelopmentSeeder.BellaOwnerEmail && x.IsOwner);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherOwner.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/memberships")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await worker.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/memberships")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherOwner.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/memberships/{list.Items[0].Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherOwner.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/memberships/{list.Items[0].Id}/permissions",
            new UpdateMemberPermissionsRequest { Version = list.Items[0].Version }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherOwner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/memberships/{list.Items[0].Id}/deactivate",
            new MembershipVersionRequest { Version = list.Items[0].Version }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherOwner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/memberships/{list.Items[0].Id}/activate",
            new MembershipVersionRequest { Version = list.Items[0].Version }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherOwner.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/memberships/{list.Items[0].Id}/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherOwner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/memberships/link-existing",
            new LinkExistingMemberRequest { Email = DevelopmentSeeder.OtherOwnerEmail }, Json)).StatusCode);
    }

    [Fact]
    public async Task Development_account_permissions_audit_and_stale_version_are_persisted()
    {
        using var owner = Client();
        await Login(owner, DevelopmentSeeder.BellaOwnerEmail);
        var created = await CreateDevelopmentMember(owner, DevelopmentSeeder.BellaBusinessId,
            appointments: true, configuration: false, members: false);
        Assert.NotEqual(DevelopmentSeeder.DemoPassword, created.TemporaryPassword);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/memberships/create-development",
            new CreateDevelopmentMemberRequest { DisplayName = "Duplicada", Email = created.Member.Email }, Json)).StatusCode);

        var update = new UpdateMemberPermissionsRequest
        {
            CanManageAppointments = true, CanManageConfiguration = true,
            CanManageMembers = false, Version = created.Member.Version
        };
        var updatedResponse = await owner.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/memberships/{created.Member.Id}/permissions",
            update, Json);
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        var updated = (await updatedResponse.Content.ReadFromJsonAsync<BusinessMemberDto>(Json))!;
        Assert.True(updated.Permissions.CanManageConfiguration);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/memberships/{created.Member.Id}/permissions",
            update, Json)).StatusCode);
        var audit = await owner.GetFromJsonAsync<List<MembershipAuditDto>>(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/memberships/{created.Member.Id}/audit", Json);
        Assert.Contains(audit!, x => x.Action == "MemberLinked");
        Assert.Contains(audit!, x => x.Action == "PermissionsChanged");
        Assert.DoesNotContain(audit!, x => x.NewState.Contains(created.TemporaryPassword, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Deactivation_revokes_every_private_operation_immediately_and_preserves_account()
    {
        using var owner = Client();
        await Login(owner, DevelopmentSeeder.BellaOwnerEmail);
        var created = await CreateDevelopmentMember(owner, DevelopmentSeeder.BellaBusinessId,
            appointments: true, configuration: true, members: true);
        using var member = Client();
        await Login(member, created.Member.Email, created.TemporaryPassword);
        int staffBefore;
        await using (var beforeScope = factory.Services.CreateAsyncScope())
            staffBefore = await beforeScope.ServiceProvider.GetRequiredService<AppDbContext>().StaffMembers.CountAsync(
                x => x.BusinessId == DevelopmentSeeder.BellaBusinessId);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/memberships")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/memberships/{created.Member.Id}/deactivate",
            new MembershipVersionRequest { Version = created.Member.Version }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/appointments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/memberships")).StatusCode);
        await using (var afterScope = factory.Services.CreateAsyncScope())
            Assert.Equal(staffBefore, await afterScope.ServiceProvider.GetRequiredService<AppDbContext>().StaffMembers.CountAsync(
                x => x.BusinessId == DevelopmentSeeder.BellaBusinessId));
        using var relogin = Client();
        await Login(relogin, created.Member.Email, created.TemporaryPassword);
    }

    [Fact]
    public async Task Last_owner_is_protected_and_ownership_can_be_transferred()
    {
        using var owner = Client();
        await Login(owner, DevelopmentSeeder.OtherOwnerEmail);
        var list = await owner.GetFromJsonAsync<BusinessMemberListDto>(
            $"/api/v1/businesses/{DevelopmentSeeder.OtherBusinessId}/memberships", Json);
        var original = list!.Items.Single(x => x.Email == DevelopmentSeeder.OtherOwnerEmail);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.OtherBusinessId}/memberships/{original.Id}/deactivate",
            new MembershipVersionRequest { Version = original.Version }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.OtherBusinessId}/memberships/{original.Id}/revoke-owner",
            new RevokeOwnershipRequest { Version = original.Version }, Json)).StatusCode);

        var successor = await CreateDevelopmentMember(owner, DevelopmentSeeder.OtherBusinessId,
            appointments: true, configuration: true, members: true);
        var granted = await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.OtherBusinessId}/memberships/{successor.Member.Id}/grant-owner",
            new MembershipVersionRequest { Version = successor.Member.Version }, Json);
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        var grantedMember = (await granted.Content.ReadFromJsonAsync<BusinessMemberDto>(Json))!;
        Assert.True(await HasRole(successor.Member.Email, "BusinessOwner"));
        Assert.False(await HasRole(successor.Member.Email, "BusinessWorker"));
        var revoke = await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.OtherBusinessId}/memberships/{original.Id}/revoke-owner",
            new RevokeOwnershipRequest
            {
                Version = original.Version, CanManageAppointments = true,
                CanManageConfiguration = false, CanManageMembers = false
            }, Json);
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        var former = (await revoke.Content.ReadFromJsonAsync<BusinessMemberDto>(Json))!;
        Assert.False(former.IsOwner);
        Assert.True(former.Permissions.CanManageAppointments);
        Assert.False(former.Permissions.CanManageMembers);
        Assert.False(await HasRole(DevelopmentSeeder.OtherOwnerEmail, "BusinessOwner"));
        Assert.True(await HasRole(DevelopmentSeeder.OtherOwnerEmail, "BusinessWorker"));

        using var successorClient = Client();
        await Login(successorClient, successor.Member.Email, successor.TemporaryPassword);
        Assert.Equal(HttpStatusCode.OK, (await successorClient.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.OtherBusinessId}/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await successorClient.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.OtherBusinessId}/images")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await successorClient.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.OtherBusinessId}/hours")).StatusCode);
        var restored = await successorClient.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.OtherBusinessId}/memberships/{original.Id}/grant-owner",
            new MembershipVersionRequest { Version = former.Version }, Json);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await successorClient.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.OtherBusinessId}/memberships/{successor.Member.Id}/deactivate",
            new MembershipVersionRequest { Version = grantedMember.Version }, Json)).StatusCode);
        Assert.False(await HasRole(successor.Member.Email, "BusinessOwner"));
        Assert.False(await HasRole(successor.Member.Email, "BusinessWorker"));
        Assert.Equal(HttpStatusCode.Forbidden, (await successorClient.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.OtherBusinessId}/profile")).StatusCode);
    }

    [Fact]
    public async Task Same_account_has_independent_permissions_in_two_businesses()
    {
        using var bellaOwner = Client();
        using var otherOwner = Client();
        await Login(bellaOwner, DevelopmentSeeder.BellaOwnerEmail);
        await Login(otherOwner, DevelopmentSeeder.OtherOwnerEmail);
        var created = await CreateDevelopmentMember(bellaOwner, DevelopmentSeeder.BellaBusinessId,
            appointments: true, configuration: false, members: false);
        var linked = await otherOwner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.OtherBusinessId}/memberships/link-existing",
            new LinkExistingMemberRequest
            {
                Email = created.Member.Email, CanManageAppointments = false,
                CanManageConfiguration = true, CanManageMembers = false
            }, Json);
        Assert.Equal(HttpStatusCode.Created, linked.StatusCode);
        var second = (await linked.Content.ReadFromJsonAsync<BusinessMemberDto>(Json))!;
        Assert.True(created.Member.Permissions.CanManageAppointments);
        Assert.False(created.Member.Permissions.CanManageConfiguration);
        Assert.False(second.Permissions.CanManageAppointments);
        Assert.True(second.Permissions.CanManageConfiguration);
    }

    [Fact]
    public async Task Concurrent_owner_deactivations_allow_at_most_one()
    {
        var setup = await CreateIsolatedBusinessWithTwoOwners();
        using var first = Client();
        using var second = Client();
        await Login(first, setup.First.Email, setup.First.Password);
        await Login(second, setup.Second.Email, setup.Second.Password);

        var operations = new[]
        {
            first.PostAsJsonAsync($"/api/v1/businesses/{setup.BusinessId}/memberships/{setup.First.MembershipId}/deactivate",
                new MembershipVersionRequest { Version = 0 }, Json),
            second.PostAsJsonAsync($"/api/v1/businesses/{setup.BusinessId}/memberships/{setup.Second.MembershipId}/deactivate",
                new MembershipVersionRequest { Version = 0 }, Json)
        };
        var responses = await Task.WhenAll(operations);
        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.BusinessMemberships.CountAsync(x =>
            x.BusinessId == setup.BusinessId && x.IsActive && x.Role == MembershipRole.Owner));
    }

    private HttpClient Client() => factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });

    private async Task<DevelopmentMemberCreatedDto> CreateDevelopmentMember(HttpClient owner, Guid businessId,
        bool appointments, bool configuration, bool members)
    {
        var email = $"miembro-{Guid.NewGuid():N}@demo.local";
        var response = await owner.PostAsJsonAsync($"/api/v1/businesses/{businessId}/memberships/create-development",
            new CreateDevelopmentMemberRequest
            {
                DisplayName = $"Miembro {email[8..14]}", Email = email,
                CanManageAppointments = appointments, CanManageConfiguration = configuration,
                CanManageMembers = members
            }, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DevelopmentMemberCreatedDto>(Json))!;
    }

    private async Task<(Guid BusinessId, Owner First, Owner Second)> CreateIsolatedBusinessWithTwoOwners()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var suffix = Guid.NewGuid().ToString("N");
        var first = new Owner($"owner-a-{suffix}@demo.local", $"OwnerA!{suffix}x1", Guid.NewGuid());
        var second = new Owner($"owner-b-{suffix}@demo.local", $"OwnerB!{suffix}x1", Guid.NewGuid());
        foreach (var owner in new[] { first, second })
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = owner.Email, Email = owner.Email,
                EmailConfirmed = true, DisplayName = owner.Email.Split('@')[0]
            };
            Assert.True((await users.CreateAsync(user, owner.Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "BusinessOwner")).Succeeded);
            owner.UserId = user.Id;
        }
        var municipality = await db.Municipalities.FirstAsync();
        var category = await db.Categories.FirstAsync();
        var businessId = Guid.NewGuid();
        db.Businesses.Add(new Business(businessId, $"concurrent-{suffix}", "Negocio concurrente",
            municipality.Id, category.Id, "Ficticio", "Dirección ficticia", "3000000000"));
        db.BusinessMemberships.AddRange(
            new BusinessMembership(first.MembershipId, businessId, first.UserId, MembershipRole.Owner),
            new BusinessMembership(second.MembershipId, businessId, second.UserId, MembershipRole.Owner));
        await db.SaveChangesAsync();
        return (businessId, first, second);
    }

    private async Task<bool> HasRole(string email, string role)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var account = await users.FindByEmailAsync(email);
        return account is not null && await users.IsInRoleAsync(account, role);
    }

    private static async Task Login(HttpClient client, string email, string? password = null)
    {
        var html = await client.GetStringAsync("/Account/Login");
        var token = AntiforgeryRegex().Match(html).Groups[1].Value.Replace("&quot;", "\"").Replace("&amp;", "&");
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["_handler"] = "login",
            ["Input.Email"] = email, ["Input.Password"] = password ?? DevelopmentSeeder.DemoPassword,
            ["Input.RememberMe"] = "false"
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private sealed class Owner(string email, string password, Guid membershipId)
    {
        public string Email { get; } = email;
        public string Password { get; } = password;
        public Guid MembershipId { get; } = membershipId;
        public Guid UserId { get; set; }
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryRegex();
}
