using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using UrabaConecta.Contracts;

namespace UrabaConecta.Web.Services;

public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ApiException api) return false;
        context.Response.StatusCode = api.StatusCode;
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = api.StatusCode, Title = api.Message,
                Extensions = { ["code"] = api.Code }
            }
        });
        return true;
    }
}

/// <summary>
/// Última barrera para fallos operativos inesperados. No registra el cuerpo ni valores de entrada:
/// método, ruta y correlación bastan para encontrar la traza sin copiar datos personales al log.
/// </summary>
public sealed class OperationalExceptionHandler(IProblemDetailsService problemDetails,
    ILogger<OperationalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
            return false;

        var correlationId = context.Response.Headers[RequestCorrelationMiddleware.HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(correlationId)) correlationId = context.TraceIdentifier;
        logger.LogError(new EventId(5000, "UnhandledOperationalFailure"), exception,
            "Fallo operativo no controlado en {Method} {Path}; correlación {CorrelationId}.",
            context.Request.Method, context.Request.Path.Value, correlationId);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "No fue posible completar la operación.",
                Extensions = { ["correlationId"] = correlationId }
            }
        });
        return true;
    }
}
