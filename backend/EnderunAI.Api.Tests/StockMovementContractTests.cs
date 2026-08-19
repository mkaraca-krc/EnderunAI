using System.Text.RegularExpressions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// STOK HAREKETİ SÖZLEŞMELERİ — "hata minimizasyonu" mimarisinin
/// derleme zamanı bekçisi.
///
/// Bu kuralların hepsi bugün ÇALIŞIYOR. Sorun onları kurmak değil,
/// YENİ BİR YOL EKLENDİĞİNDE unutulmamalarını sağlamak. Stok düşüren
/// altıncı bir servis yazılırsa ve yeterlilik kontrolü konmazsa,
/// negatif stok sessizce mümkün hale gelir ve kimse fark etmez —
/// çünkü mevcut testler yalnızca MEVCUT yolları sınıyor.
/// </summary>
public sealed class StockMovementContractTests
{
    private static string BackendPath()
    {
        var dir = AppContext.BaseDirectory;

        while (dir is not null &&
               !Directory.Exists(Path.Combine(dir, "EnderunAI.Api", "Controllers")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!, "EnderunAI.Api");
    }

    /// <summary>
    /// Kurallar YALNIZ depo stok kaydına dokunan dosyalara uygulanır.
    ///
    /// `.Quantity +=` deseni tek başına yetmiyor: örneğin
    /// `MaterialRequirementCalculator` bellekteki bir toplayıcının
    /// miktarını artırıyor — stokla ilgisi yok, maliyeti de olmaz.
    /// Kapı `WarehouseStock`: stok gerçekten değişecekse o kayda
    /// dokunmak ZORUNLU, dolayısıyla daraltma kuralı boşaltmıyor.
    /// </summary>
    private static bool TouchesStock(string code) =>
        code.Contains("WarehouseStock");

    private static IEnumerable<(string Name, string Code)> SourceFiles()
    {
        var root = BackendPath();

        foreach (var folder in new[] { "Controllers", "Services" })
        {
            var path = Path.Combine(root, folder);
            if (!Directory.Exists(path)) continue;

            foreach (var file in Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);

                // Yorumları soy: gerekçe metinleri kural sözcüklerini
                // içerebiliyor ve testi yanlış yönlendirirdi.
                var code = Regex.Replace(text, @"/\*[\s\S]*?\*/", " ");
                code = Regex.Replace(code, @"//[^\n]*", " ");

                yield return (Path.GetFileName(file), code);
            }
        }
    }

    /// <summary>
    /// STOK DÜŞÜREN HER YOL YETERLİLİK KONTROLÜ YAPAR.
    ///
    /// Negatif stok İSTİSNASIZ yasak: stokta 100 varken 150 çıkamaz.
    /// Negatife düşen stokta ne sayım ne maliyet anlamını korur.
    ///
    /// Kontrol AYNI DEĞİŞKENE ve düşüşten ÖNCEYE bağlı aranıyor.
    /// İlk yazdığım gevşek biçim — "dosyada bir yerde miktar
    /// karşılaştırması ya da 'yetersiz' kelimesi geçsin" — sondada
    /// KAÇIRDI: `RetailSaleService` içindeki alakasız bir
    /// "iade fişi iptal edilemez" mesajı ve `requested.Quantity &lt;= 0`
    /// kontrolü, silinen gerçek yeterlilik kontrolünün yerine geçti.
    /// Kural o hâliyle hiçbir şey korumuyordu.
    /// </summary>
    [Fact]
    public void StokDusurenHerYol_YeterlilikKontroluYapar()
    {
        var offenders = new List<string>();
        var checkedSites = 0;

        foreach (var (name, code) in SourceFiles())
        {
            if (!TouchesStock(code)) continue;

            foreach (Match site in Regex.Matches(code, @"(\w+)\.Quantity\s*-="))
            {
                checkedSites++;
                var variable = site.Groups[1].Value;

                // Kontrol düşüşten ÖNCE olmalı: sonra bakmak geç.
                var before = code[..site.Index];

                if (!Regex.IsMatch(before, Regex.Escape(variable) + @"\.Quantity\s*<"))
                    offenders.Add($"{name}:{variable}");
            }
        }

        // Kural boşalırsa (düşüş noktası kalmazsa) sessizce geçmesin.
        Assert.True(checkedSites >= 4,
            $"Beklenenden az stok düşüş noktası bulundu ({checkedSites}). "
            + "Kural boşalmış olabilir — desen hâlâ tutuyor mu?");

        Assert.True(
            offenders.Count == 0,
            "Bu noktalar stok DÜŞÜRÜYOR ama öncesinde aynı kaydın "
            + "miktarını kontrol etmiyor: " + string.Join(", ", offenders)
            + ". Negatif stok istisnasız yasak — çıkıştan önce depodaki "
            + "miktar kontrol edilmeli.");
    }

