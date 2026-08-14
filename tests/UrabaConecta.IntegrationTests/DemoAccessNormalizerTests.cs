using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Identity;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

public sealed class DemoAccessNormalizerTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    [Fact]
    public async Task Normalizer_resets_the_showcase_accounts_and_enforces_roles_and_memberships()
    {
        _ = factory.CreateClient();
        const string password = "Shared-Commercial-2026!";

        await factory.Services.SeedDemoShowcaseAsync(
            new TestEnvironment("Demo"), Configuration(password));
        await factory.Services.NormalizeDemoAccessAsync(
            new TestEnvironment("Demo"), Configuration(password));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var expected = new[]
        {
            (DevelopmentSeeder.PlatformAdminEmail, "PlatformAdmin", (Guid?)null),
            (DevelopmentSeeder.PartnerOperatorEmail, "PartnerOperator", (Guid?)null),
            (DemoShowcaseSeeder.BarberOwnerEmail, "BusinessOwner", (Guid?)DemoShowcaseSeeder.BarberBusinessId),
            (DemoShowcaseSeeder.BeautyOwnerEmail, "BusinessOwner", (Guid?)DemoShowcaseSeeder.BeautyBusinessId)
        };

        foreach (var (email, role, businessId) in expected)
        {
            var user = await users.FindByEmailAsync(email);
            Assert.NotNull(user);
            Assert.True(user.EmailConfirmed);
            Assert.False(user.MustChangePassword);
            Assert.Null(user.LockoutEnd);
            Assert.Equal(0, user.AccessFailedCount);
            Assert.Equal([role], await users.GetRolesAsync(user));
            Assert.True(await users.CheckPasswordAsync(user, password));

            var activeMemberships = await db.BusinessMemberships.AsNoTracking()
                .Where(x => x.UserId == user.Id && x.IsActive).ToListAsync();
            if (businessId is null)
                Assert.Empty(activeMemberships);
            else
            {
                var membership = Assert.Single(activeMemberships);
                Assert.Equal(businessId, membership.BusinessId);
                Assert.Equal(MembershipRole.Owner, membership.Role);
            }
        }
    }

    [Fact]
    public async Task Normalizer_is_blocked_outside_Demo()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.Services.NormalizeDemoAccessAsync(
                new TestEnvironment("Production"), Configuration("Shared-Commercial-2026!")));
    }

    private static IConfiguration Configuration(string password)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DemoAccess:SharedPassword"] = password,
            ["ShowcaseSeed:Enabled"] = "true"
        }).Build();

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "UrabaConecta.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
