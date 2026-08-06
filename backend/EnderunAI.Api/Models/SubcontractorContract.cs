namespace EnderunAI.Api.Models;

/// <summary>
/// Bir işin taşeronda mı bizde mi olduğu. Sözleşme kapsamı tikleri bu
/// tek enum üzerinden yürür — her kalem için ayrı bool tutmak, "yemek
/// bizde ama kim ödüyor" gibi belirsizlikler üretirdi.
/// </summary>
public enum SubcontractorResponsibility
{
    /// <summary>Yükümlülük bizde: masrafı biz karşılıyoruz.</summary>
    Us = 0,

    /// <summary>Yükümlülük taşeronda: kendi karşılıyor.</summary>
    Subcontractor = 1
}

public enum SubcontractorContractStatus
{
    Draft = 0,
    Active = 1,
    Completed = 2,
    Cancelled = 3
}

/// <summary>
/// Taşeron sözleşmesi.
///
/// Taşeron ayrı bir kart değil, "taşeron" işaretli bir CARİ + bu
/// sözleşmedir. Ayrı bir taşeron tablosu açmak, aynı firmayı hem
/// tedarikçi hem taşeron olarak iki kez kaydettirir ve cari bakiyeyi
/// ikiye böler.
///
/// KAPSAM TİKLERİ hakedişin kesinti kalemlerini belirler: bir kalem
/// <see cref="SubcontractorResponsibility.Us"/> ise o masrafı biz
/// yaptığımız için taşeron hakedişinden KESİLİR; taşerondaysa hakedişte
/// hiç görünmez. Kesinti listesini kullanıcının elle kurmasına
/// bırakmak, sözleşmeyle hakedişin sessizce ayrışması demekti.
/// </summary>
public sealed class SubcontractorContract : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>
    /// Taşeronun cari kartı. Kaydederken carinin rollerinde
    /// <see cref="CurrentAccountRoles.Subcontractor"/> aranır.
    /// </summary>
    public Guid CurrentAccountId { get; set; }
    public CurrentAccount CurrentAccount { get; set; } = null!;

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    /// <summary>
    /// Sözleşme tek bir şantiyeye bağlıysa dolu. Boşsa proje geneli —
    /// İSG yansıtmasında payda proje şantiyelerinin toplamı olur.
    /// </summary>
    public Guid? ProjectSiteId { get; set; }
    public ProjectSite? ProjectSite { get; set; }

    public string ContractNumber { get; set; } = string.Empty;

    /// <summary>İş tanımı — "kaba elektrik tesisatı" gibi.</summary>
    public string WorkDescription { get; set; } = string.Empty;

    /// <summary>
    /// Sözleşme tipi. Projeninkiyle aynı enum kullanılıyor: taşeron
    /// sözleşmesi de götürü ya da birim fiyatlı olur ve hakediş
    /// ilerlemesi buna göre hesaplanır.
    /// <see cref="ProjectContractType.Mixed"/> kabul edilmez — karma iş
    /// iki ayrı sözleşme demektir.
    /// </summary>
    public ProjectContractType ContractType { get; set; }
        = ProjectContractType.UnitPrice;

    /// <summary>Sözleşme bedeli (KDV hariç).</summary>
    public decimal ContractAmount { get; set; }

    public string CurrencyCode { get; set; } = "TRY";

    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    public SubcontractorContractStatus Status { get; set; }
        = SubcontractorContractStatus.Draft;

    // --- Kesinti oranları ---

    /// <summary>Teminat (stopaj/kesin teminat) oranı, yüzde.</summary>
    public decimal RetentionRate { get; set; }

    /// <summary>
    /// Yapım işleri KDV tevkifat oranı — faturalı ödemede kullanılır.
    /// Pay/payda olarak tutulur (ör. 4/10). Sıfırsa tevkifat yok.
    ///
    /// Sözleşmede duruyor çünkü her taşeron faturasında elle girilmesi
    /// hata kaynağı: aynı taşeronun iki faturası farklı oranla
    /// muhasebeleşirse KDV beyanı tutmaz.
    /// </summary>
    public int WithholdingNumerator { get; set; }
    public int WithholdingDenominator { get; set; }

    // --- Kapsam tikleri ---

    /// <summary>Yemek yükümlülüğü. Bizdeyse hakedişten kesilir.</summary>
    public SubcontractorResponsibility MealResponsibility { get; set; }
        = SubcontractorResponsibility.Subcontractor;

    /// <summary>Konaklama yükümlülüğü.</summary>
    public SubcontractorResponsibility AccommodationResponsibility { get; set; }
        = SubcontractorResponsibility.Subcontractor;

    /// <summary>
    /// SGK/sigorta yükümlülüğü. Bizdeyse taşeron işçileri bizim
    /// bordromuzdadır ve bordro maliyeti hakedişten kesilir
    /// (bkz. <see cref="Personnel"/> taşeron ekibi bağlantısı).
    /// </summary>
    public SubcontractorResponsibility SocialSecurityResponsibility { get; set; }
        = SubcontractorResponsibility.Subcontractor;

    /// <summary>
    /// Malzeme kimden. Bizdense verdiğimiz malzemenin bedeli
    /// hakedişten kesilir.
    /// </summary>
    public SubcontractorResponsibility MaterialResponsibility { get; set; }
        = SubcontractorResponsibility.Subcontractor;

    /// <summary>
    /// İSG yükümlülüğü. Bizdeyse işveren hakedişimizden kesilen İSG
    /// payı taşerona işçi oranıyla YANSITILIR.
    /// </summary>
    public SubcontractorResponsibility OhsResponsibility { get; set; }
        = SubcontractorResponsibility.Subcontractor;

    public string? Notes { get; set; }

    /// <summary>
    /// Sözleşmenin kapsadığı icmal kısımları. Maliyet ve kâr analizi bu
    /// bağ üzerinden yürür; götürü sözleşmede ilerleme de kısım bazında
    /// girilir.
    /// </summary>
    public ICollection<SubcontractorContractSection> Sections { get; set; }
        = new List<SubcontractorContractSection>();
}

/// <summary>
/// Sözleşmenin kapsadığı bir icmal kısmı.
///
/// Götürü sözleşmede ilerleme kısım bazında girilir ve hakediş
/// bunların AĞIRLIKLI toplamıdır; tek bir genel yüzde, kısım bazında
/// kâr karşılaştırmasını imkânsız kılardı.
/// </summary>
public sealed class SubcontractorContractSection : BaseEntity
{
    public Guid SubcontractorContractId { get; set; }
    public SubcontractorContract SubcontractorContract { get; set; } = null!;

    public Guid ProjectHakedisSectionId { get; set; }
    public ProjectHakedisSection ProjectHakedisSection { get; set; } = null!;

    /// <summary>
    /// Bu kısmın sözleşme içindeki bedeli. Götürüde ağırlıklı ilerleme
    /// bununla hesaplanır; birim fiyatlıda bilgi amaçlıdır.
    /// </summary>
    public decimal SectionAmount { get; set; }

    public int Order { get; set; }
}
