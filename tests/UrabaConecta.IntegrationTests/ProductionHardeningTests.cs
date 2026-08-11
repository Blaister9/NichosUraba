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

/// <summary>
/// Comprobaciones del endurecimiento productivo: que el sembrado ficticio no pueda alcanzar
/// Production y que el alta administrativa inicial esté acotada por ambiente y por unicidad.
/// </summary>
public sealed class ProductionHardeningTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    /// <summary>
    /// Cuando la suite corre contra una base externa compartida (Docker caído), los casos que
    /// alteran cuentas quedan fuera: ensuciarían a las demás clases de prueba.
    /// </summary>
    private static readonly bool BaseCompartida =
        Environment.GetEnvironmentVariable("URABACONECTA_TEST_PG") is not null;

    [Fact]
    public async Task The_demonstration_seed_does_nothing_in_production()
    {
        var client = factory.CreateClient();
        _ = client;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var negociosAntes = await db.Businesses.CountAsync();
        var usuariosAntes = await db.Users.CountAsync();

        // Aunque alguien encendiera el interruptor, el ambiente manda: en Production el sembrado
        // retorna sin tocar nada. StartupGuard además impide arrancar con esta combinación.
        await factory.Services.SeedDevelopmentAsync(new TestEnvironment("Production"));

        Assert.Equal(negociosAntes, await db.Businesses.CountAsync());
        Assert.Equal(usuariosAntes, await db.Users.CountAsync());
    }

    [Fact]
    public async Task The_production_bootstrap_refuses_to_run_outside_production()
    {
        _ = factory.CreateClient();
        var configuracion = Configuration("admin@negocioreal.co", "Arranque-Productivo-2026!");

        foreach (var ambiente in new[] { "Demo", "Development", "Staging" })
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                factory.Services.BootstrapProductionAdminAsync(new TestEnvironment(ambiente), configuracion));
    }

    [Fact]
    public async Task The_production_bootstrap_stays_asleep_unless_it_is_switched_on()
    {
        _ = factory.CreateClient();
        var apagado = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ProductionAdminBootstrap.EmailKey] = "admin@negocioreal.co",
            [ProductionAdminBootstrap.PasswordKey] = "Arranque-Productivo-2026!"
        }).Build();

        // Sin el interruptor no corre, ni siquiera en un ambiente que no es Production: si corriera,
        // la excepción de ambiente lo delataría.
        await factory.Services.BootstrapProductionAdminAsync(new TestEnvironment("Demo"), apagado);
    }

    [Theory]
    [InlineData("admin@urabaconecta.demo", "Arranque-Productivo-2026!")]  // correo de demostración
    [InlineData("propietaria@bella.demo", "Arranque-Productivo-2026!")]   // correo de demostración
    [InlineData("sin-arroba", "Arranque-Productivo-2026!")]               // correo inválido
    [InlineData("admin@negocioreal.co", "corta1!A")]                      // demasiado corta
    [InlineData("admin@negocioreal.co", "sinmayusculas-2026!")]           // sin mayúscula
    [InlineData("admin@negocioreal.co", "SinDigitosNiSimbolos")]          // sin dígito ni símbolo
    [InlineData("admin@negocioreal.co", "UrabaDemo!2026")]                // contraseña de demostración
    public async Task The_production_bootstrap_rejects_unfit_credentials(string email, string password)
    {
        _ = factory.CreateClient();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.Services.BootstrapProductionAdminAsync(
                new TestEnvironment("Production"), Configuration(email, password)));
    }

    [Fact]
    public async Task The_production_bootstrap_never_adds_a_second_administrator()
    {
        _ = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var administradoresAntes = (await users.GetUsersInRoleAsync("PlatformAdmin")).Count;
        Assert.True(administradoresAntes > 0, "La base de pruebas ya trae un administrador.");
        var sociasAntes = (await users.GetUsersInRoleAsync("PartnerOperator")).Count;

        await factory.Services.BootstrapProductionAdminAsync(new TestEnvironment("Production"),
            Configuration("otro-admin@negocioreal.co", "Arranque-Productivo-2026!"));

        Assert.Equal(administradoresAntes, (await users.GetUsersInRoleAsync("PlatformAdmin")).Count);
        Assert.Null(await users.FindByEmailAsync("otro-admin@negocioreal.co"));
        // Las socias se invitan desde la consola; el arranque nunca las crea.
        Assert.Equal(sociasAntes, (await users.GetUsersInRoleAsync("PartnerOperator")).Count);
        Assert.Equal(0, await db.PlatformAccessAudits.CountAsync(
            x => x.Action == PlatformAccessAction.ProductionAdministratorBootstrap));
    }

    [Fact]
    public async Task The_production_bootstrap_creates_one_administrator_that_must_change_its_password()
    {
        // Altera cuentas, así que sólo corre cuando la clase tiene su propio contenedor. Contra
        // una base externa compartida ensuciaría a las demás clases.
        if (BaseCompartida) return;
        _ = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Se simula una instalación recién creada: sin ningún PlatformAdmin todavía.
        var previos = await users.GetUsersInRoleAsync("PlatformAdmin");
        foreach (var previo in previos) await users.RemoveFromRoleAsync(previo, "PlatformAdmin");
        const string email = "fundadora@negocioreal.co";
        const string password = "Arranque-Productivo-2026!";
        try
        {
            await factory.Services.BootstrapProductionAdminAsync(new TestEnvironment("Production"),
                Configuration(email, password));

            var admin = await users.FindByEmailAsync(email);
            Assert.NotNull(admin);
            Assert.True(await users.IsInRoleAsync(admin, "PlatformAdmin"));
            Assert.Single(await users.GetUsersInRoleAsync("PlatformAdmin"));
            Assert.True(admin.EmailConfirmed);
            // La contraseña de arranque la entrega un canal humano: debe caducar al primer uso.
            Assert.True(admin.MustChangePassword);
            Assert.True(await users.CheckPasswordAsync(admin, password));
            Assert.Equal(1, await db.PlatformAccessAudits.CountAsync(
                x => x.Action == PlatformAccessAction.ProductionAdministratorBootstrap));

            // Segunda ejecución: no repone nada, aunque la variable siga puesta.
            await factory.Services.BootstrapProductionAdminAsync(new TestEnvironment("Production"),
                Configuration("tercera@negocioreal.co", "Otro-Arranque-2026!"));
            Assert.Null(await users.FindByEmailAsync("tercera@negocioreal.co"));
            Assert.Equal(1, await db.PlatformAccessAudits.CountAsync(
                x => x.Action == PlatformAccessAction.ProductionAdministratorBootstrap));
        }
        finally
        {
            // La limpieza va completa y pase lo que pase: el rastro de auditoría es justamente lo
            // que hace irrepetible al arranque, así que dejarlo aquí desactivaría a los demás casos.
            if (await users.FindByEmailAsync(email) is { } creado) await users.DeleteAsync(creado);
            await db.PlatformAccessAudits
                .Where(x => x.Action == PlatformAccessAction.ProductionAdministratorBootstrap)
                .ExecuteDeleteAsync();
            foreach (var previo in previos)
                if (!await users.IsInRoleAsync(previo, "PlatformAdmin"))
                    await users.AddToRoleAsync(previo, "PlatformAdmin");
        }
    }

    private static IConfiguration Configuration(string email, string password)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ProductionAdminBootstrap.EnabledKey] = "true",
            [ProductionAdminBootstrap.EmailKey] = email,
            [ProductionAdminBootstrap.PasswordKey] = password
        }).Build();

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "UrabaConecta.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
