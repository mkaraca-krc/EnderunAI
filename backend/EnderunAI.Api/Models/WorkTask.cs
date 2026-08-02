namespace EnderunAI.Api.Models;

public enum WorkTaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

public enum WorkTaskStatus
{
    Draft = 0,
    Open = 1,
    InProgress = 2,
    Waiting = 3,
    Completed = 4,
    Cancelled = 5
}

public sealed class WorkTask : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid? ProjectId { get; set; }

    public string TaskNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public WorkTaskPriority Priority { get; set; } = WorkTaskPriority.Normal;
    public WorkTaskStatus Status { get; set; } = WorkTaskStatus.Open;

    public Guid? AssignedToUserId { get; set; }
    public Guid? AssignedByUserId { get; set; }

    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public string? CompletionNote { get; set; }

    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }

    public string? SourceModule { get; set; }
    public Guid? SourceEntityId { get; set; }
    public string? SourceEventCode { get; set; }
    public string? Tags { get; set; }
}
