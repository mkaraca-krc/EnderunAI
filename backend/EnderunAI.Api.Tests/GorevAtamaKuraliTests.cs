using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Common;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// GÖREV TÜRÜ VE ATAMA KURALI — SAF TESTLER.
///
/// `MasrafMerkeziKuraliTests` ile aynı gerekçe: kural veritabanı
/// istemiyor, dolayısıyla üç yazma yolunun ortak kuralı doğrudan ve
/// milisaniyede sınanabiliyor. Uçtan uca testler kuralın ÇAĞRILDIĞINI
/// gösteriyor; bu dosya kuralın kendisinin DOĞRU olduğunu.
/// </summary>
public sealed class GorevAtamaKuraliTests
{
    private static readonly Guid Kullanici = Guid.NewGuid();
    private static readonly Guid Personel = Guid.NewGuid();

    // ───────── Tür ─────────

    [Fact]
    public void TurSecilmemisse_Reddedilir()
    {
        var hata = GorevAtamaKurali.Dogrula(WorkTaskKind.Belirsiz, null, null);

        Assert.NotNull(hata);
        Assert.Contains("Görev türü zorunludur", hata);
    }

    [Fact]
    public void TanimsizTurSayisi_Reddedilir()
    {
        /*
         * ENUM BİR SAYI SÜTUNUDUR, SINIRINI KENDİSİ SAVUNMAZ.
         *
         * `(WorkTaskKind)99` gönderen bir istemci `Belirsiz` DEĞİL,
         * yani ilk kapıdan geçer. Bu test o ikinci deliği kapatıyor.
         */
        var hata = GorevAtamaKurali.Dogrula((WorkTaskKind)99, null, null);

        Assert.NotNull(hata);
        Assert.Contains("tanınmıyor", hata);
    }

    [Theory]
    [InlineData(WorkTaskKind.IsEmri)]
    [InlineData(WorkTaskKind.Hatirlatma)]
    public void GecerliTur_Kabul_POZITIF_KONTROL(WorkTaskKind tur)
    {
        /*
         * POZİTİF KONTROL: yukarıdaki iki test, kural HER İSTEĞİ
         * reddetse de yeşil kalırdı. Bu test o ihtimali kapatıyor.
         */
        Assert.Null(GorevAtamaKurali.Dogrula(tur, null, null));
    }

    // ───────── Atama ─────────

    [Fact]
    public void IkiAtamaBirden_Reddedilir()
    {
        /*
         * ASIL İDDİA. "Bu işi kim yapacak" sorusunun tek cevabı olur.
         *
         * Alternatif tasarım — ikisini de kabul edip ekranda bir
         * öncelik kuralıyla seçmek — reddedildi: öncelik kuralı,
         * kaynaklar ayrıştığında sessizce birini seçer ve hangisinin
         * doğru olduğunu kimse bilemez. Bugün aynı deseni dördüncü kez
         * düzelttik (ETİKET/1).
         */
        var hata = GorevAtamaKurali.Dogrula(
            WorkTaskKind.IsEmri, Kullanici, Personel);

        Assert.NotNull(hata);
        Assert.Contains("ikisi birden seçilemez", hata);
    }

    [Fact]
    public void TekAtama_Kabul_POZITIF_KONTROL()
    {
        Assert.Null(GorevAtamaKurali.Dogrula(
            WorkTaskKind.IsEmri, Kullanici, null));

        Assert.Null(GorevAtamaKurali.Dogrula(
            WorkTaskKind.IsEmri, null, Personel));
    }

    [Fact]
    public void AtamasizGorev_Kabul()
    {
        /*
         * REDDEDİLEN ŞEY YOKLUK DEĞİL, ÇELİŞKİ.
         *
         * Henüz kimseye verilmemiş bir iş emri gerçek bir durumdur;
         * bunu reddetmek, görevi açan kişiyi atamayı uydurmaya
         * zorlardı.
         */
        Assert.Null(GorevAtamaKurali.Dogrula(
            WorkTaskKind.IsEmri, null, null));
    }

    [Fact]
    public void TurKapisi_AtamadanONCE_Olculur()
    {
        /*
         * SIRA ÖNEMLİ VE ÖLÇÜLÜYOR.
         *
         * Türsüz VE iki atamalı bir istekte hangi hata döner? Tür.
         * Sebebi: tür, isteğin ne olduğunu söyleyen alan; atama, o şeyin
         * kime verildiğini. Ne olduğu bilinmeyen bir isteğin atamasını
         * tartışmak sırayı ters çevirir.
         *
         * Bu test sırayı SABİTLİYOR: biri kapıları yer değiştirirse
         * kırmızı verir ve kararın bilinçli olduğu hatırlanır.
         */
        var hata = GorevAtamaKurali.Dogrula(
            WorkTaskKind.Belirsiz, Kullanici, Personel);

        Assert.NotNull(hata);
        Assert.Contains("Görev türü zorunludur", hata);
        Assert.DoesNotContain("ikisi birden", hata);
    }
}
