using EnderunAI.Api.Models;

namespace EnderunAI.Api.Models.HumanResources;

public enum HrApprovalStatus
{
    Draft = 0,
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Paid = 4,
    Cancelled = 5
}

public enum HrLeaveType
{
    Annual = 0,
    Excuse = 1,
    Sick = 2,
    Unpaid = 3,
    Maternity = 4,
    Paternity = 5,
    Marriage = 6,
    Bereavement = 7,
    Other = 8
}

public enum PayrollStatus
{
    Draft = 0,
    Calculated = 1,
    Approved = 2,
    Paid = 3
}

public sealed class HrLeaveRequest : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid PersonnelId { get; set; }
    public Guid? ProjectId { get; set; }
    public HrLeaveType LeaveType { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal TotalDays { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? DocumentPath { get; set; }
    public HrApprovalStatus Status { get; set; } = HrApprovalStatus.Pending;
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string? ApprovalNote { get; set; }
}

public sealed class HrOvertimeRequest : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid PersonnelId { get; set; }
    public Guid? ProjectId { get; set; }
    public DateTime WorkDate { get; set; }
    public decimal RequestedHours { get; set; }
    public decimal ApprovedHours { get; set; }
    public bool IsSundayWork { get; set; }
    public bool IsPublicHolidayWork { get; set; }
    public bool IsNightWork { get; set; }
    public string Reason { get; set; } = string.Empty;
    public HrApprovalStatus Status { get; set; } = HrApprovalStatus.Pending;
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string? ApprovalNote { get; set; }
}

public sealed class HrAdvanceRequest : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid PersonnelId { get; set; }
    public Guid? ProjectId { get; set; }
    public DateTime RequestDate { get; set; }
    public decimal RequestedAmount { get; set; }
    public decimal ApprovedAmount { get; set; }
    public string CurrencyCode { get; set; } = "TRY";
    public int DeductionInstallmentCount { get; set; } = 1;
    public DateTime? FirstDeductionDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public HrApprovalStatus Status { get; set; } = HrApprovalStatus.Pending;
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public string? PaymentReference { get; set; }
    public string? ApprovalNote { get; set; }
}

public sealed class HrPayrollRecord : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid PersonnelId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal GrossSalary { get; set; }
    public decimal NormalWorkAmount { get; set; }
    public decimal OvertimeAmount { get; set; }
    public decimal SundayWorkAmount { get; set; }
    public decimal PublicHolidayAmount { get; set; }
    public decimal BonusAmount { get; set; }
    public decimal MealAmount { get; set; }
    public decimal TravelAmount { get; set; }
    public decimal OtherEarningAmount { get; set; }
    public decimal CompensationAmount { get; set; }
    public decimal TotalEarnings { get; set; }
    public decimal SgkEmployeeDeduction { get; set; }
    public decimal IncomeTaxDeduction { get; set; }
    public decimal StampTaxDeduction { get; set; }

    /// <summary>İşçi payı işsizlik sigortası primi.</summary>
    public decimal UnemploymentEmployeeDeduction { get; set; }

    /// <summary>Prime esas kazanç (taban/tavan uygulanmış hali).</summary>
    public decimal SgkBase { get; set; }

    /// <summary>Bu ayın gelir vergisi matrahı.</summary>
    public decimal IncomeTaxBase { get; set; }

    /// <summary>
    /// Yıl başından bu ay dahil kümülatif gelir vergisi matrahı. Bir
    /// sonraki ayın dilim hesabı buradan devam eder.
    /// </summary>
    public decimal CumulativeIncomeTaxBase { get; set; }

    /// <summary>Asgari ücret gelir vergisi istisnası tutarı.</summary>
    public decimal IncomeTaxExemption { get; set; }

    /// <summary>Asgari ücret damga vergisi istisnası tutarı.</summary>
    public decimal StampTaxExemption { get; set; }

    /// <summary>İşveren payı SGK primi (indirim uygulanmış hali).</summary>
    public decimal SgkEmployerAmount { get; set; }

    /// <summary>İşveren payı işsizlik sigortası primi.</summary>
    public decimal UnemploymentEmployerAmount { get; set; }

    /// <summary>Brüt + işveren payı primler — şirkete toplam maliyet.</summary>
    public decimal TotalEmployerCost { get; set; }
    public decimal AdvanceDeduction { get; set; }
    public decimal OtherDeductionAmount { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal OfficialNetPayableAmount { get; set; }
    public decimal ActualPayableAmount { get; set; }
    public decimal NetPayableAmount { get; set; }
    public string CurrencyCode { get; set; } = "TRY";
    public PayrollStatus Status { get; set; } = PayrollStatus.Draft;
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public string? PaymentReference { get; set; }
    public string? Description { get; set; }
}
