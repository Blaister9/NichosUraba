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

public sealed class DemoAdminBootstrapTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    [Fact]
    public async Task Bootstrap_resets_the_existing_admin_once_and_requires_a_password_change()
    {
        _ = factory.CreateClient();
        Guid originalId;
        await using (var before = factory.Services.CreateAsyncScope())
            originalId = await before.ServiceProvider.GetRequiredService<AppDbContext>().Users
                .Where(x => x.Email == DevelopmentSeeder.PlatformAdminEmail)
                .Select(x => x.Id).SingleAsync();

        const string firstPassword = "Temporal-Segura-2026!";
        var configuration = Configuration(firstPassword);
        await factory.Services.BootstrapDemoAdminAsync(new DemoEnvironment(), configuration);
        await factory.Services.BootstrapDemoAdminAsync(new DemoEnvironment(),
            Configuration("Otra-Temporal-2026!"));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await users.FindByEmailAsync(DevelopmentSeeder.PlatformAdminEmail);

        Assert.NotNull(admin);
        Assert.Equal(originalId, admin.Id);
        Assert.True(admin.EmailConfirmed);
        Assert.True(admin.MustChangePassword);
        Assert.True(await users.IsInRoleAsync(admin, "PlatformAdmin"));
        Assert.True(await users.CheckPasswordAsync(admin, firstPassword));
        Assert.False(await users.CheckPasswordAsync(admin, "Otra-Temporal-2026!"));
        Assert.Equal(1, await db.PlatformAccessAudits.CountAsync(
            x => x.Action == PlatformAccessAction.DemoAdministratorBootstrap));
    }

    private static IConfiguration Configuration(string password)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DemoBootstrap:Enabled"] = "true",
            ["DemoBootstrap:AdminEmail"] = DevelopmentSeeder.PlatformAdminEmail,
            ["DemoBootstrap:AdminPassword"] = password
        }).Build();

    private sealed class DemoEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Demo";
        public string ApplicationName { get; set; } = "UrabaConecta.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
