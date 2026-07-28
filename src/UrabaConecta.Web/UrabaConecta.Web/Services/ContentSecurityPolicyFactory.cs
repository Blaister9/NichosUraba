namespace UrabaConecta.Web.Services;

public static class ContentSecurityPolicyFactory
{
    public static string Create(string? publicImageBaseUrl)
    {
        var imageSources = "'self' data:";
        if (Uri.TryCreate(publicImageBaseUrl, UriKind.Absolute, out var uri) &&
            (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
            imageSources += " " + uri.GetLeftPart(UriPartial.Authority);

        return "default-src 'self'; style-src 'self' 'unsafe-inline'; " +
               "script-src 'self' 'wasm-unsafe-eval'; " +
               $"img-src {imageSources}; connect-src 'self' ws: wss:";
    }
}
