using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using UrabaConecta.Application;

namespace UrabaConecta.Infrastructure.Security;

public sealed class PublicCodeService : IPublicCodeService
{
    private readonly byte[] _key;

    public PublicCodeService(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["URABACONECTA_TRACKING_HMAC_KEY"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException("Falta URABACONECTA_TRACKING_HMAC_KEY.");
            configured = "development-only-insecure-hmac-key-change-me-2026";
        }
        _key = Encoding.UTF8.GetBytes(configured);
    }

    public (string PlainText, string Hash, int Version) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        var code = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (code, Hash(code), 1);
    }

    public string Hash(string plainText)
        => Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(plainText))).ToLowerInvariant();
}
