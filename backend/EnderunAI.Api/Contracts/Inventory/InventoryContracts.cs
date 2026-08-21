namespace EnderunAI.Api.Contracts.Inventory;

public sealed record CreateInventoryItemRequest(
    Guid CompanyId,
    string Code,
    string Name,
    string? Category,
    string? Brand,
    string? Model,
    string Unit,
    string? Barcode,
    int Type,
    Guid? PreferredSupplierCurrentAccountId = null,
    decimal? VatRate = null,
    string? Description = null,
    // Malzemenin birim başına içerdiği bakır (kg). Bakır maruziyeti
    // raporu YALNIZCA bu alandan besleniyor; girilmediği sürece emtia
    // modülünün proje ayağı boş çalışır.
    decimal? CopperKgPerUnit = null);

/*
 * `StockReceiptRequest` kaldırıldı (S4): karşılığı olan serbest
 * giriş ucu yok. Giriş yalnız mal kabul, iade dönüşü ve gerekçeli
 * sayım düzeltmesinden yapılır.
 */

public sealed record StockIssueRequest(
    Guid WarehouseId,
    Guid InventoryItemId,
    Guid? ProjectId,
    Guid? ProjectSiteId,
    decimal Quantity,
    string? ReferenceNumber,
    DateTime MovementDate,
    string? Description,
    /// <summary>
    /// Sarfın gittiği icmal kısmı. OPSİYONEL — bilinmiyorsa boş
    /// bırakılır, maliyet proje geneline yazılır.
    /// </summary>
    Guid? ProjectHakedisSectionId = null,
    /// <summary>
    /// Sarfın gittiği icmal satırı (poz). OPSİYONEL — doldurulursa
    /// maliyet o poza ölçülmüş olarak yazılır, boşsa kısım düzeyinde
    /// kalır ve poz görünümünde dağıtılır.
    /// </summary>
    Guid? ProjectBoqItemId = null,
    /// <summary>
    /// Sarf bir taşerona veriliyorsa hangi sözleşme kapsamında.
    /// OPSİYONEL — boş bırakmak "bizim sarfımız" demektir.
    ///
    /// Doldurulursa ve sözleşmede malzeme yükümlülüğü BİZDEYSE, bu
    /// çıkışın bedeli taşeron hakedişinde malzeme kesintisi olarak
    /// otomatik önerilir. Boş bırakılan sarf taşerona YAZILMAZ —
    /// projedeki tüm sarfı taşerona yüklemek olmayan bir borç
    /// yaratırdı.
    /// </summary>
    Guid? SubcontractorContractId = null);

public sealed record StockTransferRequest(
    Guid SourceWarehouseId,
    Guid TargetWarehouseId,
    Guid InventoryItemId,
    Guid? ProjectId,
    decimal Quantity,
    string? ReferenceNumber,
    DateTime MovementDate,
    string? Description);

public sealed record StockAdjustmentRequest(
    Guid WarehouseId,
    Guid InventoryItemId,
    decimal CountedQuantity,
    Guid? ProjectId,
    DateTime MovementDate,
    string? Description);

public sealed record UpdateInventoryItemRequest(
    string Name,
    string? Category,
    string? Brand,
    string? Model,
    string Unit,
    string? Barcode,
    int Type,
    bool IsActive,
    Guid? PreferredSupplierCurrentAccountId = null,
    decimal? VatRate = null,
    string? Description = null,
    // Malzemenin birim başına içerdiği bakır (kg). Bakır maruziyeti
    // raporu YALNIZCA bu alandan besleniyor; girilmediği sürece emtia
    // modülünün proje ayağı boş çalışır.
    decimal? CopperKgPerUnit = null);

/// <summary>Malzeme kartı detayı — düzenleme ekranı bunu okur.</summary>
public sealed record InventoryItemDetail(
    Guid Id,
    Guid CompanyId,
    string CompanyName,
    string Code,
    string Name,
    string? Category,
    string? Brand,
    string? Model,
    string Unit,
    string? Barcode,
    int Type,
    bool IsActive,
    decimal AverageUnitCost,
    decimal? LastPurchasePrice,
    DateTime? LastPurchaseDate,
    Guid? PreferredSupplierCurrentAccountId,
    string? PreferredSupplierTitle,
    decimal? VatRate,
    string? Description,
    /// <summary>Birim başına bakır (kg) — bakır maruziyeti raporunun tek kaynağı.</summary>
    decimal? CopperKgPerUnit,
    string? ImagePath,
    decimal TotalStock,
    /// <summary>Toplam stok × ağırlıklı ortalama maliyet.</summary>
    decimal StockValue,
    IReadOnlyList<InventoryItemWarehouseStock> Warehouses);

public sealed record CreateWarehouseRequest(
    Guid CompanyId,
    Guid BranchId,
    Guid? ProjectId,
    Guid? ProjectSiteId,
    string Code,
    string Name,
    int Type,
    string? Address);

public sealed record UpdateWarehouseRequest(
    Guid BranchId,
    Guid? ProjectId,
    Guid? ProjectSiteId,
    string Name,
    int Type,
    string? Address,
    bool IsActive);

public sealed record InventoryItemWarehouseStock(
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    decimal Quantity);
