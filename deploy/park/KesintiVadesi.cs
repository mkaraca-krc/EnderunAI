namespace EnderunAI.Api.Services.Accounting;

/// <summary>
/// Sözleşme vadesi — kesinti hesabının hangi sınıfa yazılacağını belirler.
/// </summary>
public enum KesintiVadesi
{
    /// <summary>Bitiş yılı = başlangıç yılı. Kısa vadeli (193).</summary>
    Kisa = 0,

    /// <summary>Bitiş yılı &gt; başlangıç yılı. Uzun vadeli.</summary>
    Uzun = 1,

    /// <summary>Tarih(ler) yok — vade BİLİNMİYOR. İyi haber DEĞİL.</summary>
    Belirsiz = 2,
}

/// <summary>
/// HAKEDİŞ KESİNTİSİ — VADE ÇÖZÜMÜ VE HESAP SEÇİMİ (2026-09-17).
///
/// ═══ ÜÇ HÂL, ÜÇÜ DE AÇIK (Kural 67) ═══
///
///   KISA     -> 193, fiş üretilir
///   UZUN     -> fiş ÜRETİLMEZ (hesap henüz kararlaşmadı)
///   BELİRSİZ -> fiş ÜRETİLMEZ
///
/// ═══ BELİRSİZ NEDEN AYRI BİR HÂL ═══
///
/// İlk yazdığım kural `bitiş yılı > başlangıç yılı` idi. O kural NULL
/// bitişte `false` döner — yani tarihi olmayan bir proje SESSİZCE KISA
/// sayılırdı. Mehmet Bey yakaladı (17.09.2026): *"Sessizce kısa vade
/// seçmek, tam da uzun vade hesabı seçilmediğinde yaptığın
/// fail-closed'ın kaçırdığı kapı."*
///
/// Bilinmeyeni iyi haber saymak, ölçmemekle aynı şeydir.
///
/// ═══ UZUN VADE HESABI NEDEN AÇIK ═══
///
/// Karar metni "293" diyordu; ÖLÇÜM çelişti — canlı hesap planında
/// `293 = GELECEK YILLAR İHTİYACI STOKLAR` (bir STOK hesabı), `193`ün
/// uzun vadeli karşılığı ise `295 PEŞİN ÖDENEN VERGİLER VE FONLAR` ve
/// canlıda var. Soru müşavire gitti.
///
/// İKİNCİ VE DAHA DERİN SORU: ayrım "yıllara sari mi" değil, "MAHSUP NE
/// ZAMAN". Bakiye zamanla uzun vadeliden kısa vadeliye GÖÇ EDER; hesap
/// fiş anında sabitlenirse bir yıl sonra yanlış sınıfta kalır. Müşavir
/// "hep 193, dönem sonunda sınıflandır" derse uzun vade ayağı hiç
/// gerekmeyebilir. Bu yüzden burada UZUN için bir hesap TAHMİN
/// EDİLMİYOR — fiş üretilmiyor.
///
/// Ayrıntı: `docs/KESINTI-KARARLARI.md`, `docs/GOC-PLANI-HAKEDIS-KESINTI.md`.
/// </summary>
public static class KesintiVadeCozumu
{
    /// <summary>
    /// Projenin tarihlerinden vadeyi çözer.
    ///
    /// Başlangıç: `PlannedStartDate` yoksa `ContractDate`.
    /// Bitiş:     `PlannedEndDate`   yoksa `ContractDeadlineDate`.
    ///
    /// İKİSİNDEN BİRİ BİLE YOKSA <see cref="KesintiVadesi.Belirsiz"/>.
    /// "Başlangıç yoksa bugünü varsay" gibi bir kestirme YOK: varsayım,
    /// ölçüm değildir.
    /// </summary>
    public static KesintiVadesi Coz(
        DateTime? plannedStart,
        DateTime? contractDate,
        DateTime? plannedEnd,
        DateTime? contractDeadline)
    {
        var baslangic = plannedStart ?? contractDate;
        var bitis = plannedEnd ?? contractDeadline;

        if (baslangic is null || bitis is null)
            return KesintiVadesi.Belirsiz;

        // Bitiş başlangıçtan ÖNCE ise veri tutarsızdır; kısa saymak
        // tutarsızlığı gizler.
        if (bitis.Value < baslangic.Value)
            return KesintiVadesi.Belirsiz;

        return bitis.Value.Year > baslangic.Value.Year
            ? KesintiVadesi.Uzun
            : KesintiVadesi.Kisa;
    }

    /// <summary>
    /// Vadeye göre fiş üretilebilir mi; üretilemezse SEBEBİ ne.
    /// </summary>
    /// <returns>
    /// `null` ise üretilebilir. Değilse döndürülen metin, kullanıcıya
    /// gösterilecek sebeptir — sessiz ret yok.
    /// </returns>
    public static string? FisUretilemezSebebi(KesintiVadesi vade) => vade switch
    {
        KesintiVadesi.Kisa => null,

        KesintiVadesi.Uzun =>
            "Sözleşme birden fazla yıla yayılıyor. Uzun vadeli stopaj hesabı "
            + "henüz kararlaştırılmadı (193 mü 295 mi, ve mahsubun ne zaman "
            + "sınıflandırılacağı mali müşavire soruldu). Karar gelene kadar "
            + "bu projede stopaj fişi üretilmiyor — yanlış hesaba yazmaktansa "
            + "üretmemek tercih edildi.",

        KesintiVadesi.Belirsiz =>
            "Projenin başlangıç ya da bitiş tarihi boş; sözleşme vadesi "
            + "BİLİNMİYOR. Vade bilinmeden stopajın hangi hesaba yazılacağı "
            + "belirlenemez. Proje tarihlerini girip tekrar deneyin.",

        _ => "Bilinmeyen vade durumu.",
    };
}
