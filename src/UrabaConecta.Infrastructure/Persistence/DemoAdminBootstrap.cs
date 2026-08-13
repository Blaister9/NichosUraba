using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Identity;

namespace UrabaConecta.Infrastructure.Persistence;

/// <summary>
/// Recuperación excepcional para la cuenta administrativa de Demo. No expone endpoints ni
/// registra la contraseña.
///
/// Se ejecuta una sola vez <b>por señal</b>, no una sola vez en la vida del ambiente. Dejar la
/// variable puesta no repone la contraseña en cada despliegue —que es lo que la guarda evita—,
/// pero perder el acceso tampoco deja la demostración sin puerta de entrada para siempre: se
/// declara una señal nueva en <c>DemoBootstrap__Token</c> y la recuperación vuelve a correr una
/// vez. Cada ejecución añade su propia entrada de auditoría; ninguna borra la anterior, así que el
/// rastro de quién reinició el acceso y cuándo se conserva completo.
/// </summary>
public static class DemoAdminBootstrap
{
    public const string ExpectedAdminEmail = "admin@urabaconecta.demo";
    public const string TokenKey = "DemoBootstrap:Token";
    private const string DefaultToken = "inicial";

    /// <summary>
    /// Marca que se guarda en el resumen auditado. La señal es una etiqueta operativa, nunca la
    /// contraseña: sirve para distinguir una recuperación de la siguiente.
    /// </summary>
    private static string Marker(string token) => $"[señal:{token}]";

    public static async Task BootstrapDemoAdminAsync(this IServiceProvider services,
        IHostEnvironment environment, IConfiguration? configuration = null,
        CancellationToken cancellationToken = default)
    {
        configuration ??= services.GetRequiredService<IConfiguration>();
        if (!configuration.GetValue<bool>("DemoBootstrap:Enabled")) return;
        if (!environment.IsEnvironment("Demo"))
            throw new InvalidOperationException("DemoBootstrap__Enabled solo puede habilitarse en Demo.");

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var token = configuration[TokenKey]?.Trim() is { Length: > 0 } declared ? declared : DefaultToken;
        var marker = Marker(token);
        if (await db.PlatformAccessAudits.AnyAsync(
                x => x.Action == PlatformAccessAction.DemoAdministratorBootstrap &&
                     x.Summary.Contains(marker), cancellationToken))
            return;

        var email = configuration["DemoBootstrap:AdminEmail"]?.Trim();
        var password = configuration["DemoBootstrap:AdminPassword"];
        Validate(email, password, token);

        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync("PlatformAdmin"))
        {
            var roleResult = await roles.CreateAsync(new IdentityRole<Guid>("PlatformAdmin"));
            EnsureSucceeded(roleResult);
        }

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await users.FindByEmailAsync(ExpectedAdminEmail);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = ExpectedAdminEmail,
                Email = ExpectedAdminEmail,
                EmailConfirmed = true,
                DisplayName = "Administración UrabáConecta",
                MustChangePassword = true
            };
            EnsureSucceeded(await users.CreateAsync(admin, password!));
        }
        else
        {
            var reset = await users.GeneratePasswordResetTokenAsync(admin);
            EnsureSucceeded(await users.ResetPasswordAsync(admin, reset, password!));
            admin.EmailConfirmed = true;
            admin.MustChangePassword = true;
            EnsureSucceeded(await users.UpdateAsync(admin));
        }

        if (!await users.IsInRoleAsync(admin, "PlatformAdmin"))
            EnsureSucceeded(await users.AddToRoleAsync(admin, "PlatformAdmin"));
        EnsureSucceeded(await users.SetLockoutEndDateAsync(admin, null));
        EnsureSucceeded(await users.ResetAccessFailedCountAsync(admin));
        EnsureSucceeded(await users.UpdateSecurityStampAsync(admin));

        db.PlatformAccessAudits.Add(new PlatformAccessAudit(Guid.NewGuid(), admin.Id,
            PlatformAccessAction.DemoAdministratorBootstrap, nameof(ApplicationUser), admin.Id.ToString(),
            null, $"Se normalizó el acceso administrativo del ambiente Demo. {marker}", null,
            DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(cancellationToken);

        scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DemoAdminBootstrap")
            .LogWarning("Se realizó el reinicio administrativo de una sola ejecución para Demo.");
    }

    private static void Validate(string? email, string? password, string token)
    {
        if (!string.Equals(email, ExpectedAdminEmail, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"DemoBootstrap__AdminEmail debe ser {ExpectedAdminEmail}.");
        // La señal viaja a una columna de 400 caracteres y se busca por coincidencia parcial: se
        // acota a una etiqueta corta y sin corchetes, que son los delimitadores de la marca.
        if (token.Length > 40 || token.Contains('[') || token.Contains(']'))
            throw new InvalidOperationException(
                "DemoBootstrap__Token debe ser una etiqueta corta, sin corchetes.");
        if (password is null || password.Length < 16 ||
            !password.Any(char.IsUpper) || !password.Any(char.IsLower) ||
            !password.Any(char.IsDigit) || password.All(char.IsLetterOrDigit) ||
            string.Equals(password, "demo2026", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "DemoBootstrap__AdminPassword no cumple la política temporal de acceso.");
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "ASP.NET Identity rechazó la normalización del acceso administrativo Demo.");
    }
}
