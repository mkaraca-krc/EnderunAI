using System.Security.Claims;

namespace EnderunAI.Api.Security;

/// <summary>
/// JETONDAKİ İZİNLERİN TEK YORUMLAYICISI (JETON/1 · Ş1).
///
/// ÜÇ KODLAMA VAR ve hepsi YALNIZ burada yazılıp okunur:
///
///   all_permissions  → kullanıcıda kataloğun TAMAMI var
///   not_permissions  → kataloğun tamamı EKSİ listelenenler
///   permissions      → yalnız listelenenler
///
/// BAŞKA HİÇBİR YER BU ALAN ADLARINI DOĞRUDAN OKUMAZ. Üç kodlama üç
/// karar yerine dağılsaydı biri güncellenip ötekiler kalırdı; bu
/// programda tam olarak bu yaşandı (çek toplamları, includeVoided,
/// RoleCatalog). `JetonKodlamasiTekYerdeTests` bunu tarıyor.
///
/// ───────────────────────────────────────────────────────────────
/// NEDEN TÜMLEYEN (not_permissions) EKLENDİ
/// ───────────────────────────────────────────────────────────────
///
/// Önce ikili bir dünya vardı: ya "hepsi" bayrağı ya tam liste. Bu
/// bir UÇURUM yaratıyordu — tam yetkili bir rolden TEK bir izin
/// çıkarmak bayrağı düşürüyor ve bütün liste jetona yazılıyordu.
///
/// Canlıda olan (2026-08-29): ÖP/1a'da `payment.plan.approve` Admin'den
/// çıkarıldı, Admin 141'den 140 izne düştü, jeton 4394 bayta çıktı,
/// tarayıcı 4096 baytı aşan çerezi SESSİZCE attı, giriş döngüye girdi.
/// Yayın günü hiçbir belirti yoktu; arıza eldeki jetonun süresi
/// dolunca ortaya çıktı (Kural 56).
///
/// Tümleyenle Admin'in jetonu tek anahtar taşıyor.
///
/// SINIRI AÇIKÇA: bu kodlama UÇURUMU kaldırır, ÖLÇEKLENMEYİ ÇÖZMEZ.
/// Kataloğun yaklaşık YARISINA sahip bir rol hâlâ şişer. Ne zaman
/// yetmeyeceği DURUM.md'de tetikle birlikte yazılı (JETON/2).
/// </summary>
public static class JetonIzinKodlamasi
{
    public const string HepsiAlani = "all_permissions";
    public const string ListeAlani = "permissions";
    public const string TumleyenAlani = "not_permissions";

    /// <summary>
    /// ÇEREZ SINIRI. Tarayıcılar ad+değer toplamı bunu aşan çerezi
    /// sessizce atıyor.
    /// </summary>
    public const int CerezSiniri = 4096;

    /// <summary>
    /// PAYLI EŞİK — uçurumun kenarında değil, yaklaşırken uyarı.
    /// Üretim bu eşiği aşan jetonu REDDEDER (Ş3): tarayıcının sessizce
    /// atacağı bir jetonu göndermektense açık hata vermek yeğdir.
    /// Bugünkü arızanın teşhisi saatler aldı çünkü hiçbir katman
    /// "bu çerez atıldı" demedi.
    /// </summary>
    public const int PayliEsik = 3500;

    /// <summary>
    /// YAZMA — kodlama BOYUTA GÖRE ve DETERMİNİSTİK seçilir (Ş2).
    ///
    /// Kural tek: <c>|izinler| &lt;= |tümleyen|</c> ise liste, değilse
    /// tümleyen. Eşitlik hâli de kuralın içinde — sınıra yakın bir rol
    /// iki üretim arasında kodlama değiştirmesin diye. Kodlamanın
    /// üretimden üretime oynaması, hata ayıklamayı imkânsız kılardı.
    /// </summary>
    public static IReadOnlyList<Claim> Yaz(IEnumerable<string> izinler)
    {
        var verilen = izinler
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (PermissionCatalog.HasEveryPermission(verilen))
            return [new Claim(HepsiAlani, "true")];

        var verilenKume = verilen.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var tumleyen = PermissionCatalog.Permissions
            .Select(tanim => tanim.Key)
            .Where(anahtar => !verilenKume.Contains(anahtar))
            .ToArray();

        // DETERMİNİSTİK: eşitlikte liste kazanır.
        if (verilen.Length <= tumleyen.Length)
            return [.. verilen.Select(izin => new Claim(ListeAlani, izin))];

        // ═══════════════════════════════════════════════════════════
        // BAYRAK GÖNDERİLMİYOR — ANLAMAYAN OKUYUCU KAPALI TARAFA DÜŞSÜN
        // ═══════════════════════════════════════════════════════════
        //
        // Önce `all_permissions: true` + `not_permissions` birlikte
        // gönderiliyordu. Tümleyeni BİLMEYEN bir okuyucu bayrağı görüp
        // HER ŞEYİ verirdi — Admin'e ödeme onayı dahil, yani İ2'nin
        // tam tersi. Ve bu GÖRÜNMEZ bir hatadır.
        //
        // Bu teorik değil: safe-deploy sağlık kontrolü düşerse ön yüzü
        // GERİ ALIYOR, ama kullanıcıların çerezindeki yeni biçimli
        // jeton 12 saat yaşıyor. O pencerede eski middleware yol
        // korumasını tamamen açardı.
        //
        // Artık yalnız tümleyen gönderiliyor. Anlamayan okuyucu ne
        // bayrak ne liste görür → izin kümesi BOŞ → kullanıcı ekrana
        // giremez. Eksik yetki GÖRÜNÜR ve düzeltilir; fazla yetki
        // görünmez ve zararlıdır.
        return [.. tumleyen.Select(izin => new Claim(TumleyenAlani, izin))];
    }

    /// <summary>
    /// OKUMA — üç kodlamanın hepsini tek yerde çözer.
    ///
    /// `all_permissions` + `not_permissions` BİRLİKTE gelir: bayrak
    /// "hepsi" der, tümleyen "şunlar hariç" der. Bayrağı tek başına
    /// okuyan eski bir tüketici, tümleyeni GÖRMEZ ve kullanıcıya
    /// olmayan bir yetkiyi verirdi — bu yüzden okuma da tek yerde.
    /// </summary>
    public static IReadOnlyCollection<string> Oku(
        Func<string, IEnumerable<string>> claimDegerleri)
    {
        // SIRA ÖNEMLİ: tümleyen ÖNCE bakılır.
        //
        // Tümleyen kodlamasında bayrak GÖNDERİLMİYOR (yukarıdaki
        // gerekçe), ama sıra tersine kurulsaydı ve bir gün ikisi
        // birden gelirse, bayrağı önce okuyan kod tümleyeni yok sayıp
        // fazla yetki verirdi. Sıra o hatayı yapısal olarak imkânsız
        // kılıyor.
        var haric = claimDegerleri(TumleyenAlani)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (haric.Count > 0)
        {
            return PermissionCatalog.Permissions
                .Select(tanim => tanim.Key)
                .Where(anahtar => !haric.Contains(anahtar))
                .ToArray();
        }

        var hepsi = claimDegerleri(HepsiAlani)
            .Any(deger => string.Equals(deger, "true", StringComparison.OrdinalIgnoreCase));

        if (hepsi)
        {
            return PermissionCatalog.Permissions
                .Select(tanim => tanim.Key)
                .ToArray();
        }

        return claimDegerleri(ListeAlani)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
