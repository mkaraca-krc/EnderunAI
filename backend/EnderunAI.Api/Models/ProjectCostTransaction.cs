namespace EnderunAI.Api.Models;

public enum ProjectCostType
{
    Material = 0,
    Labor = 1,
    Equipment = 2,
    Subcontractor = 3,
    Overhead = 4,
    Other = 5
}

/// <summary>
/// Maliyet sınıfı — icmalin üç bileşeniyle (malzeme / işçilik / GG&amp;K)
/// karşılaştırılabilmesi için gereken kırılım.
///
/// KULLANICI DOLDURMAZ: sınıf her zaman kaynaktan türetilir (stok sarfı,
/// gider hesabı, bordro). Elle seçilseydi aynı tür maliyet farklı
/// kişilerce farklı sınıflanır ve karşılaştırma anlamını yitirirdi.
/// <see cref="ProjectCostType"/> ise elle girişte kullanıcının kendi
/// etiketi olarak kalır; ikisi farklı işler.
/// </summary>
public enum ProjectCostClass
{
    /// <summary>Depo sarfı, malzeme alışı.</summary>
    Material = 0,

    /// <summary>Kendi personelimiz — puantaj/bordro şantiye maliyeti.</summary>
    Labor = 1,

    /// <summary>
    /// Taşeron işçiliği. Sınıf şimdilik TANIMLI AMA BOŞ: taşeron modülü
    /// gelince hakediş/sözleşme kayıtları buraya yazacak. Şimdiden
    /// tanımlı olması, o modül geldiğinde geçmiş veriyi yeniden
    /// sınıflandırma ihtiyacını ortadan kaldırır.
    /// </summary>
    SubcontractorLabor = 2,

    /// <summary>
    /// Genel gider: nakliye, kira, elektrik, müşavirlik, ekipman
    /// kiralama — imalata doğrudan girmeyen her şey.
    /// </summary>
    Overhead = 3
}

public sealed class ProjectCostTransaction : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid? ProjectSiteId { get; set; }
    public ProjectSite? ProjectSite { get; set; }

    /// <summary>
    /// Maliyetin yazıldığı icmal KISMI. OPSİYONEL: kısım seçilmeyen
    /// maliyet proje geneline yazılır ve analizde "Genel" satırında
    /// toplanır. Zorunlu tutulsaydı saha, kısmını bilmediği bir sarfı
    /// hiç kaydedemez ya da rastgele bir kısım seçerdi.
    /// </summary>
    public Guid? ProjectHakedisSectionId { get; set; }
    public ProjectHakedisSection? ProjectHakedisSection { get; set; }

    public ProjectCostType CostType { get; set; }

    /// <summary>
    /// Kaynaktan türetilen maliyet sınıfı. Elle girişte kullanıcının
    /// seçtiği <see cref="CostType"/> üzerinden eşlenir.
    /// </summary>
    public ProjectCostClass CostClass { get; set; } = ProjectCostClass.Material;

    public DateTime CostDate { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;

    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }

    /// <summary>
    /// Bu maliyet kaydının muhasebedeki karşılığı (fişin maliyet satırı).
    /// Proje maliyeti defteri ile genel muhasebe gider hesaplarının
    /// mutabakatını sağlar: dolu satırlar iki tarafta da aynı tutardır,
    /// boş olanlar henüz muhasebeleşmemiş demektir
    /// (bkz. GET /api/projects/{id}/cost-reconciliation).
    /// </summary>
    public Guid? AccountingVoucherLineId { get; set; }
    public AccountingVoucherLine? AccountingVoucherLine { get; set; }
}
