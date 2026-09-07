using System.Text.RegularExpressions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// HUB YAYIN MUHAFIZI — ALICI SEÇİMİNİN TEK MEŞRU BİÇİMİ.
///
/// ═══ NE TUTUYOR ═══
///
/// SignalR yayınının alıcısı üç biçimde seçilebilir:
///   · `Clients.All` / `Others` / `AllExcept`  → HERKESE
///   · `Clients.Group("konusma:...")`          → konuşma grubuna
///   · `Clients.Group(KullaniciGrubu(id))`     → tek kullanıcıya
///
/// İlk ikisi alıcıyı BAĞLANTIDAN alır: gruba kim girdiyse alır ve
/// grup üyeliği bağlantı kurulurken belirlenir. Yayın anında izni
/// alınmış biri o grupta durmaya devam eder.
///
/// Yalnız üçüncüsü, alıcıyı yayın anında çözmeye ZORLAR — kimin
/// grubuna gönderileceğine karar vermek için önce kimin alması
/// gerektiğini hesaplamak gerekir.
///
/// ═══ NEYİ TUTMUYOR — AÇIKÇA SÖYLENİYOR ═══
///
/// "Alıcılar TAZE çözüldü mü" iddiası **taranamaz**. Kaynak metninde
/// eski bir listeyle yeni bir listeyi ayıran hiçbir iz yok. Bu
/// muhafızı o iddianın güvencesi saymak, OLMAYAN BİR GÜVENCE
/// üretirdi. O iddiayı tutan şey `YayinAnindaYetkiTests`.
///
/// Bu dosya yalnız şunu tutuyor: yeni bir yayın eklenirken alıcı
/// seçimi için kullanıcı grubu DIŞINDA bir yol seçilmesin.
/// </summary>
public sealed class HubYayinMuhafiziTests
{
    private const string IzinliDesen = @"KullaniciGrubu\(";

    private static readonly string[] YasakDesenler =
    [
        @"Clients\s*\.\s*All\b",
        @"Clients\s*\.\s*Others\b",
        @"Clients\s*\.\s*AllExcept\b",
        @"Clients\s*\.\s*OthersInGroup\b",
    ];

