namespace EnderunAI.Api.Models;

/// <summary>
/// Hakediş kesinti türleri.
///
/// Sayısal değerler kalıcıdır: mevcut kayıtlarda kesinti türü int olarak
/// saklanıyordu ve bu enum onların üzerine oturuyor. Değerler
/// DEĞİŞTİRİLMEMELİ, yalnızca sona ekleme yapılmalı.
/// </summary>
public enum HakedisDeductionType
{
    /// <summary>Serbest kalem (toplu koruma vb.).</summary>
    Other = 0,

    /// <summary>Kesin teminat — sözleşme gereği tutulan güvence (%5).</summary>
    PerformanceBond = 1,

    /// <summary>All-risk inşaat sigortası payı (%0,5).</summary>
    AllRiskInsurance = 2,

    /// <summary>İşverenin verdiği malzemenin bedeli (%10).</summary>
    MaterialDeduction = 3,

    /// <summary>
    /// Barter — hakedişin mal/hizmet olarak ödenecek kısmı. Şantiye
    /// bazında değişken oranlı.
    /// </summary>
    Barter = 4,

    /// <summary>Yemek kesintisi — alt kalemli (kahvaltı/öğlen/akşam/kumanya).</summary>
    Meal = 5,

    /// <summary>Konaklama/kamp kesintisi — alt kalemli (yatılı/evci).</summary>
    Accommodation = 6,

    /// <summary>İSG ceza kesintisi.</summary>
    OhsPenalty = 7,

    /// <summary>İSG katılım payı (aylık).</summary>
    OhsContribution = 8
}

/// <summary>
/// Alt kalemli kesintilerin (yemek, konaklama, İSG) tek bir satırı:
/// birim fiyat × adet, üzerine KDV.
///
/// Alt kalemlerin toplamı ana kesinti tutarını verir; ana kesinti bu
/// durumda orana göre değil, alt kalemlerden hesaplanır.
/// </summary>
public sealed class ProgressPaymentDeductionLine : BaseEntity
{
    public Guid ProgressPaymentDeductionId { get; set; }
    public ProgressPaymentDeduction ProgressPaymentDeduction { get; set; } = null!;

    public int LineNumber { get; set; }

    /// <summary>Alt kalem adı: "Kahvaltı", "Öğlen", "Yatılı" vb.</summary>
    public string Name { get; set; } = string.Empty;

    public decimal UnitPrice { get; set; }

    /// <summary>Aylık girilen adet.</summary>
    public decimal Quantity { get; set; }

    public decimal VatRate { get; set; }

    /// <summary>Birim fiyat × adet (KDV hariç).</summary>
    public decimal NetAmount { get; set; }

    public decimal VatAmount { get; set; }

    /// <summary>KDV dahil toplam — ana kesintiye giren tutar.</summary>
    public decimal GrossAmount { get; set; }

    public string? Notes { get; set; }
}
