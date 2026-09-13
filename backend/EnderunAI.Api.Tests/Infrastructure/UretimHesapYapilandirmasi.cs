namespace EnderunAI.Api.Tests.Infrastructure;

/// <summary>
/// ÜRETİMİN HESAP YAPILANDIRMASI — ZEMİN SESSİZCE AYRIŞMASIN.
///
/// ═══ NEDEN VAR (Kural 81'in en saf hâli, ölçüldü 2026-09-13) ═══
///
/// `TestDataFactory` stok hesaplarını `RequiresProject` /
/// `RequiresCostCenter` BAYRAKLARI OLMADAN kuruyordu. Sonuç:
///
///   · stok muhasebesi testleri YEŞİLDİ
///   · üretimde stok→muhasebe hattı HİÇ ÇALIŞMAMIŞTI
///     (canlıda 150/153/770/740.03.09/379.01 hesaplarına ait
///      TEK fiş satırı yok)
///
/// Zemin üretimi taklit etmiyordu ve fark yalnızca CANLIDA görünüyordu.
/// Bu dosya, zeminin üretimden neyi devralacağını TEK YERDE ilan eder;
/// `FiksturHesapYapilandirmasiTests` de fikstürün gerçekten buna
/// uyduğunu ölçer.
///
/// ═══ DEĞERLER NEREDEN GELİYOR ═══
///
/// Canlı `enderun_ai` veritabanından OKUNDU (2026-09-13), tahmin
/// edilmedi. 150 ve 153'ün `RequiresProject` değeri, Mehmet Bey'in
/// kararıyla `false`'a çekiliyor: proje ve masraf merkezi SONUÇ
/// hesaplarının (6xx/7xx) boyutudur; bilanço hesabında zorunluluk,
/// düşünülmüş bir karar değilse hatadır.
///
/// ═══ BU LİSTE DEĞİŞİRSE ═══
///
/// Üretimde bir hesabın bayrağı değişirse burası da değişmeli. Değişmezse
/// muhafız kırmızı yanmaz — ama o zaman zemin ile üretim yine ayrışır.
/// Bu listenin kendisi bir BEYANDIR; doğruluğu canlı ölçümle tazelenir.
/// </summary>
public static class UretimHesapYapilandirmasi
{
    public sealed record HesapAyari(
        string Kod,
        bool ProjeZorunlu,
        bool MasrafMerkeziZorunlu,
        string Gerekce);

    /// <summary>Stok→muhasebe hattının dokunduğu hesapların üretimdeki hâli.</summary>
    public static readonly IReadOnlyList<HesapAyari> Ayarlar =
    [
        // ── BİLANÇO (1xx-3xx): boyut YOK ──────────────────────────────
        new("150", false, false, "Bilanço. Depoya giren mal henüz bir projenin gideri değil."),
        new("153", false, false, "Bilanço. Aynı gerekçe."),
        new("379", false, false, "Bilanço. Ana hesap, fiş kesilemez."),
        new("379.01", false, false, "Bilanço. GR-IR; canlıda zaten boyutsuz."),

        // ── SONUÇ (6xx-7xx): boyut VAR ve bilinçli ────────────────────
        // Canlıda 7'li sınıfın 89 hesabının hepsinde masraf merkezi
        // zorunlu; 740 ayrıca proje istiyor.
        new("740", true, true, "Maliyet hesabı. Üretimde proje + masraf merkezi zorunlu."),
        new("740.03.09", true, true, "Proje malzemesi. Üretimde ikisi de zorunlu."),
        new("770", false, true, "Merkez gideri. Projesi yok, masraf merkezi ZORUNLU."),
        new("621", false, false, "Satılan malın maliyeti; canlıda boyutsuz."),
        new("689", false, false, "Ana hesap, fiş kesilemez."),
        new("649", false, false, "Ana hesap."),
    ];

    public static HesapAyari? Bul(string kod) =>
        Ayarlar.FirstOrDefault(x => x.Kod == kod);
}
