namespace EnderunAI.Api.Models.Expenses;

/// <summary>Giderin nasıl ödendiği.</summary>
public enum ExpensePaymentMethod
{
    /// <summary>Banka/kasa üzerinden, resmî deftere giren ödeme.</summary>
    Bank = 0,

    /// <summary>
    /// ELDEN. Bu kalemler <c>extra_payment.view</c> maskesine tabi:
    /// yetkisiz kullanıcıya satır HİÇ GELMEZ ve toplam yalnızca
    /// görünen kalemlerden oluşur.
    /// </summary>
    Cash = 1
}

/// <summary>Gideri belgeleyen kâğıt.</summary>
public enum ExpenseDocumentType
{
    None = 0,
    Receipt = 1,
    Invoice = 2
}

/// <summary>
/// ELLE GİRİLEN gider kaydı — yalnızca otomatik akmayan kalemler
/// için (kira, çay-şeker, kırtasiye).
///
/// MUHASEBEYE VE KASAYA YAZMAZ (kilitli karar): bu bir yönetim
/// kaydıdır, resmî kayıt gider faturasıdır. Fiş üretseydi aynı gider
/// hem burada hem faturada sayılır; kasa hareketi üretseydi elden
/// ödenen kalemler resmî bakiyeye sızardı. Nakit akış projeksiyonu
/// bu kayıtları ÇIKIŞ olarak OKUR — okuma, deftere postalama değil.
///
/// OTOMATİK KALEMLER BURAYA GİRMEZ: satın alma, görev masrafı,
/// işçilik ve taşeron kendi kaynaklarından okunur. Bu yüzden
/// otomatik kategoriler (malzeme/işçilik/taşeron/yol) elle giriş
/// için reddedilir — aynı gideri iki kaynaktan saymanın en kolay
/// yolu budur.
///
/// BELGE: tür ve numara tutuluyor, dosya eki YOK. Ek dosya ayrı bir
/// yükleme altyapısı ister ve bu bloğun kapsamında değil.
/// </summary>
public sealed class ExpenseEntry : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>
    /// Merkezin türü. Kimlik aşağıdaki üç alandan yalnız birinde
    /// durur; polimorfik tek kolon yerine ayrı FK'lar tutuluyor ki
    /// silinen bir proje kaydı sessizce sahipsiz bırakmasın.
    /// </summary>
    public ExpenseCenterType CenterType { get; set; }

    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }

    /// <summary>
    /// Proje merkezinde projenin kendisi; ŞANTİYE merkezinde de
    /// şantiyenin bağlı olduğu proje. Rapor "proje altında topla"
    /// diyebilsin diye yazma anında doldurulur.
    /// </summary>
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public Guid? ProjectSiteId { get; set; }
    public ProjectSite? ProjectSite { get; set; }

    public Guid ExpenseCategoryId { get; set; }
    public ExpenseCategory ExpenseCategory { get; set; } = null!;

    public DateTime ExpenseDate { get; set; }

    public decimal Amount { get; set; }

    public string Description { get; set; } = string.Empty;

    public ExpensePaymentMethod PaymentMethod { get; set; }

    public ExpenseDocumentType DocumentType { get; set; }
    public string? DocumentNumber { get; set; }

    /// <summary>Tedarikçi — opsiyonel; her giderin carisi olmuyor.</summary>
    public Guid? SupplierCurrentAccountId { get; set; }
    public CurrentAccount? SupplierCurrentAccount { get; set; }

    /// <summary>
    /// Bu kaydı üreten tekrarlayan şablon (varsa) ve hangi döneme
    /// ait olduğu. Şablon aynı ayı ikinci kez üretmesin diye kayıt
    /// kendi dönemini taşıyor.
    /// </summary>
    public Guid? RecurringTemplateId { get; set; }
    public int? PeriodYear { get; set; }
    public int? PeriodMonth { get; set; }
}
