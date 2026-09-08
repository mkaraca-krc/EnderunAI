using Microsoft.Extensions.Configuration;

namespace EnderunAI.Api.Services.Upload;

/// <summary>
/// DİSKE YAZILAN BÜTÜN KÖKLERİN TEK ÇÖZÜCÜSÜ (SIZINTI/1).
///
/// ═══ NEDEN VAR ═══
///
/// Üç ayrı yerde sabit kodlanmış üç kök vardı:
///   UploadService              -> /var/www/enderun-ai/uploads
///   EInvoiceArchive            -> /var/www/enderun-ai/uploads/e-fatura
///   ProjectDocumentsController -> /var/www/enderun-data/project-files
///
/// Testler bu servisleri SAHTELEMEDEN çağırıyor. Satırları
/// `enderun_ai_test`e gidiyor ve o veritabanı her koşuda düşürülüyor;
/// DOSYALAR ise canlı diskte kalıyor. Ölçüldü (2026-09-08):
///   uploads/       16.696 dosya, 12'si gerçek
///   project-files/  3.250 dosya, 0'ı gerçek (project_documents boş)
/// Ortanca dosya boyutu 20 ve 9 bayt; içerikler test fikstürlerinin
/// ta kendisi (`%PDF-1.4 test`, `test-icerik`).
///
/// Rig'in canlı `.next` dizinine yazmasıyla AYNI SINIF — bir haftada
/// ikinci kez. Bu yüzden çözüm de aynı: kök DIŞARIDAN VERİLEBİLİR
/// (NEXT_DIST_DIR deseni) ve bir FAIL-CLOSED KAPI var.
///
/// ═══ KAPI NEYE BAKIYOR ═══
///
/// Test ortamının işareti olarak AYRI BİR BAYRAK KULLANMIYORUM:
/// unutulabilir bir bayrak, unutulduğu gün sessizce açık kalır.
/// Zaten sabitlenmiş ve unutulamayacak bir işaret var — bağlantı
/// dizesindeki veritabanı adı. `enderun_ai_test`e konuşan bir süreç
/// canlı yazma köküne DOKUNAMAZ. Yeni bir test fabrikası yazan kişi
/// kökü ayarlamayı unutursa süreç BAŞLAMAZ.
///
/// ALT DİZİN DE SAYILIR: `uploads/gecici` gibi bir kök de canlı
/// dizinin içindedir ve yedeğe girer; kapı onu da reddediyor.
/// </summary>
public static class YazmaKokleri
{
    public const string CanliYuklemeKoku = "/var/www/enderun-ai/uploads";
    public const string CanliProjeDosyaKoku = "/var/www/enderun-data/project-files";

    private const string TestVeritabaniAdi = "enderun_ai_test";

    /// <summary>Genel yükleme kökü (kategori klasörlerinin üstü).</summary>
    public static string Yukleme(IConfiguration cfg) =>
        Dogrula(cfg, Secilen(cfg["Uploads:Root"], CanliYuklemeKoku));

    /// <summary>e-Fatura XML arşivi. Varsayılanı yükleme kökünün altında.</summary>
    public static string EFaturaArsivi(IConfiguration cfg)
    {
        var acik = cfg["EInvoice:ArchivePath"];

        return string.IsNullOrWhiteSpace(acik)
            ? Dogrula(cfg, Path.Combine(Yukleme(cfg), "e-fatura"))
            : Dogrula(cfg, acik.Trim());
    }

    /// <summary>
    /// Proje belgeleri kökü — ayrı bir disk yolu.
    ///
    /// ANAHTAR UYDURULMADI: `Storage:ProjectFilesRoot` zaten vardı ve
    /// ProjectFileCleaner onu okuyordu. İkinci bir anahtar açsaydım
    /// aynı kökün iki adı olurdu; biri ayarlanıp öteki unutulduğunda
    /// temizleyici ile yazıcı FARKLI dizinlere bakardı (Kural 79).
    /// </summary>
    public static string ProjeDosyalari(IConfiguration cfg) =>
        Dogrula(cfg, Secilen(cfg["Storage:ProjectFilesRoot"], CanliProjeDosyaKoku));

    private static string Secilen(string? yapilandirilmis, string varsayilan) =>
        string.IsNullOrWhiteSpace(yapilandirilmis) ? varsayilan : yapilandirilmis.Trim();

    /// <summary>
    /// FAIL-CLOSED KAPI. Test veritabanına bağlıyken canlı bir yazma
    /// kökü seçilmişse İSTİSNA FIRLATIR — süreç başlamaz.
    /// </summary>
    public static string Dogrula(IConfiguration cfg, string kok)
    {
        if (!TestVeritabaninaBagli(cfg)) return kok;

        foreach (var canli in new[] { CanliYuklemeKoku, CanliProjeDosyaKoku })
        {
            if (!IcindeMi(kok, canli)) continue;

            throw new InvalidOperationException(
                $"YAZMA KÖKÜ KAPISI (SIZINTI/1): süreç '{TestVeritabaniAdi}' " +
                $"veritabanına bağlı ama yazma kökü canlı dizin: '{kok}' " +
                $"('{canli}' içinde). Testler canlı diske dosya bırakamaz. " +
                "Kökü 'Uploads:Root', 'EInvoice:ArchivePath' veya " +
                "'Storage:ProjectFilesRoot' ayarıyla geçici bir dizine alın.");
        }

        return kok;
    }

    private static bool TestVeritabaninaBagli(IConfiguration cfg)
    {
        var baglanti =
            cfg.GetConnectionString("DefaultConnection")
            ?? Environment.GetEnvironmentVariable("DB_CONNECTION")
            ?? string.Empty;

        return baglanti.Contains(
            $"Database={TestVeritabaniAdi}", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// `aday`, `kok` ile aynı dizin mi ya da onun altında mı.
    /// Yol normalleştiriliyor: `uploads/../uploads` ile `uploads`
    /// aynı yerdir ve kapı ikisini de görmek zorunda.
    /// </summary>
    private static bool IcindeMi(string aday, string kok)
    {
        var a = Path.GetFullPath(aday).TrimEnd('/');
        var k = Path.GetFullPath(kok).TrimEnd('/');

        return string.Equals(a, k, StringComparison.Ordinal)
               || a.StartsWith(k + "/", StringComparison.Ordinal);
    }
}
