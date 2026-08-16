using EnderunAI.Api.Services.Units;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// BİRİM YAZIMI NORMALİZASYONU.
///
/// Asıl güvence İKİ YÖNLÜ: eşdeğer yazımlar birleşmeli AMA farklı
/// fiziksel birimler ASLA birleşmemeli. İkincisi olmadan fonksiyon
/// tehlikeli olurdu — "m" yazan bir reçete satırının "Adet" kartına
/// bağlanması, reçeteye yanlış miktar yazmak demektir ve bunu sonradan
/// fark etmek çok zordur.
///
/// NEDEN GEREKTİ: poz kütüphanesinde adet birimi "Ad" (7.429 kayıt) ve
/// "AD" (7.199 kayıt); Enderun stok kartlarında "Adet". Reçete
/// aktarımı birimi kartla karşılaştırdığı için "Ad" yazan HER satır
/// uyuşmazlık sayılıp atlanıyordu.
/// </summary>
public sealed class UnitNormalizerTests
{
    [Theory]
    [InlineData("Ad")]
    [InlineData("AD")]
    [InlineData("ad")]
    [InlineData("adet")]
    [InlineData("Adet")]
    [InlineData("ADET")]
    [InlineData("adt")]
    public void AdetYazimlari_TekKanonikDegere_Iner(string yazim)
    {
        Assert.Equal("ADET", UnitNormalizer.Normalize(yazim));
    }

    /// <summary>Baştaki/sondaki boşluk ve karma harf tolere edilir.</summary>
    [Theory]
    [InlineData("  Ad  ")]
    [InlineData("\tADET\n")]
    [InlineData("aDeT")]
    public void BoslukVeKarmaHarf_ToleransliDir(string yazim)
    {
        Assert.Equal("ADET", UnitNormalizer.Normalize(yazim));
    }

    /// <summary>
    /// FARKLI FİZİKSEL BİRİMLER BİRLEŞMEZ. Bu testin kırılması,
    /// sözlüğe yanlış bir eşdeğerlik girdiğini gösterir.
    /// </summary>
    [Theory]
    [InlineData("m", "Adet")]
    [InlineData("m²", "m")]
    [InlineData("Kg", "Adet")]
    [InlineData("m³", "m²")]
    [InlineData("Saat", "Adet")]
    public void FarkliBirimler_Esitlenmez(string sol, string sag)
    {
        Assert.NotEqual(UnitNormalizer.Normalize(sol), UnitNormalizer.Normalize(sag));
        Assert.False(UnitNormalizer.AreEquivalent(sol, sag));
    }

    /// <summary>Aynı birimin farklı yazımları eşdeğer sayılır.</summary>
    [Theory]
    [InlineData("Ad", "Adet")]
    [InlineData("MT", "m")]
    [InlineData("m2", "m²")]
    [InlineData("Sa", "SAAT")]
    [InlineData("LT", "l")]
    public void AyniBirimin_FarkliYazimlari_Esdegerdir(string sol, string sag)
    {
        Assert.True(UnitNormalizer.AreEquivalent(sol, sag));
    }

    /// <summary>
    /// Sözlükte olmayan birim UYDURULMAZ, olduğu gibi döner. Tanınmayan
    /// bir birimi rastgele bir karşılığa eşlemek sessiz yanlış eşleşme
    /// üretirdi.
    /// </summary>
    [Fact]
    public void BilinmeyenBirim_OlduguGibiDoner()
    {
        Assert.Equal("ÇUVAL", UnitNormalizer.Normalize("çuval"));
        Assert.True(UnitNormalizer.AreEquivalent("çuval", "ÇUVAL"));
        Assert.False(UnitNormalizer.AreEquivalent("çuval", "Adet"));
    }

    /// <summary>
    /// BOŞ BİRİMLER EŞİT SAYILMAZ: birimi olmayan iki kayıt "aynı
    /// birimde" demek değil, bilgi eksik demektir.
    /// </summary>
    [Fact]
    public void BosBirim_EsitSayilmaz()
    {
        Assert.Equal(string.Empty, UnitNormalizer.Normalize(null));
        Assert.Equal(string.Empty, UnitNormalizer.Normalize("   "));

        Assert.False(UnitNormalizer.AreEquivalent(null, null));
        Assert.False(UnitNormalizer.AreEquivalent("", "Adet"));
    }
}
