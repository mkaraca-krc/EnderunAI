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
        AppDbContext db,
        ILogger<WorkHourAccessMiddleware> logger)
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

        if (evaluation.Karar == MesaiKarari.Belirlenemedi)
        {
            // MESAİ/1 — BU KAPI KARAR VEREMİYORSA ÇEKİLİR, UYDURMAZ.
            //
            // Kullanıcı satırı okunamadı. Bu "mesai dışı" DEĞİL; eskiden
            // öyle etiketleniyordu ve satırı silinmiş kullanıcı "mesainiz
            // bitti" cevabı alıyor, denetime sahte bir mesai reddi
            // yazılıyordu (ölçüldü, 2026-09-10).
            //
            // Soru hesabın VARLIĞI ve bunun sahibi sıradaki izin ara
            // katmanı: aynı satırı okuyup yoksa 401 HesapPasif veriyor ve
            // günlüğe doğru sebeple yazıyor. Bu çekilmenin güvenliği o
            // katmana BAĞLI — `OkunamayanSatirMesaiDisiDegildirTests`
            // bağımlılığı kilitliyor: hesap katmanı geçirirse test 200
            // görür ve kırmızı yanar.
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
                // Bu ara katman portal yolunu zaten muaf tutuyor ama
                // maskeleme yine de uygulanıyor: muafiyet listesi bir
                // gün değişirse koruma kendiliğinden devrede olsun.
                path = SensitivePathMasker.Mask(path)
            }),
            IpAddress = context.Connection.RemoteIpAddress?.ToString(),
            UserAgent = context.Request.Headers.UserAgent.ToString(),
            OccurredAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(context.RequestAborted);

        // GÜNLÜK/1: bu 401 sessizdi — denetim tablosuna yazılıyordu ama
        // günlüğe DÜŞMÜYORDU; "neden çıkarıldı?" sorusu günlükten
        // cevaplanamıyordu (ölçüldü, `ErisimGunluguMesaiTests`).
        ErisimGunlugu.Ret(logger, ErisimRetSebebi.MesaiDisi, userId, context.Request.Path.Value);

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "Mesai saatiniz sona erdiği için oturumunuz kapatıldı.",
            outsideWorkHours = true
        });
    }
}