    /// <summary>
    /// SERBEST ELLE GİRİŞ UCU GERİ GELMEZ.
    ///
    /// `POST inventory/receipts` siparişe bağlı değildi ve MALİYET
    /// YAZMIYORDU: sıfır maliyetli stok girip ağırlıklı ortalamayı
    /// bozuyordu. Giriş yalnız üç kapıdan olur — mal kabul, iade
    /// dönüşü, sayım düzeltme.
    /// </summary>
    [Fact]
    public void SerbestElleGirisUcu_GeriGelmez()
    {
        var inventory = SourceFiles()
            .Single(x => x.Name == "InventoryController.cs").Code;

        Assert.DoesNotMatch(@"HttpPost\(""receipts""\)", inventory);
    }

    /// <summary>
    /// STOK ARTIRAN HER YOL MALİYET YAZAR.
    ///
    /// Kaldırılan serbest giriş ucunun asıl zararı buydu: miktarı
    /// artırıp `UnitCost`/`TotalCost` boş bırakıyordu. Maliyetsiz giriş,
    /// stok değeri ile muhasebeyi ilk günden ayırır.
    /// </summary>
    [Fact]
    public void StokArtiranHerYol_MaliyetYazar()
    {
        var offenders = new List<string>();

        foreach (var (name, code) in SourceFiles())
        {
            if (!TouchesStock(code)) continue;
            if (!Regex.IsMatch(code, @"\.Quantity\s*\+=")) continue;

            var writesCost =
                code.Contains("UnitCost") ||
                code.Contains("AverageUnitCost") ||
                code.Contains("WeightedAverageCostCalculator");

            if (!writesCost) offenders.Add(name);
        }

        Assert.True(
            offenders.Count == 0,
            "Bu dosyalar stok ARTIRIYOR ama maliyet yazmıyor: "
            + string.Join(", ", offenders)
            + ". Maliyetsiz giriş ağırlıklı ortalamayı bozar ve stok "
            + "değeri ile muhasebe birbirini tutmaz.");
    }

    /// <summary>
    /// BİRİM KİLİDİ: hareket isteği birim ALMAZ.
    ///
    /// Birim kartın alanıdır ve kart açılırken kategorinin izin
    /// verdiği listeden seçilip sabitlenir. Hareket girişinde birim
    /// sorulsaydı metre malzemeye adet girilebilirdi.
    /// </summary>
    [Fact]
    public void HareketIstekleri_BirimAlmaz()
    {
        var root = BackendPath();
        var contracts = Path.Combine(root, "Contracts");

        var offenders = new List<string>();

        if (Directory.Exists(contracts))
        {
            foreach (var file in Directory.GetFiles(contracts, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);

                foreach (Match match in Regex.Matches(
                    text,
                    @"record\s+(Stock\w*Request|\w*MovementRequest)\s*\(([^)]*)\)",
                    RegexOptions.Singleline))
                {
                    if (Regex.IsMatch(match.Groups[2].Value, @"\bstring\s+Unit\b"))
                        offenders.Add($"{Path.GetFileName(file)}:{match.Groups[1].Value}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Bu hareket istekleri BİRİM alıyor: " + string.Join(", ", offenders)
            + ". Birim kartta sabittir; harekette sorulursa metre "
            + "malzemeye adet girilebilir.");
    }

    /// <summary>
    /// SAYIM DÜZELTMESİ GEREKÇE İSTER.
    ///
    /// Belgeye bağlı olmadan stok değiştirebilen tek yol bu; ne olduğu
    /// yazılmazsa kaldırdığımız serbest giriş kapısı arkadan açılır.
    /// </summary>
    [Fact]
    public void SayimDuzeltmesi_GerekceIster()
    {
        var inventory = SourceFiles()
            .Single(x => x.Name == "InventoryController.cs").Code;

        var adjustment = inventory[inventory.IndexOf("adjustments", StringComparison.Ordinal)..];

        Assert.Matches(
            @"IsNullOrWhiteSpace\(request\.Description\)",
            adjustment[..Math.Min(2500, adjustment.Length)]);
    }
}
