using System.Text.Json;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PurchaseOrderEntity = EnderunAI.Api.Models.PurchaseOrder.PurchaseOrder;

namespace EnderunAI.Api.Data.Interceptors;

public sealed class AuditSaveChangesInterceptor(
    ICurrentUserService currentUserService,
    IHttpContextAccessor httpContextAccessor)
    : SaveChangesInterceptor
{
    private static readonly HashSet<Type> AuditedEntityTypes =
    [
        typeof(AppUser),
        typeof(UserRole),
        typeof(Personnel),
        typeof(Project),
        typeof(ProjectCostTransaction),
        typeof(PurchaseOrderEntity),
        typeof(PurchaseRequest),
        typeof(AccountingVoucher),
        typeof(EmployerPortalLink),
        typeof(RolePermission),
        typeof(UserPermissionOverride),
        typeof(UserDataScope),
        typeof(RoleWorkHourWindow),
        typeof(AccessRequest),
        typeof(TemporaryAccessGrant),
        typeof(ProjectDocument)
    ];

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyAuditInformation(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditInformation(eventData.Context);

        return base.SavingChangesAsync(
            eventData,
            result,
            cancellationToken);
    }

    private void ApplyAuditInformation(DbContext? context)
    {
        if (context is null)
            return;

        var now = DateTime.UtcNow;
        var userId = currentUserService.UserId;

        RecordSecurityAuditEvents(context, userId);

        foreach (var entry in context.ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = now;
                    entry.Entity.CreatedByUserId = userId;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAtUtc = now;
                    entry.Entity.UpdatedByUserId = userId;

                    entry.Property(x => x.CreatedAtUtc).IsModified = false;
                    entry.Property(x => x.CreatedByUserId).IsModified = false;

                    if (entry.Entity.IsDeleted)
                    {
                        entry.Entity.DeletedAtUtc ??= now;
                        entry.Entity.DeletedByUserId ??= userId;
                        entry.Entity.IsActive = false;
                    }

                    break;

                case EntityState.Deleted:
                    entry.State = EntityState.Modified;

                    entry.Entity.IsDeleted = true;
                    entry.Entity.IsActive = false;
                    entry.Entity.DeletedAtUtc = now;
                    entry.Entity.DeletedByUserId = userId;
                    entry.Entity.UpdatedAtUtc = now;
                    entry.Entity.UpdatedByUserId = userId;

                    entry.Property(x => x.CreatedAtUtc).IsModified = false;
                    entry.Property(x => x.CreatedByUserId).IsModified = false;
                    break;
            }
        }
    }

    private void RecordSecurityAuditEvents(DbContext context, Guid? userId)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var ipAddress = httpContext?.Connection.RemoteIpAddress?.ToString();
        var userAgent = httpContext?.Request.Headers.UserAgent.ToString();
        var username = currentUserService.Username;

        var entries = context.ChangeTracker.Entries().ToList();

        foreach (var entry in entries)
        {
            if (!AuditedEntityTypes.Contains(entry.Entity.GetType()))
                continue;

            var action = entry.State switch
            {
                EntityState.Added => "Created",
                EntityState.Modified => "Updated",
                EntityState.Deleted => "Deleted",
                _ => null
            };

            if (action is null)
                continue;

            var (entityId, summary) = Describe(entry.Entity);

            context.Set<SecurityAuditEvent>().Add(new SecurityAuditEvent
            {
                ActorUserId = userId,
                ActorUsername = username,
                Action = action,
                EntityType = entry.Entity.GetType().Name,
                EntityId = entityId,
                DetailsJson = summary is null
                    ? null
                    : JsonSerializer.Serialize(new { summary }),
                IpAddress = ipAddress,
                UserAgent = userAgent,
                OccurredAtUtc = DateTime.UtcNow
            });
        }
    }

    private static (Guid? Id, string? Summary) Describe(object entity) => entity switch
    {
        AppUser u => (u.Id, u.Username),
        UserRole ur => ((Guid?)null, $"UserId={ur.UserId} RoleId={ur.RoleId}"),
        Personnel p => (p.Id, $"{p.EmployeeNumber} {p.FullName}".Trim()),
        Project pr => (pr.Id, $"{pr.Code} {pr.Name}".Trim()),
        ProjectCostTransaction ct => (ct.Id, ct.Description),
        PurchaseOrderEntity po => (po.Id, po.OrderNumber),
        PurchaseRequest req => (req.Id, req.RequestNumber),
        AccountingVoucher v => (v.Id, v.VoucherNumber),
        EmployerPortalLink link => (link.Id, link.EmployerEmail ?? link.Token),
        RolePermission rp => ((Guid?)null, $"RoleId={rp.RoleId} PermissionId={rp.PermissionId}"),
        UserPermissionOverride upo => (upo.Id, $"UserId={upo.UserId} PermissionId={upo.PermissionId} Effect={upo.Effect}"),
        UserDataScope uds => (uds.Id, $"UserId={uds.UserId} ScopeType={uds.ScopeType}"),
        RoleWorkHourWindow w => ((Guid?)null, $"RoleId={w.RoleId} Day={w.DayOfWeek} {w.StartTime}-{w.EndTime}"),
        AccessRequest ar => (ar.Id, $"UserId={ar.UserId} Status={ar.Status}"),
        TemporaryAccessGrant g => (g.Id, $"UserId={g.UserId} ExpiresAtUtc={g.ExpiresAtUtc:o}"),
        ProjectDocument pd => (pd.Id, $"ProjectId={pd.ProjectId} {pd.Folder}/{pd.FileName} v{pd.VersionNumber}"),
        _ => (null, null)
    };
}
