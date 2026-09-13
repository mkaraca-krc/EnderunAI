using System.Text.Json;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// K3 — DOĞRULAMA İHLALİ 500 DÖNEMEZ; ÇÖKÜŞ DE 400 DÖNEMEZ.
///
/// ═══ ÖLÇÜLEN KUSUR (2026-09-13, uçtan uca prova) ═══
///
/// İki gerçek iş kuralı ihlali kullanıcıya şöyle dönüyordu:
///     500 · "Beklenmeyen bir hata oluştu. Lütfen tekrar deneyin…"
/// Oysa sunucu sebebi biliyordu ("150 hesabında proje seçimi
/// zorunludur."). Kullanıcı düzeltebilecekken sonsuz tekrara
/// yollanıyordu.
///
/// ═══ POZİTİF KONTROL NEDEN ŞART ═══
///
/// "Hepsini 400 yaptım" çözümü GİZLİ HATA YUTMA olurdu: gerçek çöküş
/// de geçerli bir yanıt gibi görünür, günlükte Error seviyesinde
/// görünmez, kimse fark etmez. Bu yüzden her testin bir de "hâlâ 500
/// dönüyor" kardeşi var.
/// </summary>
public sealed class GlobalExceptionHandlerTests
{
    private static async Task<(int Kod, string Mesaj)> CalistirAsync(Exception hata)
    {
        var baglam = new DefaultHttpContext();
        baglam.Request.Path = "/api/olcum";
        baglam.Request.Method = "POST";
        baglam.Response.Body = new MemoryStream();

        var isleyici = new GlobalExceptionHandler(
            NullLogger<GlobalExceptionHandler>.Instance);

        var ele = await isleyici.TryHandleAsync(baglam, hata, CancellationToken.None);
        Assert.True(ele, "İşleyici hatayı ele almadı.");

        baglam.Response.Body.Position = 0;
        using var belge = await JsonDocument.ParseAsync(baglam.Response.Body);

        return (baglam.Response.StatusCode,
                belge.RootElement.GetProperty("message").GetString() ?? string.Empty);
    }

    /// <summary>
    /// KIRMIZIYA DÖNERSE: kullanıcı düzeltebileceği bir hatada yine
    /// "beklenmeyen hata, tekrar deneyin" görür ve sonsuz tekrara düşer.
    /// </summary>
    [Fact]
    public async Task IsKuraliIhlali_400_ve_KENDI_MESAJI()
    {
        // Uçtan uca provada gerçekten atılan mesaj.
        var (kod, mesaj) = await CalistirAsync(
            new ArgumentException("150 hesabında proje seçimi zorunludur."));

        Assert.Equal(StatusCodes.Status400BadRequest, kod);
        Assert.Equal("150 hesabında proje seçimi zorunludur.", mesaj);
    }

    /// <summary>Depoda bu iş için yazılmış özel tür de 400 dönmeli.</summary>
    [Fact]
    public async Task DogrulamaIstisnasiTuru_400_Doner()
    {
        var (kod, mesaj) = await CalistirAsync(
            new SahteDogrulamaValidationException("Araç bulunamadı."));

        Assert.Equal(StatusCodes.Status400BadRequest, kod);
        Assert.Equal("Araç bulunamadı.", mesaj);
    }

    /// <summary>
    /// POZİTİF KONTROL — BU OLMADAN "hepsini 400 yaptım" GİZLİ HATA
    /// YUTMA olur. KIRMIZIYA DÖNERSE: gerçek çöküşler 400 diye
    /// raporlanır, günlükte Error seviyesinde görünmez ve fark edilmez.
    /// </summary>
    [Fact]
    public async Task GercekCokus_HALA_500_Doner()
    {
        foreach (var hata in new Exception[]
                 {
                     new NullReferenceException("nesne boş"),
                     new InvalidOperationException("bu duruma düşmemeliydi"),
                     new TimeoutException("zaman aşımı"),
                 })
        {
            var (kod, mesaj) = await CalistirAsync(hata);

            Assert.Equal(StatusCodes.Status500InternalServerError, kod);
            Assert.StartsWith("Beklenmeyen bir hata", mesaj, StringComparison.Ordinal);
            // İÇ AYRINTI SIZMIYOR: özgün mesaj kullanıcıya gitmiyor.
            Assert.DoesNotContain(hata.Message, mesaj, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// KIRMIZIYA DÖNERSE: `.NET`in eklediği "(Parameter 'x')" metni
    /// kullanıcıya gösterilir — iç ayrıntı sızar. `paramName` taşıyan
    /// ArgumentException bir programlama sözleşmesi ihlalidir, çöküştür.
    /// </summary>
    [Fact]
    public async Task ParametreAdiTasiyanArgumentException_500_Doner()
    {
        var hata = new ArgumentException("Şirket bilgisi zorunludur.", "companyId");

        var (kod, mesaj) = await CalistirAsync(hata);

        Assert.Equal(StatusCodes.Status500InternalServerError, kod);
        Assert.DoesNotContain("Parameter", mesaj, StringComparison.Ordinal);
        Assert.DoesNotContain("companyId", mesaj, StringComparison.Ordinal);
    }

    /// <summary>
    /// ÖLÇÜM SAĞLIĞI (Kural 48): iki dal da GERÇEKTEN ayrışıyor mu.
    /// Aynı mesajla iki farklı tip, iki farklı kod dönmeli — dönmüyorsa
    /// yukarıdaki iddiaların hepsi aynı dalı ölçüyor olabilir.
    /// </summary>
    [Fact]
    public async Task IkiDalAyrisiyor()
    {
        const string ayniMesaj = "Aynı metin.";

        var kural = await CalistirAsync(new ArgumentException(ayniMesaj));
        var cokus = await CalistirAsync(new InvalidOperationException(ayniMesaj));

        Assert.Equal(400, kural.Kod);
        Assert.Equal(500, cokus.Kod);
        Assert.NotEqual(kural.Mesaj, cokus.Mesaj);
    }

    private sealed class SahteDogrulamaValidationException(string message)
        : Exception(message);
}
