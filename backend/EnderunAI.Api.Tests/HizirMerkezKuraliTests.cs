using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// ÜÇÜNCÜ YAZMA YOLU ORTAK KURALI ÇAĞIRIYOR MU — YETİM MUHAFIZ NÖBETİ.
///
/// ── NEDEN KAYNAK TESTİ, DAVRANIŞ TESTİ DEĞİL ──
///
/// Dürüst cevap: bugün o çağrı DAVRANIŞSAL OLARAK ETKİSİZ. Hızır
/// yalnız `Hatirlatma` açıyor ve hatırlatmada merkez aranmıyor, yani
/// kural her çağrıda `null` dönüyor. Çağrıyı silmek bugün hiçbir
/// gözlemlenebilir davranışı değiştirmez — dolayısıyla onu davranışla
/// yakalayan bir test YAZILAMAZ. Yazdığımı iddia etseydim, aslında
/// başka bir şeyi ölçen ve silinmeyi hiç görmeyen bir test yazmış
/// olurdum.
///
/// ── PEKİ NEDEN VAR ──
///
/// Bu kod tabanının tekrar eden yarası tam olarak budur: `2d90c946`,
/// merkez kuralının POST gövdesindeki bloğunu metin aralığıyla kesti;
/// 26 satır kayıtsız gitti ve 2965 testin hiçbiri görmedi. Kural
/// silinmemişti — yalnız EN ÖNEMLİ ÇAĞIRANINI KAYBETMİŞTİ.
///
/// Çağrının bugünkü değeri, kural yarın değiştiğinde bu yolun da o
/// değişikliği görmesidir. "Bugün etkisiz" ile "gereksiz" aynı şey
/// değildir; aradaki farkı koruyan şey bu testtir.
///
/// Depoda aynı desenin başka örnekleri var (`UcuzKapilarTekTanimTests`,
/// `SirAdlariTekListeTests`, `BackupScriptSyncTests`) — kaynağı okuyan
/// muhafız burada yeni bir icat değil.
/// </summary>
public sealed class HizirMerkezKuraliTests
{
    private static string DepoKok()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);

        while (dizin is not null &&
               !Directory.Exists(Path.Combine(dizin.FullName, "deploy", "scripts")))
        {
            dizin = dizin.Parent;
        }

        Assert.True(dizin is not null, "Depo kökü bulunamadı.");
        return dizin!.FullName;
    }

    private static string HizirKaynagi()
    {
        var yol = Path.Combine(
            DepoKok(), "backend", "EnderunAI.Api", "Services", "Hizir",
            "HizirActionTools.cs");

        Assert.True(File.Exists(yol), $"Dosya yok: {yol}");
        return File.ReadAllText(yol);
    }

    /// <summary>
    /// POZİTİF KONTROL — DOSYA GERÇEKTEN OKUNUYOR.
    ///
    /// Bu olmadan aşağıdaki iddia, dosya yolu bozulduğu gün
    /// "bulunamadı" ile "yok" arasını ayırt edemezdi. Kural 48.
    /// </summary>
    [Fact]
    public void HizirKaynagi_Okunabiliyor_POZITIF_KONTROL()
    {
        Assert.Contains("CreateReminderAsync", HizirKaynagi());
    }

    /// <summary>
    /// İDDİA: hatırlatma açan yol ortak merkez kuralını ÇAĞIRIYOR.
    ///
    /// Sonda: bu çağrı `HizirActionTools`'tan silinirse test kırmızı
    /// verir. Kural yerinde durduğu hâlde çağrısız kalması, bu
    /// dosyanın tarihinde bir kez gerçekten yaşandı.
    /// </summary>
    [Fact]
    public void HatirlatmaYolu_MerkezKuraliniCagirir()
    {
        Assert.Contains("MasrafMerkeziKurali.Dogrula(", HizirKaynagi());
    }
}
