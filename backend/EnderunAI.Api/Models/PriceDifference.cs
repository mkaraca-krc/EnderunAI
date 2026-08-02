namespace EnderunAI.Api.Models;

public enum PriceDifferenceCalculationType
{
    PublicContractFormula = 0,
    FixedRate = 1,
    Manual = 2
}

public sealed class PriceDifferenceProfile : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string ProfileName { get; set; } = string.Empty;
    public PriceDifferenceCalculationType CalculationType { get; set; }
        = PriceDifferenceCalculationType.PublicContractFormula;

    public int BaseYear { get; set; }
    public int BaseMonth { get; set; }
    public string CurrencyCode { get; set; } = "TRY";

    public bool IsDefault { get; set; }
    public bool IsVatIncluded { get; set; }

    public string? FormulaName { get; set; }
    public string? Notes { get; set; }

    public PriceDifferenceCoefficient? Coefficient { get; set; }
}

public sealed class PriceDifferenceCoefficient : BaseEntity
{
    public Guid PriceDifferenceProfileId { get; set; }
    public PriceDifferenceProfile PriceDifferenceProfile { get; set; } = null!;

    public decimal A { get; set; }
    public decimal B1 { get; set; }
    public decimal B2 { get; set; }
    public decimal B3 { get; set; }
    public decimal B4 { get; set; }
    public decimal B5 { get; set; }
    public decimal C { get; set; }
}

public sealed class PriceDifferenceIndexPeriod : BaseEntity
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string? PeriodLabel { get; set; }

    public decimal LaborIndex { get; set; }
    public decimal FuelIndex { get; set; }
    public decimal MaterialIndex { get; set; }
    public decimal MachineryIndex { get; set; }
    public decimal CementIndex { get; set; }
    public decimal OtherIndex { get; set; }
    public decimal CopperIndex { get; set; }
    public decimal SteelIndex { get; set; }
    public decimal ElectricityIndex { get; set; }
    public decimal UsdRate { get; set; }
    public decimal EurRate { get; set; }

    public string? Notes { get; set; }
}
