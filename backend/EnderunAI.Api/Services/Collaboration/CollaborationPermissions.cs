using EnderunAI.Api.Security;

namespace EnderunAI.Api.Services.Collaboration;

/// <summary>
/// VARLIK TİPİ → YORUM İZNİ. TEK TANIM.
///
/// Bu tablo, "bu kaydın tartışmasını kim okuyabilir/yazabilir" ve
/// "ek dosyasını kim indirebilir" sorusunun TEK cevabıdır. Yorum
/// listesi, yorum yazma, düzenleme, gizleme, ek listeleme, ek
/// yükleme ve EK İNDİRME aynı tablodan geçer.
///
/// NEDEN AYRI DOSYA: kapı tek olmalı. Denetleyicinin içine gömülü
/// bir `switch` olsaydı, sonraki tip eklenirken bir uçta unutulur
/// ve o uç sessizce açık kalırdı — G3'te tam olarak bu yaşandı
/// (`RetailSalesController` sekiz gün `[Authorize]`'suz kaldı).
///
/// EKRAN KAPISI İLE AYNI OLMAK ZORUNDA DEĞİL — ÜÇ YERDE BİLEREK
/// AYRILIYOR:
///
///   - Teklif: ekran `projects.view` ile açılıyor ve bu izin 15
///     rolün 12'sinde var. Fiyat pazarlığı tartışmasını buna
///     bağlamak, onu neredeyse herkese açmak olurdu.
///     `offer_tracking.view` (5 rol) doğru sınır.
///
///   - Mal kabul: ekran genel `/depo` kuralına düşüp
///     `inventory.view` ile açılıyor. Mal kabul tartışması bir depo
///     listesi değil, tedarikçi ve eksik teslim konusu.
///
///   - Satın alma talebi: `purchasing.view` MODÜL kapısı,
///     `purchasing-requests.view` KAYIT kapısı. Yorum kayda ait.
///
/// İLKE: ekranı açabilmek, TEK BİR KAYDIN tartışmasını okuyabilmek
/// demek DEĞİLDİR. Ekran kapısı gevşekse onu kopyalamak hatayı
/// çoğaltır.
///
/// ÇEK İÇİN NOT: `cheque.view` diye bir izin YOK (yalnız
/// `cheque.edit` ve `cheque.void-closed` var). Uydurulmadı;
/// `finance.view` kullanılıyor. Bu, çek yorumlarını Finans
/// Sorumlusu ve Genel Müdür'ün yanı sıra Teknik Ofis ve Teknik
/// Koordinatör'e de açar. Daraltmak yeni bir anahtar açmayı
/// gerektirir ve o AYRI BİR KARARDIR.
/// </summary>
public static class CollaborationPermissions
{
    /*
     * KAPALI TARAFA DÜŞER.
     *
     * Tabloda karşılığı olmayan tip REDDEDİLİR. Varsayılanın
     * "izin ver" olması, yeni bir tip eklerken bu dosyayı unutan
     * kişinin o tipi herkese açması demekti — ve unutulduğu
     * kimseye görünmezdi. Şimdi unutulursa özellik ÇALIŞMAZ;
     * gürültülü başarısızlık, sessiz sızıntıdan iyidir.
     *
     * `CollaborationPermissionMapTests` ayrıca
     * `EntityContextResolver.SupportedTypes` içindeki her tipin
     * burada karşılığı olmasını şart koşuyor.
     */
    private static readonly Dictionary<string, string> Tablo =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Görev: iş akışının kendi tartışması.
            ["WorkTask"] = PermissionCatalog.Keys.TasksView,

            // Proje geneli.
            ["Project"] = PermissionCatalog.Keys.ProjectsView,

            // Hakediş: kesinti ve metraj tartışması.
            ["ProgressPayment"] = PermissionCatalog.Keys.HakedisView,

            // Satın alma talebi — modül değil KAYIT kapısı.
            ["PurchaseRequest"] = PermissionCatalog.Keys.PurchasingRequestsView,

            // Mal kabul: eksik/hasarlı teslim notu.
            ["GoodsReceipt"] = PermissionCatalog.Keys.PurchasingReceiptsView,

            // Teklif: revizyon gerekçeleri, fiyat pazarlığı.
            ["Offer"] = PermissionCatalog.Keys.OfferTrackingView,

            // Çek: vade, ciro, karşılıksız takibi.
            ["Cheque"] = PermissionCatalog.Keys.FinanceView
        };

    /// <summary>
    /// Tipin gerektirdiği izin anahtarı. Tanımsız tip için
    /// <c>null</c> — çağıran taraf bunu REDDETME olarak yorumlar.
    /// </summary>
    public static string? GerekenIzin(string? entityType) =>
        entityType is not null && Tablo.TryGetValue(entityType.Trim(), out var izin)
            ? izin
            : null;

    /// <summary>
    /// KAPININ KARARI — SAF FONKSİYON.
    ///
    /// NEDEN AYRI: kapalı-taraf varsayılanı denetleyicinin içinde
    /// dururken UÇTAN GÖZLENEMİYORDU. Bilinmeyen bir tipte izin
    /// kontrolü atlansa bile `EntityContextResolver` o tipi zaten
    /// çözemiyor ve uç yine 404 dönüyordu; yani "varsayılanı
    /// serbest yap" sabotajı testleri KIRMADI ve test bir şey
    /// ölçmüyordu.
    ///
    /// Tehlikeli durum başkaydı: biri
    /// `EntityContextResolver.SupportedTypes`'a tip ekleyip BU
    /// TABLOYU unutursa, çözümleyici tipi tanır ve serbest
    /// varsayılanla kapı ardına kadar açılırdı. O durum ancak bu
    /// karar ayrı ve saf olduğunda sınanabiliyor.
    ///
    /// `izinVarMi` dışarıdan geliyor: test, TÜM İZİNLERE SAHİP bir
    /// kullanıcıyı taklit edip (`_ => true`) reddin sebebinin
    /// yetersiz izin değil TİPİN TANINMAMASI olduğunu kanıtlayabilsin.
    /// </summary>
    public static bool ErisebilirMi(string? entityType, Func<string, bool> izinVarMi)
    {
        var gereken = GerekenIzin(entityType);

        // Tanımsız tip: KAPALI. Kullanıcının izinlerine hiç bakılmaz.
        return gereken is not null && izinVarMi(gereken);
    }

    /// <summary>Testlerin ve bekçilerin okuduğu tam liste.</summary>
    public static IReadOnlyDictionary<string, string> Tumu => Tablo;
}
