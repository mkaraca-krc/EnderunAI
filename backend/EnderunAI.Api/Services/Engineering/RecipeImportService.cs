using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Engineering;

/// <summary>Bir satırın aktarımda ne yapacağı.</summary>
public enum RecipeImportAction
{
    /// <summary>Hatalı ya da eksik — aktarılmayacak.</summary>
    Skip = 0,

    /// <summary>Mevcut stok kartına bağlanacak.</summary>
    UseExistingItem = 1,

    /// <summary>Stok kartı bulunamadı, açılacak.</summary>
    CreateItem = 2
}

public sealed record RecipeImportPreviewRow(
    int RowNumber,
    string? PositionCode,
    string? PositionName,
    string? MaterialCode,
    string? MaterialName,
    decimal? Quantity,
    string? Unit,
    decimal WastePercent,
    RecipeImportAction Action,
    string ActionName,
    string? Error,
    bool PositionCodeInherited,
    /// <summary>
    /// Eşleşen stok kartının birimi dosyadakinden farklıysa dolu.
    /// Miktar çevrilmez — "kg" reçeteyle "ton" kartı çarpılırsa ihtiyaç
    /// bin kat şişer; satır hata olur.
    /// </summary>
    string? ExistingItemUnit);

public sealed record RecipeImportPositionSummary(
    string PositionCode,
    string? PositionName,
    bool PositionFound,
    int MaterialCount,
    /// <summary>Pozun bugünkü geçerli reçete sürümü; yoksa 0.</summary>
    int CurrentVersion);

public sealed record RecipeImportPreview(
    int TotalRows,
    int ValidRows,
    int InvalidRows,
    int PositionCount,
    int MissingPositionCount,
    int NewInventoryItemCount,
    int InheritedPositionCodeCount,
    IReadOnlyList<string> FileWarnings,
    IReadOnlyList<RecipeImportPositionSummary> Positions,
    IReadOnlyList<RecipeImportPreviewRow> Rows);

public sealed record RecipeImportCommitResult(
    int CreatedRecipes,
    int CreatedInventoryItems,
    int ImportedMaterials,
    int SkippedRows,
    string Message);

public sealed record RecipeImportOptions(
    Guid CompanyId,
    /// <summary>
    /// Tanınmayan malzeme için stok kartı açılsın mı.
    ///
    /// Kapalıysa kartı olmayan satır AKTARILMAZ (sessizce serbest metin
    /// olarak yazılmaz). Sebebi: proje malzeme ihtiyacında depo mevcudu
    /// ve açık talep yalnız stok kartı üzerinden düşülebiliyor; kartsız
    /// malzeme "eksik" hesabına hiç giremez, yani reçetede durması
    /// yanıltıcı olurdu.
    /// </summary>
    bool CreateMissingInventoryItems);

public interface IRecipeImportService
{
    Task<RecipeImportPreview> PreviewAsync(
        RecipeImportParseResult parsed,
        RecipeImportOptions options,
        CancellationToken cancellationToken);

    Task<RecipeImportCommitResult> CommitAsync(
        RecipeImportParseResult parsed,
        RecipeImportOptions options,
        CancellationToken cancellationToken);
}

