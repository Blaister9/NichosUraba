using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using UrabaConecta.Application;

namespace UrabaConecta.Infrastructure.Security;

/// <summary>
/// Tokens de invitación de 256 bits. Se entrega el valor en claro una sola vez y se persiste
/// únicamente su HMAC, de modo que leer la base de datos no permite reconstruir ningún enlace.
/// </summary>
public sealed class InvitationTokenService : IInvitationTokenService
{
    private readonly byte[] _key;

    public InvitationTokenService(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["URABACONECTA_INVITATION_HMAC_KEY"]
            ?? configuration["URABACONECTA_TRACKING_HMAC_KEY"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException("Falta URABACONECTA_INVITATION_HMAC_KEY.");
            configured = "development-only-insecure-invitation-key-change-me-2026";
        }
        _key = Encoding.UTF8.GetBytes(configured);
    }

    public (string PlainText, string Hash) Generate()
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public string Hash(string plainText)
        => Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(plainText))).ToLowerInvariant();

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
