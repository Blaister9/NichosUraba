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
        // Se cuenta la señal propia y no el total: otras pruebas de esta clase comparten la base y
        // añaden recuperaciones con señales distintas.
        Assert.Equal(1, await db.PlatformAccessAudits.CountAsync(
            x => x.Action == PlatformAccessAction.DemoAdministratorBootstrap &&
                 x.Summary.Contains("[señal:inicial]")));
    }

    /// <summary>
    /// Perder la contraseña administrativa no puede dejar la demostración sin puerta de entrada:
    /// una señal nueva habilita exactamente una recuperación más, y la anterior sigue auditada.
    /// </summary>
    [Fact]
    public async Task A_new_token_allows_exactly_one_more_recovery_without_erasing_the_previous_one()
    {
        _ = factory.CreateClient();
        await factory.Services.BootstrapDemoAdminAsync(new DemoEnvironment(),
            Configuration("Primera-Recuperacion-2026!", "rotacion-a"));

        const string second = "Segunda-Recuperacion-2026!";
        await factory.Services.BootstrapDemoAdminAsync(new DemoEnvironment(),
            Configuration(second, "rotacion-b"));
        // Repetir la misma señal no vuelve a reponer la contraseña.
        await factory.Services.BootstrapDemoAdminAsync(new DemoEnvironment(),
            Configuration("Tercera-Ignorada-2026!", "rotacion-b"));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await users.FindByEmailAsync(DevelopmentSeeder.PlatformAdminEmail);

        Assert.True(await users.CheckPasswordAsync(admin!, second));
        Assert.False(await users.CheckPasswordAsync(admin!, "Tercera-Ignorada-2026!"));
        var resumenes = await db.PlatformAccessAudits
            .Where(x => x.Action == PlatformAccessAction.DemoAdministratorBootstrap)
            .Select(x => x.Summary).ToListAsync();
        Assert.Contains(resumenes, x => x.Contains("[señal:rotacion-a]"));
        Assert.Contains(resumenes, x => x.Contains("[señal:rotacion-b]"));
    }

    [Fact]
    public async Task A_token_with_the_marker_delimiters_is_rejected()
        => await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.Services.BootstrapDemoAdminAsync(new DemoEnvironment(),
                Configuration("Temporal-Segura-2026!", "[rotacion]")));

    private static IConfiguration Configuration(string password, string? token = null)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DemoBootstrap:Enabled"] = "true",
            ["DemoBootstrap:AdminEmail"] = DevelopmentSeeder.PlatformAdminEmail,
            ["DemoBootstrap:AdminPassword"] = password,
            [DemoAdminBootstrap.TokenKey] = token
        }).Build();

    private sealed class DemoEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Demo";
        public string ApplicationName { get; set; } = "UrabaConecta.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
