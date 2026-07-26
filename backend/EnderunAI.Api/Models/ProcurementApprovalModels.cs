namespace EnderunAI.Api.Models;

public enum ProcurementApprovalDocumentType
{
    PurchaseRequest = 0,
    PurchaseOrder = 1,
    RfqAward = 2
}

public enum ApprovalFlowMode
{
    Sequential = 0,
    Parallel = 1
}

public enum ApprovalInstanceStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    RevisionRequested = 3,
    Cancelled = 4
}

public enum ApprovalStepStatus
{
    Waiting = 0,
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    RevisionRequested = 4,
    Skipped = 5
}

public enum ApprovalActionType
{
    Submitted = 0,
    Approved = 1,
    Rejected = 2,
    RevisionRequested = 3,
    Cancelled = 4
}

public sealed class ProcurementApprovalRule : BaseEntity
{
    public Guid CompanyId { get; set; }
    public ProcurementApprovalDocumentType DocumentType { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal MinimumAmount { get; set; }
    public decimal? MaximumAmount { get; set; }
    public string CurrencyCode { get; set; } = "TRY";
    public ApprovalFlowMode FlowMode { get; set; } = ApprovalFlowMode.Sequential;
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; }
    public ICollection<ProcurementApprovalRuleStep> Steps { get; set; } = new List<ProcurementApprovalRuleStep>();
}

public sealed class ProcurementApprovalRuleStep : BaseEntity
{
    public Guid RuleId { get; set; }
    public ProcurementApprovalRule Rule { get; set; } = null!;
    public int SequenceNo { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public bool IsRequired { get; set; } = true;
}

public sealed class ProcurementApprovalInstance : BaseEntity
{
    public Guid CompanyId { get; set; }
    public ProcurementApprovalDocumentType DocumentType { get; set; }
    public Guid DocumentId { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "TRY";
    public Guid RuleId { get; set; }
    public ApprovalFlowMode FlowMode { get; set; }
    public ApprovalInstanceStatus Status { get; set; } = ApprovalInstanceStatus.Pending;
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public ICollection<ProcurementApprovalInstanceStep> Steps { get; set; } = new List<ProcurementApprovalInstanceStep>();
    public ICollection<ProcurementApprovalHistory> History { get; set; } = new List<ProcurementApprovalHistory>();
}

public sealed class ProcurementApprovalInstanceStep : BaseEntity
{
    public Guid InstanceId { get; set; }
    public ProcurementApprovalInstance Instance { get; set; } = null!;
    public int SequenceNo { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public bool IsRequired { get; set; } = true;
    public ApprovalStepStatus Status { get; set; } = ApprovalStepStatus.Waiting;
    public Guid? ActionByUserId { get; set; }
    public string? ActionByName { get; set; }
    public DateTime? ActionAtUtc { get; set; }
    public string? Comment { get; set; }
}

public sealed class ProcurementApprovalHistory : BaseEntity
{
    public Guid InstanceId { get; set; }
    public ProcurementApprovalInstance Instance { get; set; } = null!;
    public Guid? StepId { get; set; }
    public ApprovalActionType ActionType { get; set; }
    public Guid? ActionByUserId { get; set; }
    public string? ActionByName { get; set; }
    public string? RoleName { get; set; }
    public DateTime ActionAtUtc { get; set; } = DateTime.UtcNow;
    public string? IpAddress { get; set; }
    public string? Comment { get; set; }
}
