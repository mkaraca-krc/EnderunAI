using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Engineering;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Purchasing;

/// <summary>
/// Reçetedeki tek bir malzemenin alış geçmişi.
/// </summary>
/// <param name="InventoryItemId">Stok kartı; bağ yoksa null.</param>
/// <param name="MaterialCode">Reçetedeki malzeme kodu.</param>
/// <param name="MaterialName">Malzeme adı.</param>
/// <param name="Unit">Reçetedeki birim.</param>
/// <param name="EffectiveQuantity">Fire dahil birim poz başına miktar.</param>
/// <param name="HasStockLink">Reçete satırı bir stok kartına bağlı mı.</param>
/// <param name="LastPurchaseUnitPrice">Son alış birim fiyatı (TL).</param>
/// <param name="LastPurchaseDate">Son alış tarihi.</param>
/// <param name="LastSupplierTitle">Son alışın tedarikçisi.</param>
/// <param name="WeightedAverageUnitPrice">Ağırlıklı ortalama birim fiyat
/// (TL).</param>
/// <param name="PurchasedQuantity">Ortalamaya giren toplam miktar.</param>
/// <param name="InvoiceCount">Ortalamaya giren fatura sayısı.</param>
/// <param name="Message">Sayı üretilemediyse nedeni.</param>
public sealed record MaterialPurchaseInsight(
    Guid? InventoryItemId,
    string MaterialCode,
    string MaterialName,
    string Unit,
    decimal EffectiveQuantity,
    bool HasStockLink,
    decimal? LastPurchaseUnitPrice,
    DateTime? LastPurchaseDate,
    string? LastSupplierTitle,
    decimal? WeightedAverageUnitPrice,
    decimal? PurchasedQuantity,
    int InvoiceCount,
    string? Message);

