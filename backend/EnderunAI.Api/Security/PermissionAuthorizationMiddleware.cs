using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace EnderunAI.Api.Security;

public sealed class PermissionAuthorizationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IUserAuthorizationService userAuthorizationService)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var roleNames = context.User
            .FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Concat(context.User.FindAll("role").Select(claim => claim.Value))
            .Concat(context.User.FindAll("roles").Select(claim => claim.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var userIdValue =
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
            context.User.FindFirstValue("sub");

        IReadOnlyCollection<string> permissions = [];

        if (Guid.TryParse(userIdValue, out var userId))
        {
            var authorization = await userAuthorizationService.GetAsync(
                userId,
                context.RequestAborted);

            if (authorization is null || !authorization.IsActive)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "Kullanıcı hesabı pasif veya bulunamadı."
                });
                return;
            }

            roleNames = authorization.RoleNames.ToArray();
            permissions = authorization.Permissions;
        }

        var requiredPermission = ResolveRequiredPermission(context.Request);
        if (requiredPermission is null)
        {
            await next(context);
            return;
        }

        if (roleNames.Contains("Admin", StringComparer.OrdinalIgnoreCase) ||
            permissions.Contains(requiredPermission, StringComparer.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

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
