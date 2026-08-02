using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;

namespace EnderunAI.Api.Security;

/// <summary>
/// Aktif bir oturumun mesai penceresi kapandığı anda sonraki istekte
/// kesilmesini sağlar (giriş anındaki kontrol tek başına yeterli değil,
/// zira oturum saatler sürebilir). İşveren portalı ve login/erişim talebi/
/// mesai durumu uçları kasıtlı olarak muaf — bu uçlar zaten anonim veya
/// durumun kendisini raporlamak için var.
/// </summary>
public sealed class WorkHourAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IWorkHourAccessService workHourAccessService,
        AppDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
        if (!path.StartsWith("/api/") ||
            path.StartsWith("/api/portal") ||
            path.StartsWith("/api/health") ||
            path.StartsWith("/api/swagger") ||
            path.StartsWith("/api/auth/login") ||
            path.StartsWith("/api/auth/access-requests") ||
            path.StartsWith("/api/auth/work-hours-status"))
        {
            await next(context);
            return;
        }

        var userIdValue =
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
            context.User.FindFirstValue("sub");

        if (!Guid.TryParse(userIdValue, out var userId))
        {
            await next(context);
            return;
        }

        var evaluation = await workHourAccessService.EvaluateAsync(userId, context.RequestAborted);
        if (evaluation.IsAllowed)
        {
            await next(context);
            return;
        }

        db.SecurityAuditEvents.Add(new SecurityAuditEvent
        {
            ActorUserId = userId,
            ActorUsername = context.User.FindFirstValue(ClaimTypes.Name) ?? context.User.FindFirstValue("username"),
            Action = "WorkHoursSessionRejected",
            EntityType = "WorkHourAccess",
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                summary = "Aktif oturum mesai penceresi kapandığı için kesildi.",
                path
            }),
            IpAddress = context.Connection.RemoteIpAddress?.ToString(),
            UserAgent = context.Request.Headers.UserAgent.ToString(),
            OccurredAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(context.RequestAborted);

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "Mesai saatiniz sona erdiği için oturumunuz kapatıldı.",
            outsideWorkHours = true
        });
    }
}
