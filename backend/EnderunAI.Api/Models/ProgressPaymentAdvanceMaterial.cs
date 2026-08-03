namespace EnderunAI.Api.Models;

/// <summary>
/// İhzarat: sahaya gelmiş ama henüz monte edilmemiş malzeme.
///
/// İmalat gibi tam bedelle değil, sözleşmedeki oranla bedellendirilir
/// (ör. malzeme bedelinin %80'i). Malzeme monte edilip imalata
/// dönüştüğünde ihzarat mahsup edilir: açık ihzarat azalır, imalat
/// artar, toplam değişmez. Aynı iş iki kez tahsil edilemez.
///
/// Kayıt, açıldığı hakedişe bağlıdır ama bakiyesi proje boyunca
/// yaşar — mahsuplar sonraki hakedişlerde yapılır.
/// </summary>
public sealed class ProgressPaymentAdvanceMaterial : BaseEntity
{
    public Guid ProgressPaymentId { get; set; }
    public ProgressPayment ProgressPayment { get; set; } = null!;

    public int LineNumber { get; set; }

    /// <summary>
    /// İhzaratın karşılık geldiği poz. Mahsup önerisi bu kodla
    /// eşleştirilerek yapılır.
    /// </summary>
    public string PositionCode { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Bedellendirme oranı (%). Sözleşmede ihzarata ödenecek oran;
    /// genelde %100'ün altındadır.
    /// </summary>
    public decimal ValuationRate { get; set; } = 100m;

    /// <summary>Miktar × birim fiyat × bedellendirme oranı.</summary>
    public decimal Amount { get; set; }

    /// <summary>Bugüne kadar mahsup edilen toplam.</summary>
    public decimal OffsetAmount { get; set; }

    /// <summary>Açık bakiye = tutar − mahsup. Negatife düşemez.</summary>
    public decimal OpenAmount => Amount - OffsetAmount;

    public string? Notes { get; set; }

    public ICollection<ProgressPaymentAdvanceMaterialOffset> Offsets { get; set; }
        = new List<ProgressPaymentAdvanceMaterialOffset>();
}

/// <summary>
/// Bir ihzarat kaleminin bir hakedişte mahsup edilen kısmı.
/// Açık bakiyeyi aşan mahsup serviste reddedilir; çift tahsilatın
/// engellenmesi arayüze bırakılmaz.
/// </summary>
public sealed class ProgressPaymentAdvanceMaterialOffset : BaseEntity
{
    public Guid AdvanceMaterialId { get; set; }
    public ProgressPaymentAdvanceMaterial AdvanceMaterial { get; set; } = null!;

    /// <summary>Mahsubun yapıldığı hakediş.</summary>
    public Guid ProgressPaymentId { get; set; }
    public ProgressPayment ProgressPayment { get; set; } = null!;

    public decimal Amount { get; set; }

    public string? Notes { get; set; }
}
