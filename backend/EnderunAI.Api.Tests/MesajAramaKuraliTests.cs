using EnderunAI.Api.Services.Messaging;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// MESAJ ARAMA KURALI — SAF TESTLER.
///
/// Kural iki yerde geçerli (sunucu + ekran) ve tek yerde yaşıyor.
/// İki yere gömülseydi eşzamanlılık paketinde yaşadığımızın aynısı
/// olurdu: iki bariyer birbirini örter, hiçbiri tek başına
/// sondalanamaz ve yeşil hiçbir şey söylemez (Kural 25).
/// </summary>
public sealed class MesajAramaKuraliTests
{
    /// <summary>
    /// ÜÇ HARFTEN KISA SORGU GEÇMEZ.
    ///
    /// Ölçüldü (2026-08-25): iki harfte trigram indeksi devre dışı
    /// kalıyor, 200 bin satırda 86 ms sıra taraması. İkinci indeks
    /// açmak yazmayı %25 ağırlaştırırdı.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData(" ab ")]
    public void UcHarftenKisa_Gecmez(string? sorgu) =>
        Assert.False(MesajAramaKurali.Gecerli(sorgu));

    [Theory]
    [InlineData("abc")]
    [InlineData("  abc  ")]
    [InlineData("insaat")]
    public void UcHarfVeUzeri_Gecer(string sorgu) =>
        Assert.True(MesajAramaKurali.Gecerli(sorgu));

    /// <summary>
    /// TÜRKÇE HARF DE HARFTİR.
    ///
    /// "İŞÇ" üç harftir. Katlama sonrası uzunluk değişmediği için
    /// geçmeli; katlama karakter SAYISINI değiştirseydi kullanıcı
    /// üç harf yazıp "en az 3 harf" uyarısı alırdı.
    /// </summary>
    [Theory]
    [InlineData("İŞÇ")]
    [InlineData("şğü")]
    public void TurkceUcHarf_Gecer(string sorgu) =>
        Assert.True(MesajAramaKurali.Gecerli(sorgu));

    /// <summary>
    /// NORMALİZE, VERİTABANIYLA AYNI KATLAMAYI YAPAR.
    ///
    /// `messages.SearchFold` kolonu `enderun_fold` ile üretiliyor.
    /// Buradaki katlama ondan ayrışırsa aynı arama bir yerde bulur,
    /// diğerinde bulamaz.
    /// </summary>
    [Theory]
    [InlineData("İNŞAAT", "insaat")]
    [InlineData("  ŞANTİYE  ", "santiye")]
    [InlineData("Ölçüm", "olcum")]
    public void Normalize_TurkceKatlar(string girdi, string beklenen) =>
        Assert.Equal(beklenen, MesajAramaKurali.Normalize(girdi));
}
