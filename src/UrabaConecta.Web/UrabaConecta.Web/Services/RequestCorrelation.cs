using System.Security.Claims;

namespace UrabaConecta.Web.Services;

/// <summary>
/// Identidad del despliegue, resuelta una vez y adjuntada a cada registro. Permite responder
/// "¿qué versión hizo esto?" sin cruzar a mano la hora del incidente con el historial de Railway.
/// </summary>
public sealed record DeploymentIdentity(string Environment, string Version, string Commit);

/// <summary>
/// Correlación de peticiones. Cada registro sale con el mismo identificador que devuelve la
/// respuesta, de modo que un socio puede reportar un fallo citando la cabecera y el operador
/// encuentra la traza completa.
///
/// Nada de lo que se agrega aquí es dato personal: identificadores, ambiente, versión y el
/// identificador del usuario cuando lo hay. Ni correos, ni teléfonos, ni contraseñas, ni la
/// cadena de conexión.
/// </summary>
public sealed class RequestCorrelationMiddleware(RequestDelegate next, DeploymentIdentity deployment,
    ILogger<RequestCorrelationMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";
    private const int MaximumLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Sanitize(context.Request.Headers[HeaderName].ToString())
                            ?? context.TraceIdentifier;
        context.Response.Headers[HeaderName] = correlationId;

        var scope = new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["RequestId"] = context.TraceIdentifier,
            ["Environment"] = deployment.Environment,
            ["AppVersion"] = deployment.Version,
            ["Commit"] = deployment.Commit
        };
        // El actor sólo cuando existe, y por identificador: el correo es dato personal y no
        // pertenece al registro de operación.
        if (context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } actor)
            scope["ActorUserId"] = actor;
        // El negocio en curso se toma de la ruta, que es donde vive en toda la API privada.
        if (context.Request.RouteValues.TryGetValue("businessId", out var businessId) && businessId is not null)
            scope["BusinessId"] = businessId.ToString();

        using (logger.BeginScope(scope))
            await next(context);
    }

    /// <summary>
    /// La cabecera llega del cliente, así que se acota antes de que llegue al registro: sin
    /// saltos de línea no se puede falsificar una entrada, y sin longitud no se puede inundar.
    /// </summary>
    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > MaximumLength) trimmed = trimmed[..MaximumLength];
        return trimmed.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? trimmed : null;
    }
}
