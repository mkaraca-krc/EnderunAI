using EnderunAI.Api.Models.Finance;
using EnderunAI.Api.Services.Finance;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// ÖDEME PLANININ SAF KARARLARI (ÖP/1a · K2, K3, K4, K6, K8, K10).
///
/// VERİTABANI YOK: kararlar saf fonksiyonlarda olduğu için testler
/// milisaniyeler sürüyor ve sondalar TEK bir kararı yalıtabiliyor.
/// ÇEK/2'de kilidi iki yerde kurmuş ve sondanın hangisini ölçtüğünü
/// göremez hâle gelmiştim (Kural 25, 45); bu ayrım onun dersi.
/// </summary>
public sealed class OdemePlaniKurallariTests
{
    private static OdemePlaniSatiri OnaylanmisSatir()
    {
        var cari = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var hesap = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var vade = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);

        return new OdemePlaniSatiri
        {
            CurrentAccountId = cari,
            OnerilenTutar = 50_000m,
            OnaylananTutar = 50_000m,
            Yontem = OdemeYontemi.HavaleEft,
            CekVadesi = vade,
            Oncelik = 3,
            CashAccountId = hesap,
            Karar = OdemeSatirKarari.Onaylandi,

            OnayliCurrentAccountId = cari,
            OnayliTutar = 50_000m,
            OnayliYontem = OdemeYontemi.HavaleEft,
            OnayliCekVadesi = vade,
            OnayliOncelik = 3,
            OnayliCashAccountId = hesap
        };
    }

    // ═══ K2 — ONAY ANLIK GÖRÜNTÜSÜ ═══

    [Fact]
    public void K2_DegismemisSatir_OnayliKalir()
        => Assert.Empty(OdemePlaniKurallari.DegisenOnayAlanlari(OnaylanmisSatir()));

    /// <summary>
    /// PAKETİN EN KRİTİK KURALI: onaydan sonra tutar değişirse ödeme
    /// yapılmaz. Aksi hâlde onay hiçbir şey ifade etmez.
    /// </summary>
    [Fact]
    public void K2_TutarDegisirse_YakalanIr()
    {
        var satir = OnaylanmisSatir();
        satir.OnaylananTutar = 75_000m;

        Assert.Contains("Tutar", OdemePlaniKurallari.DegisenOnayAlanlari(satir));
    }

    /// <summary>
    /// ÖNCELİK DE ONAYIN PARÇASI (K7). Sırayı değiştirmek, kimin
    /// parasını alacağını değiştirmektir — biçim değil, ödeme kararı.
    /// </summary>
    [Fact]
    public void K2_OncelikDegisirse_YakalanIr()
    {
        var satir = OnaylanmisSatir();
        satir.Oncelik = 1;

        Assert.Contains("Öncelik", OdemePlaniKurallari.DegisenOnayAlanlari(satir));
    }

    [Theory]
    [InlineData("Cari")]
    [InlineData("Ödeme yöntemi")]
    [InlineData("Çek vadesi")]
    [InlineData("Çıkış hesabı")]
    public void K2_HerOnayliAlan_DegisinceYakalanIr(string alan)
    {
        var satir = OnaylanmisSatir();

        switch (alan)
        {
            case "Cari":
                satir.CurrentAccountId = Guid.NewGuid(); break;
            case "Ödeme yöntemi":
                satir.Yontem = OdemeYontemi.Cek; break;
            case "Çek vadesi":
                satir.CekVadesi = satir.CekVadesi!.Value.AddDays(1); break;
            case "Çıkış hesabı":
                satir.CashAccountId = Guid.NewGuid(); break;
        }

        Assert.Contains(alan, OdemePlaniKurallari.DegisenOnayAlanlari(satir));
    }

    /// <summary>Onay kaydı olmayan satır ödenemez — sessizce geçmez.</summary>
    [Fact]
    public void K2_OnaysizSatir_OdenemezSayilir()
    {
        var satir = OnaylanmisSatir();
        satir.OnayliTutar = null;

        Assert.NotEmpty(OdemePlaniKurallari.DegisenOnayAlanlari(satir));
    }

    /// <summary>
    /// AYNI GÜNÜN FARKLI SAATİ KARAR DEĞİŞİKLİĞİ DEĞİL. Vade gün
    /// bazında karşılaştırılıyor; saat farkı yüzünden satırı yeniden
    /// onaya döndürmek, kuralı gürültüye boğar ve susturulmasına yol
    /// açardı (Kural 42).
    /// </summary>
    [Fact]
    public void K2_AyniGununFarkliSaati_DegisiklikSayilmaz()
    {
        var satir = OnaylanmisSatir();
        satir.CekVadesi = satir.OnayliCekVadesi!.Value.AddHours(9);

        Assert.DoesNotContain("Çek vadesi",
            OdemePlaniKurallari.DegisenOnayAlanlari(satir));
    }

    // ═══ K3 — ÖDENEN ≤ ONAYLANAN ═══

    [Theory]
    [InlineData(100, 0, 100, false)]   // tam ödeme
    [InlineData(100, 0, 40, false)]    // kısmi — serbest
    [InlineData(100, 60, 40, false)]   // ikinci kısmi, tam dolduruyor
    [InlineData(100, 60, 41, true)]    // aşıyor
    [InlineData(100, 0, 100.01, true)] // kuruş bile aşamaz
    public void K3_SinirDogruIsliyor(
        decimal onaylanan, decimal halihazir, decimal yeni, bool asmali)
        => Assert.Equal(asmali,
            OdemePlaniKurallari.OdemeSiniriAsiliyorMu(onaylanan, halihazir, yeni));

    // ═══ K4 — HAZIRLAYAN ≠ ONAYLAYAN ═══

    [Fact]
    public void K4_HazirlayanKendiSatiriniOnaylayamaz()
    {
        var kisi = Guid.NewGuid();
        Assert.False(OdemePlaniKurallari.OnaylayabilirMi(kisi, kisi, null));
    }

    /// <summary>
    /// SON DEĞİŞTİREN DE ONAYLAYAMAZ. Yalnız hazırlayana bakılsaydı
    /// kural "hazırla, başkasına onaylat, sonra değiştir" ile
    /// atlatılırdı.
    /// </summary>
    [Fact]
    public void K4_SonDegistirenDeOnaylayamaz()
    {
        var kisi = Guid.NewGuid();
        Assert.False(
            OdemePlaniKurallari.OnaylayabilirMi(kisi, Guid.NewGuid(), kisi));
    }

    [Fact]
    public void K4_BaskasiOnaylayabilir()
        => Assert.True(OdemePlaniKurallari.OnaylayabilirMi(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

    // ═══ K6 — İKİ AYRI BÜTÇE SAYISI ═══

    /// <summary>
    /// ÇEK BU CUMANIN PARASINI HARCAMAZ. Tek sayıya toplanırsa hafta
    /// olduğundan pahalı görünür ve gerçek nakit ihtiyacı kaybolur.
    /// </summary>
    [Theory]
    [InlineData(OdemeYontemi.HavaleEft, true, false)]
    [InlineData(OdemeYontemi.Nakit, true, false)]
    [InlineData(OdemeYontemi.Cek, false, true)]
    public void K6_NakitVeYukumlulukAyri(
        OdemeYontemi yontem, bool nakit, bool yukumluluk)
    {
        Assert.Equal(nakit, OdemePlaniKurallari.NakitCikisiMi(yontem));
        Assert.Equal(yukumluluk, OdemePlaniKurallari.GelecekYukumlulukMu(yontem));
    }

    // ═══ K8 — YAŞLANMA ═══

    [Theory]
    [InlineData(0, true)]
    [InlineData(13, true)]
    [InlineData(20, true)]
    [InlineData(21, false)]   // tam 3 hafta — DÜŞER
    [InlineData(42, false)]
    public void K8_UcHaftayiAsanOnayDuser(int gunOnce, bool gecerli)
    {
        var simdi = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(gecerli,
            OdemePlaniKurallari.OnayGecerliMi(simdi.AddDays(-gunOnce), simdi));
    }

    [Theory]
    [InlineData(6, 0)]
    [InlineData(7, 1)]
    [InlineData(20, 2)]
    public void K8_BeklemeHaftasi(int gunOnce, int beklenen)
    {
        var simdi = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(beklenen,
            OdemePlaniKurallari.BeklemeHaftasi(simdi.AddDays(-gunOnce), simdi));
    }

    // ═══ K10 — KAPANIŞ SEBEBİ ═══

    [Theory]
    [InlineData(OdemeSatirKarari.Onaylandi, OdemeSatirOdemeDurumu.Odenmedi, true)]
    [InlineData(OdemeSatirKarari.Onaylandi, OdemeSatirOdemeDurumu.KismenOdendi, true)]
    [InlineData(OdemeSatirKarari.Kismi, OdemeSatirOdemeDurumu.Odenmedi, true)]
    [InlineData(OdemeSatirKarari.Onaylandi, OdemeSatirOdemeDurumu.Odendi, false)]
    [InlineData(OdemeSatirKarari.Reddedildi, OdemeSatirOdemeDurumu.Odenmedi, false)]
    public void K10_SebepGerekliligi(
        OdemeSatirKarari karar, OdemeSatirOdemeDurumu odeme, bool gerekli)
    {
        var satir = new OdemePlaniSatiri { Karar = karar, OdemeDurumu = odeme };
        Assert.Equal(gerekli, OdemePlaniKurallari.KapanisSebebiGerekliMi(satir));
    }

    [Fact]
    public void K10_DigerSecilirse_AciklamaZorunlu()
    {
        Assert.True(OdemePlaniKurallari.KapanisAciklamasiGerekliMi(
            OdemeKapanisSebebi.Diger));
        Assert.False(OdemePlaniKurallari.KapanisAciklamasiGerekliMi(
            OdemeKapanisSebebi.ParaYetmedi));
    }
}
