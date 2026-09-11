using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace EnderunAI.Api.Security;

public sealed class PermissionAuthorizationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IUserAuthorizationService userAuthorizationService,
        ILogger<PermissionAuthorizationMiddleware> logger)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        // ROL/1: burada jetondaki rol talepleri toplanıyordu. Kullanıcı
        // kimliği çözülünce veritabanındaki rollerle eziliyordu ve hiçbir
        // kararda kullanılmıyordu — ölü bir kopya okuyucusu. Kaldırıldı:
        // yarın biri onu "rol burada var" diye kullanırsa eskiyen jeton rolü
        // yeniden karar verirdi. Rol yalnız taze anlık görüntüden okunur.
        UserAuthorizationSnapshot? authorization = null;

        var userIdValue =
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
            context.User.FindFirstValue("sub");

        IReadOnlyCollection<string> permissions = [];

        if (Guid.TryParse(userIdValue, out var userId))
        {
            authorization = await userAuthorizationService.GetAsync(
                userId,
                context.RequestAborted);

            if (authorization is null || !authorization.IsActive)
            {
                // GÜNLÜK/1: bu karar sessizdi ve 9 Eylül'de teşhis
                // edilemedi. Sekiz sebepten biri olarak yazılıyor.
                ErisimGunlugu.Ret(
                    logger,
                    ErisimRetSebebi.HesapPasif,
                    userId,
                    context.Request.Path.Value);

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "Kullanıcı hesabı pasif veya bulunamadı."
                });
                return;
            }

            permissions = authorization.Permissions;

            // JETON/1: bu isteğin geri kalanı (ICurrentUserService) izni ve
            // rolü BURADAN okur, jetondan değil. Yalnız bu isteğin ömrü.
            IstekYetkisi.Koy(context, authorization);
        }

        // ── ROL/1: ROL KAPISI — izinle AYNI geçiş noktası, AYNI taze anlık görüntü ──
        //
        // `[Authorize(Roles = …)]` rolü jetondan okuyordu (12 saat eski) ve
        // reddi sessizdi. `[RolGerekli]` burada değerlendirilir. Anlık
        // görüntü yoksa (kimlik çözülemedi) rol listesi BOŞ sayılır →
        // KAPALI düşer; jetondaki role geri dönüş YOK.
        var rolGerekli = context.GetEndpoint()?.Metadata.GetMetadata<RolGerekliAttribute>();
        if (rolGerekli is not null)
        {
            var roller = authorization?.RoleNames ?? [];
            if (!rolGerekli.Roller.Any(rol => roller.Contains(rol, StringComparer.OrdinalIgnoreCase)))
            {
                ErisimGunlugu.Ret(
                    logger,
                    ErisimRetSebebi.RolYok,
                    Guid.TryParse(userIdValue, out var kid0) ? kid0 : null,
                    context.Request.Path.Value);

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "Bu işlem için yetkiniz bulunmuyor."
                });
                return;
            }
        }

        /*
         * ADMIN KISAYOLU KALDIRILDI (YETKİ/3 · YT4, 2026-09-09).
         *
         * Buradaki blok `roleNames.Contains("Admin")` görünce BÜTÜN
         * izin kontrollerini atlıyordu. Etkisi ölçüldü (YT1, çağırarak):
         * Admin rolündeki bir kullanıcı `payment.plan.approve` iznine
         * SAHİP OLMADAN ödeme planını onaylayabiliyordu.
         *
         * BU BİR FAZLADAN YETKİ DEĞİL, YAZILI BİR KARARIN ÇİĞNENMESİYDİ:
         * `RoleCatalog.SensitiveKeys` "ödeme onayı Admin'e GİTMEZ (ÖP/1a
         * · İ2)" diyor. Katalog bir şey diyordu, middleware başka bir şey
         * yapıyordu ve kazanan middleware'di.
         *
         * KALDIRMANIN ETKİSİ ÖLÇÜLDÜ, VARSAYILMADI (2026-09-09, canlı):
         *   · Admin'in eksik olduğu izin TAM OLARAK BİR: payment.plan.approve
         *     (permissions 147, Admin grant 146)
         *   · O izni isteyen uç TAM OLARAK BİR: OdemePlanlariController:180
         *   · Admin rolündeki tek kullanıcı `mehmet` ve o izni GENEL MÜDÜR
         *     rolünden zaten taşıyor
         *   · Admin kullanıcısında Deny kaydı YOK
         * Yani hiçbir kullanıcının fiilî erişimi değişmiyor; değişen tek
         * şey, İ2'nin nihayet yürürlüğe girmesi.
         *
         * BU BLOK NEDEN "SAVUNMA" DEĞİL: bir kapı değil, kapıların
         * ETRAFINDAN GEÇEN yoldu. Kaldırılması yüzeyi daraltıyor.
         *
         * İKİ DAVRANIŞ BİLEREK DEĞİŞTİ:
         *   1. Admin'e konulan bir Deny kaydı artık ISIRIR (bugün Deny
         *      yok; YETKİ/1'in Admin'e de uygulanması).
         *   2. `sub` çözümlenemeyen bir jetonda roller yalnız talep
         *      listesinden okunuyor ve izin kümesi boş kalıyor; böyle bir
         *      jeton eskiden kısayoldan geçiyordu, artık 403 alır.
         * İkisi de sıkılaştırma.
         */

        // Asıl kaynak: action/controller üzerindeki [RequirePermission]
        // attribute'ları (birden fazlası varsa herhangi biri yeterli).
        // Henüz attribute eklenmemiş uçlar için path-heuristiği geçici bir
        // güvenlik ağı olarak devam ediyor (Faz 2 tamamlandıkça daralacak).
        var attributePermissions = context.GetEndpoint()?
            .Metadata
            .GetOrderedMetadata<RequirePermissionAttribute>()
            .Select(attribute => attribute.Permission)
            .ToArray();

        if (attributePermissions is { Length: > 0 })
        {
            if (attributePermissions.Any(permission =>
                    permissions.Contains(permission, StringComparer.OrdinalIgnoreCase)))
            {
                await next(context);
                return;
            }

            ErisimGunlugu.Ret(
                logger,
                ErisimRetSebebi.IzinYok,
                Guid.TryParse(userIdValue, out var kid1) ? kid1 : null,
                context.Request.Path.Value);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "Bu işlem için yetkiniz bulunmuyor.",
                requiredPermission = attributePermissions[0]
            });
            return;
        }

        var requiredPermission = ResolveRequiredPermission(context.Request);
        if (requiredPermission is null)
        {
            await next(context);
            return;
        }

        if (permissions.Contains(requiredPermission, StringComparer.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        ErisimGunlugu.Ret(
            logger,
            ErisimRetSebebi.IzinYokYoldan,
            Guid.TryParse(userIdValue, out var kid2) ? kid2 : null,
            context.Request.Path.Value);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "Bu işlem için yetkiniz bulunmuyor.",
            requiredPermission
        });
    }

    private static string? ResolveRequiredPermission(HttpRequest request)
    {
        var path = request.Path.Value?.ToLowerInvariant() ?? string.Empty;
        if (!path.StartsWith("/api/") ||
            path.StartsWith("/api/auth") ||
            path.StartsWith("/api/health") ||
            path.StartsWith("/api/swagger") ||
            path.StartsWith("/api/portal"))
        {
            return null;
        }

        var isRead = HttpMethods.IsGet(request.Method) ||
                     HttpMethods.IsHead(request.Method) ||
                     HttpMethods.IsOptions(request.Method);
        var isApproval = path.Contains("/approve") ||
                         path.Contains("/approval") ||
                         path.Contains("/onay");

        if (path.StartsWith("/api/user-management"))
            return PermissionCatalog.Keys.SystemUsersManage;

        if (ContainsAny(path, "payroll", "bordro", "salary", "ucret", "ücret", "advance", "avans"))
            return isRead
                ? PermissionCatalog.Keys.PayrollView
                : PermissionCatalog.Keys.PayrollManage;

        if (ContainsAny(path, "attendance", "puantaj", "leave", "izin", "overtime", "fazla-mesai"))
            return isRead
                ? PermissionCatalog.Keys.AttendanceView
                : PermissionCatalog.Keys.AttendanceManage;

        if (ContainsAny(path, "/api/personnel", "/api/hr", "recruitment", "career", "training",
                "certificate", "competency", "performance", "disciplinary", "workforce", "asset"))
            return isRead
                ? PermissionCatalog.Keys.PersonnelView
                : PermissionCatalog.Keys.PersonnelManage;

        if (ContainsAny(path, "hakedis", "hakediş", "price-adjustment", "fiyat-farki", "metraj"))
        {
            if (isApproval)
                return PermissionCatalog.Keys.HakedisApprove;
            return isRead
                ? PermissionCatalog.Keys.HakedisView
                : PermissionCatalog.Keys.HakedisManage;
        }

        if (ContainsAny(path, "finance", "finans", "payment", "collection", "cash", "bank"))
        {
            if (isApproval)
                return PermissionCatalog.Keys.FinanceApprove;
            return isRead
                ? PermissionCatalog.Keys.FinanceView
                : PermissionCatalog.Keys.FinanceManage;
        }

        if (ContainsAny(path, "accounting", "muhasebe", "ledger", "journal", "chart-of-accounts"))
            return isRead
                ? PermissionCatalog.Keys.AccountingView
                : PermissionCatalog.Keys.AccountingManage;

        if (ContainsAny(path, "purchase", "purchasing", "rfq", "supplier", "satin-alma", "satinalma"))
        {
            if (isApproval)
                return PermissionCatalog.Keys.PurchasingApprove;
            return isRead
                ? PermissionCatalog.Keys.PurchasingView
                : PermissionCatalog.Keys.PurchasingManage;
        }

        if (ContainsAny(path, "warehouse", "inventory", "stock", "goods-receipt", "mal-kabul", "depo"))
            return isRead
                ? PermissionCatalog.Keys.InventoryView
                : PermissionCatalog.Keys.InventoryManage;

        if (ContainsAny(path, "engineering", "muhendislik", "position", "recipe", "recete", "kesif"))
            return isRead
                ? PermissionCatalog.Keys.EngineeringView
                : PermissionCatalog.Keys.EngineeringManage;

        if (ContainsAny(path, "secretariat", "sekreterya", "document", "cargo", "visitor",
                "meeting", "appointment", "phone-note"))
            return isRead
                ? PermissionCatalog.Keys.SecretariatView
                : PermissionCatalog.Keys.SecretariatManage;

        if (ContainsAny(path, "task", "gorev", "görev"))
            return isRead
                ? PermissionCatalog.Keys.TasksView
                : PermissionCatalog.Keys.TasksManage;

        if (ContainsAny(path, "report", "rapor"))
            return PermissionCatalog.Keys.ReportsView;

        if (ContainsAny(path, "ai-", "/api/ai", "analysis"))
            return PermissionCatalog.Keys.AiUse;

        if (ContainsAny(path, "/api/companies", "/api/branches", "/api/current-accounts"))
            return isRead
                ? PermissionCatalog.Keys.CompaniesView
                : PermissionCatalog.Keys.CompaniesManage;

        if (ContainsAny(path, "/api/projects", "/api/project"))
            return isRead
                ? PermissionCatalog.Keys.ProjectsView
                : PermissionCatalog.Keys.ProjectsManage;

        return null;
    }

    private static bool ContainsAny(string path, params string[] values) =>
        values.Any(path.Contains);
}