    /// <summary>
    /// Api projesinin kökü.
    ///
    /// ALTINCI KOPYA. Depoda aynı işi yapan beş `BulKok()` daha var
    /// (`AuthorizeGuardTests`, `CommentEntityTypeGuardTests`,
    /// `CoverageBaselineTests`, `DocumentNumberConcurrencyTests`,
    /// `GoodsReceiptAccountingTests`). Ortak yardımcıya çıkarmak
    /// MUHAFIZ-YORUM/1'in işi; burada YENİ BİR ORTAK KATMAN AÇMIYORUM,
    /// çünkü o paket tam olarak bu tekrarları tek yerde toplayacak ve
    /// yarım bir ortaklaştırma onun işini zorlaştırırdı.
    /// </summary>
    private static string ApiKoku()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);

        while (dizin is not null &&
               !Directory.Exists(Path.Combine(dizin.FullName, "EnderunAI.Api")))
        {
            dizin = dizin.Parent;
        }

        return dizin is null
            ? throw new InvalidOperationException("Çözüm kökü bulunamadı.")
            : Path.Combine(dizin.FullName, "EnderunAI.Api");
    }

    private static IReadOnlyList<string> KaynakDosyalari() =>
        Directory.EnumerateFiles(
                ApiKoku(), "*.cs", SearchOption.AllDirectories)
            .Where(yol => !yol.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(yol => !yol.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(yol => !yol.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .ToList();

    /// <summary>
    /// Yorum ve dize sabitleri ATILIYOR.
    ///
    /// Bu muhafızın kendi gerekçesi `Clients.All` yazıyor; kendi
    /// yorumunu ihlal sayan bir kapı üç kez yaşandı (en son
    /// `redwood-contract`, benim yorumumdaki `&lt;ErpShell&gt;` ile).
    /// </summary>
    private static string YorumsuzGovde(string metin)
    {
        var govde = Regex.Replace(metin, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        govde = Regex.Replace(govde, @"//[^\n]*", " ");
        govde = Regex.Replace(govde, "\"(?:[^\"\\\\\n]|\\\\.)*\"", "\"\"");
        return govde;
    }

    /// <summary>
    /// İDDİA: kaynakta toptan yayın çağrısı YOK.
    /// </summary>
    [Fact]
    public void ToptanYayin_Kullanilmiyor()
    {
        var ihlaller = new List<string>();

        foreach (var yol in KaynakDosyalari())
        {
            var govde = YorumsuzGovde(File.ReadAllText(yol));

            foreach (var desen in YasakDesenler)
            {
                if (Regex.IsMatch(govde, desen))
                    ihlaller.Add($"{Path.GetFileName(yol)} → {desen}");
            }
        }

        Assert.True(
            ihlaller.Count == 0,
            "TOPTAN YAYIN BULUNDU. Alıcı, bağlantıdan değil YAYIN ANINDA "
            + "çözülmeli — bağlantı anındaki yetki, yayın anındaki yetki "
            + "değildir. Kullanıcı grubu kullanın "
            + "(MesajHub.KullaniciGrubu).\n  " + string.Join("\n  ", ihlaller));
    }

    /// <summary>
    /// İDDİA: her `Clients.Group(...)` çağrısı kullanıcı grubuna gidiyor.
    ///
    /// Konuşma grubuna yayın, üyeliği bağlantı anında dondururdu.
    /// </summary>
    [Fact]
    public void GrupYayini_YalnizKullaniciGrubuna()
    {
        var ihlaller = new List<string>();

        foreach (var yol in KaynakDosyalari())
        {
            var govde = YorumsuzGovde(File.ReadAllText(yol));

            foreach (Match eslesme in Regex.Matches(govde, @"\.\s*Group\s*\("))
            {
                var kuyruk = govde[eslesme.Index..Math.Min(
                    govde.Length, eslesme.Index + 120)];

                if (!Regex.IsMatch(kuyruk, IzinliDesen))
                    ihlaller.Add($"{Path.GetFileName(yol)} → {kuyruk.Split('\n')[0].Trim()}");
            }
        }

        Assert.True(
            ihlaller.Count == 0,
            "KULLANICI GRUBU DIŞINA YAYIN BULUNDU:\n  "
            + string.Join("\n  ", ihlaller));
    }

    /// <summary>
    /// POZİTİF KONTROL — MUHAFIZ GERÇEKTEN ISIRIYOR.
    ///
    /// Üstteki iki test, tarayıcı hiçbir dosya okumasaydı da yeşil
    /// kalırdı. Kural 48: boş sonuç yokluğun kanıtı değildir.
    ///
    /// İKİ AYAK: (a) yüzey gerçekten taranıyor mu, (b) yasak desen
    /// gerçekten yakalanıyor mu.
    /// </summary>
    [Fact]
    public void Muhafiz_Isiriyor()
    {
        // (a) Yüzey: taranan dosya sayısı beklenen büyüklükte.
        var dosyalar = KaynakDosyalari();
        Assert.True(
            dosyalar.Count > 200,
            $"Taranan dosya sayısı beklenenden az ({dosyalar.Count}). "
            + "Kök bulucu yanlış yeri gösteriyor olabilir.");

        // (b) Isırma: sahte bir ihlal metni desenle yakalanıyor.
        const string sahte = "await hub.Clients.All.SendAsync(\"MesajGeldi\", ozet);";
        var yakalandi = YasakDesenler.Any(
            desen => Regex.IsMatch(YorumsuzGovde(sahte), desen));

        Assert.True(yakalandi, "Yasak desen sahte ihlali YAKALAYAMADI.");

        // (c) Meşru kullanım yanlışlıkla yakalanmıyor.
        const string mesru =
            "await hub.Clients.Group(MesajHub.KullaniciGrubu(uye)).SendAsync(\"x\", o);";
        var yanlisAlarm = YasakDesenler.Any(
            desen => Regex.IsMatch(YorumsuzGovde(mesru), desen));

        Assert.False(yanlisAlarm, "Meşru kullanım yasak desene takıldı.");
    }
}
