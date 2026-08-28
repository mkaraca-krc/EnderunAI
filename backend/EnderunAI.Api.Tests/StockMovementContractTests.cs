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

    /// <summary>
    /// STOK MİKTARINI DEĞİŞTİREN HER YOL SATIR KİLİDİ ALIR.
    ///
    /// Stok değişimi oku-değiştir-yaz: iki istek aynı kalemi aynı anda
    /// okursa ikisi de "1 adet var" görür. PostgreSQL varsayılanı Read
    /// Committed olduğundan iki işlem çakışmadan tamamlanır: veritabanı
    /// hata VERMEZ, stok 0 görünür ama tek maldan iki çıkış yapılmıştır.
    ///
    /// KURAL METOT GÖVDESİ BAZINDA — dosya bazında DEĞİL. İlk yazdığım
    /// biçim "dosyada bir yerde kilit çağrısı geçsin" diyordu ve sondada
    /// KAÇIRDI: `SupplierInvoiceStockPoster` içinde iki mutasyon
    /// noktası var; iade tarafındaki kilit silindiğinde giriş
    /// tarafındaki çağrı kuralı yeşil tutuyordu.
    ///
    /// Kilit ayrıca DEĞİŞİKLİKTEN ÖNCE aranıyor: sonra alınan kilit,
    /// bayat veriyle verilmiş kararı düzeltmez.
    /// </summary>
    [Fact]
    public void StokMiktariniDegistirenHerYol_SatirKilidiAlir()
    {
        var offenders = new List<string>();
        var checkedSites = 0;

        foreach (var (name, code) in SourceFiles())
        {
            if (!TouchesStock(code)) continue;

            foreach (var (uye, govde) in Uyeler(code))
            {
                foreach (Match site in Regex.Matches(
                    govde,
                    @"\bstock\.Quantity\s*(\+=|-=|=(?!=))"))
                {
                    checkedSites++;

                    var oncesi = govde[..site.Index];

                    if (!oncesi.Contains("stokKilidi.KilitleAsync"))
                        offenders.Add($"{name}:{uye}");
                }
            }
        }

        // Kural boşalırsa (desen tutmaz olursa) sessizce geçmesin.
        Assert.True(checkedSites >= 8,
            $"Beklenenden az stok yazma noktası bulundu ({checkedSites}). "
            + "Kural boşalmış olabilir — değişken adı hâlâ 'stock' mu?");

        Assert.True(
            offenders.Count == 0,
            "Bu noktalar depo stoğunu DEĞİŞTİRİYOR ama öncesinde satır "
            + "kilidi almıyor: " + string.Join(", ", offenders)
            + ". Kilitsiz oku-değiştir-yaz, eşzamanlı iki istekte tek "
            + "maldan iki çıkış üretir ve veritabanı hata vermez.");
    }

    /// <summary>
    /// SATIR KİLİDİ YALNIZ ADANMIŞ KİLİT SERVİSLERİNDE ALINIR.
    ///
    /// Zimmet paketinde kilit o akışa özel bir `FOR UPDATE` cümlesiyle
    /// alınmıştı; kilidi bir taraf alıp diğerleri almadığında yarış
    /// aynen sürüyordu. Aynı kararın ikinci bir kopyası çıkarsa
    /// kopyalar zamanla ayrışır (Kural 25).
    ///
    /// KURAL GENİŞLETİLDİ (2026-08-27), GEVŞETİLMEDİ. Önceki hâli TEK
    /// bir dosya adını sabitliyordu: `StokSatirKilidiService.cs`.
    /// ÖP/1a ödeme satırı için ikinci bir kilit servisi getirdi
    /// (`OdemeSatirKilidiService`) — FARKLI bir tabloyu kilitliyor ve
    /// `IStokSatirKilidi` onu kilitleyemez, çünkü o depo+kart
    /// anahtarıyla çalışıyor.
    ///
    /// Yeni kural, eskisinin NİYETİNİ koruyor ve dişlerini artırıyor:
    ///   (1) `FOR UPDATE` yalnız `*KilidiService.cs` dosyalarında —
    ///       bir iş akışının içinde elle kilit alınamaz,
    ///   (2) her kilit servisinde EN FAZLA BİR `FOR UPDATE` — kilit
    ///       servisi bir torbaya dönüşemez.
    ///
    /// İkinci şart öncekinde YOKTU: tek dosya kuralı, o dosyanın
    /// içinde beş ayrı kilit birikmesini engellemiyordu.
    /// </summary>
    [Fact]
    public void SatirKilidi_YalnizAdanmisKilitServislerinde()
    {
        /*
         * YORUMLAR AYIKLANIYOR (Kural 31: komuta bak, kelimeye değil).
         *
         * İlk yazımda ham metinde sayıyordum ve İKİ kilit servisi de
         * "birden çok FOR UPDATE" diye kırmızı verdi — çünkü ikisi de
         * çözümü YORUMDA anlatıyor. Kuralı yazarken kuralın kendi
         * tuzağına düştüm.
         */
        static string KomutMetni(string kod) =>
            string.Join("\n", kod.Split('\n')
                .Where(x =>
                {
                    var t = x.TrimStart();
                    return !t.StartsWith("//", StringComparison.Ordinal)
                        && !t.StartsWith("*", StringComparison.Ordinal)
                        && !t.StartsWith("/*", StringComparison.Ordinal);
                }));

        var kilitYazanlar = SourceFiles()
            .Select(x => (x.Name, Kod: KomutMetni(x.Code)))
            .Where(x => x.Kod.Contains("FOR UPDATE"))
            .ToList();

        var akisIcinde = kilitYazanlar
            .Where(x => !x.Name.EndsWith("KilidiService.cs", StringComparison.Ordinal))
            .Select(x => x.Name)
            .ToList();

        Assert.True(
            akisIcinde.Count == 0,
            "Bu dosyalar kendi FOR UPDATE cümlesini yazıyor: "
            + string.Join(", ", akisIcinde)
            + ". Satır kilidi yalnız adanmış bir *KilidiService üzerinden alınır.");

        /*
         * KOMUT SAYILIYOR, KELİME DEĞİL (Kural 31, ikinci kez).
         *
         * "FOR UPDATE" geçişlerini saymak yanlıştı: her iki kilit
         * servisi de HATA MESAJINDA o ifadeyi anıyor
         * ("...alınamaz: FOR UPDATE yalnız ifade boyunca tutar").
         * Mesaj metni bir kilit cümlesi DEĞİLDİR.
         *
         * Sayılan şey artık kilidi fiilen alan ÇAĞRI:
         * `ExecuteSqlRawAsync`. Bir kilit servisi tek bir bütünü
         * kilitler; ikinci bir kilit cümlesi ayrı servise çıkar.
         */
        var torbalasanlar = kilitYazanlar
            .Where(x => System.Text.RegularExpressions.Regex
                .Matches(x.Kod, @"ExecuteSqlRaw\w*\(").Count > 1)
            .Select(x => x.Name)
            .ToList();

        Assert.True(
            torbalasanlar.Count == 0,
            "Bu kilit servisleri birden çok FOR UPDATE taşıyor: "
            + string.Join(", ", torbalasanlar)
            + ". Her kilit servisi TEK bir bütünü kilitler; ikincisi "
            + "ayrı bir servise çıkarılır.");
    }

    /// <summary>
    /// Kaynağı üye (metot/özellik) gövdelerine böler.
    ///
    /// Kaba ama yeterli: girinti 4 olan `public`/`private`/`internal`
    /// bildirimleri sınır kabul edilir. Amaç tam bir ayrıştırıcı değil,
    /// bir metottaki kilidin komşu metodu örtmesini engellemek.
    /// </summary>
    private static IEnumerable<(string Name, string Body)> Uyeler(string code)
    {
        var sinirlar = Regex.Matches(
            code, @"\n    (?:public|private|internal|protected)[^\n(]*")
            .ToList();

        if (sinirlar.Count == 0)
        {
            yield return ("<dosya>", code);
            yield break;
        }

        for (var i = 0; i < sinirlar.Count; i++)
        {
            var basi = sinirlar[i].Index;
            var sonu = i + 1 < sinirlar.Count ? sinirlar[i + 1].Index : code.Length;

            var ad = sinirlar[i].Value.Trim().Split(' ').LastOrDefault() ?? "?";
            yield return (ad, code[basi..sonu]);
        }
    }
}
