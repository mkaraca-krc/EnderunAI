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
            // YOL MASKELENEREK YAZILIR: portal bağlantısı sırrı
            // yolun kendisinde taşıyor ve ham hâliyle loglanırsa
            // ilk işlenmeyen hatada anahtar günlüğe düşer.
            SensitivePathMasker.Mask(httpContext.Request.Path.Value),
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
