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
