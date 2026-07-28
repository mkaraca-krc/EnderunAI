using EnderunAI.Api.Data;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Security;

public sealed class PermissionAuthorizationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        AppDbContext db,
        ICurrentUserService currentUser)
    {
        if (!currentUser.IsAuthenticated)
        {
            await next(context);
            return;
        }

        if (currentUser.UserId is not Guid userId)
        {
            await WriteUnauthorizedAsync(
                context,
                "Oturum kullanıcısı doğrulanamadı.");
            return;
        }

        var userSnapshot = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new
            {
                user.IsActive,
                RoleNames = user.UserRoles
                    .Select(userRole => userRole.Role.Name)
                    .ToArray()
            })
            .SingleOrDefaultAsync(context.RequestAborted);

        if (userSnapshot is null || !userSnapshot.IsActive)
        {
            await WriteUnauthorizedAsync(
                context,
                "Kullanıcı hesabı pasif veya bulunamadı.");
            return;
        }

        var requiredPermissions =
            ResolveRequiredPermissions(context);

        if (requiredPermissions.Count == 0)
        {
            await next(context);
            return;
        }

        var roleNames = userSnapshot.RoleNames;
        var effectivePermissions =
            PermissionCatalog.Resolve(roleNames);

        if (roleNames.Contains(
                "Admin",
                StringComparer.OrdinalIgnoreCase) ||
            requiredPermissions.All(effectivePermissions.Contains))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode =
            StatusCodes.Status403Forbidden;

        await context.Response.WriteAsJsonAsync(new
        {
            message = "Bu işlem için yetkiniz bulunmuyor.",
            requiredPermission =
                requiredPermissions.FirstOrDefault(),
            requiredPermissions
        });
    }

    private static async Task WriteUnauthorizedAsync(
        HttpContext context,
        string message)
    {
        context.Response.StatusCode =
            StatusCodes.Status401Unauthorized;

        await context.Response.WriteAsJsonAsync(new
        {
            message
        });
    }

    private static IReadOnlyCollection<string>
        ResolveRequiredPermissions(HttpContext context)
    {
        var explicitPermissions = context
            .GetEndpoint()?
            .Metadata
            .GetOrderedMetadata<RequirePermissionAttribute>()
            .Select(attribute => attribute.Permission)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        if (explicitPermissions.Length > 0)
            return explicitPermissions;

        var pathPermission =
            ResolvePathPermission(context.Request);

        return pathPermission is null
            ? Array.Empty<string>()
            : [pathPermission];
    }

    private static string? ResolvePathPermission(
        HttpRequest request)
    {
        var path =
            request.Path.Value?.ToLowerInvariant() ??
            string.Empty;

        if (!path.StartsWith("/api/") ||
            path.StartsWith("/api/auth") ||
            path.StartsWith("/api/health") ||
            path.StartsWith("/api/swagger"))
        {
            return null;
        }

        var isRead =
            HttpMethods.IsGet(request.Method) ||
            HttpMethods.IsHead(request.Method) ||
            HttpMethods.IsOptions(request.Method);

        var isApproval =
            path.Contains("/approve") ||
            path.Contains("/approval") ||
            path.Contains("/onay");

        if (path.StartsWith("/api/user-management"))
            return PermissionCatalog.Keys.SystemUsersManage;

        if (ContainsAny(
                path,
                "payroll",
                "bordro",
                "salary",
                "ucret",
                "ücret",
                "advance",
                "avans"))
        {
            return isRead
                ? PermissionCatalog.Keys.PayrollView
                : PermissionCatalog.Keys.PayrollManage;
        }

        if (ContainsAny(
                path,
                "attendance",
                "puantaj",
                "leave",
                "izin",
                "overtime",
                "fazla-mesai"))
        {
            return isRead
                ? PermissionCatalog.Keys.AttendanceView
                : PermissionCatalog.Keys.AttendanceManage;
        }

        if (ContainsAny(
                path,
                "/api/personnel",
                "/api/hr",
                "recruitment",
                "career",
                "training",
                "certificate",
                "competency",
                "performance",
                "disciplinary",
                "workforce",
                "asset"))
        {
            return isRead
                ? PermissionCatalog.Keys.PersonnelView
                : PermissionCatalog.Keys.PersonnelManage;
        }

        if (ContainsAny(
                path,
                "hakedis",
                "hakediş",
                "price-adjustment",
                "fiyat-farki",
                "metraj"))
        {
            if (isApproval)
                return PermissionCatalog.Keys.HakedisApprove;

            return isRead
                ? PermissionCatalog.Keys.HakedisView
                : PermissionCatalog.Keys.HakedisManage;
        }

        if (ContainsAny(
                path,
                "finance",
                "finans",
                "payment",
                "collection",
                "cash",
                "bank"))
        {
            if (isApproval)
                return PermissionCatalog.Keys.FinanceApprove;

            return isRead
                ? PermissionCatalog.Keys.FinanceView
                : PermissionCatalog.Keys.FinanceManage;
        }

        if (ContainsAny(
                path,
                "accounting",
                "muhasebe",
                "ledger",
                "journal",
                "chart-of-accounts"))
        {
            return isRead
                ? PermissionCatalog.Keys.AccountingView
                : PermissionCatalog.Keys.AccountingManage;
        }

        if (ContainsAny(
                path,
                "purchase",
                "purchasing",
                "rfq",
                "supplier",
                "satin-alma",
                "satinalma"))
        {
            if (isApproval)
            {
                return
                    PermissionCatalog.Keys.PurchasingApprove;
            }

            return isRead
                ? PermissionCatalog.Keys.PurchasingView
                : PermissionCatalog.Keys.PurchasingManage;
        }

        if (ContainsAny(
                path,
                "warehouse",
                "inventory",
                "stock",
                "goods-receipt",
                "mal-kabul",
                "depo"))
        {
            return isRead
                ? PermissionCatalog.Keys.InventoryView
                : PermissionCatalog.Keys.InventoryManage;
        }

        if (ContainsAny(
                path,
                "engineering",
                "muhendislik",
                "position",
                "recipe",
                "recete",
                "kesif"))
        {
            return isRead
                ? PermissionCatalog.Keys.EngineeringView
                : PermissionCatalog.Keys.EngineeringManage;
        }

        if (ContainsAny(
                path,
                "secretariat",
                "sekreterya",
                "document",
                "cargo",
                "visitor",
                "meeting",
                "appointment",
                "phone-note"))
        {
            return isRead
                ? PermissionCatalog.Keys.SecretariatView
                : PermissionCatalog.Keys.SecretariatManage;
        }

        if (ContainsAny(path, "task", "gorev", "görev"))
        {
            return isRead
                ? PermissionCatalog.Keys.TasksView
                : PermissionCatalog.Keys.TasksManage;
        }

        if (ContainsAny(path, "report", "rapor"))
            return PermissionCatalog.Keys.ReportsView;

        if (ContainsAny(path, "ai-", "/api/ai", "analysis"))
            return PermissionCatalog.Keys.AiUse;

        if (ContainsAny(
                path,
                "/api/companies",
                "/api/branches",
                "/api/current-accounts"))
        {
            return isRead
                ? PermissionCatalog.Keys.CompaniesView
                : PermissionCatalog.Keys.CompaniesManage;
        }

        if (ContainsAny(
                path,
                "/api/projects",
                "/api/project"))
        {
            return isRead
                ? PermissionCatalog.Keys.ProjectsView
                : PermissionCatalog.Keys.ProjectsManage;
        }

        return null;
    }

    private static bool ContainsAny(
        string path,
        params string[] values) =>
        values.Any(path.Contains);
}
