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
/// Alta de la única cuenta administrativa con la que nace Production. No es un endpoint: corre
/// en el arranque y sólo en Production, de modo que no queda ninguna ruta de bootstrap expuesta.
///
/// Crea un PlatformAdmin y nada más. Las socias no se crean aquí: el administrador las invita
/// desde la consola y cada una define su propia contraseña, así que no existe una credencial
/// compartida ni una contraseña que viaje por WhatsApp.
/// </summary>
public static class ProductionAdminBootstrap
{
    public const string EnabledKey = "ProductionBootstrap:Enabled";
    public const string EmailKey = "ProductionBootstrap:AdminEmail";
    public const string PasswordKey = "ProductionBootstrap:AdminPassword";
    public const int MinimumPasswordLength = 16;

    public static async Task BootstrapProductionAdminAsync(this IServiceProvider services,
        IHostEnvironment environment, IConfiguration? configuration = null,
        CancellationToken cancellationToken = default)
    {
        configuration ??= services.GetRequiredService<IConfiguration>();
        if (!configuration.GetValue<bool>(EnabledKey)) return;
        // Bloqueo por ambiente antes de tocar la base: habilitarlo en Demo o en Development es un
        // error de configuración, no una alternativa silenciosa.
        if (!environment.IsProduction())
            throw new InvalidOperationException(
                "ProductionBootstrap__Enabled solo puede habilitarse en Production.");

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("UrabaConecta.ProductionBootstrap");

        // De una sola ejecución. Dejar la variable puesta tras el primer arranque no vuelve a
        // reponer la contraseña: si se perdió el acceso, la ruta es la recuperación documentada.
        if (await db.PlatformAccessAudits.AnyAsync(
                x => x.Action == PlatformAccessAction.ProductionAdministratorBootstrap, cancellationToken))
        {
            logger.LogInformation("El bootstrap productivo ya se ejecutó; no se repite.");
            return;
        }

        var email = configuration[EmailKey]?.Trim();
        var password = configuration[PasswordKey];
        Validate(email, password);

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        // Production arranca con un administrador, no con un juego de cuentas. Si ya hay alguno,
        // este camino no debe crear un segundo por descuido.
        var existingAdmins = await users.GetUsersInRoleAsync("PlatformAdmin");
        if (existingAdmins.Count > 0)
        {
            logger.LogWarning("Ya existe una cuenta PlatformAdmin; el bootstrap productivo no crea otra.");
            return;
        }

        foreach (var role in new[] { "PlatformAdmin", "PartnerOperator", "BusinessOwner", "BusinessWorker" })
            if (!await roles.RoleExistsAsync(role))
                EnsureSucceeded(await roles.CreateAsync(new IdentityRole<Guid>(role)));

        var admin = await users.FindByEmailAsync(email!);
        if (admin is not null)
            throw new InvalidOperationException(
                "ProductionBootstrap__AdminEmail ya corresponde a una cuenta existente.");

        admin = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "Administración UrabáConecta",
            // La contraseña de arranque es temporal por definición: la entrega un canal humano y
            // debe dejar de ser válida en el primer inicio de sesión.
            MustChangePassword = true
        };
        EnsureSucceeded(await users.CreateAsync(admin, password!));
        EnsureSucceeded(await users.AddToRoleAsync(admin, "PlatformAdmin"));

        db.PlatformAccessAudits.Add(new PlatformAccessAudit(Guid.NewGuid(), admin.Id,
            PlatformAccessAction.ProductionAdministratorBootstrap, nameof(ApplicationUser),
            admin.Id.ToString(), null,
            "Se creó la cuenta administrativa inicial del ambiente Production.", null,
            DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(cancellationToken);

        // Se registra el hecho y el identificador, nunca la contraseña ni el correo completo.
        logger.LogWarning("Se creó la cuenta administrativa inicial de Production ({UsuarioId}). " +
                          "Retire ProductionBootstrap__* de las variables tras el primer inicio de sesión.",
            admin.Id);
    }

    private static void Validate(string? email, string? password)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') ||
            email.EndsWith(".demo", StringComparison.OrdinalIgnoreCase) ||
            email.Contains("urabaconecta.demo", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "ProductionBootstrap__AdminEmail debe ser un correo real, nunca uno de demostración.");
        if (password is null || password.Length < MinimumPasswordLength ||
            !password.Any(char.IsUpper) || !password.Any(char.IsLower) ||
            !password.Any(char.IsDigit) || password.All(char.IsLetterOrDigit))
            throw new InvalidOperationException(
                $"ProductionBootstrap__AdminPassword exige al menos {MinimumPasswordLength} caracteres " +
                "con mayúscula, minúscula, dígito y un carácter no alfanumérico.");
        if (StartupGuard.ForbiddenProductionPasswords.Contains(password, StringComparer.Ordinal))
            throw new InvalidOperationException(
                "ProductionBootstrap__AdminPassword es una contraseña de demostración conocida.");
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "ASP.NET Identity rechazó la creación de la cuenta administrativa de Production.");
    }
}