/// <summary>
/// Bir pozun gerçek alış maliyeti ile resmî fiyatının karşılaştırması.
/// </summary>
/// <param name="EngineeringPositionId">Poz.</param>
/// <param name="PositionCode">Poz numarası.</param>
/// <param name="PositionName">Poz adı.</param>
/// <param name="Unit">Poz birimi.</param>
/// <param name="EngineeringRecipeId">Kullanılan reçete.</param>
/// <param name="RecipeVersion">Reçete sürümü.</param>
/// <param name="OfficialUnitPrice">Resmî birim fiyat.</param>
/// <param name="OfficialYear">Resmî fiyatın yılı.</param>
/// <param name="OfficialSource">Resmî fiyatın kaynağı.</param>
/// <param name="LastPurchaseMaterialCost">Son alış fiyatlarıyla malzeme
/// maliyeti; eksik malzeme varsa null.</param>
/// <param name="WeightedAverageMaterialCost">Ağırlıklı ortalamayla
/// malzeme maliyeti; eksik malzeme varsa null.</param>
/// <param name="MaterialCount">Reçetedeki malzeme sayısı.</param>
/// <param name="LinkedMaterialCount">Stok kartına bağlı olan sayısı.</param>
/// <param name="PricedMaterialCount">Alış geçmişi bulunan sayısı.</param>
/// <param name="Materials">Malzeme kırılımı.</param>
/// <param name="Warnings">Rakamın nerede eksik olduğunu söyleyen uyarılar.</param>
public sealed record PositionPurchaseIntelligence(
    Guid EngineeringPositionId,
    string PositionCode,
    string PositionName,
    string Unit,
    Guid? EngineeringRecipeId,
    int? RecipeVersion,
    decimal? OfficialUnitPrice,
    int? OfficialYear,
    string? OfficialSource,
    decimal? LastPurchaseMaterialCost,
    decimal? WeightedAverageMaterialCost,
    int MaterialCount,
    int LinkedMaterialCount,
    int PricedMaterialCount,
    IReadOnlyList<MaterialPurchaseInsight> Materials,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Tedarikçi fiyat zekâsı: pozun reçetesindeki malzemelerin GERÇEK alış
/// fiyatlarını bulur ve resmî birim fiyatla karşılaştırır.
///
/// ZİNCİR: poz → reçete → reçete malzemesi → stok kartı → alış faturası
/// kalemi. Zincirin her halkası kopabilir ve kopan halkada SAYI
/// ÜRETİLMEZ:
/// - Reçete satırı stok kartına bağlı değilse o malzeme fiyatlanmaz.
/// - Stok kartının alış faturası yoksa fiyatlanmaz.
/// - Malzemelerden biri bile fiyatlanamıyorsa pozun TOPLAM maliyeti
///   null döner; eksik toplam, "bu poz ucuza mal oluyor" diye okunacak
///   yanıltıcı bir rakam üretirdi.
///
/// Uyarılar her zaman doldurulur ki kullanıcı rakamın neden eksik
/// olduğunu görsün.
/// </summary>
public sealed class SupplierPriceIntelligenceService(
    AppDbContext db,
    IPositionPriceService positionPrices)
{
    /// <summary>
    /// Ağırlıklı ortalamanın baktığı pencere. Daha eski alışlar fiyat
    /// seviyesi olarak anlamını yitiriyor; enflasyonlu bir ortamda iki
    /// yıl önceki fiyatı ortalamaya katmak bugünkü maliyeti olduğundan
    /// düşük gösterir.
    /// </summary>
    public const int DefaultLookbackMonths = 12;

    /// <summary>
    /// Pozun alış maliyeti zekâsı.
    /// </summary>
    /// <param name="companyId">Şirket — alış faturaları buna göre süzülür.</param>
    /// <param name="positionId">Poz.</param>
    /// <param name="lookbackMonths">Ortalama penceresi (ay).</param>
    /// <param name="officialYear">Karşılaştırılacak resmî fiyat yılı.</param>
    public async Task<PositionPurchaseIntelligence?> AnalyzeAsync(
        Guid companyId,
        Guid positionId,
        int? lookbackMonths,
        int? officialYear,
        CancellationToken cancellationToken)
    {
        var position = await db.EngineeringPositions
            .AsNoTracking()
            .Where(x => x.Id == positionId)
            .Select(x => new { x.Id, x.Code, x.Name, x.Unit })
            .SingleOrDefaultAsync(cancellationToken);

        if (position is null)
            return null;

        var warnings = new List<string>();

        var recipe = await db.EngineeringRecipes
            .AsNoTracking()
            .Where(x => x.EngineeringPositionId == positionId)
            .OrderByDescending(x => x.Version)
            .Select(x => new { x.Id, x.Version })
            .FirstOrDefaultAsync(cancellationToken);

        var resolution = await positionPrices.ResolveAsync(
            positionId, officialYear, null, cancellationToken);

        if (recipe is null)
        {
            warnings.Add(
                "Bu pozun reçetesi yok; malzeme bazlı alış karşılaştırması " +
                "yapılamıyor.");

            return new PositionPurchaseIntelligence(
                position.Id, position.Code, position.Name, position.Unit,
                null, null,
                resolution.Found ? resolution.UnitPrice : null,
                resolution.Year,
                resolution.SourceNote ?? resolution.Explanation,
                null, null, 0, 0, 0, [], warnings);
        }

        var materials = await db.EngineeringRecipeMaterials
            .AsNoTracking()
            .Where(x => x.EngineeringRecipeId == recipe.Id)
            .Select(x => new
            {
                x.InventoryItemId,
                x.MaterialCode,
                x.MaterialName,
                x.Unit,
                x.Quantity,
                x.WastePercent
            })
            .ToListAsync(cancellationToken);

        var months = lookbackMonths is > 0 and <= 60
            ? lookbackMonths.Value
            : DefaultLookbackMonths;

        var since = DateTime.SpecifyKind(
            DateTime.UtcNow.Date.AddMonths(-months), DateTimeKind.Utc);

        var linkedIds = materials
            .Where(x => x.InventoryItemId.HasValue)
            .Select(x => x.InventoryItemId!.Value)
            .Distinct()
            .ToList();

        // Alış geçmişi: yalnızca ONAYLI, iade OLMAYAN, stok tipi
        // faturalar. Taslak fatura henüz gerçek bir alış değil; iade
        // kalemi ise fiyat değil düzeltmedir ve ortalamayı bozar.
        var purchases = linkedIds.Count == 0
            ? []
            : await db.SupplierInvoiceItems
                .AsNoTracking()
                .Where(x =>
                    x.InventoryItemId != null &&
                    linkedIds.Contains(x.InventoryItemId.Value) &&
                    x.OriginalItemId == null &&
                    x.Quantity > 0 &&
                    x.SupplierInvoice.CompanyId == companyId &&
                    x.SupplierInvoice.Status == SupplierInvoiceStatus.Approved &&
                    !x.SupplierInvoice.IsReturn &&
                    x.SupplierInvoice.InvoiceDate >= since)
                .Select(x => new
                {
                    InventoryItemId = x.InventoryItemId!.Value,
                    x.Quantity,
                    // Dövizli fatura kendi kuruyla TL'ye çevrilir; kur
                    // faturanın kesildiği gündendir, bugünkü kur değil.
                    UnitPriceTry = x.UnitPrice * x.SupplierInvoice.ExchangeRate,
                    x.SupplierInvoice.InvoiceDate,
                    SupplierTitle = x.SupplierInvoice.SupplierCurrentAccount.Title
                })
                .ToListAsync(cancellationToken);

        var byItem = purchases
            .GroupBy(x => x.InventoryItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var insights = new List<MaterialPurchaseInsight>(materials.Count);

        decimal lastTotal = 0m;
        decimal weightedTotal = 0m;
        var pricedCount = 0;
        var allPriced = true;

        foreach (var material in materials)
        {
            var effectiveQuantity = decimal.Round(
                material.Quantity * (1 + material.WastePercent / 100m), 6);

            if (material.InventoryItemId is null)
            {
                allPriced = false;

                insights.Add(new MaterialPurchaseInsight(
                    null, material.MaterialCode, material.MaterialName,
                    material.Unit, effectiveQuantity,
                    HasStockLink: false,
                    LastPurchaseUnitPrice: null,
                    LastPurchaseDate: null,
                    LastSupplierTitle: null,
                    WeightedAverageUnitPrice: null,
                    PurchasedQuantity: null,
                    InvoiceCount: 0,
                    Message: "Reçete satırı bir stok kartına bağlı değil; " +
                             "alış geçmişi aranamıyor."));

                continue;
            }

            if (!byItem.TryGetValue(material.InventoryItemId.Value, out var rows) ||
                rows.Count == 0)
            {
                allPriced = false;

                insights.Add(new MaterialPurchaseInsight(
                    material.InventoryItemId, material.MaterialCode,
                    material.MaterialName, material.Unit, effectiveQuantity,
                    HasStockLink: true,
                    LastPurchaseUnitPrice: null,
                    LastPurchaseDate: null,
                    LastSupplierTitle: null,
                    WeightedAverageUnitPrice: null,
                    PurchasedQuantity: null,
                    InvoiceCount: 0,
                    Message: $"Son {months} ayda onaylı alış faturası yok."));

                continue;
            }

            var last = rows.OrderByDescending(x => x.InvoiceDate).First();

            var quantitySum = rows.Sum(x => x.Quantity);
            var valueSum = rows.Sum(x => x.Quantity * x.UnitPriceTry);

            var weighted = quantitySum > 0m
                ? decimal.Round(valueSum / quantitySum, 6)
                : last.UnitPriceTry;

            pricedCount++;
            lastTotal += effectiveQuantity * last.UnitPriceTry;
            weightedTotal += effectiveQuantity * weighted;

            insights.Add(new MaterialPurchaseInsight(
                material.InventoryItemId, material.MaterialCode,
                material.MaterialName, material.Unit, effectiveQuantity,
                HasStockLink: true,
                LastPurchaseUnitPrice: decimal.Round(last.UnitPriceTry, 6),
                LastPurchaseDate: last.InvoiceDate,
                LastSupplierTitle: last.SupplierTitle,
                WeightedAverageUnitPrice: weighted,
                PurchasedQuantity: decimal.Round(quantitySum, 4),
                InvoiceCount: rows.Count,
                Message: null));
        }

        var unlinked = materials.Count(x => x.InventoryItemId is null);
        var linkedButUnpriced = insights.Count(x =>
            x.HasStockLink && x.LastPurchaseUnitPrice is null);

        if (unlinked > 0)
        {
            warnings.Add(
                $"{unlinked} reçete malzemesi stok kartına bağlı değil. " +
                "Bağ kurulmadan gerçek alış fiyatı bulunamaz.");
        }

        if (linkedButUnpriced > 0)
        {
            warnings.Add(
                $"{linkedButUnpriced} malzemenin son {months} ayda onaylı " +
                "alış faturası yok.");
        }

        if (!allPriced && materials.Count > 0)
        {
            warnings.Add(
                "Malzemelerin bir kısmı fiyatlanamadığı için pozun toplam " +
                "alış maliyeti hesaplanmadı; eksik toplam maliyeti olduğundan " +
                "düşük gösterirdi.");
        }

        if (!resolution.Found)
            warnings.Add($"Resmî fiyat bulunamadı. {resolution.Explanation}");

        return new PositionPurchaseIntelligence(
            position.Id,
            position.Code,
            position.Name,
            position.Unit,
            recipe.Id,
            recipe.Version,
            resolution.Found ? resolution.UnitPrice : null,
            resolution.Year,
            resolution.SourceNote ?? resolution.Explanation,
            // Toplam yalnızca TÜM malzemeler fiyatlandıysa üretilir.
            allPriced && materials.Count > 0
                ? decimal.Round(lastTotal, 2)
                : null,
            allPriced && materials.Count > 0
                ? decimal.Round(weightedTotal, 2)
                : null,
            materials.Count,
            materials.Count - unlinked,
            pricedCount,
            insights,
            warnings);
    }
}
