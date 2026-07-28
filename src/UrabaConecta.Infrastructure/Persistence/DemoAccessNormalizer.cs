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
/// Normalización operativa de las cinco cuentas comerciales de Demo.
/// La presencia del secreto actúa como activador temporal; fuera de Demo siempre falla.
/// </summary>
public static class DemoAccessNormalizer
{
    private const string SecretKey = "DemoAccess:SharedPassword";

    private static readonly AccountDefinition[] Accounts =
    [
        new(DevelopmentSeeder.PlatformAdminEmail, "Administración UrabáConecta", "PlatformAdmin", null),
        new(DevelopmentSeeder.PartnerOperatorEmail, "Socia demostrativa", "PartnerOperator", null),
        new(DevelopmentSeeder.BellaOwnerEmail, "Propietaria Bella", "BusinessOwner",
            DevelopmentSeeder.BellaBusinessId),
        new(DevelopmentSeeder.CorteOwnerEmail, "Propietario El Corte", "BusinessOwner",
            DevelopmentSeeder.CorteBusinessId),
        new(DevelopmentSeeder.SazonOwnerEmail, "Propietario Sazón Local", "BusinessOwner",
            DevelopmentSeeder.SazonBusinessId)
    ];

    public static async Task NormalizeDemoAccessAsync(this IServiceProvider services,
        IHostEnvironment environment, IConfiguration? configuration = null,
        CancellationToken cancellationToken = default)
    {
        configuration ??= services.GetRequiredService<IConfiguration>();
        var password = configuration[SecretKey];
        if (string.IsNullOrWhiteSpace(password)) return;
        if (!environment.IsEnvironment("Demo"))
            throw new InvalidOperationException("DemoAccess__SharedPassword solo puede utilizarse en Demo.");

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        foreach (var role in Accounts.Select(x => x.Role).Distinct())
            if (!await roles.RoleExistsAsync(role))
                EnsureSucceeded(await roles.CreateAsync(new IdentityRole<Guid>(role)));

        var expectedBusinesses = Accounts.Where(x => x.BusinessId.HasValue)
            .Select(x => x.BusinessId!.Value).ToHashSet();
        var existingBusinesses = await db.Businesses.Where(x => expectedBusinesses.Contains(x.Id))
            .Select(x => x.Id).ToListAsync(cancellationToken);
        if (existingBusinesses.Count != expectedBusinesses.Count)
            throw new InvalidOperationException(
                "No se normalizaron accesos: falta uno de los tres negocios originales de Demo.");

        foreach (var definition in Accounts)
        {
            var user = await users.FindByEmailAsync(definition.Email);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    UserName = definition.Email,
                    Email = definition.Email,
                    EmailConfirmed = true,
                    DisplayName = definition.DisplayName,
                    MustChangePassword = false
                };
                EnsureSucceeded(await users.CreateAsync(user, password));
            }
            else
            {
                var resetToken = await users.GeneratePasswordResetTokenAsync(user);
                EnsureSucceeded(await users.ResetPasswordAsync(user, resetToken, password));
                user.UserName = definition.Email;
                user.Email = definition.Email;
                user.EmailConfirmed = true;
                user.DisplayName = definition.DisplayName;
                user.MustChangePassword = false;
                EnsureSucceeded(await users.UpdateAsync(user));
            }

            var currentRoles = await users.GetRolesAsync(user);
            var unwantedRoles = currentRoles.Where(x =>
                !string.Equals(x, definition.Role, StringComparison.Ordinal)).ToArray();
            if (unwantedRoles.Length > 0)
                EnsureSucceeded(await users.RemoveFromRolesAsync(user, unwantedRoles));
            if (!await users.IsInRoleAsync(user, definition.Role))
                EnsureSucceeded(await users.AddToRoleAsync(user, definition.Role));

            EnsureSucceeded(await users.SetLockoutEndDateAsync(user, null));
            EnsureSucceeded(await users.ResetAccessFailedCountAsync(user));
            EnsureSucceeded(await users.UpdateSecurityStampAsync(user));

            var memberships = await db.BusinessMemberships
                .Where(x => x.UserId == user.Id).ToListAsync(cancellationToken);
            foreach (var membership in memberships.Where(x =>
                         !definition.BusinessId.HasValue || x.BusinessId != definition.BusinessId.Value))
                if (membership.IsActive)
                    membership.Deactivate(DateTimeOffset.UtcNow, membership.Version);

            if (definition.BusinessId is { } businessId)
            {
                var membership = memberships.SingleOrDefault(x => x.BusinessId == businessId);
                if (membership is null)
                    db.BusinessMemberships.Add(new BusinessMembership(
                        Guid.NewGuid(), businessId, user.Id, MembershipRole.Owner));
                else
                {
                    if (!membership.IsActive)
                        membership.Activate(DateTimeOffset.UtcNow, membership.Version);
                    if (membership.Role != MembershipRole.Owner)
                        membership.GrantOwnership(DateTimeOffset.UtcNow, membership.Version);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        db.ChangeTracker.Clear();
        foreach (var definition in Accounts)
        {
            var user = await users.FindByEmailAsync(definition.Email)
                ?? throw new InvalidOperationException("La verificación de accesos Demo no encontró las cinco cuentas.");
            if (!user.EmailConfirmed || user.MustChangePassword || user.LockoutEnd is not null ||
                !await users.CheckPasswordAsync(user, password) ||
                !await users.IsInRoleAsync(user, definition.Role))
                throw new InvalidOperationException(
                    "La verificación de accesos Demo detectó una cuenta sin normalizar.");

            var activeMemberships = await db.BusinessMemberships.AsNoTracking()
                .Where(x => x.UserId == user.Id && x.IsActive).ToListAsync(cancellationToken);
            if (definition.BusinessId is null && activeMemberships.Count != 0 ||
                definition.BusinessId is { } expectedId &&
                (activeMemberships.Count != 1 || activeMemberships[0].BusinessId != expectedId ||
                 activeMemberships[0].Role != MembershipRole.Owner))
                throw new InvalidOperationException(
                    "La verificación de accesos Demo detectó una membresía incorrecta.");
        }

        scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DemoAccessNormalizer")
            .LogWarning("Se normalizaron y verificaron cinco accesos comerciales del ambiente Demo.");
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "ASP.NET Identity rechazó la normalización de accesos Demo.");
    }

    private sealed record AccountDefinition(
        string Email, string DisplayName, string Role, Guid? BusinessId);
}
