namespace EnderunAI.Api.Models;

public sealed class HrCompensationComponent : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid PersonnelId { get; set; }
    public Guid? ProjectId { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public int ComponentType { get; set; }
    public int CalculationType { get; set; }
    public int PaymentMethod { get; set; }

    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "TRY";

    public DateTime EffectiveStartDate { get; set; }
    public DateTime? EffectiveEndDate { get; set; }

    public bool IsAttendanceBased { get; set; }
    public bool IncludeInPayroll { get; set; }
    public bool IncludeInSgkBase { get; set; }
    public bool IncludeInIncomeTaxBase { get; set; }
    public bool IncludeInStampTaxBase { get; set; }
    public bool IncludeInProjectCost { get; set; }
    public bool IncludeInProgressPaymentCost { get; set; }

    public string? Description { get; set; }
}
