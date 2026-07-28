using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security.CurrentUser;

namespace EnderunAI.Api.Security;

public interface ISecurityAuditService
{
    Task WriteAsync(
        string action,
        string entityType,
        Guid? entityId,
        object? details = null,
        CancellationToken cancellationToken = default);
}

public sealed class SecurityAuditService(
    AppDbContext db,
    ICurrentUserService currentUser,
    IHttpContextAccessor httpContextAccessor) : ISecurityAuditService
{
    public async Task WriteAsync(
        string action,
        string entityType,
        Guid? entityId,
        object? details = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("İşlem adı boş olamaz.", nameof(action));
        if (string.IsNullOrWhiteSpace(entityType))
            throw new ArgumentException("Varlık türü boş olamaz.", nameof(entityType));

        var request = httpContextAccessor.HttpContext?.Request;
        db.SecurityAuditEvents.Add(new SecurityAuditEvent
        {
            ActorUserId = currentUser.UserId,
            ActorUsername = currentUser.Username,
            Action = action.Trim(),
            EntityType = entityType.Trim(),
            EntityId = entityId,
            DetailsJson = details is null
                ? null
                : JsonSerializer.Serialize(details),
            IpAddress = httpContextAccessor.HttpContext?
                .Connection.RemoteIpAddress?.ToString(),
            UserAgent = Truncate(
                request?.Headers.UserAgent.ToString(),
                500),
            OccurredAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= maxLength
                ? value
                : value[..maxLength];
}
