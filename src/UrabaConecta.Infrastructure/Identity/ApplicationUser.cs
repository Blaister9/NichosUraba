using Microsoft.AspNetCore.Identity;

namespace UrabaConecta.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
}
