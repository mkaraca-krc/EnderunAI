namespace EnderunAI.Api.Models;

/// <summary>
/// Aletin o anki durumu.
/// </summary>
public enum ToolAssetStatus
{
    /// <summary>Depoda, kimseye zimmetli değil.</summary>
    InWarehouse = 0,

    /// <summary>Kullanımda — birine ya da bir şantiyeye zimmetli.</summary>
    InUse = 1,

    /// <summary>
    /// Serviste. Zimmet KAPANMAZ: kişi hâlâ sorumludur, alet yalnızca
    /// geçici olarak elinden çıkmıştır. Servis kapanınca kullanıma
    /// döner.
    /// </summary>
    InService = 2,

    /// <summary>Hurdaya ayrıldı; bir daha kullanıma dönmez.</summary>
    Scrapped = 3
}

/// <summary>
/// Aletin bulunduğu yer.
/// </summary>
public enum ToolAssetLocationType
{
    /// <summary>Merkez.</summary>
    HeadOffice = 0,

    /// <summary>Bir şantiyede.</summary>
    Site = 1
}

/// <summary>
/// Demirbaş / el aleti kartı.
///
/// SARFTAN AYRI: alet tüketilmez, kullanılır ve geri gelir. Depo
/// stoğunda (InventoryItem) izlenseydi her zimmet bir stok çıkışı
/// olur, geri gelince giriş olurdu ve "bu matkap kaç kez arızalandı"
/// sorusunun cevabı hiçbir yerde durmazdı.
///
/// KART OLMASI ŞART: zimmet kaydında alet adı ve seri numarası serbest
/// metin olarak tutuluyordu; aynı alet her zimmette yeniden yazıldığı
/// için servis geçmişi, garanti takibi ve arıza sıklığı
/// hesaplanamıyordu. Bu kart o eksiği kapatır.
/// </summary>
public sealed class ToolAsset : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>Şirket içinde benzersiz alet kodu.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Brand { get; set; }
    public string? Model { get; set; }

    /// <summary>
    /// Üretici seri numarası. Aynı model onlarca alet olabildiği için
    /// tekil ayrım buradan yürür; girilmişse şirket içinde benzersizdir.
    /// </summary>
    public string? SerialNumber { get; set; }

    public DateTime? PurchaseDate { get; set; }

    /// <summary>Alım bedeli — servis masrafıyla karşılaştırmak için.</summary>
    public decimal? PurchaseCost { get; set; }

    /// <summary>
    /// Garanti bitiş tarihi. Servis kararında ücretli/garanti ayrımının
    /// dayanağı ve "garantisi bitiyor" uyarısının kaynağı.
    /// </summary>
    public DateTime? WarrantyEndDate { get; set; }

    public ToolAssetStatus Status { get; set; } = ToolAssetStatus.InWarehouse;

    public ToolAssetLocationType LocationType { get; set; }
        = ToolAssetLocationType.HeadOffice;

    /// <summary>Şantiyedeyse hangi şantiye.</summary>
    public Guid? ProjectSiteId { get; set; }
    public ProjectSite? ProjectSite { get; set; }

    /// <summary>
    /// Şu an zimmetli olduğu personel. Zimmet defteri
    /// <see cref="HrAssetAssignment"/> içinde tutulur; bu alan yalnızca
    /// "kimde" sorusunu tek sorguda cevaplamak için özet olarak
    /// taşınır.
    /// </summary>
    public Guid? AssignedPersonnelId { get; set; }
    public Personnel? AssignedPersonnel { get; set; }

    public string? Notes { get; set; }

    /// <summary>Garantisi verilen tarihte sürüyor mu.</summary>
    public bool IsUnderWarrantyOn(DateTime date) =>
        WarrantyEndDate is not null && WarrantyEndDate.Value.Date >= date.Date;
}