/// <summary>
/// Reçete aktarımı: dosya satırlarını poza ve stok kartına bağlar,
/// poz başına tek reçete kurar.
///
/// ÖNİZLEME VE AKTARIM AYNI KARARI KULLANIR: satır kararı
/// <see cref="Resolve"/> içinde bir kez hesaplanır. İki ayrı yerde
/// hesaplansaydı önizlemede "aktarılacak" görünen satır aktarımda
/// sessizce düşebilirdi.
/// </summary>
public sealed class RecipeImportService(AppDbContext db) : IRecipeImportService
{
    public async Task<RecipeImportPreview> PreviewAsync(
        RecipeImportParseResult parsed,
        RecipeImportOptions options,
        CancellationToken cancellationToken)
    {
        var context = await LoadContextAsync(parsed, options, cancellationToken);

        var rows = parsed.Rows
            .Select(row => Resolve(row, context, options))
            .ToList();

        var positions = context.PositionCodes
            .Select(code =>
            {
                var position = context.PositionsByCode.GetValueOrDefault(code);

                return new RecipeImportPositionSummary(
                    code,
                    position?.Name,
                    position is not null,
                    rows.Count(x =>
                        string.Equals(x.PositionCode, code, StringComparison.OrdinalIgnoreCase) &&
                        x.Action != RecipeImportAction.Skip),
                    position is null
                        ? 0
                        : context.CurrentVersionByPosition.GetValueOrDefault(position.Id));
            })
            .OrderBy(x => x.PositionCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RecipeImportPreview(
            parsed.Rows.Count,
            rows.Count(x => x.Action != RecipeImportAction.Skip),
            rows.Count(x => x.Action == RecipeImportAction.Skip),
            positions.Count,
            positions.Count(x => !x.PositionFound),
            rows.Count(x => x.Action == RecipeImportAction.CreateItem),
            rows.Count(x => x.PositionCodeInherited),
            parsed.FileWarnings,
            positions,
            rows);
    }

    public async Task<RecipeImportCommitResult> CommitAsync(
        RecipeImportParseResult parsed,
        RecipeImportOptions options,
        CancellationToken cancellationToken)
    {
        var context = await LoadContextAsync(parsed, options, cancellationToken);

        var rows = parsed.Rows
            .Select(row => Resolve(row, context, options))
            .ToList();

        var usable = rows
            .Where(x => x.Action != RecipeImportAction.Skip)
            .ToList();

        if (usable.Count == 0)
        {
            return new RecipeImportCommitResult(
                0, 0, 0, rows.Count,
                "Aktarılabilecek geçerli satır yok. Sütun eşlemesini ve " +
                "poz kodlarını kontrol edin.");
        }

        var createdItems = 0;

        // Aynı malzeme dosyada birden çok pozda geçebilir; kart bir kez
        // açılır. Sözlük olmasaydı ikinci satır benzersiz kod kısıtına
        // takılıp tüm aktarımı düşürürdü.
        var itemIdByKey = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in usable.Where(x => x.Action == RecipeImportAction.CreateItem))
        {
            var key = row.MaterialCode!;

            if (itemIdByKey.ContainsKey(key))
                continue;

            var item = new InventoryItem
            {
                CompanyId = options.CompanyId,
                Code = key.ToUpperInvariant(),
                Name = row.MaterialName!,
                Unit = row.Unit!,
                Type = InventoryItemType.Material,
                Category = "Reçete aktarımı"
            };

            db.InventoryItems.Add(item);
            itemIdByKey[key] = item.Id;
            createdItems++;
        }

        var importedMaterials = 0;
        var createdRecipes = 0;

        foreach (var group in usable.GroupBy(
                     x => x.PositionCode!, StringComparer.OrdinalIgnoreCase))
        {
            var position = context.PositionsByCode[group.Key];

            // Pozun önceki geçerli reçetesi varsayılan olmaktan çıkar,
            // silinmez: hangi reçeteyle hesap yapıldığı geriye dönük
            // sorulabilmeli.
            foreach (var existing in context.DefaultRecipesByPosition
                         .GetValueOrDefault(position.Id, []))
            {
                existing.IsDefault = false;
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }

            var recipe = new EngineeringRecipe
            {
                EngineeringPositionId = position.Id,
                Version = context.CurrentVersionByPosition.GetValueOrDefault(position.Id) + 1,
                IsDefault = true,
                Description = "Excel aktarımı"
            };

            foreach (var row in group)
            {
                recipe.Materials.Add(new EngineeringRecipeMaterial
                {
                    InventoryItemId = row.Action == RecipeImportAction.CreateItem
                        ? itemIdByKey[row.MaterialCode!]
                        : context.ItemsByCode[row.MaterialCode!].Id,
                    MaterialCode = row.MaterialCode!,
                    MaterialName = row.MaterialName!,
                    Quantity = row.Quantity!.Value,
                    Unit = row.Unit!,
                    WastePercent = row.WastePercent,
                    Notes = null
                });

                importedMaterials++;
            }

            db.EngineeringRecipes.Add(recipe);
            createdRecipes++;
        }

        await db.SaveChangesAsync(cancellationToken);

        return new RecipeImportCommitResult(
            createdRecipes,
            createdItems,
            importedMaterials,
            rows.Count(x => x.Action == RecipeImportAction.Skip),
            $"{createdRecipes} poz için reçete aktarıldı, " +
            $"{importedMaterials} malzeme satırı yazıldı." +
            (createdItems > 0 ? $" {createdItems} stok kartı açıldı." : string.Empty));
    }

