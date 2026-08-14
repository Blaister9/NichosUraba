using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

public sealed class DemoShowcaseSeederTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    [Fact]
    public async Task Seed_is_idempotent_scoped_and_keeps_memberships_attached()
    {
        _ = factory.CreateClient();
        await using var beforeScope = factory.Services.CreateAsyncScope();
        var beforeDb = beforeScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var protectedBusiness = await beforeDb.Businesses.AsNoTracking()
            .FirstAsync(x => x.Id != DemoShowcaseSeeder.BarberBusinessId &&
                             x.Id != DemoShowcaseSeeder.BeautyBusinessId);
        var protectedSnapshot = new
        {
            protectedBusiness.Name, protectedBusiness.Slug, protectedBusiness.Address,
            protectedBusiness.PublicPhone, protectedBusiness.Version
        };

        var configuration = Configuration();
        await factory.Services.SeedDemoShowcaseAsync(new TestEnvironment("Demo"), configuration);
        await factory.Services.SeedDemoShowcaseAsync(new TestEnvironment("Demo"), configuration);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var barber = await db.Businesses.AsNoTracking()
            .SingleAsync(x => x.Id == DemoShowcaseSeeder.BarberBusinessId);
        var beauty = await db.Businesses.AsNoTracking()
            .SingleAsync(x => x.Id == DemoShowcaseSeeder.BeautyBusinessId);
        Assert.Contains("DEMO", barber.Name);
        Assert.Contains("DEMO", beauty.Name);
        Assert.Single(await db.BusinessModules.AsNoTracking().Where(x =>
            x.BusinessId == barber.Id && x.Module == BusinessModuleKind.VirtualQueues).ToListAsync());
        Assert.Single(await db.BusinessModules.AsNoTracking().Where(x =>
            x.BusinessId == beauty.Id && x.Module == BusinessModuleKind.PickupOrders).ToListAsync());
        Assert.Equal(3, await db.Services.CountAsync(x => x.BusinessId == barber.Id));
        Assert.Equal(4, await db.Products.CountAsync(x => x.BusinessId == beauty.Id));
        Assert.False(await db.BusinessMemberships.AnyAsync(x =>
            !db.Businesses.Select(b => b.Id).Contains(x.BusinessId)));

        var untouched = await db.Businesses.AsNoTracking().SingleAsync(x => x.Id == protectedBusiness.Id);
        Assert.Equal(protectedSnapshot.Name, untouched.Name);
        Assert.Equal(protectedSnapshot.Slug, untouched.Slug);
        Assert.Equal(protectedSnapshot.Address, untouched.Address);
        Assert.Equal(protectedSnapshot.PublicPhone, untouched.PublicPhone);
        Assert.Equal(protectedSnapshot.Version, untouched.Version);
    }

    [Fact]
    public async Task Enabled_showcase_is_rejected_outside_Demo()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.Services.SeedDemoShowcaseAsync(new TestEnvironment("Production"), Configuration()));
    }

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ShowcaseSeed:Enabled"] = "true",
            ["ShowcaseSeed:BusinessPassword"] = "Showcase-Owner-2026!"
        }).Build();

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "UrabaConecta.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
