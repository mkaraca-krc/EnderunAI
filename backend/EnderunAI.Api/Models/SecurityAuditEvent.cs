namespace EnderunAI.Api.Models;

public sealed class SecurityAuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? ActorUserId { get; set; }
    public string? ActorUsername { get; set; }

    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }

    public string? DetailsJson { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
