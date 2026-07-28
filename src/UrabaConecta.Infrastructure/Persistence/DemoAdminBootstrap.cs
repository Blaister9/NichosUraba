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
/// Recuperación excepcional y de una sola ejecución para la cuenta administrativa de Demo.
/// No expone endpoints ni registra la contraseña.
/// </summary>
public static class DemoAdminBootstrap
{
    public const string ExpectedAdminEmail = "admin@urabaconecta.demo";

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
        if (await db.PlatformAccessAudits.AnyAsync(
                x => x.Action == PlatformAccessAction.DemoAdministratorBootstrap, cancellationToken))
            return;

        var email = configuration["DemoBootstrap:AdminEmail"]?.Trim();
        var password = configuration["DemoBootstrap:AdminPassword"];
        Validate(email, password);

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
            var token = await users.GeneratePasswordResetTokenAsync(admin);
            EnsureSucceeded(await users.ResetPasswordAsync(admin, token, password!));
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
            null, "Se normalizó una vez el acceso administrativo del ambiente Demo.", null,
            DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(cancellationToken);

        scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DemoAdminBootstrap")
            .LogWarning("Se realizó el reinicio administrativo de una sola ejecución para Demo.");
    }

    private static void Validate(string? email, string? password)
    {
        if (!string.Equals(email, ExpectedAdminEmail, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"DemoBootstrap__AdminEmail debe ser {ExpectedAdminEmail}.");
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
