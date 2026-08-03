namespace EnderunAI.Api.Models;

public enum ProgressPaymentStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    Posted = 3,
    Cancelled = 4
}

public sealed class ProgressPayment : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid? ProjectMeasurementId { get; set; }

    public string ProgressPaymentNumber { get; set; } = string.Empty;
    public int PeriodNumber { get; set; }

    public DateTime? PeriodStartDate { get; set; }
    public DateTime? PeriodEndDate { get; set; }
    public DateTime ProgressPaymentDate { get; set; }

    public ProgressPaymentStatus Status { get; set; }
        = ProgressPaymentStatus.Draft;

    public string CurrencyCode { get; set; } = "TRY";

    public decimal ContractAmount { get; set; }
    public decimal PreviousAmount { get; set; }
    public decimal CurrentAmount { get; set; }
    public decimal CumulativeAmount { get; set; }

    public decimal PriceDifferenceAmount { get; set; }

    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }

    public int WithholdingNumerator { get; set; }
    public int WithholdingDenominator { get; set; }
    public decimal WithholdingAmount { get; set; }

    public decimal TotalDeductionAmount { get; set; }
    public decimal GrossPayableAmount { get; set; }
    public decimal NetPayableAmount { get; set; }

    public string? Description { get; set; }
    public string? Notes { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? PostedAtUtc { get; set; }

    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    /// <summary>
    /// Kesinleştirmede (Post) otomatik üretilen gelir fişi:
    /// 120 Alıcılar (borç) / 600 Yurtiçi Satışlar + 391 Hesaplanan KDV
    /// (alacak), kesintiler kendi hesaplarına borç.
    /// </summary>
    public Guid? AccountingVoucherId { get; set; }
    public AccountingVoucher? AccountingVoucher { get; set; }

    public ICollection<ProgressPaymentItem> Items { get; set; }
        = new List<ProgressPaymentItem>();

    public ICollection<ProgressPaymentDeduction> Deductions { get; set; }
        = new List<ProgressPaymentDeduction>();
}

public sealed class ProgressPaymentItem : BaseEntity
{
    public Guid ProgressPaymentId { get; set; }
    public ProgressPayment ProgressPayment { get; set; } = null!;

    public Guid? EngineeringPositionId { get; set; }

    public int LineNumber { get; set; }

    public string PositionCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    public decimal ContractQuantity { get; set; }
    public decimal PreviousQuantity { get; set; }
    public decimal CurrentQuantity { get; set; }
    public decimal CumulativeQuantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal PreviousAmount { get; set; }
    public decimal CurrentAmount { get; set; }
    public decimal CumulativeAmount { get; set; }

    public decimal CompletionRate { get; set; }

    public string? MeasurementReference { get; set; }
    public string? Notes { get; set; }
}

public sealed class ProgressPaymentDeduction : BaseEntity
{
    public Guid ProgressPaymentId { get; set; }
    public ProgressPayment ProgressPayment { get; set; } = null!;

    public int LineNumber { get; set; }
    public int DeductionType { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Rate { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal Amount { get; set; }

    public bool IsManualAmount { get; set; }

    /// <summary>
    /// Bu kesintinin borç yazılacağı muhasebe hesabı (ör. teminat için
    /// 126, stopaj için 193). Boşsa şirket finans ayarındaki varsayılan
    /// kesinti hesabı kullanılır.
    /// </summary>
    public Guid? AccountingAccountId { get; set; }
    public AccountingAccount? AccountingAccount { get; set; }

    public string? Notes { get; set; }
}
