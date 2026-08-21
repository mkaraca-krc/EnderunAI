namespace EnderunAI.Api.Contracts.Inventory;

/// <summary>Depo bazlı asgari/azami seviye tanımı (kaydetme).</summary>
public sealed record SaveWarehouseStockLevelRequest(
    Guid WarehouseId,
    Guid InventoryItemId,
    decimal MinimumQuantity,
    decimal? MaximumQuantity,
    string? Note);

/// <summary>Tanımlı seviye satırı ve o andaki fiili durumu.</summary>
public sealed record WarehouseStockLevelRow(
    Guid Id,
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    Guid InventoryItemId,
    string ItemCode,
    string ItemName,
    string Unit,
    decimal MinimumQuantity,
    decimal? MaximumQuantity,
    string? Note,
    decimal CurrentQuantity,
    /// <summary>Mevcut miktar asgariye eşit ya da altında mı.</summary>
    bool IsBelowMinimum,
    /// <summary>Depoda hiç kalmadı — uyarının en şiddetli hâli.</summary>
    bool IsDepleted,
    /// <summary>
    /// Önerilen sipariş miktarı: azami − mevcut. Azami tanımlı
    /// değilse NULL — miktar tahmin edilmez.
    /// </summary>
    decimal? SuggestedQuantity,
    decimal AverageUnitCost,
    /// <summary>Öneri × ortalama maliyet; azami yoksa NULL.</summary>
    decimal? SuggestedCost,
    Guid? PreferredSupplierCurrentAccountId,
    string? PreferredSupplierTitle);

/// <summary>Öneriden satın alma talebi üretme isteği.</summary>
public sealed record GeneratePurchaseRequestFromStockLevelsRequest(
    Guid WarehouseId,
    Guid ProjectId,
    string RequestedByName,
    int Priority,
    DateTime? NeededByDate,
    string? Description,
    IReadOnlyList<StockLevelPurchaseLine> Lines);

/// <summary>
/// Talebe girecek tek satır. Miktar İSTEMCİDEN geliyor: öneri bir
/// öneridir, kullanıcı azalttıysa azalttığı miktar sipariş edilmeli.
/// Sunucu miktarı yeniden hesaplasaydı ekranda görülen sayı ile
/// kaydedilen sayı ayrışırdı.
/// </summary>
public sealed record StockLevelPurchaseLine(
    Guid InventoryItemId,
    decimal Quantity);

public sealed record GeneratePurchaseRequestFromStockLevelsResponse(
    Guid PurchaseRequestId,
    string RequestNumber,
    Guid WarehouseId,
    string WarehouseName,
    int LineCount,
    decimal TotalQuantity);
