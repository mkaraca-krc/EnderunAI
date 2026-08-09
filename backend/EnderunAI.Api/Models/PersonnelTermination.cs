namespace EnderunAI.Api.Models;

/// <summary>
/// Personelin ayrılış türü. Tazminat hakkı doğrudan buradan türer;
/// kullanıcı hakları elle işaretleyemez (bkz. TerminationRights).
/// </summary>
public enum TerminationReason
{
    /// <summary>İşveren feshi, haklı neden olmadan. Kıdem + ihbar.</summary>
    EmployerTermination = 0,

    /// <summary>İstifa. Hiçbir tazminat doğurmaz.</summary>
    Resignation = 1,

    /// <summary>İşçinin haklı nedenle feshi (24. madde). Kıdem var, ihbar yok.</summary>
    ResignationWithJustCause = 2,

    /// <summary>Emeklilik. Kıdem var.</summary>
    Retirement = 3,

    /// <summary>Askerlik. Kıdem var.</summary>
    MilitaryService = 4,

    /// <summary>Evlilik nedeniyle ayrılma (kadın işçi, 1 yıl içinde). Kıdem var.</summary>
    Marriage = 5,

    /// <summary>İşverenin haklı nedenle feshi (25/II ahlak ve iyi niyet). Tazminat yok.</summary>
    EmployerTerminationWithJustCause = 6,

    /// <summary>Belirli süreli sözleşmenin süresi dolarak sona ermesi. Tazminat yok.</summary>
    FixedTermContractEnd = 7,

    /// <summary>Vefat. Kıdem mirasçıya ödenir.</summary>
    Death = 8
}

/// <summary>
/// Ayrılış değerlendirmesi — tekrar işe alım kodu.
///
/// Yasal çıkış nedeninden (TerminationReason) AYRIDIR ve onun yerine
/// geçmez: neden SGK/İş Kanunu tarafını, bu kod İK'nın "bu kişiyi
/// yeniden alır mıyız" değerlendirmesini tutar. Aynı çıkış nedeni iki
/// kişide farklı değerlendirme doğurabilir.
///
/// Alan BOŞ bırakılabilir: değerlendirilmemiş çıkış nötrdür, ne engel
/// ne uyarı üretir. Sessizce "sorunsuz" saymak da yanlış olurdu —
/// değerlendirilmemiş olmak, sorunsuz olmak değildir.
/// </summary>
public enum RehireCode
{
    /// <summary>Sorunsuz ayrıldı; yeniden alınabilir.</summary>
    Green = 0,

    /// <summary>Dikkat: şartlı değerlendirilir. Uyarı verir, engellemez.</summary>
    Yellow = 1,

    /// <summary>İşe alınamaz. Yeni kayıt ve yeniden aktifleştirme engellenir.</summary>
    Red = 2
}

public enum TerminationStatus
{
    Draft = 0,
    Finalized = 1
}

/// <summary>
/// Personel çıkış kaydı ve hesaplanan tazminat.
///
/// Hesap sonuçları kayda YAZILIR (parametreler ve maaş sonradan
/// değişebilir; kesinleşmiş bir çıkışın tutarı geçmişe dönük
/// oynamamalı). Resmi ve gerçek tutarlar ayrı ayrı tutulur; gerçek
/// tutarlar yalnızca extra_payment.view iznine sahip kullanıcıya döner.
/// </summary>
public sealed class PersonnelTermination : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid PersonnelId { get; set; }
    public Personnel Personnel { get; set; } = null!;

    public DateTime TerminationDate { get; set; }
    public TerminationReason Reason { get; set; }
    public TerminationStatus Status { get; set; } = TerminationStatus.Draft;

    /// <summary>Hizmet süresi (gün). Hesabın izlenebilmesi için saklanır.</summary>
    public int ServiceDays { get; set; }

    /// <summary>Kullanılmayan yıllık izin günü (hesaplanan, elle düzeltilebilir).</summary>
    public decimal UnusedLeaveDays { get; set; }

    // --- Hesaba giren ücretler ---

    /// <summary>SGK'ya bildirilen aylık brüt ücret.</summary>
    public decimal OfficialMonthlyGross { get; set; }

    /// <summary>Aylık elden ödenen tutar (varsa).</summary>
    public decimal ExtraMonthlyAmount { get; set; }

    // --- RESMİ tutarlar (belgelenen) ---

    public decimal OfficialSeveranceGross { get; set; }
    public decimal OfficialSeveranceStampTax { get; set; }
    public decimal OfficialNoticeGross { get; set; }
    public decimal OfficialNoticeIncomeTax { get; set; }
    public decimal OfficialNoticeStampTax { get; set; }
    public decimal OfficialLeaveGross { get; set; }
    public decimal OfficialLeaveSgk { get; set; }
    public decimal OfficialLeaveIncomeTax { get; set; }
    public decimal OfficialLeaveStampTax { get; set; }
    public decimal OfficialNetTotal { get; set; }

    /// <summary>Kıdem tavanı resmi hesabı kesti mi (şeffaflık için).</summary>
    public bool SeveranceCeilingApplied { get; set; }

    public decimal ActualNetTotal { get; set; }

    /// <summary>Elden ödenecek fark: gerçek net − resmi net.</summary>
    public decimal ExtraPaymentDifference { get; set; }

    public string? Note { get; set; }

    // --- Ayrılış değerlendirmesi (tekrar işe alım) ---
    //
    // Tazminat notundan AYRI tutuluyor: Note çıkışın mali tarafını
    // anlatır, buradaki gerekçe İK'nın değerlendirmesidir. Tek alanda
    // birleştirmek iki farklı amacı karıştırırdı.
    //
    // GİZLİLİK: kod ve gerekçe yalnızca İK/GM'ye döner; saha
    // personeli (personnel.view) hiçbir uçtan göremez.

    /// <summary>Boşsa "değerlendirilmedi" — nötr, engel/uyarı üretmez.</summary>
    public RehireCode? RehireCode { get; set; }

    /// <summary>
    /// Değerlendirmenin gerekçesi. Kırmızı ve sarıda ZORUNLU: kod tek
    /// başına "neden" sorusunu cevaplamaz ve gerekçesiz bir engel,
    /// itiraz edilemez bir engeldir.
    ///
    /// Faktüel ve mesleki olmalı (davranış, uyum, performans,
    /// devamsızlık); sağlık, inanç, sendika gibi özel nitelikli
    /// kişisel veri yazılmamalı.
    /// </summary>
    public string? RehireNote { get; set; }

    public Guid? RehireMarkedByUserId { get; set; }
    public DateTime? RehireMarkedAtUtc { get; set; }

    public DateTime? FinalizedAtUtc { get; set; }
    public Guid? FinalizedByUserId { get; set; }
}
