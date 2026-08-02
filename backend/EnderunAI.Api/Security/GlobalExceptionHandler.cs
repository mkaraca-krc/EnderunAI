using Microsoft.AspNetCore.Diagnostics;

namespace EnderunAI.Api.Security;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = httpContext.TraceIdentifier;

        logger.LogError(
            exception,
            "İşlenmeyen hata. TraceId={TraceId} Path={Path} Method={Method}",
            traceId,
            httpContext.Request.Path,
            httpContext.Request.Method);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/json; charset=utf-8";

        await httpContext.Response.WriteAsJsonAsync(new
        {
            message = "Beklenmeyen bir hata oluştu. Lütfen tekrar deneyin veya sorun devam ederse sistem yöneticisiyle iletişime geçin.",
            traceId
        }, cancellationToken);

        return true;
    }
}
