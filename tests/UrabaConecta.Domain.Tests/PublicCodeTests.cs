using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using UrabaConecta.Infrastructure.Security;

namespace UrabaConecta.Domain.Tests;

public sealed class PublicCodeTests
{
    [Fact]
    public void Code_has_128_bits_is_url_safe_and_hash_is_deterministic()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["URABACONECTA_TRACKING_HMAC_KEY"] = "unit-test-key-with-at-least-32-bytes-123" }).Build();
        var service = new PublicCodeService(configuration, new TestEnvironment());
        var first = service.Generate();
        var second = service.Generate();
        Assert.Equal(22, first.PlainText.Length);
        Assert.Matches("^[A-Za-z0-9_-]{22}$", first.PlainText);
        Assert.NotEqual(first.PlainText, second.PlainText);
        Assert.NotEqual(first.PlainText, first.Hash);
        Assert.Equal(first.Hash, service.Hash(first.PlainText));
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
