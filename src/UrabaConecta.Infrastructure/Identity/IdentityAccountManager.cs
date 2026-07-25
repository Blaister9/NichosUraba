using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using UrabaConecta.Application;
using UrabaConecta.Contracts;

namespace UrabaConecta.Infrastructure.Identity;

public sealed class IdentityAccountManager(UserManager<ApplicationUser> users, IHostEnvironment environment)
    : IIdentityAccountManager
{
    public bool DevelopmentAccountCreationEnabled => environment.IsDevelopment();

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

    private static IdentityAccount ToAccount(ApplicationUser user)
        => new(user.Id, user.Email ?? "", string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.Email?.Split('@')[0] ?? "Cuenta" : user.DisplayName);

    private static string CreateTemporaryPassword()
    {
        var random = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
            .Replace('+', 'x').Replace('/', 'Y').TrimEnd('=');
        return $"U{random}a1!";
    }
}
