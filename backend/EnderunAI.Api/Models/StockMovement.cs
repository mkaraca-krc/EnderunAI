namespace EnderunAI.Api.Models;

public enum StockMovementType
{
    Receipt = 0,
    Issue = 1,
    TransferIn = 2,
    TransferOut = 3,
    Return = 4,
    Adjustment = 5,
    Count = 6
}

public sealed class StockMovement : BaseEntity
{
    public Guid CompanyId { get; set; }

    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public Guid InventoryItemId { get; set; }
    public InventoryItem InventoryItem { get; set; } = null!;

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public Guid? ProjectSiteId { get; set; }
    public ProjectSite? ProjectSite { get; set; }

    /// <summary>
    /// Sarfın hangi icmal kısmına gittiği ("bu kablo kolon kablolarına").
    /// OPSİYONEL — bilinmiyorsa boş bırakılır ve maliyet proje geneline
    /// yazılır.
    /// </summary>
    public Guid? ProjectHakedisSectionId { get; set; }
    public ProjectHakedisSection? ProjectHakedisSection { get; set; }

    /// <summary>
    /// Sarf bir taşerona verildiyse hangi sözleşme kapsamında.
    ///
    /// OPSİYONEL ve yalnızca çıkış (Issue) hareketlerinde anlamlı.
    /// Sözleşmede malzeme yükümlülüğü BİZDEYSE, bu etiketi taşıyan
    /// çıkışların bedeli taşeron hakedişinden malzeme kesintisi olarak
    /// düşülür.
    ///
    /// Etiketsiz sarf taşerona YAZILMAZ: projedeki tüm sarfı taşerona
    /// yüklemek, olmayan bir borç yaratmak olurdu. Bu yüzden kesinti
    /// önerisi yalnızca etiketlenmiş çıkışlardan üretilir.
    /// </summary>
    public Guid? SubcontractorContractId { get; set; }
    public SubcontractorContract? SubcontractorContract { get; set; }

    public Guid? RelatedWarehouseId { get; set; }
    public Warehouse? RelatedWarehouse { get; set; }

    public Guid? PurchaseRequestId { get; set; }
    public PurchaseRequest? PurchaseRequest { get; set; }

    /// <summary>
    /// Bu hareket bir mal kabulden geldiyse belge zinciri: hareket -> mal
    /// kabul -> sipariş -> teklif -> talep (frontend'de mal kabul üzerinden
    /// tıklanarak izlenir).
    /// </summary>
    public Guid? GoodsReceiptId { get; set; }
    public Models.GoodsReceipt.GoodsReceipt? GoodsReceipt { get; set; }

    public StockMovementType Type { get; set; }

    /// <summary>
    /// Adjustment (sayım düzeltme) hareketlerinde işaretli fark (pozitif =
    /// fazla çıktı, negatif = eksik çıktı); diğer tüm tiplerde her zaman
    /// pozitif, taşınan/işlenen miktar.
    /// </summary>
    public decimal Quantity { get; set; }

    public string ReferenceNumber { get; set; } = string.Empty;
    public DateTime MovementDate { get; set; } = DateTime.UtcNow;
    public string? Description { get; set; }

    /// <summary>
    /// Hareket anındaki TRY birim maliyeti — sonradan InventoryItem.AverageUnitCost
    /// değişse bile bu alan sabit kalır (geçmiş hareketin maliyeti donmuş halde tutulur).
    /// </summary>
    public decimal? UnitCost { get; set; }

    public decimal? TotalCost { get; set; }
}
