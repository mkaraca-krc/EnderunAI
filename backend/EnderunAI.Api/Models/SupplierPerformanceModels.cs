namespace EnderunAI.Api.Models;

public enum SupplierQualityEventType
{
    Accepted = 0,
    ConditionalAcceptance = 1,
    Rejected = 2,
    Returned = 3,
    WarrantyIssue = 4
}

public enum SupplierRiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public sealed class SupplierPerformanceSnapshot : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid SupplierCurrentAccountId { get; set; }
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public decimal DeliveryScore { get; set; }
    public decimal QualityScore { get; set; }
    public decimal PriceScore { get; set; }
    public decimal TechnicalScore { get; set; }
    public decimal FinancialScore { get; set; }
    public decimal CommunicationScore { get; set; }
    public decimal OverallScore { get; set; }
    public SupplierRiskLevel RiskLevel { get; set; }
    public int TotalOrderCount { get; set; }
    public int CompletedOrderCount { get; set; }
    public int LateOrderCount { get; set; }
    public decimal TotalOrderAmountTry { get; set; }
    public decimal OnTimeDeliveryRate { get; set; }
    public decimal ReturnRate { get; set; }
    public string? Notes { get; set; }
}

public sealed class SupplierQualityRecord : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid SupplierCurrentAccountId { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public Guid? GoodsReceiptId { get; set; }
    public Guid? MaterialId { get; set; }
    public SupplierQualityEventType EventType { get; set; }
    public decimal Quantity { get; set; }
    public decimal ImpactScore { get; set; }
    public string? Description { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime EventDateUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SupplierManualEvaluation : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid SupplierCurrentAccountId { get; set; }
    public decimal CommunicationScore { get; set; }
    public decimal FinancialScore { get; set; }
    public decimal QualityScore { get; set; }
    public decimal TechnicalScore { get; set; }
    public string? Comment { get; set; }
    public Guid? EvaluatedByUserId { get; set; }
    public string EvaluatedByName { get; set; } = string.Empty;
    public DateTime EvaluationDateUtc { get; set; } = DateTime.UtcNow;
}
