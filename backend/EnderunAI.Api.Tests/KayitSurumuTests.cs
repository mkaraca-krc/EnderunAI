using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// KAYIT SÜRÜMÜ — TOLERANSIN İKİ KENARI DA SABİT.
///
/// Bu testlerin varlık sebebi tek bir cümle: **tolerans ne çok geniş
/// ne çok dar olabilir**, ve iki yanlış yönün sonuçları taban tabana
/// zıt.
///
///   ÇOK DAR (tam eşitlik)  → HER istek çakışma verir, kimse hiçbir
///                            kaydı düzenleyemez. Görünür ama felç.
///   ÇOK GENİŞ (saniye)     → gerçek çakışma KAÇAR, kayıp güncelleme
///                            sessizce olur. Görünmez ve zararlı.
///
/// Toleransın kendisi bir hile değil ZORUNLULUK: PostgreSQL zaman
/// damgasını MİKROSANİYE tutuyor, JSON'a giden değer MİLİSANİYEDE
/// kesiliyor. Tam eşitlik aranırsa tel üzerinden dönen değer hiçbir
/// zaman veritabanındakine eşit olmaz.
///
/// BU YÜZDEN İKİ KENAR DA TESTTE: biri olmadan diğeri "gereksiz
/// karmaşıklık" gibi görünür ve kaldırılır.
/// </summary>
public sealed class KayitSurumuTests
{
    private sealed class SahteKayit : BaseEntity;

    private static SahteKayit Kayit(DateTime guncelleme) =>
        new() { CreatedAtUtc = guncelleme.AddDays(-1), UpdatedAtUtc = guncelleme };

    private static readonly DateTime Damga =
        new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

    // ═══════════════════════════════════════════════════════════════
    // KENAR 1 — MİKROSANİYE FARKI EŞİT SAYILMALI
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// TELDEN DÖNEN DEĞER MİKROSANİYEYİ KAYBEDER.
    ///
    /// Bu test kırmızıya dönerse sistem FELÇ olur: her güncelleme
    /// "başkası değiştirdi" hatası alır, çünkü istemcinin geri
    /// gönderdiği damga veritabanındakinden mikrosaniye farklıdır.
    /// </summary>
    [Fact]
    public void MikrosaniyeFarki_EsitSayilir()
    {
        var kayit = Kayit(Damga.AddTicks(7_777));   // ~0,78 ms altı
        var telden = Damga;                          // milisaniyede kesilmiş

        // Fırlatmamalı.
        KayitSurumu.Dogrula(kayit, telden);
    }

    /// <summary>Ters yönde de aynı: damga tam milisaniyede olabilir.</summary>
    [Fact]
    public void MikrosaniyeFarki_TersYonde_EsitSayilir()
    {
        var kayit = Kayit(Damga);
        var telden = Damga.AddTicks(9_999);

        KayitSurumu.Dogrula(kayit, telden);
    }

    // ═══════════════════════════════════════════════════════════════
    // KENAR 2 — MİLİSANİYE FARKI FARKLI SAYILMALI
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// BİR MİLİSANİYE GERÇEK BİR DEĞİŞİKLİKTİR.
    ///
    /// Bu test kırmızıya dönerse gerçek çakışmalar KAÇAR: iki
    /// kullanıcı aynı kaydı düzenler, ikincisi birincinin yazdığını
    /// sessizce ezer. Görünmez hata.
    /// </summary>
    [Fact]
    public void MilisaniyeFarki_FarkliSayilir()
    {
        var kayit = Kayit(Damga.AddMilliseconds(1));
        var telden = Damga;

        Assert.Throws<DbUpdateConcurrencyException>(
            () => KayitSurumu.Dogrula(kayit, telden));
    }

    /// <summary>Büyük fark da yakalanmalı — kural yön bağımsız.</summary>
    [Fact]
    public void SaniyeFarki_FarkliSayilir()
    {
        var kayit = Kayit(Damga.AddSeconds(30));

        Assert.Throws<DbUpdateConcurrencyException>(
            () => KayitSurumu.Dogrula(kayit, Damga));
    }

    // ═══════════════════════════════════════════════════════════════
    // SÜRÜM ZORUNLU (Kural 39)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// EKSİK SÜRÜM ATLANMAZ, REDDEDİLİR.
    ///
    /// "Yoksa kontrolü atla" davranışı, alanı göndermeyen herkese
    /// eşzamanlılık korumasını kapatma yolu açardı.
    /// </summary>
    [Fact]
    public void SurumYoksa_Reddedilir()
    {
        var hata = Assert.Throws<ArgumentException>(
            () => KayitSurumu.Dogrula(Kayit(Damga), null));

        // Mesaj kullanıcıya NE YAPACAĞINI söylemeli.
        Assert.Contains("Sayfayı yenileyip", hata.Message);
    }

    // ═══════════════════════════════════════════════════════════════
    // OKUMA
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// HİÇ GÜNCELLENMEMİŞ KAYITTA `CreatedAtUtc` KULLANILIR.
    ///
    /// `null` dönseydi istemci sürüm gönderemez ve YENİ AÇILAN kayıt
    /// hiç düzenlenemezdi.
    /// </summary>
    [Fact]
    public void HicGuncellenmemisKayit_OlusturmaDamgasiniTasir()
    {
        var kayit = new SahteKayit { CreatedAtUtc = Damga, UpdatedAtUtc = null };

        Assert.Equal(Damga, KayitSurumu.Oku(kayit));
    }

    /// <summary>Güncellenmiş kayıtta güncelleme damgası kazanır.</summary>
    [Fact]
    public void GuncellenmisKayit_GuncellemeDamgasiniTasir()
    {
        var sonra = Damga.AddHours(3);

        Assert.Equal(sonra, KayitSurumu.Oku(Kayit(sonra)));
    }
}
