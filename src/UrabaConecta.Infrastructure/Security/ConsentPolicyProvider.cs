using Microsoft.Extensions.Options;
using UrabaConecta.Application;

namespace UrabaConecta.Infrastructure.Security;

/// <summary>
/// Expone la versión de la política que deben aceptar los formularios públicos.
/// En Production <c>Legal__PolicyVersion</c> es obligatoria (lo verifica <see cref="StartupGuard"/>);
/// en Development y Demo se conserva "pilot-1" para no invalidar los datos ficticios existentes.
/// </summary>
public sealed class ConsentPolicyProvider(IOptions<LegalOptions> legal) : IConsentPolicyProvider
{
    public const string FallbackVersion = "pilot-1";

    public string CurrentVersion => string.IsNullOrWhiteSpace(legal.Value.PolicyVersion)
        ? FallbackVersion
        : legal.Value.PolicyVersion.Trim();
}
