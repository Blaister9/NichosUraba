using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UrabaConecta.Application;
using UrabaConecta.Contracts;

namespace UrabaConecta.Infrastructure.Identity;

public sealed class InvitationIdentityGateway(
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole<Guid>> roles) : IInvitationIdentityGateway
{
    public async Task<IdentityAccount?> FindByExactEmailAsync(string email, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(email.Trim());
        return user is null ? null : ToAccount(user);
    }

    /// <summary>
    /// Crea la cuenta sin contraseña. Hasta que la persona acepte la invitación no puede iniciar sesión,
    /// porque Identity rechaza el inicio de sesión de un usuario sin hash de contraseña.
    /// </summary>
    public async Task<IdentityAccount> CreatePendingAsync(string displayName, string email, string role,
        CancellationToken cancellationToken)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = normalized, Email = normalized,
            EmailConfirmed = true, DisplayName = displayName.Trim(), MustChangePassword = false
        };
        var result = await users.CreateAsync(user);
        if (!result.Succeeded) throw Failed(result);
        await EnsureRoleAsync(user.Id, role, cancellationToken);
        return ToAccount(user);
    }

    public async Task EnsureRoleAsync(Guid userId, string role, CancellationToken cancellationToken)
    {
        var user = await RequireAsync(userId);
        if (!await roles.RoleExistsAsync(role))
        {
            var created = await roles.CreateAsync(new IdentityRole<Guid>(role));
            if (!created.Succeeded) throw Failed(created);
        }
        if (await users.IsInRoleAsync(user, role)) return;
        var result = await users.AddToRoleAsync(user, role);
        if (!result.Succeeded) throw Failed(result);
    }

    public async Task RemoveRoleAsync(Guid userId, string role, CancellationToken cancellationToken)
    {
        var user = await RequireAsync(userId);
        if (!await users.IsInRoleAsync(user, role)) return;
        var result = await users.RemoveFromRoleAsync(user, role);
        if (!result.Succeeded) throw Failed(result);
    }

    public async Task SetPasswordAndActivateAsync(Guid userId, string password, CancellationToken cancellationToken)
    {
        var user = await RequireAsync(userId);
        // Quitar y volver a fijar el hash evita tener que conocer la contraseña anterior.
        if (await users.HasPasswordAsync(user))
        {
            var removed = await users.RemovePasswordAsync(user);
            if (!removed.Succeeded) throw Failed(removed);
        }
        var added = await users.AddPasswordAsync(user, password);
        if (!added.Succeeded) throw Failed(added);
        user.MustChangePassword = false;
        user.EmailConfirmed = true;
        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded) throw Failed(updated);
        await users.ResetAccessFailedCountAsync(user);
        // Invalida cualquier cookie emitida antes de definir la contraseña.
        await users.UpdateSecurityStampAsync(user);
    }

    public async Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await RequireAsync(userId);
        var result = await users.UpdateSecurityStampAsync(user);
        if (!result.Succeeded) throw Failed(result);
    }

    public async Task<IReadOnlyList<PlatformAccountDto>> ListByRoleAsync(string role,
        CancellationToken cancellationToken)
    {
        var members = await users.GetUsersInRoleAsync(role);
        return members.OrderBy(x => x.Email)
            .Select(x => new PlatformAccountDto(x.Id, x.Email ?? "", x.DisplayName,
                x.LockoutEnd is null || x.LockoutEnd <= DateTimeOffset.UtcNow, x.LockoutEnd))
            .ToList();
    }

    private async Task<ApplicationUser> RequireAsync(Guid userId)
        => await users.Users.SingleOrDefaultAsync(x => x.Id == userId)
            ?? throw new ApiException("ACCOUNT_NOT_FOUND", "No encontramos esa cuenta.", 404);

    private static ApiException Failed(IdentityResult result)
        => new("IDENTITY_OPERATION_FAILED", string.Join(" ", result.Errors.Select(x => x.Description)), 400);

    private static IdentityAccount ToAccount(ApplicationUser user)
        => new(user.Id, user.Email ?? "", string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.Email?.Split('@')[0] ?? "Cuenta" : user.DisplayName, user.MustChangePassword);
}
