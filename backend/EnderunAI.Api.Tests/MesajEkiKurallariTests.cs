using EnderunAI.Api.Services.Messaging;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// MESAJ EKİ — AD VE BOYUT KAPISI (MESAJ/3 C1-C4).
///
/// Bu dosya İÇERİK denetimini sınamıyor; o ayrı bir kapı ve ayrı bir
/// test dosyası (`DosyaIcerikDenetimiTests`). İkisi de geçilmeden
/// dosya kabul edilmiyor — ayrı ayrı sınanmaları da o yüzden.
/// </summary>
public sealed class MesajEkiKurallariTests
{
    private const long BirMb = 1024 * 1024;

    [Theory]
    [InlineData("rapor.pdf")]
    [InlineData("tablo.xlsx")]
    [InlineData("sunum.pptx")]
    [InlineData("foto.JPG")]        // büyük harf uzantı da kabul
    [InlineData("arsiv.zip")]
    [InlineData("liste.csv")]
    [InlineData("Şantiye Ölçüm Raporu.pdf")]  // Türkçe ve boşluklu ad
    public void IzinliDosya_KabulEdiliyor(string ad)
    {
        Assert.Equal(
            MesajEkiKurallari.Karar.Kabul,
            MesajEkiKurallari.Denetle(ad, 1 * BirMb));
    }

    /// <summary>
    /// ÇİFT UZANTI — DAVRANIŞ VARSAYILMIYOR, ÖLÇÜLÜYOR (C3).
    ///
    /// `Path.GetExtension("rapor.pdf.exe")` SON uzantıyı verir.
    /// Kaynağı okuyup "herhalde öyledir" demek yerine burada
    /// sınanıyor: yarın ad çözümü değişirse test düşer.
    /// </summary>
    [Theory]
    [InlineData("rapor.pdf.exe")]
    [InlineData("resim.png.bat")]
    [InlineData("belge.docx.js")]
    [InlineData("arsiv.zip.ps1")]
    public void CiftUzanti_Reddediliyor(string ad)
    {
        Assert.Equal(
            MesajEkiKurallari.Karar.UzantiYasakli,
            MesajEkiKurallari.Denetle(ad, 1 * BirMb));
    }

    [Theory]
    [InlineData("kotu.exe")]
    [InlineData("betik.sh")]
    [InlineData("kurulum.msi")]
    [InlineData("kod.jar")]
    public void YasakliUzanti_Reddediliyor(string ad)
    {
        Assert.Equal(
            MesajEkiKurallari.Karar.UzantiYasakli,
            MesajEkiKurallari.Denetle(ad, 1 * BirMb));
    }

    /// <summary>
    /// YOL İÇEREN AD REDDEDİLİYOR (C4).
    ///
    /// Diskteki ad zaten sunucunun ürettiği GUID; kullanıcının adı yol
    /// oluşturmada KULLANILMIYOR. Kapı yine de var: "nasılsa
    /// kullanmıyoruz" bir savunma değildir.
    /// </summary>
    [Theory]
    [InlineData("../../etc/passwd.pdf")]
    [InlineData("/etc/shadow.txt")]
    [InlineData("klasor\\dosya.pdf")]
    [InlineData("..\\..\\gizli.docx")]
    public void YolIcerenAd_Reddediliyor(string ad)
    {
        Assert.Equal(
            MesajEkiKurallari.Karar.YolIceriyor,
            MesajEkiKurallari.Denetle(ad, 1 * BirMb));
    }

    /// <summary>
    /// BOYUT SINIRI — TAM SINIRDA KABUL, BİR BAYT ÜSTÜ RED.
    ///
    /// Sınır testleri "yaklaşık" olamaz: 20 MB'ın kendisi geçmeli,
    /// 20 MB + 1 bayt geçmemeli. Aradaki fark bir karşılaştırma
    /// operatörüdür ve sessizce yanlış yazılır.
    /// </summary>
    [Fact]
    public void BoyutSiniri_TamSinirdaKabul_UstuRed()
    {
        Assert.Equal(
            MesajEkiKurallari.Karar.Kabul,
            MesajEkiKurallari.Denetle("rapor.pdf", MesajEkiKurallari.DosyaBasinaEnFazlaBayt));

        Assert.Equal(
            MesajEkiKurallari.Karar.CokBuyuk,
            MesajEkiKurallari.Denetle("rapor.pdf", MesajEkiKurallari.DosyaBasinaEnFazlaBayt + 1));
    }

    /// <summary>BOŞ DOSYA DA REDDEDİLİYOR — 0 bayt bir dosya değildir.</summary>
    [Fact]
    public void BosDosya_Reddediliyor()
    {
        Assert.Equal(
            MesajEkiKurallari.Karar.CokBuyuk,
            MesajEkiKurallari.Denetle("rapor.pdf", 0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void BosAd_Reddediliyor(string? ad)
    {
        Assert.Equal(
            MesajEkiKurallari.Karar.AdBos,
            MesajEkiKurallari.Denetle(ad, 1 * BirMb));
    }

    /// <summary>UZANTISIZ AD — beyaz listeye giremez.</summary>
    [Fact]
    public void UzantisizAd_Reddediliyor()
    {
        Assert.Equal(
            MesajEkiKurallari.Karar.UzantiIzinsiz,
            MesajEkiKurallari.Denetle("dosya", 1 * BirMb));
    }

    /// <summary>
    /// POZİTİF KONTROL — KAPI GERÇEKTEN AYIRT EDİYOR.
    ///
    /// Yukarıdaki testlerin çoğu `Denetle` her zaman bir RED
    /// dönseydi de yeşil kalırdı. Bu test aynı boyutta, yalnız
    /// uzantısı farklı iki adı yan yana koyuyor: biri kabul, biri
    /// red. Ayrım gerçekten yapılıyor (Kural 48).
    /// </summary>
    [Fact]
    public void AyniBoyut_UzantiyaGoreAyriKarar()
    {
        Assert.Equal(
            MesajEkiKurallari.Karar.Kabul,
            MesajEkiKurallari.Denetle("a.pdf", 5 * BirMb));

        Assert.Equal(
            MesajEkiKurallari.Karar.UzantiYasakli,
            MesajEkiKurallari.Denetle("a.exe", 5 * BirMb));
    }

    /// <summary>
    /// HER RED SEBEBİNİ SÖYLÜYOR — sessiz red yok.
    ///
    /// Kullanıcı "dosya yüklenmedi" değil NEDEN yüklenmediğini
    /// görmeli. Boş ya da genel bir metin, üç ayrı sebebi aynı
    /// gösterirdi.
    /// </summary>
    [Fact]
    public void HerKarar_AnlamliMesajDonduruyor()
    {
        foreach (var karar in Enum.GetValues<MesajEkiKurallari.Karar>())
        {
            var mesaj = MesajEkiKurallari.Mesaj(karar);
            Assert.False(string.IsNullOrWhiteSpace(mesaj));
            Assert.True(mesaj.Length > 10, $"{karar} için mesaj çok kısa: {mesaj}");
        }
    }
}
