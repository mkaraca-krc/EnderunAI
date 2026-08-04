namespace EnderunAI.Api.Models;

public enum ProgressPaymentPaymentType
{
    /// <summary>Nakit/havale — vadesiz.</summary>
    Cash = 0,

    /// <summary>Vadeli çek — çek defterine alınan çek olarak girer.</summary>
    Cheque = 1
}

/// <summary>
/// Hakedişin tahsil edilecek tutarının ödeme parçası.
///
/// NATURA'da üç parça var: nakit + 90 gün vadeli çek + 120 gün vadeli
/// çek. Oranlar projeden gelir ve hakedişte düzeltilebilir; toplam %100
/// olmadan hakediş kesinleştirilemez.
///
/// Kesinleştirmede çek parçaları için çek defterine alınan çek kaydı
/// açılır; vadeleri nakit akışına kendiliğinden düşer.
/// </summary>
public sealed class ProgressPaymentPaymentPlan : BaseEntity
{
    public Guid ProgressPaymentId { get; set; }
    public ProgressPayment ProgressPayment { get; set; } = null!;

    public int LineNumber { get; set; }

    public ProgressPaymentPaymentType PaymentType { get; set; }

    /// <summary>Tahsil edilecek tutarın bu parçaya düşen oranı (%).</summary>
    public decimal Rate { get; set; }

    public decimal Amount { get; set; }

    /// <summary>Çek parçasında vade gün sayısı (90, 120 vb.).</summary>
    public int? MaturityDays { get; set; }

    /// <summary>Hesaplanan vade tarihi: hakediş tarihi + vade günü.</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>Kesinleştirmede oluşturulan çek; nakit parçada boş.</summary>
    public Guid? ChequeId { get; set; }
    public Cheque? Cheque { get; set; }

    public string? Description { get; set; }
}
