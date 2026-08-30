using EnderunAI.Api.Security;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// JETON İZİN KODLAMASI — ÜÇ HÂL, DETERMİNİSTİK SEÇİM (JETON/1 · Ş2).
///
/// Kodlama BOYUTA GÖRE seçilir ve kural TEKTİR:
/// <c>|izinler| &lt;= |tümleyen|</c> ise liste, değilse tümleyen.
/// Eşitlik hâli de kuralın içinde — sınıra yakın bir rol iki üretim
/// arasında kodlama değiştirmesin diye. Oynak bir kodlama, hata
/// ayıklamayı imkânsız kılardı: aynı kullanıcı bir girişte çalışır,
/// ötekinde çalışmazdı.
/// </summary>
public sealed class JetonIzinKodlamasiTests
{
    private static string[] Katalog() =>
        [.. PermissionCatalog.Permissions.Select(x => x.Key)];

    private static string Deger(IEnumerable<System.Security.Claims.Claim> c, string tur)
        => string.Join(",", c.Where(x => x.Type == tur).Select(x => x.Value));

    private static int Sayi(IEnumerable<System.Security.Claims.Claim> c, string tur)
        => c.Count(x => x.Type == tur);

    // ═══ ÜÇ HÂL ═══

    /// <summary>TAM KATALOG → yalnız bayrak, liste yok.</summary>
    [Fact]
    public void TamKatalog_YalnizBayrak()
    {
        var claims = JetonIzinKodlamasi.Yaz(Katalog());

        Assert.Equal(1, Sayi(claims, JetonIzinKodlamasi.HepsiAlani));
        Assert.Equal(0, Sayi(claims, JetonIzinKodlamasi.ListeAlani));
        Assert.Equal(0, Sayi(claims, JetonIzinKodlamasi.TumleyenAlani));
    }

    /// <summary>
    /// BİR EKSİK → YALNIZ TÜMLEYEN, BAYRAK YOK.
    ///
    /// Bu, canlıda girişi kıran durumun ta kendisi: Admin'den
    /// `payment.plan.approve` çıkarıldı, 140 izin tek tek yazıldı ve
    /// jeton 4394 bayta çıktı.
    ///
    /// BAYRAĞIN GÖNDERİLMEMESİ TASARIMIN PARÇASI — bkz. aşağıdaki
    /// "eski okuyucu" testi.
    /// </summary>
    [Fact]
    public void BirEksik_YalnizTumleyen()
    {
        var katalog = Katalog();
        var eksik = PermissionCatalog.Keys.PaymentPlanApprove;
        var izinler = katalog.Where(x => x != eksik).ToArray();

        var claims = JetonIzinKodlamasi.Yaz(izinler);

        Assert.Equal(eksik, Deger(claims, JetonIzinKodlamasi.TumleyenAlani));
        Assert.Equal(0, Sayi(claims, JetonIzinKodlamasi.HepsiAlani));
        Assert.Equal(0, Sayi(claims, JetonIzinKodlamasi.ListeAlani));
    }

    // ═══════════════════════════════════════════════════════════════
    // ESKİ OKUYUCU KAPALI TARAFA DÜŞER
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// TÜMLEYENİ BİLMEYEN BİR OKUYUCU HİÇBİR İZİN GÖRMELİ.
    ///
    /// NEDEN GERÇEK BİR TEHLİKE: safe-deploy sağlık kontrolü düşerse
    /// ön yüzü GERİ ALIYOR, ama kullanıcıların çerezindeki yeni
    /// biçimli jeton 12 saat yaşıyor. O pencerede eski middleware
    /// yeni jetonu okur.
    ///
    /// Önceki kodlama `all_permissions: true` + `not_permissions`
    /// gönderiyordu; eski okuyucu bayrağı görüp HER ŞEYİ verirdi —
    /// Admin'e ödeme onayı dahil, yani İ2'nin tam tersi ve GÖRÜNMEZ.
    ///
    /// Artık bayrak gönderilmiyor: eski okuyucu ne bayrak ne liste
    /// görür, kümesi BOŞ kalır, kullanıcı ekrana giremez. Eksik yetki
    /// GÖRÜNÜR ve düzeltilir; fazla yetki görünmez ve zararlıdır.
    /// </summary>
    [Fact]
    public void EskiOkuyucu_HicIzinGormez()
    {
        var eksik = PermissionCatalog.Keys.PaymentPlanApprove;
        var izinler = Katalog().Where(x => x != eksik).ToArray();

        var claims = JetonIzinKodlamasi.Yaz(izinler);

        // ESKİ OKUYUCUNUN TAKLİDİ: yalnız bayrak ve liste bilir.
        var eskiBayrak = claims.Any(c =>
            c.Type == JetonIzinKodlamasi.HepsiAlani && c.Value == "true");

        var eskiListe = claims
            .Where(c => c.Type == JetonIzinKodlamasi.ListeAlani)
            .Select(c => c.Value)
            .ToArray();

        Assert.False(
            eskiBayrak,
            "Tümleyen kodlamasında `all_permissions` GÖNDERİLMEMELİ: "
            + "onu gören eski okuyucu kullanıcıya OLMAYAN bir yetkiyi "
            + "verir ve bu görünmez.");

        Assert.Empty(eskiListe);
    }