    private sealed record ImportContext(
        IReadOnlyList<string> PositionCodes,
        Dictionary<string, EngineeringPosition> PositionsByCode,
        Dictionary<Guid, int> CurrentVersionByPosition,
        Dictionary<Guid, List<EngineeringRecipe>> DefaultRecipesByPosition,
        Dictionary<string, InventoryItem> ItemsByCode,
        Dictionary<string, InventoryItem> ItemsByName);

    private async Task<ImportContext> LoadContextAsync(
        RecipeImportParseResult parsed,
        RecipeImportOptions options,
        CancellationToken cancellationToken)
    {
        var positionCodes = parsed.Rows
            .Where(x => !string.IsNullOrWhiteSpace(x.PositionCode))
            .Select(x => x.PositionCode!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var positions = await db.EngineeringPositions
            .Where(x =>
                x.CompanyId == options.CompanyId &&
                positionCodes.Contains(x.Code))
            .ToListAsync(cancellationToken);

        var positionsByCode = positions
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var positionIds = positions.Select(x => x.Id).ToList();

        var recipes = await db.EngineeringRecipes
            .Where(x => positionIds.Contains(x.EngineeringPositionId))
            .ToListAsync(cancellationToken);

        var currentVersion = recipes
            .GroupBy(x => x.EngineeringPositionId)
            .ToDictionary(x => x.Key, x => x.Max(y => y.Version));

        var defaults = recipes
            .Where(x => x.IsDefault)
            .GroupBy(x => x.EngineeringPositionId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var items = await db.InventoryItems
            .Where(x => x.CompanyId == options.CompanyId)
            .ToListAsync(cancellationToken);

        return new ImportContext(
            positionCodes,
            positionsByCode,
            currentVersion,
            defaults,
            items
                .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase),
            items
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Satırın kararı. Önizleme ve aktarım aynı yolu kullanır.
    /// </summary>
    private static RecipeImportPreviewRow Resolve(
        RecipeImportRow row,
        ImportContext context,
        RecipeImportOptions options)
    {
        RecipeImportPreviewRow Skip(string error, string? existingUnit = null) =>
            new(row.RowNumber, row.PositionCode,
                row.PositionCode is not null
                    ? context.PositionsByCode.GetValueOrDefault(row.PositionCode)?.Name
                    : null,
                row.MaterialCode, row.MaterialName, row.Quantity, row.Unit,
                row.WastePercent, RecipeImportAction.Skip, "Atlanacak",
                error, row.PositionCodeInherited, existingUnit);

        if (!row.IsValid)
            return Skip(row.Error!);

        var position = context.PositionsByCode.GetValueOrDefault(row.PositionCode!);

        if (position is null)
            return Skip($"Poz bulunamadı: {row.PositionCode}");

        // Kart eşleştirme sırası: önce kod, sonra ad. Kod kesin eşleşme
        // olduğu için önce denenir; ad eşleşmesi aynı malzemenin iki
        // kez açılmasını önler.
        var item = row.MaterialCode is not null
            ? context.ItemsByCode.GetValueOrDefault(row.MaterialCode)
            : null;

        item ??= row.MaterialName is not null
            ? context.ItemsByName.GetValueOrDefault(row.MaterialName)
            : null;

        if (item is not null)
        {
            if (!string.Equals(item.Unit, row.Unit, StringComparison.OrdinalIgnoreCase))
            {
                return Skip(
                    $"Birim uyuşmuyor: dosyada \"{row.Unit}\", stok kartında \"{item.Unit}\".",
                    item.Unit);
            }

            return new RecipeImportPreviewRow(
                row.RowNumber, row.PositionCode, position.Name,
                item.Code, row.MaterialName, row.Quantity, row.Unit,
                row.WastePercent, RecipeImportAction.UseExistingItem,
                "Mevcut stok kartına bağlanacak", null,
                row.PositionCodeInherited, item.Unit);
        }

        if (!options.CreateMissingInventoryItems)
        {
            return Skip(
                "Stok kartı bulunamadı ve kart açma kapalı. " +
                "Kartsız malzeme depo/eksik hesabına giremez.");
        }

        if (string.IsNullOrWhiteSpace(row.MaterialCode))
        {
            return Skip(
                "Stok kartı açılacak ama malzeme kodu yok. " +
                "Malzeme kodu sütununu eşleyin.");
        }

        return new RecipeImportPreviewRow(
            row.RowNumber, row.PositionCode, position.Name,
            row.MaterialCode, row.MaterialName, row.Quantity, row.Unit,
            row.WastePercent, RecipeImportAction.CreateItem,
            "Stok kartı açılacak", null,
            row.PositionCodeInherited, null);
    }
}
