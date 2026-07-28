using UrabaConecta.Web.Services;

namespace UrabaConecta.IntegrationTests;

public sealed class ContentSecurityPolicyTests
{
    [Fact]
    public void Public_object_storage_origin_is_allowed_for_images_only()
    {
        var policy = ContentSecurityPolicyFactory.Create(
            "https://pub-example.r2.dev/some/path");

        Assert.Contains("img-src 'self' data: https://pub-example.r2.dev;", policy);
        Assert.DoesNotContain("connect-src 'self' https://pub-example.r2.dev", policy);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/media")]
    [InlineData("javascript:alert(1)")]
    public void Relative_or_unsafe_values_do_not_expand_the_policy(string? value)
    {
        var policy = ContentSecurityPolicyFactory.Create(value);

        Assert.Contains("img-src 'self' data:;", policy);
        Assert.DoesNotContain(value ?? "never-present", policy);
    }
}
