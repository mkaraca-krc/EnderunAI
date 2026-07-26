namespace EnderunAI.Api.Models;

public enum BudgetStatus
{
    Draft = 0,
    Active = 1,
    Closed = 2,
    Cancelled = 3
}

public enum BudgetConsumptionType
{
    Commitment = 0,
    Actual = 1,
    Forecast = 2,
    ManualAdjustment = 3
}

public enum BudgetAlertLevel
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public sealed class ProjectBudget : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid ProjectId { get; set; }
    public string BudgetNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = "TRY";
    public decimal BaseAmount { get; set; }
    public decimal WarningThresholdPercent { get; set; } = 90m;
    public decimal CriticalThresholdPercent { get; set; } = 100m;
    public BudgetStatus Status { get; set; } = BudgetStatus.Draft;
    public DateTime EffectiveDateUtc { get; set; } = DateTime.UtcNow;
    public string? Description { get; set; }

    public ICollection<ProjectBudgetItem> Items { get; set; } = new List<ProjectBudgetItem>();
    public ICollection<ProjectBudgetRevision> Revisions { get; set; } = new List<ProjectBudgetRevision>();
}

public sealed class ProjectBudgetItem : BaseEntity
{
    public Guid ProjectBudgetId { get; set; }
    public ProjectBudget ProjectBudget { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? MaterialId { get; set; }
    public string? Category { get; set; }
    public decimal PlannedAmount { get; set; }
    public string CurrencyCode { get; set; } = "TRY";
    public int SequenceNo { get; set; }
}

public sealed class ProjectBudgetRevision : BaseEntity
{
    public Guid ProjectBudgetId { get; set; }
    public ProjectBudget ProjectBudget { get; set; } = null!;
    public int RevisionNumber { get; set; }
    public decimal PreviousAmount { get; set; }
    public decimal RevisedAmount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime RevisionDateUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ProjectBudgetConsumption : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ProjectBudgetId { get; set; }
    public Guid? ProjectBudgetItemId { get; set; }
    public BudgetConsumptionType Type { get; set; }
    public string ReferenceType { get; set; } = string.Empty;
    public Guid? ReferenceId { get; set; }
    public string? ReferenceNumber { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "TRY";
    public decimal ExchangeRate { get; set; } = 1m;
    public DateTime ConsumptionDateUtc { get; set; } = DateTime.UtcNow;
    public string? Description { get; set; }
}

public sealed class ProjectBudgetAlert : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ProjectBudgetId { get; set; }
    public Guid? ProjectBudgetItemId { get; set; }
    public BudgetAlertLevel Level { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public decimal BudgetAmount { get; set; }
    public decimal UsedAmount { get; set; }
    public decimal ProposedAmount { get; set; }
    public decimal VarianceAmount { get; set; }
    public bool IsResolved { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}
