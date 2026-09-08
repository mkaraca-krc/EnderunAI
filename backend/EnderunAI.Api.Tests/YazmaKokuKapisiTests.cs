using EnderunAI.Api.Services.Upload;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// YAZMA KÖKÜ KAPISI — SIZINTI/1 · SZ2.
///
/// Testler canlı disk köklerine yazıyordu. Kökler artık dışarıdan
/// veriliyor (SZ1) ama "verilir" yetmez: verilmediği gün sessizce
/// canlıya döner. Bu yüzden FAIL-CLOSED bir kapı var ve bu dosya
/// kapının GERÇEKTEN ısırdığını kanıtlıyor.
///
/// ÜRETİM AYAĞI DA SINANIYOR: bir kapının en kötü hâli, koruduğu
/// şeyi kırmasıdır. Canlı veritabanına bağlı bir süreç canlı köke
/// yazabilmeli — yoksa bu kapı yayını düşürürdü.
/// </summary>
public sealed class YazmaKokuKapisiTests
{
    private const string CanliBaglanti =
        "Host=localhost;Database=enderun_ai;Username=x;Password=y";

    private const string TestBaglanti =
        "Host=localhost;Database=enderun_ai_test;Username=x;Password=y";

    private static IConfiguration Yapilandirma(
        string baglanti, params (string, string)[] ayarlar)
    {
        var sozluk = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = baglanti,
        };

        foreach (var (k, v) in ayarlar) sozluk[k] = v;

        return new ConfigurationBuilder().AddInMemoryCollection(sozluk).Build();
    }

    [Fact]
    public void TestVeritabani_CanliYuklemeKokune_Yazamaz()
    {
        var cfg = Yapilandirma(TestBaglanti);   // Uploads:Root VERİLMEDİ

        var hata = Assert.Throws<InvalidOperationException>(
            () => YazmaKokleri.Yukleme(cfg));

        Assert.Contains("SIZINTI/1", hata.Message);
        Assert.Contains(YazmaKokleri.CanliYuklemeKoku, hata.Message);
    }

    [Fact]
    public void TestVeritabani_CanliProjeKokune_Yazamaz()
    {
        var cfg = Yapilandirma(TestBaglanti);

        Assert.Throws<InvalidOperationException>(
            () => YazmaKokleri.ProjeDosyalari(cfg));
    }

    [Fact]
    public void TestVeritabani_CanliKokunALTINA_da_Yazamaz()
    {
        // `uploads/gecici` da canlı dizinin içindedir ve yedeğe girer.
        var cfg = Yapilandirma(
            TestBaglanti,
            ("Uploads:Root", YazmaKokleri.CanliYuklemeKoku + "/gecici"));

        Assert.Throws<InvalidOperationException>(() => YazmaKokleri.Yukleme(cfg));
    }

    [Fact]
    public void TestVeritabani_EFaturaArsivi_de_Kapiya_Takilir()
    {
        var cfg = Yapilandirma(
            TestBaglanti,
            ("EInvoice:ArchivePath", YazmaKokleri.CanliYuklemeKoku + "/e-fatura"));

        Assert.Throws<InvalidOperationException>(() => YazmaKokleri.EFaturaArsivi(cfg));
    }

    [Fact]
    public void TestVeritabani_GeciciKokle_Calisir()
    {
        var gecici = Path.Combine(Path.GetTempPath(), "enderun-sonda-kok");

        var cfg = Yapilandirma(TestBaglanti, ("Uploads:Root", gecici));

        Assert.Equal(gecici, YazmaKokleri.Yukleme(cfg));
    }

    /// <summary>
    /// ÜRETİM AYAĞI — kapı canlıyı kırmıyor.
    /// Bu test olmasaydı "hep fırlatan" bir kapı da yeşil görünürdü.
    /// </summary>
    [Fact]
    public void CanliVeritabani_CanliKoke_Yazabilir()
    {
        var cfg = Yapilandirma(CanliBaglanti);

        Assert.Equal(YazmaKokleri.CanliYuklemeKoku, YazmaKokleri.Yukleme(cfg));
        Assert.Equal(YazmaKokleri.CanliProjeDosyaKoku, YazmaKokleri.ProjeDosyalari(cfg));
        Assert.Equal(
            Path.Combine(YazmaKokleri.CanliYuklemeKoku, "e-fatura"),
            YazmaKokleri.EFaturaArsivi(cfg));
    }

    /// <summary>
    /// MUHAFIZ: canlı kök YOLU başka hiçbir kaynak dosyada yazmasın.
    ///
    /// Kapı yalnız YazmaKokleri'nden geçenleri görüyor. Yarın biri
    /// yeni bir servise `"/var/www/enderun-ai/uploads"` yazarsa kapı
    /// onu hiç görmez ve sızıntı sessizce geri döner — Kural 79'un
    /// "ikinci okuyucu" tuzağı.
    /// </summary>
    [Fact]
    public void CanliKokYolu_BaskaHicbirKaynaktaGecmemeli()
    {
        var kok = DepoKoku();
        var api = Path.Combine(kok, "backend", "EnderunAI.Api");

        var yollar = new[] { "/var/www/enderun-ai/uploads", "/var/www/enderun-data/project-files" };

        var ihlaller = new List<string>();

        foreach (var dosya in Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories))
        {
            var goreli = Path.GetRelativePath(api, dosya).Replace('\\', '/');

            // TEK İSTİSNA: kökleri TANIMLAYAN dosyanın kendisi.
            if (goreli == "Services/Upload/YazmaKokleri.cs") continue;
            if (goreli.StartsWith("Migrations/", StringComparison.Ordinal)) continue;

            var metin = File.ReadAllText(dosya);

            foreach (var yol in yollar)
            {
                if (metin.Contains($"\"{yol}", StringComparison.Ordinal))
                    ihlaller.Add($"{goreli} -> {yol}");
            }
        }

        Assert.True(
            ihlaller.Count == 0,
            "Canlı yazma kökü doğrudan yazılmış (kapı bunları GÖRMEZ):\n  " +
            string.Join("\n  ", ihlaller) +
            "\n\nYazmaKokleri üzerinden çözün.");
    }

    /// <summary>
    /// POZİTİF KONTROL: muhafız gerçekten arıyor mu.
    /// Yol hiçbir yerde geçmiyor diye yeşil olan bir test ile,
    /// hiç bakmadığı için yeşil olan bir test aynı görünür (Kural 48).
    /// </summary>
    [Fact]
    public void Muhafiz_TanimDosyasinda_YoluBulabiliyor()
    {
        var tanim = Path.Combine(
            DepoKoku(), "backend", "EnderunAI.Api",
            "Services", "Upload", "YazmaKokleri.cs");

        Assert.True(File.Exists(tanim), $"Tanım dosyası bulunamadı: {tanim}");

        Assert.Contains(
            "\"/var/www/enderun-ai/uploads\"",
            File.ReadAllText(tanim), StringComparison.Ordinal);
    }

    private static string DepoKoku()
    {
        var dizin = new DirectoryInfo(AppContext.BaseDirectory);

        while (dizin is not null && !Directory.Exists(Path.Combine(dizin.FullName, ".git")))
            dizin = dizin.Parent;

        return dizin?.FullName
            ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }
}
