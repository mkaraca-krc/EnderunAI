using EnderunAI.Api.Services.Upload;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// DOSYA İÇERİK DENETİMİ — UZANTIYA VE İSTEMCİYE GÜVENMEDEN (MESAJ/3 C2).
///
/// ═══ ÖLÇÜLEN EKSİK ═══
///
/// Mevcut yükleme yolu YALNIZ uzantıya bakıyordu; kaynakta içerik
/// doğrulaması sıfır eşleşme veriyordu. `kotu.exe` dosyasını
/// `rapor.pdf` diye yeniden adlandırmak uzantı kapısını geçmeye
/// yetiyordu.
/// </summary>
public sealed class DosyaIcerikDenetimiTests
{
    private static byte[] Bayt(params int[] degerler) =>
        degerler.Select(x => (byte)x).ToArray();

    /// <summary>GERÇEK İMZALAR TANINIYOR.</summary>
    [Theory]
    [InlineData(".pdf", new[] { 0x25, 0x50, 0x44, 0x46, 0x2D })]
    [InlineData(".png", new[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })]
    [InlineData(".jpg", new[] { 0xFF, 0xD8, 0xFF, 0xE0 })]
    [InlineData(".gif", new[] { 0x47, 0x49, 0x46, 0x38, 0x39 })]
    [InlineData(".zip", new[] { 0x50, 0x4B, 0x03, 0x04 })]
    [InlineData(".docx", new[] { 0x50, 0x4B, 0x03, 0x04 })]
    public void GercekDosya_Uyuyor(string uzanti, int[] bas)
    {
        Assert.Equal(
            DosyaIcerikDenetimi.Sonuc.Uyuyor,
            DosyaIcerikDenetimi.Denetle(uzanti, Bayt(bas)));
    }

    /// <summary>
    /// ASIL SALDIRI: çalıştırılabilir dosya, belge uzantısıyla.
    ///
    /// `MZ` (0x4D 0x5A) Windows çalıştırılabilir başlığı. `.pdf`,
    /// `.docx`, `.png` adıyla gelse de içerik onu ele veriyor.
    /// </summary>
    [Theory]
    [InlineData(".pdf")]
    [InlineData(".docx")]
    [InlineData(".png")]
    [InlineData(".zip")]
    [InlineData(".txt")]
    [InlineData(".csv")]
    public void CalistirilabilirDosya_HangiUzantiylaGelirseGelsin_Reddediliyor(string uzanti)
    {
        // MZ + tipik NUL dolgusu.
        var exe = Bayt(0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00);

        Assert.NotEqual(
            DosyaIcerikDenetimi.Sonuc.Uyuyor,
            DosyaIcerikDenetimi.Denetle(uzanti, exe));
    }

    /// <summary>
    /// DÜZ METİN TÜRLERİNİN SİHİRLİ BAYTI YOKTUR — KURAL FARKLI.
    ///
    /// Türkçe karakterler UTF-8'de yüksek bayt olarak geçiyor ve
    /// engellenmemeli; bu test onu da tutuyor.
    /// </summary>
    [Fact]
    public void DuzMetin_TurkceKarakterlerle_Kabul()
    {
        var metin = System.Text.Encoding.UTF8.GetBytes(
            "Şantiye raporu; ölçüm 12,5 m³\r\nİkinci satır\tsekmeli\n");

        Assert.Equal(
            DosyaIcerikDenetimi.Sonuc.Uyuyor,
            DosyaIcerikDenetimi.Denetle(".txt", metin));
        Assert.Equal(
            DosyaIcerikDenetimi.Sonuc.Uyuyor,
            DosyaIcerikDenetimi.Denetle(".csv", metin));
    }

    /// <summary>
    /// TANIMADIĞI TÜR SESSİZCE KABUL EDİLMİYOR — KAPALI TARAFA DÜŞÜYOR.
    ///
    /// Beyaz listeye yeni bir tür eklenip imzası yazılmazsa, kapı
    /// "tanımadım" der ve çağıran bunu RED olarak işler. Sessizce
    /// kabul etmek, kapının olmamasından kötüdür: kapı var sanılır.
    /// </summary>
    [Fact]
    public void TanimayanTur_TanimadimDiyor()
    {
        Assert.Equal(
            DosyaIcerikDenetimi.Sonuc.Tanimadim,
            DosyaIcerikDenetimi.Denetle(".xyz", Bayt(0x01, 0x02, 0x03)));
    }

    /// <summary>
    /// BOŞ DOSYA UYMUYOR SAYILIYOR.
    ///
    /// Boş içerik hiçbir imzayı taşımaz; "uyuyor" demek onu sessizce
    /// geçirirdi (Kural 48: boş sonuç yokluğun kanıtı değildir).
    /// </summary>
    [Fact]
    public void BosDosya_Reddediliyor()
    {
        Assert.Equal(
            DosyaIcerikDenetimi.Sonuc.Uymuyor,
            DosyaIcerikDenetimi.Denetle(".pdf", ReadOnlySpan<byte>.Empty));
    }

    /// <summary>
    /// POZİTİF KONTROL — DENETİM GERÇEKTEN AYIRT EDİYOR.
    ///
    /// Yukarıdaki testler, `Denetle` her zaman `Uymuyor` dönseydi de
    /// büyük ölçüde yeşil kalırdı. Bu test aynı uzantı için doğru
    /// içeriğin KABUL, yanlış içeriğin RED aldığını yan yana
    /// gösteriyor — ayrım gerçekten yapılıyor.
    /// </summary>
    [Fact]
    public void AyniUzanti_DogruIcerikKabul_YanlisIcerikRed()
    {
        var gercekPdf = Bayt(0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37);
        var sahtePdf = Bayt(0x4D, 0x5A, 0x90, 0x00);

        Assert.Equal(DosyaIcerikDenetimi.Sonuc.Uyuyor,
            DosyaIcerikDenetimi.Denetle(".pdf", gercekPdf));
        Assert.Equal(DosyaIcerikDenetimi.Sonuc.Uymuyor,
            DosyaIcerikDenetimi.Denetle(".pdf", sahtePdf));
    }
}
