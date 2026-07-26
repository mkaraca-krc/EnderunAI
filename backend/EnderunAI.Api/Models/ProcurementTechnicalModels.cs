namespace EnderunAI.Api.Models;

public enum TechnicalCriterionType
{
    Brand = 0,
    Model = 1,
    Standard = 2,
    Certificate = 3,
    Text = 4,
    NumericMinimum = 5,
    NumericMaximum = 6,
    ExactValue = 7
}

public enum TechnicalComplianceStatus
{
    NotEvaluated = 0,
    Compliant = 1,
    ConditionallyCompliant = 2,
    NonCompliant = 3
}

public sealed class TechnicalSpecification : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? RfqId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<TechnicalCriterion> Criteria { get; set; } = new List<TechnicalCriterion>();
}

public sealed class TechnicalCriterion : BaseEntity
{
    public Guid TechnicalSpecificationId { get; set; }
    public TechnicalSpecification TechnicalSpecification { get; set; } = null!;
    public Guid? RfqItemId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TechnicalCriterionType Type { get; set; }
    public string? ExpectedValue { get; set; }
    public decimal? NumericValue { get; set; }
    public string? Unit { get; set; }
    public bool IsMandatory { get; set; } = true;
    public decimal Weight { get; set; } = 1m;
}

public sealed class SupplierOfferTechnicalResponse : BaseEntity
{
    public Guid SupplierOfferId { get; set; }
    public Guid SupplierOfferItemId { get; set; }
    public Guid TechnicalCriterionId { get; set; }
    public string? OfferedValue { get; set; }
    public decimal? OfferedNumericValue { get; set; }
    public bool? IsProvided { get; set; }
    public string? EvidenceReference { get; set; }
    public TechnicalComplianceStatus Status { get; set; } = TechnicalComplianceStatus.NotEvaluated;
    public decimal Score { get; set; }
    public string? EvaluationNote { get; set; }
    public Guid? EvaluatedByUserId { get; set; }
    public string? EvaluatedByName { get; set; }
    public DateTime? EvaluatedAtUtc { get; set; }
}
