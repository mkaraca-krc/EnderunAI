using EnderunAI.Api.Search;
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
