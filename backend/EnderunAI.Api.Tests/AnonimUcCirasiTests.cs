using System.Text.RegularExpressions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// ANONİM UÇ ÇIRASI — YÜZEY SESSİZCE GENİŞLEMESİN.
///
/// `[AllowAnonymous]` taşıyan her uç `AnonimUclar.txt`te gerekçesiyle
/// yazılı olmak zorunda. Sayı çizginin ÜSTÜNE çıkarsa kırmızı; ALTINA
/// düşerse de kırmızı (gevşek çizgi, geri gelen bir anonim ucu gizler).
///
/// NEDEN AYRI LİSTE: `MuafUclar.txt` "kimliği var, yetki niteliği yok"
/// diyen uçları taşır. Bu liste "kimlik HİÇ İSTENMİYOR" diyenleri.
/// İki soru farklı; o dosyanın başlığı birleştirmeyi zaten gerekçesiyle
/// reddediyor.
/// </summary>
public sealed class AnonimUcCirasiTests
{
    private static string DepoKoku()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);
        while (dizin is not null && !Directory.Exists(Path.Combine(dizin.FullName, "deploy")))
            dizin = dizin.Parent;
        return dizin?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static string ApiKoku() => Path.Combine(DepoKoku(), "backend", "EnderunAI.Api");

    /// <summary>
    /// BİLDİRİM SAYAR, BAHSİ DEĞİL.
    ///
    /// İlk yazımda desen düz `[AllowAnonymous]` arıyordu ve 7 yerine 12
    /// saydı: beşi YORUM ya da DİZGE içindeydi — `UcKapisiDenetimi`
    /// kapının kendisi olduğu için niteliği adıyla anlatıyor, `Program.cs`
    /// yorumları da öyle. Kendi çıramın yanlış kırmızısıydı (Kural 84:
    /// sonda, ölçtüğü ayrımı korumak zorundadır — "bildirim" ile
    /// "bahis" aynı şey değil).
    ///
    /// Bildirim, KENDİ SATIRINDA durur: `[AllowAnonymous]` satırın
    /// başındadır, `.AllowAnonymous()` ise zincir çağrısıdır.
    /// </summary>
    internal static bool BildirimSatiri(string satir)
    {
        var t = satir.Trim();
        if (t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*")) return false;
        if (t.StartsWith("[AllowAnonymous]")) return true;
        return Regex.IsMatch(t, @"^\.AllowAnonymous\(\)\s*;?$")
            || Regex.IsMatch(t, @"\)\s*\.AllowAnonymous\(\)\s*;");
    }

    private static int BildirimSayisi()
    {
        var sayi = 0;
        foreach (var yol in Directory.GetFiles(ApiKoku(), "*.cs", SearchOption.AllDirectories))
            foreach (var satir in File.ReadAllLines(yol))
                if (BildirimSatiri(satir)) sayi++;
        return sayi;
    }

    private static int Cizgi()
    {
        var yol = Path.Combine(DepoKoku(), "deploy", "bekci", "anonim-uc-cizgi.txt");
        var satir = File.ReadAllLines(yol).First(s => s.TrimStart().StartsWith("bildirim:"));
        return int.Parse(satir.Split(':')[1].Trim());
    }

    private static List<string> BeyanSatirlari()
    {
        var yol = Path.Combine(ApiKoku(), "Security", "UcKapisi", "AnonimUclar.txt");
        return File.ReadAllLines(yol)
            .Where(s => !s.TrimStart().StartsWith('#') && s.Contains('|'))
            .ToList();
    }

    [Fact]
    public void TaramaSagligi_BildirimBulundu_BosKumeKanitDegil()
    {
        // Kural 48: tarama kökü yanlışsa aşağıdaki testler boş kümede yeşil yanar.
        Assert.True(BildirimSayisi() > 0, "Hiç [AllowAnonymous] bulunamadı — kök yanlış olabilir.");
        Assert.True(BeyanSatirlari().Count >= 5, "Beyan listesi beklenenden kısa.");
    }

    [Fact]
    public void SondaIsiriyor_BildirimIleBahsiAYIRIYOR()
    {
        // Kural 93: yeşil, sondanın ısırabildiği gösterilmeden rapora girmez.
        Assert.True(BildirimSatiri("    [AllowAnonymous]"));
        Assert.True(BildirimSatiri("   .AllowAnonymous();"));
        // BAHİS sayılmaz — bunlar gerçek dosyalardan alınmış satırlardır:
        Assert.False(BildirimSatiri("/// taşır, ya `[AllowAnonymous]` taşır, ya da muafiyet"));
        Assert.False(BildirimSatiri("            // `[AllowAnonymous]` GÜRÜLTÜLÜ BİR BEYANDIR"));
        Assert.False(BildirimSatiri(" * ya [RequirePermission], ya [AllowAnonymous], ya da"));
        Assert.False(BildirimSatiri("                \"taşımalı, ya [AllowAnonymous] taşımalı\" +"));
    }

    [Fact]
    public void AnonimBildirimSayisi_CizgiyiASMAMIS()
    {
        var olculen = BildirimSayisi();
        Assert.True(
            olculen <= Cizgi(),
            $"ANONİM YÜZEY GENİŞLEDİ: ölçülen {olculen}, çizgi {Cizgi()}. "
            + "Yeni anonim uç meşruysa önce AnonimUclar.txt'e GEREKÇESİYLE yazın, "
            + "sonra deploy/bekci/anonim-uc-cizgi.txt sayısını elle yükseltin.");
    }

    [Fact]
    public void AnonimBildirimSayisi_CizginiN_ALTINA_DA_DUSMEMIS()
    {
        // ÇİZGİNİN ALTI DA KIRMIZI: gevşek çizgi, kaldırılmış bir anonim ucun
        // geri gelmesini gizler. Azalma meşruysa çizgi İNDİRİLİR.
        var olculen = BildirimSayisi();
        Assert.True(
            olculen >= Cizgi(),
            $"ÇİZGİ GEVŞEDİ: ölçülen {olculen}, çizgi {Cizgi()}. "
            + "Anonim uç kaldırıldıysa çizgiyi de indirin; gevşek çizgi çırayı süse çevirir.");
    }

    [Fact]
    public void HerBeyanSatiri_UcVeGerekce_Tasiyor()
    {
        foreach (var satir in BeyanSatirlari())
        {
            var parca = satir.Split('|');
            Assert.True(parca.Length >= 3, $"Beyan satırı eksik (kategori | uç | gerekçe): {satir}");
            Assert.False(string.IsNullOrWhiteSpace(parca[1]), $"Uç boş: {satir}");
            Assert.True(
                parca[2].Trim().Length >= 40,
                $"Gerekçe fazla kısa — tek satırlık GERÇEK bir sebep yazın: {satir}");
        }
    }
}