    /// <summary>
    /// TAM YETKİDE BAYRAK GÖNDERİLİR — ve bu doğrudur.
    ///
    /// Eski okuyucu "her şey" görür; kullanıcıda gerçekten her şey
    /// VAR. Kapalı tarafa düşürme kuralı, doğru bilgiyi saklamak
    /// anlamına gelmiyor.
    /// </summary>
    [Fact]
    public void TamYetkide_EskiOkuyucuDaDogruGorur()
    {
        var claims = JetonIzinKodlamasi.Yaz(Katalog());

        Assert.True(claims.Any(c =>
            c.Type == JetonIzinKodlamasi.HepsiAlani && c.Value == "true"));
    }

    /// <summary>AZ İZİN → düz liste, bayrak yok.</summary>
    [Fact]
    public void AzIzin_DuzListe()
    {
        var claims = JetonIzinKodlamasi.Yaz(Katalog().Take(5));

        Assert.Equal(5, Sayi(claims, JetonIzinKodlamasi.ListeAlani));
        Assert.Equal(0, Sayi(claims, JetonIzinKodlamasi.HepsiAlani));
        Assert.Equal(0, Sayi(claims, JetonIzinKodlamasi.TumleyenAlani));
    }

    // ═══ DETERMİNİSTİK SINIR ═══

    /// <summary>
    /// TAM YARIDA LİSTE KAZANIR — eşitlik kuralın içinde.
    ///
    /// Katalog çift sayıda ise yarısı ile tümleyeni eşit olur.
    /// Kural "&lt;=" olduğu için liste seçilir; "&lt;" olsaydı eşitlikte
    /// tümleyene düşerdi ve katalog bir izin büyüyünce aynı rol
    /// kodlama değiştirirdi.
    /// </summary>
    [Fact]
    public void Yarida_ListeKazanir()
    {
        var katalog = Katalog();
        var yari = katalog.Length / 2;

        var claims = JetonIzinKodlamasi.Yaz(katalog.Take(yari));

        // yari <= (uzunluk - yari) olduğu sürece liste
        if (yari <= katalog.Length - yari)
        {
            Assert.Equal(yari, Sayi(claims, JetonIzinKodlamasi.ListeAlani));
            Assert.Equal(0, Sayi(claims, JetonIzinKodlamasi.TumleyenAlani));
        }
    }

    /// <summary>
    /// AYNI GİRDİ HER ZAMAN AYNI KODLAMA. Sıra değişse bile.
    /// Oynak kodlama, "bir girişte oluyor bir girişte olmuyor"
    /// arızasının kaynağı olurdu.
    /// </summary>
    [Fact]
    public void AyniGirdi_AyniKodlama()
    {
        var izinler = Katalog().Take(100).ToArray();

        var a = JetonIzinKodlamasi.Yaz(izinler);
        var b = JetonIzinKodlamasi.Yaz(izinler.Reverse().ToArray());

        Assert.Equal(
            Sayi(a, JetonIzinKodlamasi.TumleyenAlani) > 0,
            Sayi(b, JetonIzinKodlamasi.TumleyenAlani) > 0);
        Assert.Equal(
            Sayi(a, JetonIzinKodlamasi.ListeAlani) > 0,
            Sayi(b, JetonIzinKodlamasi.ListeAlani) > 0);
    }

