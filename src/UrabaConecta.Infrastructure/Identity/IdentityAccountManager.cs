using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using UrabaConecta.Application;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.Infrastructure.Identity;

public sealed class IdentityAccountManager(UserManager<ApplicationUser> users, IHostEnvironment environment,
    AppDbContext db)
    : IIdentityAccountManager
{
    public bool DevelopmentAccountCreationEnabled => environment.IsDevelopment() || environment.IsEnvironment("Demo");

    public async Task<IdentityAccount?> FindByExactEmailAsync(string email, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(email.Trim());
        return user is null ? null : ToAccount(user);
    }

    public async Task<CreatedIdentityAccount> CreateDevelopmentAsync(string displayName, string email,
        CancellationToken cancellationToken)
    {
        if (!DevelopmentAccountCreationEnabled)
            throw new ApiException("NOT_FOUND", "La función no está disponible.", 404);
        var password = CreateTemporaryPassword();
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = normalizedEmail, Email = normalizedEmail,
            EmailConfirmed = true, DisplayName = displayName.Trim()
        };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new ApiException("ACCOUNT_CREATION_FAILED",
                string.Join(" ", result.Errors.Select(x => x.Description)), 400);
        var role = await users.AddToRoleAsync(user, "BusinessWorker");
        if (!role.Succeeded)
            throw new ApiException("ACCOUNT_CREATION_FAILED",
                string.Join(" ", role.Errors.Select(x => x.Description)), 400);
        return new(ToAccount(user), password);
    }

    public async Task<CreatedIdentityAccount> CreatePilotAsync(string displayName, string email,
        CancellationToken cancellationToken)
    {
        if (!DevelopmentAccountCreationEnabled)
            throw new ApiException("PILOT_ACCOUNT_DISABLED", "La creación directa no está disponible.", 404);
        if (await users.FindByEmailAsync(email.Trim()) is not null)
            throw new ApiException("ACCOUNT_EXISTS", "Ya existe una cuenta con ese correo.", 409);
        var password = CreateTemporaryPassword();
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = normalizedEmail, Email = normalizedEmail,
            EmailConfirmed = true, DisplayName = displayName.Trim(), MustChangePassword = true
        };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new ApiException("ACCOUNT_CREATION_FAILED",
                string.Join(" ", result.Errors.Select(x => x.Description)), 400);
        var role = await users.AddToRoleAsync(user, "BusinessOwner");
        if (!role.Succeeded)
            throw new ApiException("ACCOUNT_CREATION_FAILED",
                string.Join(" ", role.Errors.Select(x => x.Description)), 400);
        return new(ToAccount(user), password);
    }

    public async Task SynchronizeMembershipRolesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString())
            ?? throw new ApiException("ACCOUNT_NOT_FOUND", "No encontramos la cuenta.", 404);
        var activeRoles = await db.BusinessMemberships.AsNoTracking().Where(x => x.UserId == userId && x.IsActive)
            .Select(x => x.Role).Distinct().ToListAsync(cancellationToken);
        await SetDerivedRole(user, "BusinessOwner", activeRoles.Contains(MembershipRole.Owner));
        await SetDerivedRole(user, "BusinessWorker", activeRoles.Contains(MembershipRole.Worker));
    }

    private async Task SetDerivedRole(ApplicationUser user, string role, bool shouldHave)
    {
        var hasRole = await users.IsInRoleAsync(user, role);
        if (hasRole == shouldHave) return;
        var result = shouldHave ? await users.AddToRoleAsync(user, role) : await users.RemoveFromRoleAsync(user, role);
        if (!result.Succeeded)
            throw new ApiException("ROLE_SYNC_FAILED",
                string.Join(" ", result.Errors.Select(x => x.Description)), 409);
    }

    private static IdentityAccount ToAccount(ApplicationUser user)
        => new(user.Id, user.Email ?? "", string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.Email?.Split('@')[0] ?? "Cuenta" : user.DisplayName, user.MustChangePassword);

    private static string CreateTemporaryPassword()
    {
        var random = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
            .Replace('+', 'x').Replace('/', 'Y').TrimEnd('=');
        return $"U{random}a1!";
    }
}
