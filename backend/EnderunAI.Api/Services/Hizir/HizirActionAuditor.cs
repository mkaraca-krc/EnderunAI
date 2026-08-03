using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.AspNetCore.Http;

namespace EnderunAI.Api.Services.Hizir;

public interface IHizirActionAuditor
{
    /// <summary>
    /// Hızır üzerinden yapılan her eylem adımını denetim kaydına yazar:
    /// kim, hangi eylemi, ne zaman, onaylı mı.
    /// </summary>
    Task RecordAsync(
        Guid userId,
        string? username,
        string step,
        HizirPendingAction action,
        CancellationToken cancellationToken = default);

    /// <summary>Onay gerektirmeyen (güvenli kademe) eylemler için.</summary>
    Task RecordSafeAsync(
        Guid userId,
        string? username,
        string actionName,
        string summary,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Hızır eylemlerinin denetim kaydı. Mevcut SecurityAuditEvent
/// altyapısına yazar; Action alanı "Hizir." önekiyle başlar, böylece
/// asistan üzerinden yapılan işlemler elle yapılanlardan ayırt edilir.
/// </summary>
public sealed class HizirActionAuditor(
    AppDbContext db,
    IHttpContextAccessor httpContextAccessor) : IHizirActionAuditor
{
    public async Task RecordAsync(
        Guid userId,
        string? username,
        string step,
        HizirPendingAction action,
        CancellationToken cancellationToken = default)
    {
        Write(userId, username, $"Hizir.{action.ActionName}.{step}", action.Id, new
        {
            action.ActionName,
            action.Summary,
            step,
            requiresApproval = true,
            approved = action.Status == HizirPendingActionStatus.Executed,
            status = action.Status.ToString(),
            action.ResultMessage
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordSafeAsync(
        Guid userId,
        string? username,
        string actionName,
        string summary,
        CancellationToken cancellationToken = default)
    {
        Write(userId, username, $"Hizir.{actionName}.calistirildi", null, new
        {
            actionName,
            summary,
            requiresApproval = false,
            approved = (bool?)null
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private void Write(
        Guid userId, string? username, string action, Guid? entityId, object details)
    {
        var http = httpContextAccessor.HttpContext;

        db.SecurityAuditEvents.Add(new SecurityAuditEvent
        {
            ActorUserId = userId,
            ActorUsername = username,
            Action = action,
            EntityType = "HizirAction",
            EntityId = entityId,
            DetailsJson = JsonSerializer.Serialize(details),
            IpAddress = http?.Connection.RemoteIpAddress?.ToString(),
            UserAgent = http?.Request.Headers.UserAgent.ToString(),
            OccurredAtUtc = DateTime.UtcNow
        });
    }
}
