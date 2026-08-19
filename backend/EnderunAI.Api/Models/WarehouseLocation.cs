namespace EnderunAI.Api.Models;

/// <summary>
/// Bölgenin tipi — raf/kat girilip girilmeyeceğini belirler.
/// </summary>
public enum WarehouseZoneKind
{
    /// <summary>
    /// RAFLI: konum üç seviye — Bölge → Raf → Kat
    /// ("Oda 2 - Raf 3 - Kat 2").
    /// </summary>
    Shelved = 0,

    /// <summary>
    /// AÇIK: rafa sığmayan büyük malzeme (dış metal oda, büyük tavalar).
    /// Konum yalnız BÖLGE seviyesindedir; raf/kat istemek olmayan bir
    /// ayrıntıyı zorunlu kılmak olurdu.
    /// </summary>
    Open = 1
}

/// <summary>
/// DEPO BÖLGESİ — "Oda 1", "Hol", "Dış Metal Oda".
///
/// Depoya bağlı: konum fiziksel bir yer ve o yer bir şirketin
/// deposundadır. Kategori SİSTEM GENELİ olduğu için varsayılan konum
/// kategoride tutulamaz — bkz. <see cref="WarehouseCategoryLocation"/>.
/// </summary>
public sealed class WarehouseZone : BaseEntity
{
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public WarehouseZoneKind Kind { get; set; } = WarehouseZoneKind.Shelved;

    public int SortOrder { get; set; }

    public ICollection<WarehouseShelf> Shelves { get; set; }
        = new List<WarehouseShelf>();
}

/// <summary>Raflı bölgedeki bir raf.</summary>
public sealed class WarehouseShelf : BaseEntity
{
    public Guid WarehouseZoneId { get; set; }
    public WarehouseZone WarehouseZone { get; set; } = null!;

    public string Code { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public ICollection<WarehouseShelfLevel> Levels { get; set; }
        = new List<WarehouseShelfLevel>();
}

/// <summary>
/// Rafın katı. Kat ayrımı takip ediliyor: "Raf 3" yetmez, malzeme
/// hangi katta olduğu bilinmeden aranır.
/// </summary>
public sealed class WarehouseShelfLevel : BaseEntity
{
    public Guid WarehouseShelfId { get; set; }
    public WarehouseShelf WarehouseShelf { get; set; } = null!;

    public string Code { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

/// <summary>
/// VARSAYILAN KONUM: bu DEPODA bu KATEGORİ nereye gider.
///
/// NEDEN KATEGORİDE DEĞİL: kategori SİSTEM GENELİ ("kablo tavası" her
/// şirkette aynı şey), konum ise belirli bir şirketin belirli bir
/// deposundaki fiziksel yer. Kategoriye varsayılan konum konsaydı
/// ikinci şirket eklendiğinde alan ya anlamsızlaşır ya YANLIŞ yeri
/// gösterirdi — ve o an kimse hatırlamazdı.
///
/// Kart açılırken seçilen depoya göre konum otomatik gelir; kullanıcı
/// elle değiştirebilir.
/// </summary>
public sealed class WarehouseCategoryLocation : BaseEntity
{
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public Guid InventoryCategoryId { get; set; }
    public InventoryCategory InventoryCategory { get; set; } = null!;

    public Guid WarehouseZoneId { get; set; }
    public WarehouseZone WarehouseZone { get; set; } = null!;

    /// <summary>Raflı bölgede zorunlu, açık bölgede null.</summary>
    public Guid? WarehouseShelfId { get; set; }
    public WarehouseShelf? WarehouseShelf { get; set; }

    public Guid? WarehouseShelfLevelId { get; set; }
    public WarehouseShelfLevel? WarehouseShelfLevel { get; set; }
}