    // ═══ GİDİŞ-DÖNÜŞ: YAZILAN OKUNUYOR ═══

    /// <summary>
    /// HER ROL İÇİN YAZ→OKU AYNI KÜMEYİ VERİYOR.
    ///
    /// Asıl tehlike burada: kodlama küçülttü diye yetki kaybolursa
    /// ya da fazladan yetki doğarsa, boyut kazancının hiçbir anlamı
    /// kalmaz. Gerçek rollerden sürülüyor (Kural 58) — elle kurulmuş
    /// bir küme değil.
    /// </summary>
    [Theory]
    [MemberData(nameof(KatalogRolleri))]
    public void GidisDonus_AyniKumeyiVeriyor(string rolAdi)
    {
        var rol = RoleCatalog.Roles.Single(x => x.Name == rolAdi);

        var claims = JetonIzinKodlamasi.Yaz(rol.PermissionKeys);

        var okunan = JetonIzinKodlamasi.Oku(alan =>
            claims.Where(c => c.Type == alan).Select(c => c.Value));

        // KÜME EŞİTLİĞİ — HER İKİ YÖNDE, kapsama DEĞİL.
        //
        // "çözülmüş ⊇ gerçek" demek fazla yetki verilmesini yakalamaz
        // ve korunmak istediğimiz yön tam olarak odur: eksik yetki
        // görünür (kullanıcı ekrana giremez), fazla yetki görünmez.
        var gercek = rol.PermissionKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cozulen = okunan.ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(
            cozulen.SetEquals(gercek),
            $"\"{rolAdi}\": eksik = [{string.Join(", ", gercek.Except(cozulen))}], "
            + $"FAZLA = [{string.Join(", ", cozulen.Except(gercek))}]");
    }

    public static TheoryData<string> KatalogRolleri()
    {
        var veri = new TheoryData<string>();

        foreach (var rol in RoleCatalog.Roles)
            veri.Add(rol.Name);

        return veri;
    }

    /// <summary>
    /// TÜMLEYEN OKUNMAZSA YETKİ KAYBOLUR — FAZLA YETKİ DOĞMAZ.
    ///
    /// BU TESTİN İDDİASI BİR KEZ TERSİNE ÇEVRİLDİ ve sebebi kayda
    /// değer. İlk hâli şöyleydi: "tümleyeni yok sayan bir okuma
    /// FAZLA yetki üretir." O sırada kodlama `all_permissions: true`
    /// + `not_permissions` gönderiyordu ve iddia DOĞRUYDU.
    ///
    /// Ama aynı doğru, tasarımın kusuruydu: `not_permissions`ı
    /// bilmeyen bir okuyucu (yayın geri alınırsa 12 saat boyunca eski
    /// middleware) bayrağı görüp HER ŞEYİ verirdi. Kodlama kapalı
    /// tarafa düşecek şekilde değiştirildi — bayrak artık
    /// gönderilmiyor.
    ///
    /// Test silinmedi; İDDİASI YENİ SÖZLEŞMEYE ÇEVRİLDİ. Artık
    /// tümleyeni yok saymak hiçbir izin bırakmıyor: kullanıcı ekrana
    /// giremez. Eksik yetki GÖRÜNÜR ve düzeltilir; fazla yetki
    /// görünmez ve zararlıdır (Kural 60).
    /// </summary>
    [Fact]
    public void Tumleyen_YoksayilirsaYetkiKaybolur_FazlaYetkiDogmaz()
    {
        var eksik = PermissionCatalog.Keys.PaymentPlanApprove;
        var izinler = Katalog().Where(x => x != eksik).ToArray();

        var claims = JetonIzinKodlamasi.Yaz(izinler);

        var dogru = JetonIzinKodlamasi.Oku(alan =>
            claims.Where(c => c.Type == alan).Select(c => c.Value));

        Assert.DoesNotContain(eksik, dogru);
        Assert.Equal(izinler.Length, dogru.Count);

        // TÜMLEYENİ GÖRMEYEN OKUMA: kapalı tarafa düşer.
        var tumleyensiz = JetonIzinKodlamasi.Oku(alan =>
            alan == JetonIzinKodlamasi.TumleyenAlani
                ? []
                : claims.Where(c => c.Type == alan).Select(c => c.Value));

        Assert.Empty(tumleyensiz);
    }
}
