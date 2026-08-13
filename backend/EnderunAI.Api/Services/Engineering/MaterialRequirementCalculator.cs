namespace EnderunAI.Api.Services.Engineering;

/// <summary>Reçetenin tek malzeme satırı: birim iş için ne kadar.</summary>
public sealed record MaterialRequirementRecipeLine(
    Guid? InventoryItemId,
    string MaterialCode,
    string MaterialName,
    string Unit,
    /// <summary>Pozun BİR biriminde kullanılan miktar.</summary>
    decimal QuantityPerUnit,
    decimal WastePercent);

/// <summary>
/// İhtiyacı doğuran satır: proje icmali kalemi ya da teklif kalemi.
/// <see cref="Recipe"/> null ise o pozun reçetesi yoktur.
/// </summary>
public sealed record MaterialRequirementSource(
    int LineNumber,
    string? PositionCode,
    string? PositionName,
    decimal Quantity,
    IReadOnlyList<MaterialRequirementRecipeLine>? Recipe);

public sealed record MaterialRequirementLine(
    Guid? InventoryItemId,
    string MaterialCode,
    string MaterialName,
    string Unit,
    decimal Quantity,
    IReadOnlyList<int> SourceLineNumbers);

public sealed record MaterialRequirementIssue(
    int LineNumber,
    string? PositionCode,
    string? PositionName,
    string Reason);

public sealed record MaterialRequirementResult(
    IReadOnlyList<MaterialRequirementLine> Materials,
    /// <summary>
    /// Reçetesi olmayan kaynak satırlar. İHTİYACA SIFIR KATARLAR ve
    /// burada ayrıca listelenirler: sessiz sıfır, "bu malzemeye ihtiyaç
    /// yok" ile "bu pozun reçetesi yok" arasındaki farkı yok eder.
    /// </summary>
    IReadOnlyList<MaterialRequirementIssue> MissingRecipes,
    /// <summary>
    /// Aynı malzemenin farklı birimlerle geçtiği durumlar. Miktarlar
    /// ÇEVRİLMEZ ve TOPLANMAZ — "kg" ile "ton" toplanırsa ihtiyaç bin
    /// kat şişer. Ayrı satır olarak kalırlar, sorun burada bildirilir.
    /// </summary>
    IReadOnlyList<MaterialRequirementIssue> UnitConflicts);

/// <summary>
/// MALZEME İHTİYACI MOTORU — saf ve statik: veritabanı yok, ağ yok.
///
///   ihtiyaç = Σ ( poz miktarı × reçete miktarı × (1 + fire/100) )
///
/// TEK MOTOR: hem proje malzeme tedariki hem teklif → satın alma
/// talebi bu hesabı buradan okur. İki kopya zamanla ayrışır; ayrışınca
/// aynı proje için teklif tarafında başka, tedarik tarafında başka
/// miktar çıkar ve hangisinin doğru olduğu anlaşılamaz.
///
/// YUVARLAMA, taşınmadan önceki teklif yolunda ne ise o: fireli birim
/// miktar 6, satır ihtiyacı 4 hane. Bilerek korundu — motoru ortaklaştırmak
/// mevcut teklif çıktısının rakamlarını değiştirmemeli.
/// </summary>
public static class MaterialRequirementCalculator
{
    public static MaterialRequirementResult Calculate(
        IEnumerable<MaterialRequirementSource> sources)
    {
        var consolidated = new Dictionary<string, Accumulator>(StringComparer.OrdinalIgnoreCase);
        var missingRecipes = new List<MaterialRequirementIssue>();
        var unitConflicts = new List<MaterialRequirementIssue>();

        // Aynı malzemenin hangi birimle görüldüğü: ikinci bir birimle
        // karşılaşılırsa toplama YAPILMAZ, çakışma bildirilir.
        var unitByMaterial = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            if (source.Recipe is null || source.Recipe.Count == 0)
            {
                missingRecipes.Add(new MaterialRequirementIssue(
                    source.LineNumber,
                    source.PositionCode,
                    source.PositionName,
                    "Pozun reçetesi yok; malzeme ihtiyacı hesaplanamadı."));

                continue;
            }

            foreach (var material in source.Recipe)
            {
                var code = material.MaterialCode?.Trim() ?? string.Empty;
                var name = material.MaterialName.Trim();
                var unit = material.Unit.Trim();

                var effectiveRecipeQuantity = decimal.Round(
                    material.QuantityPerUnit * (1m + material.WastePercent / 100m),
                    6);

                var required = decimal.Round(
                    source.Quantity * effectiveRecipeQuantity,
                    4);

                if (required <= 0)
                    continue;

                // Kimlik önce STOK KARTI: aynı kart, kodu/adı dosyadan
                // dosyaya farklı yazılmış olsa bile tek satırda toplanır.
                var identity = material.InventoryItemId is Guid itemId
                    ? $"ITEM:{itemId}"
                    : !string.IsNullOrWhiteSpace(code)
                        ? $"CODE:{code}"
                        : $"NAME:{name}";

                if (unitByMaterial.TryGetValue(identity, out var knownUnit) &&
                    !string.Equals(knownUnit, unit, StringComparison.OrdinalIgnoreCase))
                {
                    unitConflicts.Add(new MaterialRequirementIssue(
                        source.LineNumber,
                        source.PositionCode,
                        source.PositionName,
                        $"\"{name}\" bir yerde \"{knownUnit}\", burada \"{unit}\" birimiyle " +
                        "geçiyor; miktarlar toplanmadı."));
                }
                else
                {
                    unitByMaterial.TryAdd(identity, unit);
                }

                // Birim kimliğin PARÇASI: farklı birimler asla aynı
                // satırda toplanmaz.
                var key = $"{identity}|UNIT:{unit}";

                if (!consolidated.TryGetValue(key, out var accumulator))
                {
                    accumulator = new Accumulator(
                        material.InventoryItemId, code, name, unit);

                    consolidated.Add(key, accumulator);
                }

                accumulator.Quantity += required;
                accumulator.SourceLineNumbers.Add(source.LineNumber);
            }
        }

        var materials = consolidated.Values
            .OrderBy(x => x.MaterialName, StringComparer.CurrentCulture)
            .ThenBy(x => x.Unit, StringComparer.CurrentCulture)
            .Select(x => new MaterialRequirementLine(
                x.InventoryItemId,
                x.MaterialCode,
                x.MaterialName,
                x.Unit,
                decimal.Round(x.Quantity, 4),
                x.SourceLineNumbers.Distinct().OrderBy(y => y).ToList()))
            .ToList();

        return new MaterialRequirementResult(materials, missingRecipes, unitConflicts);
    }

    private sealed class Accumulator(
        Guid? inventoryItemId,
        string materialCode,
        string materialName,
        string unit)
    {
        public Guid? InventoryItemId { get; } = inventoryItemId;
        public string MaterialCode { get; } = materialCode;
        public string MaterialName { get; } = materialName;
        public string Unit { get; } = unit;

        public decimal Quantity { get; set; }
        public List<int> SourceLineNumbers { get; } = [];
    }
}
