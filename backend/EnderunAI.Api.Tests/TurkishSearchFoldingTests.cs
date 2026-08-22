using EnderunAI.Api.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using EnderunAI.Api.Tests.Infrastructure;
using EnderunAI.Api.Data;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// TÜRKÇE KATLAMA — SUNUCU İLE EKRAN AYNI SONUCU VERMELİ.
///
/// Küçük listeler ekranda (`lib/search/fold.ts`), büyük listeler
/// sunucuda süzülüyor. İki katlama ayrışırsa aynı arama bir listede
/// kaydı bulur, diğerinde bulamaz — kullanıcı hangisinin doğru olduğunu
/// bilemez ve "kaydım kayboldu" der.
///
/// BEKLENEN DEĞERLER fold.ts'in ÇIKTISI: bu dosyadaki her satır
/// tarayıcıda `foldTurkish(...)` çalıştırılarak alındı, elle
/// yazılmadı.
/// </summary>
public sealed class TurkishSearchFoldingTests
{
    [Theory]
    // Türkçe harfler ASCII karşılığına iniyor.
    [InlineData("Şube Ticaret", "sube ticaret")]
    [InlineData("Müdürlüğü", "mudurlugu")]
    [InlineData("Çınar Yapı", "cinar yapi")]
    [InlineData("Ağrı", "agri")]
    [InlineData("Öztürk", "ozturk")]
    // ASIL TUZAK: Türkçe kültürde ToLower() burada "schneıder" üretir
    // ve marka aranamaz hale gelir.
    [InlineData("SCHNEIDER Elektrik", "schneider elektrik")]
    [InlineData("İSTANBUL", "istanbul")]
    [InlineData("ISPARTA", "isparta")]
    // BU SEKTÖRÜN EN SIK KELİMESİ: neredeyse her cari unvanında geçiyor
    // ve düzeltmeden önce "insaat" yazan hiçbirini bulamıyordu.
    [InlineData("YILMAZ İNŞAAT", "yilmaz insaat")]
    [InlineData("Yılmaz İnşaat", "yilmaz insaat")]
    [InlineData("İZMİR Ticaret", "izmir ticaret")]
    // Zaten katlanmış metin değişmiyor (idempotent).
    [InlineData("sube ticaret", "sube ticaret")]
    [InlineData("", "")]
    public void Katlama_FoldTsIleAyniSonucuVerir(string girdi, string beklenen)
    {
        Assert.Equal(beklenen, TurkishSearch.Fold(girdi));
    }

    /// <summary>
    /// KÜLTÜR BAĞIMSIZ. Sunucunun kültürü Türkçe olarak ayarlansa bile
    /// katlama değişmemeli — doğruluk konteynerin dil ayarına bağlı
    /// kalmamalı.
    /// </summary>
    [Fact]
    public void TurkKulturundeBile_AyniSonuc()
    {
        var onceki = Thread.CurrentThread.CurrentCulture;

        try
        {
            Thread.CurrentThread.CurrentCulture =
                new System.Globalization.CultureInfo("tr-TR");

            Assert.Equal("schneider elektrik", TurkishSearch.Fold("SCHNEIDER Elektrik"));

            // Kültür duyarlı ToLower() BAŞKA sonuç veriyor — tuzağın
            // hâlâ orada olduğunun kanıtı.
            Assert.NotEqual(
                "schneider elektrik",
                "SCHNEIDER Elektrik".ToLower());
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = onceki;
        }
    }
}

/// <summary>
/// ÜÇÜNCÜ KATMAN: VERİTABANI.
///
/// Katlama üç yerde yaşıyor — ekran (`lib/search/fold.ts`), sunucu
/// belleği (`TurkishSearch.Fold`) ve veritabanı (`enderun_fold`).
/// Küçük listeler ekranda, büyük listeler sunucuda süzülüyor; üçü
/// ayrışırsa aynı arama bir listede kaydı bulur, ötekinde bulamaz.
///
/// Bu test veritabanındaki fonksiyonu GERÇEKTEN çağırıp sunucu
/// sürümüyle karşılaştırıyor. Aynı beklenen değerleri iki yere elle
/// yazmak yeterli olmazdı: ikisi birlikte yanlış olabilirdi.
/// </summary>
[Collection("Integration")]
public sealed class TurkishFoldFunctionTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData("YILMAZ İNŞAAT")]
    [InlineData("SCHNEIDER Elektrik")]
    [InlineData("Şube Müdürlüğü")]
    [InlineData("Çınar Yapı A.Ş.")]
    [InlineData("İSTANBUL")]
    [InlineData("ISPARTA")]
    [InlineData("Ağrı Dağı")]
    public async Task VeritabaniKatlamasi_SunucuylaAyni(string girdi)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var veritabani = await db.Database
            .SqlQuery<string>($"SELECT enderun_fold({girdi}) AS \"Value\"")
            .SingleAsync();

        Assert.Equal(TurkishSearch.Fold(girdi), veritabani);
    }
}
