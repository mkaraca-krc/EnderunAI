using System.Text.RegularExpressions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// YAYIN DERLEMESİNDE PAYLAŞILAN DERLEYİCİ KAPALI OLMALI (2026-09-16).
///
/// ═══ ÖLÇÜLEN OLAY ═══
///
/// `dotnet` arkada bir Roslyn derleyici sunucusu bırakır ve o süreç
/// büyür: biri 5,5 GB tutuyordu, PID ile kapatınca kullanılabilir
/// bellek 650 MB'dan 6.161 MB'a çıktı. Makine marjı zaten dar ve bu
/// gece birkaç ölçüm koşusu bu yüzden öldürüldü.
///
/// ═══ HANGİ BAYRAK — ÖLÇÜLDÜ, TAHMİN EDİLMEDİ ═══
///
///   A bayraksız ................ 268 sn · kalıntı 1
///   B UseSharedCompilation=false 249 sn · kalıntı 0   &lt;- İŞE YARAYAN
///   C nodeReuse=false .......... 252 sn · kalıntı 1   &lt;- İŞE YARAMIYOR
///   D ikisi birden ............. 270 sn · kalıntı 0   &lt;- B'den iyi değil
///
/// `nodeReuse` MSBuild işçi düğümlerini yönetir, Roslyn sunucusunu
/// değil. Bu yüzden EKLENMEDİ: ikisini birden koymak hangisinin
/// çalıştığını bilmemek olurdu.
///
/// İLK ÖLÇÜMÜM GEÇERSİZDİ: kollar arası ara çıktı (obj/) duruyordu,
/// B/C/D artımlı koştu (3-5 sn) ve hiçbir şey derlemedi. Hiçbir şey
/// derlemeyen bir koşu derleyici sunucusu da BAŞLATMAZ — yani
/// "kalıntı=0" bayrağın işe yaradığını değil, derleme olmadığını
/// gösteriyordu. Ölçüm her kolda tam derlemeyle tekrarlandı.
///
/// ═══ SÜREÇ ÖLDÜRME YAYIN BETİĞİNE GİRMEZ ═══
///
/// Mehmet Bey'in kararı: yanlış PID canlıyı düşürür. Çözüm temizlik
/// değil, sunucunun HİÇ DOĞMAMASI. Bu test o kararın bekçisidir.
/// </summary>
public sealed class PaylasilanDerleyiciKapaliTests
{
    private static string DepoKoku()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);
        while (dizin is not null && !Directory.Exists(Path.Combine(dizin.FullName, "deploy")))
            dizin = dizin.Parent;
        return dizin?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static string Betik() =>
        File.ReadAllText(Path.Combine(DepoKoku(), "deploy", "scripts", "safe-deploy.sh"));

    /// <summary>Yorum satırları atılıyor — gerekçe, çağrı sanılmasın.</summary>
    private static string YorumsuzGovde(string metin) =>
        string.Join("\n", metin.Split('\n').Where(s => !s.TrimStart().StartsWith('#')));

    [Fact]
    public void TaramaSagligi_BetikOkundu_VeDerlemeCagrisiVar()
    {
        var govde = YorumsuzGovde(Betik());
        Assert.True(govde.Length > 1000, "safe-deploy.sh okunamadı.");
        Assert.Matches(@"dotnet\s+publish", govde);
        Assert.Matches(@"dotnet\s+test", govde);
    }

    [Fact]
    public void HerDotnetPublishVeTest_PaylasilanDerleyiciyiKAPATIYOR()
    {
        var govde = YorumsuzGovde(Betik());

        // Çağrı ve devamındaki satır sonu kaçışları birlikte okunuyor:
        // bayrak genelde bir alt satırda duruyor.
        // ÇAĞRI SAYAR, BAHSİ DEĞİL (gecenin DÖRDÜNCÜ aynı hatası).
        //
        // İlk desen `fail "dotnet publish başarısız oldu."` satırını da
        // yakaladı — o bir HATA MESAJI, çağrı değil. Çağrı satırın
        // BAŞINDA durur (`^\s*dotnet`); dizge içindeki bahis durmaz.
        // Aynı ayrımı bu gece dört kez yapmak zorunda kaldım:
        // AnonimUcCirasi, fiş tipi sondası, PkillYasagi, ve burası.
        foreach (Match m in Regex.Matches(
                     govde,
                     @"(?m)^[ \t]*dotnet[ \t]+(publish|test)(?:[^\n]*\\\n)*[^\n]*"))
        {
            Assert.True(
                m.Value.Contains("UseSharedCompilation=false"),
                "Yayın derlemesinde paylaşılan derleyici AÇIK kalmış — arkada "
                + "5 GB'a büyüyen bir VBCSCompiler bırakır (2026-09-16 ölçümü). "
                + "Eksik çağrı: " + m.Value.Split('\n')[0].Trim());
        }
    }

    [Fact]
    public void SondaIsiriyor_CagriIleBahsiAYIRIYOR()
    {
        // Kural 93: yeşil, sondanın ısırabildiği gösterilmeden rapora girmez.
        const string desen = @"(?m)^[ \t]*dotnet[ \t]+(publish|test)(?:[^\n]*\\\n)*[^\n]*";
        Assert.Matches(desen, "    dotnet publish x -c Release");
        Assert.Matches(desen, "  dotnet test y --configuration Release");
        // BAHİS sayılmaz — gerçek dosyadan alınmış satır:
        Assert.DoesNotMatch(desen, "        fail \"dotnet publish başarısız oldu.\"");

        // DEVAM SATIRI OKUNUYOR MU — bayrak bir alt satırda olabilir.
        // İlk desenim devamı "içinde \\ olan satır" diye arıyordu; oysa
        // devam, ÖNCEKİ satırın \\ ile bitmesiyle belirlenir. Son devam
        // satırında ters eğik çizgi YOKTUR ve bayrak tam oradaydı.
        var cokSatirli = "    dotnet test \"$P\" --configuration Release \\\n"
                       + "        -p:UseSharedCompilation=false 2>&1 | tee -a x; then";
        Assert.Contains("UseSharedCompilation=false", Regex.Match(cokSatirli, desen).Value);
    }

    [Fact]
    public void NodeReuse_EKLENMEMIS_CunkuIseYaramiyor()
    {
        // C kolu ölçüldü: nodeReuse=false kalıntıyı 1'de bıraktı.
        // Çalışmayan bir bayrağı "ne olur ne olmaz" diye eklemek,
        // sonraki okuyucuya onun çalıştığını söyler.
        Assert.DoesNotContain("nodeReuse", YorumsuzGovde(Betik()));
    }
}
