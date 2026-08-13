using EnderunAI.Api.Services.Engineering;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// MALZEME İHTİYACI MOTORU — saf hesap, veritabanı yok.
///
///   ihtiyaç = Σ ( poz miktarı × reçete miktarı × (1 + fire/100) )
///
/// Bu motor hem proje malzeme tedariki hem teklif → satın alma talebi
/// tarafından okunuyor. Sayı yanlışsa yanlış malzeme sipariş edilir;
/// bu yüzden fire, konsolidasyon ve eleme kuralları tek tek sınanıyor.
/// </summary>
public sealed class MaterialRequirementCalculatorTests
{
    private static MaterialRequirementRecipeLine Material(
        string code,
        string name,
        decimal quantityPerUnit,
        string unit = "m",
        decimal waste = 0m,
        Guid? inventoryItemId = null) =>
        new(inventoryItemId, code, name, unit, quantityPerUnit, waste);

    private static MaterialRequirementSource Source(
        int lineNumber,
        string positionCode,
        decimal quantity,
        params MaterialRequirementRecipeLine[] recipe) =>
        new(lineNumber, positionCode, null, quantity, recipe);

    /// <summary>
    /// Kullanıcının verdiği sağlama: 100 birim poz, poz başına 2 birim
    /// malzeme, %5 fire → 210 birim ihtiyaç.
    /// </summary>
    [Fact]
    public void Fire_IhtiyacaDogruUygulanir()
    {
        var result = MaterialRequirementCalculator.Calculate(
        [
            Source(1, "POZ-1", 100m, Material("KBL-01", "NYA Kablo", 2m, waste: 5m))
        ]);

        var line = Assert.Single(result.Materials);

        Assert.Equal(210m, line.Quantity);
        Assert.Equal("m", line.Unit);
        Assert.Empty(result.MissingRecipes);
    }

    /// <summary>
    /// Aynı malzeme iki ayrı pozdan geliyorsa TEK satırda toplanır.
    /// Toplanmasaydı satın alma aynı malzemeyi iki kalem olarak açar,
    /// iki ayrı teklif toplanır ve miktar iskontosu kaybedilirdi.
    /// </summary>
    [Fact]
    public void AyniMalzemeIkiPozdan_TekSatirdaToplanir()
    {
        var result = MaterialRequirementCalculator.Calculate(
        [
            Source(1, "POZ-1", 100m, Material("KBL-01", "NYA Kablo", 2m)),
            Source(2, "POZ-2", 50m, Material("KBL-01", "NYA Kablo", 3m))
        ]);

        var line = Assert.Single(result.Materials);

        Assert.Equal(350m, line.Quantity);
        Assert.Equal([1, 2], line.SourceLineNumbers);
    }

    /// <summary>
    /// Kod farklı yazılmış olsa da AYNI STOK KARTIYSA tek satırdır:
    /// kimlik önce karttan okunur.
    /// </summary>
    [Fact]
    public void AyniStokKarti_KodFarkliYazilsaBileToplanir()
    {
        var itemId = Guid.NewGuid();

        var result = MaterialRequirementCalculator.Calculate(
        [
            Source(1, "POZ-1", 10m,
                Material("KBL-01", "NYA Kablo", 2m, inventoryItemId: itemId)),
            Source(2, "POZ-2", 10m,
                Material("KABLO-1", "NYA kablo 2.5", 3m, inventoryItemId: itemId))
        ]);

        var line = Assert.Single(result.Materials);

        Assert.Equal(50m, line.Quantity);
        Assert.Equal(itemId, line.InventoryItemId);
    }

    /// <summary>
    /// REÇETESİZ POZ İHTİYACA SIFIR KATAR ve ayrı listede döner.
    /// Sessizce atlanmış olsaydı, ihtiyaç listesi eksik olduğu hâlde
    /// tam görünürdü — eksik malzeme şantiyede fark edilirdi.
    /// </summary>
    [Fact]
    public void RecetesizPoz_IhtiyacaGirmez_AyriListede()
    {
        var result = MaterialRequirementCalculator.Calculate(
        [
            Source(1, "POZ-1", 100m, Material("KBL-01", "NYA Kablo", 2m)),
            new MaterialRequirementSource(2, "POZ-2", "Reçetesiz iş", 500m, null)
        ]);

        var line = Assert.Single(result.Materials);
        Assert.Equal(200m, line.Quantity);

        var missing = Assert.Single(result.MissingRecipes);
        Assert.Equal(2, missing.LineNumber);
        Assert.Equal("POZ-2", missing.PositionCode);
        Assert.Contains("reçetesi yok", missing.Reason);
    }

    /// <summary>Boş reçete de reçetesiz sayılır — kalemi olmayan reçete ihtiyaç üretmez.</summary>
    [Fact]
    public void BosRecete_RecetesizSayilir()
    {
        var result = MaterialRequirementCalculator.Calculate(
        [
            new MaterialRequirementSource(1, "POZ-1", null, 10m, [])
        ]);

        Assert.Empty(result.Materials);
        Assert.Single(result.MissingRecipes);
    }

    /// <summary>
    /// Birim uyuşmazlığı: miktarlar ÇEVRİLMEZ ve TOPLANMAZ. "kg" ile
    /// "ton" toplansaydı ihtiyaç bin kat şişer ve o miktar satın almaya
    /// giderdi. Ayrı satır kalır, çakışma bildirilir.
    /// </summary>
    [Fact]
    public void BirimUyusmazsa_ToplanmazVeBildirilir()
    {
        var result = MaterialRequirementCalculator.Calculate(
        [
            Source(1, "POZ-1", 10m, Material("DMR-01", "Nervürlü demir", 2m, unit: "kg")),
            Source(2, "POZ-2", 10m, Material("DMR-01", "Nervürlü demir", 3m, unit: "ton"))
        ]);

        Assert.Equal(2, result.Materials.Count);
        Assert.Equal(20m, result.Materials.Single(x => x.Unit == "kg").Quantity);
        Assert.Equal(30m, result.Materials.Single(x => x.Unit == "ton").Quantity);

        var conflict = Assert.Single(result.UnitConflicts);
        Assert.Contains("toplanmadı", conflict.Reason);
    }

    [Fact]
    public void SifirMiktarliSatir_IhtiyacUretmez()
    {
        var result = MaterialRequirementCalculator.Calculate(
        [
            Source(1, "POZ-1", 0m, Material("KBL-01", "NYA Kablo", 2m))
        ]);

        Assert.Empty(result.Materials);
        Assert.Empty(result.MissingRecipes);
    }

    /// <summary>
    /// Ondalıklı fire ve miktarda yuvarlama: fireli birim miktar 6,
    /// satır ihtiyacı 4 hane. Bu kural motor ortaklaştırılmadan önceki
    /// teklif yolundan AYNEN taşındı — ortaklaştırma mevcut teklif
    /// çıktısının rakamlarını değiştirmemeliydi.
    /// </summary>
    [Fact]
    public void Yuvarlama_TeklifYolundakiyleAyni()
    {
        var result = MaterialRequirementCalculator.Calculate(
        [
            Source(1, "POZ-1", 3m, Material("BOY-01", "Boya", 0.3333m, waste: 7.5m))
        ]);

        // 0,3333 × 1,075 = 0,358298 (6 hane) → × 3 = 1,0749 (4 hane)
        Assert.Equal(1.0749m, Assert.Single(result.Materials).Quantity);
    }
}
