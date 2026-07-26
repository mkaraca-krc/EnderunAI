namespace EnderunAI.Api.Models;

public enum RfqInvitationStatus
{
    Pending = 0,
    Sent = 1,
    Delivered = 2,
    Opened = 3,
    OfferSubmitted = 4,
    Expired = 5,
    Failed = 6,
    Revoked = 7
}

public sealed class RfqSupplierInvitation : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid RfqId { get; set; }
    public Guid SupplierCurrentAccountId { get; set; }
    public string RecipientEmail { get; set; } = string.Empty;
    public string RecipientName { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public bool SingleUse { get; set; }
    public RfqInvitationStatus Status { get; set; } = RfqInvitationStatus.Pending;
    public DateTime? SentAtUtc { get; set; }
    public DateTime? OpenedAtUtc { get; set; }
    public DateTime? OfferSubmittedAtUtc { get; set; }
    public DateTime? LastReminderAtUtc { get; set; }
    public int ReminderCount { get; set; }
    public int SendAttemptCount { get; set; }
    public string? LastError { get; set; }
}

public sealed class RfqInvitationEvent : BaseEntity
{
    public Guid InvitationId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime EventDateUtc { get; set; } = DateTime.UtcNow;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Detail { get; set; }
}
