using System.Diagnostics;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// `say.sh` MUHAFIZI — SAYAN ALETİN KENDİSİ ÖLÇÜLÜR.
///
/// ═══ NEDEN VAR ═══
///
/// Kural 84 (kapsamı yazılmayan süzgeç, kapsamadığını "yok" gösterir)
/// 2026-09-13'te ÜÇ KEZ ısırdı. Üçüncüsü: `ls Migrations/*.cs` ile
/// sayım, `Migrations/HumanResources/` altındaki 7 göçü görmedi ve
/// "canlıda kodda olmayan göç var" diye yanlış alarm üretti.
///
/// Üç kez tekrarlayan hata disiplinle değil ARAÇLA kapatılır:
/// `deploy/scripts/say.sh`. Bu test o aracı sınıyor — aracın kendisi
/// sessizce bozulursa, ona güvenen her sayım da sessizce bozulur.
///
/// ═══ SINANAN ÜÇ DAVRANIŞ ═══
///
///   1. ÖZYİNELEME VARSAYILAN — alt klasördeki dosyalar sayılır
///   2. SIFIRIN İKİ ANLAMI AYRI — kök yoksa ÖLÇEMEDİ (çıkış 3),
///      kök var ama eşleşme yoksa 0 (çıkış 0)
///   3. KAPSAM ÇIKTIDA — sayının yanında kök ve desen görünür
/// </summary>
public sealed class SayAraciTests
{
    private static string DepoKoku()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);
        while (dizin is not null && !Directory.Exists(Path.Combine(dizin.FullName, "deploy")))
        {
            dizin = dizin.Parent;
        }

        return dizin?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static (int Kod, string Cikti) Say(params string[] argumanlar)
    {
        var kok = DepoKoku();
        var baslangic = new ProcessStartInfo("/usr/bin/env")
        {
            WorkingDirectory = kok,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        baslangic.ArgumentList.Add("bash");
        baslangic.ArgumentList.Add(Path.Combine(kok, "deploy", "scripts", "say.sh"));
        foreach (var a in argumanlar)
        {
            baslangic.ArgumentList.Add(a);
        }

        using var surec = Process.Start(baslangic)!;
        var cikti = surec.StandardOutput.ReadToEnd() + surec.StandardError.ReadToEnd();
        surec.WaitForExit();
        return (surec.ExitCode, cikti);
    }

    private const string GocDizini = "backend/EnderunAI.Api/Migrations";

    [Fact]
    public void Ozyineleme_Varsayilan_AltKlasorDeSayilir()
    {
        var (ozyinelemeliKod, ozyinelemeli) = Say("--kok", GocDizini, "--desen", "*.cs",
            "--haric", "*.Designer.cs", "--haric", "*ModelSnapshot.cs", "--sade");
        var (duzKod, duz) = Say("--kok", GocDizini, "--desen", "*.cs",
            "--haric", "*.Designer.cs", "--haric", "*ModelSnapshot.cs", "--sade", "--duz");

        Assert.Equal(0, ozyinelemeliKod);
        Assert.Equal(0, duzKod);

        var hepsi = int.Parse(ozyinelemeli.Trim());
        var ustKlasor = int.Parse(duz.Trim());

        // ALT KLASÖR GERÇEKTEN VAR: pozitif kontrol. Alt klasör boşalırsa
        // bu test "fark yok" diye yeşil kalır ve hiçbir şey ölçmez.
        Assert.True(hepsi > ustKlasor,
            $"Özyinelemeli sayım ({hepsi}) üst klasör sayımından ({ustKlasor}) büyük olmalı — " +
            "alt klasörde göç yoksa bu test kapsamını yitirmiştir.");
    }

    [Fact]
    public void OlmayanKok_OLCEMEDI_Der_SessizSifirDegil()
    {
        var (kod, cikti) = Say("--kok", GocDizini + "/HicOlmayanKlasor", "--desen", "*.cs");

        Assert.Equal(3, kod);
        Assert.Contains("ÖLÇEMEDİ", cikti);
        Assert.DoesNotContain("0   [", cikti);
    }

    [Fact]
    public void VarOlanKok_EslesmeYok_SifirVeYesil()
    {
        var (kod, cikti) = Say("--kok", GocDizini, "--desen", "*.boyleBirUzantiYok");

        Assert.Equal(0, kod);
        Assert.StartsWith("0", cikti.TrimStart());
    }

    [Fact]
    public void Kapsam_CiktidaGorunur()
    {
        var (_, cikti) = Say("--kok", GocDizini, "--desen", "*.cs", "--haric", "*.Designer.cs");

        Assert.Contains("kök=" + GocDizini, cikti);
        Assert.Contains("desen=*.cs", cikti);
        Assert.Contains("özyinelemeli", cikti);
        Assert.Contains("hariç=*.Designer.cs", cikti);
    }
}
