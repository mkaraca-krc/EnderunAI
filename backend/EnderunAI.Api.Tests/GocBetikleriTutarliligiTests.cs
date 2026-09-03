using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// GÖÇ BETİKLERİ — PROVA İLE UYGULAMA AYNI YOLDAN GEÇSİN.
///
/// ── DOĞURAN OLAY (2026-09-03, DEPARTMAN/1) ──
///
/// `goc-provasi.sh` GEÇTİ, hemen ardından `goc-uygula.sh` aynı göçü
/// canlıya uygularken DÜŞTÜ. Sebep göç değildi: `dotnet ef` çağrısı üç
/// ayrı yerde, üç ayrı ortamla yazılmıştı.
///
///   prova    : JWT_SECRET veriyordu  + --no-build
///   uygulama : JWT_SECRET vermiyordu + --no-build YOK (yeniden derliyor)
///
/// İkisi de provanın SINAYAMADIĞI noktaydı, çünkü fark provanın
/// kendisindeydi. İkinci ayrışma daha sinsiydi: prova diskteki mevcut
/// ikiliyi doğruluyor, uygulama yeniden derleyip BAŞKA bir ikiliden
/// göç uyguluyordu.
///
/// ── SONUCU SİNSİYDİ ──
///
/// Göç canlıya UYGULANDI, betik yine de çıkış 1 verdi. Araç, işin
/// yapılmadığını değil, YAPILDIĞI HÂLDE yapılmadığını söyledi. Kaydına
/// güvenilemeyen bir dağıtım aracı, olmayan bir aracın iki katı
/// zararlıdır.
///
/// ── BU TESTLERİN İŞİ ──
///
/// Kabuk betikleri testsiz bir yüzey; ayrışmanın geri gelmesi hiçbir
/// yerde kırmızıya dönmezdi.
/// </summary>
public sealed class GocBetikleriTutarliligiTests
{
    private static string BetikYolu(string ad)
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);

        while (dizin is not null &&
               !Directory.Exists(Path.Combine(dizin.FullName, "deploy", "scripts")))
        {
            dizin = dizin.Parent;
        }

        Assert.True(dizin is not null, "Repo kökü bulunamadı.");
        return Path.Combine(dizin!.FullName, "deploy", "scripts", ad);
    }

    private static string BetikOku(string ad)
    {
        var yol = BetikYolu(ad);
        Assert.True(File.Exists(yol), $"Betik yok: {yol}");
        return File.ReadAllText(yol);
    }

    /// <summary>
    /// Betiğin YALNIZ KODU — `#` ile başlayan yorum satırları soyulmuş.
    ///
    /// NEDEN GEREKLİ: bu dosyanın ilk hâli `JWT_SECRET` kelimesini ham
    /// metinde arıyordu ve SONDA onu yakaladı — atama satırı silindiği
    /// hâlde test YEŞİL kaldı, çünkü kelime betikteki AÇIKLAMA
    /// YORUMUNDA geçiyordu. Yani hatayı anlatan yorum, hatanın testini
    /// boşa çıkarıyordu. Aynı tuzağa aynı gün ön yüzde de düşülmüştü.
    /// </summary>
    private static string Kod(string ad) =>
        string.Join(
            "\n",
            BetikOku(ad)
                .Split('\n')
                .Where(satir => !satir.TrimStart().StartsWith('#')));

    [Fact]
    public void Betikler_Okunabiliyor_POZITIF_KONTROL()
    {
        /*
         * Yol bozulursa aşağıdaki testler "bulunamadı" diye değil, boş
         * metinde arayarak yanlış cevap verirdi (Kural 48).
         */
        Assert.Contains("goc_onkosul_dogrula", Kod("goc-uygula.sh"));
        Assert.Contains("prova_baglanti", Kod("goc-provasi.sh"));
        Assert.Contains("ef_kos", Kod("goc-ortak.sh"));
    }

    [Fact]
    public void HerIkiBetik_De_ORTAK_ef_kos_Kullaniyor()
    {
        /*
         * ASIL KURAL BU: ayrışacak nokta kalmasın. `ef_kos` tek yerde
         * tanımlı; ortam, bayraklar ve proje yolu oradan geliyor.
         */
        Assert.Contains("ef_kos", Kod("goc-uygula.sh"));
        Assert.Contains("ef_kos", Kod("goc-provasi.sh"));
        Assert.Contains("goc-ortak.sh", Kod("goc-uygula.sh"));
        Assert.Contains("goc-ortak.sh", Kod("goc-provasi.sh"));
    }

    [Fact]
    public void HicbirBetik_dotnet_ef_i_DOGRUDAN_CAGIRMIYOR()
    {
        /*
         * `ef_kos`u atlayıp doğrudan `$EF_ARACI` çağırmak, ayrışmanın
         * geri gelmesinin TAM OLARAK yoluydu. Ortak katmanın kendisi
         * hariç kimse aracı ÇAĞIRAMAZ.
         *
         * ── TESTİN İLK HÂLİ FAZLA KÜNTTÜ, SONDA GÖSTERDİ ──
         *
         * Önce `"$EF_ARACI"` dizesinin HİÇ geçmemesi aranıyordu ve
         * `goc-provasi.sh`i kırmızıya düşürdü. Oradaki eşleşme bir
         * çağrı değil, aracın VARLIK KONTROLÜYDÜ
         * (`[ ! -x "$EF_ARACI" ]`) — ve prova tek başına da
         * koşulabildiği için o kontrolün orada durması meşru.
         *
         * Aranan şey "aracın adı geçmesin" değil, "araç ÇAĞRILMASIN".
         * Bu yüzden alt komutlarıyla birlikte aranıyor.
         */
        foreach (var betik in new[] { "goc-uygula.sh", "goc-provasi.sh" })
        {
            var kod = Kod(betik);

            Assert.DoesNotContain("\"$EF_ARACI\" migrations", kod);
            Assert.DoesNotContain("\"$EF_ARACI\" database", kod);
            Assert.DoesNotContain("\"$EF_ARACI\" \"$@\"", kod);
        }

        // POZİTİF KONTROL: çağrı gerçekten ortak katmanda duruyor.
        // Bu olmadan yukarıdaki üç iddia, araç hiç kullanılmasa da
        // yeşil kalırdı.
        Assert.Contains("\"$EF_ARACI\" \"$@\"", Kod("goc-ortak.sh"));
    }

    [Fact]
    public void GocBetikleri_JWT_SECRET_ISTEMIYOR()
    {
        /*
         * KÖK ÇÖZÜMÜN MEKANİK KARŞILIĞI. `HrDbContextFactory`
         * yazılmadan önce göç yolu bu değişkenin VARLIĞINA bağlıydı;
         * bir göçün hiç kullanmadığı bir uygulama sırrına muhtaç olması
         * yapısal bir kusurdu.
         *
         * Yorumlar soyuluyor: betikler bu tarihi ANLATIYOR ve anlatının
         * kendisi testi boşa çıkarmamalı.
         */
        Assert.DoesNotContain("JWT_SECRET", Kod("goc-uygula.sh"));
        Assert.DoesNotContain("JWT_SECRET", Kod("goc-provasi.sh"));
        Assert.DoesNotContain("JWT_SECRET", Kod("goc-ortak.sh"));
    }

    [Fact]
    public void Uygulama_OnKosulu_GOCTEN_ONCE_Dogruluyor()
    {
        /*
         * YARIDA DÜŞEN GÖÇ, HİÇ BAŞLAMAYAN GÖÇTEN PAHALIDIR.
         *
         * SAHA göçünde AppDbContext uygulandı, HrDbContext AÇILAMADI.
         * Şema yarım kalmadı ama bu ŞANSTI: o bağlamda uygulanacak göç
         * yoktu. Sıra tersine olsaydı gerçekten yarım kalırdı.
         *
         * SIRA DAVRANIŞIN PARÇASI: denetim, uygulama satırından ÖNCE
         * gelmeli. Sonra gelseydi hiçbir şeyi önlemezdi.
         */
        var kod = Kod("goc-uygula.sh");

        var denetim = kod.IndexOf("goc_onkosul_dogrula", StringComparison.Ordinal);
        var uygula = kod.IndexOf("database update", StringComparison.Ordinal);

        Assert.True(denetim >= 0, "Ön koşul denetimi çağrılmıyor.");
        Assert.True(uygula >= 0, "Uygulama çağrısı bulunamadı.");
        Assert.True(
            denetim < uygula,
            "Ön koşul denetimi göç uygulamasından SONRA çağrılıyor; " +
            "bu sırayla hiçbir şeyi önlemez.");
    }

    [Fact]
    public void Derleme_BIR_KEZ_Basta_Yapiliyor()
    {
        /*
         * İKİNCİ AYRIŞMA BUYDU: prova `--no-build` ile diskteki mevcut
         * ikiliyi doğruluyor, uygulama yeniden derleyip başka bir
         * ikiliden göç uyguluyordu. Kaynak arada değişmişse doğrulanan
         * ile uygulanan AYNI DEĞİLDİ.
         */
        Assert.Contains("goc_derle", Kod("goc-uygula.sh"));
        Assert.Contains("--no-build", Kod("goc-ortak.sh"));
    }

    [Fact]
    public void HerIkiBaglam_Da_TEK_YERDE_Tanimli()
    {
        /*
         * Bağlam listesi iki betikte ayrı ayrı yazılsaydı, üçüncü bir
         * DbContext eklendiğinde biri güncellenir diğeri unutulurdu —
         * bu olayın kendisi tam olarak o hataydı.
         */
        var ortak = Kod("goc-ortak.sh");

        Assert.Contains("GOC_BAGLAMLAR", ortak);
        Assert.Contains("AppDbContext", ortak);
        Assert.Contains("HrDbContext", ortak);
    }
}
