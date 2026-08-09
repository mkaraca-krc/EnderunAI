namespace EnderunAI.Api.Models;

/// <summary>
/// Sadeleştirilmiş 4 durumlu proje akışı: Keşif/Teklif → Aktif →
/// Tamamlandı/İptal. Ordinal değerler bilinçli olarak eski
/// (kullanılmayan) 7 durumlu enum'daki karşılıklarıyla aynı tutuldu
/// (Active=2, Completed=4, Cancelled=5) — canlıda tek kullanılan değer
/// Active(2) olduğundan veri migrasyonu gerekmedi.
/// </summary>
/// <summary>
/// Sözleşme tipi. Keşif ile gerçekleşen arasındaki sapmanın anlamı
/// tamamen buna bağlıdır.
/// </summary>
public enum ProjectContractType
{
    /// <summary>Henüz seçilmedi — sapma yorumlanmaz.</summary>
    Undetermined = 0,

    /// <summary>
    /// Anahtar teslim (götürü): bedel sabittir. Keşif üstü gerçekleşme
    /// ek gelir getirmez, doğrudan kâr erozyonudur.
    /// </summary>
    LumpSum = 1,

    /// <summary>
    /// Birim fiyatlı: yapılan iş kadar ödenir. Keşif üstü gerçekleşme
    /// ilave hakediş fırsatıdır.
    /// </summary>
    UnitPrice = 2,

    /// <summary>
    /// Karma: bölümlerin bir kısmı götürü, bir kısmı birim fiyatlı.
    /// Tip bölüm bazında belirlenir.
    /// </summary>
    Mixed = 3
}

public enum ProjectStatus
{
    Kesif = 0,
    Active = 2,
    Completed = 4,
    Cancelled = 5
}

/// <summary>
/// Hakediş düzenleme periyodu. Nakit akışı tahmini buna dayanıyor:
/// aylık hakediş kesen bir işle iş bitiminde tek ödeme alınan iş aynı
/// takvimde nakit üretmez.
/// </summary>
public enum ProjectProgressPaymentPeriod
{
    /// <summary>Belirlenmedi — nakit takvimi tahmin edilmez.</summary>
    Unspecified = 0,

    Monthly = 1,
    BiWeekly = 2,
    Quarterly = 3,

    /// <summary>İş bitiminde tek hakediş.</summary>
    OnCompletion = 4,

    Other = 5
}

public enum ProjectHealthStatus
{
    Green = 0,
    Yellow = 1,
    Red = 2
}

public sealed class Project : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    /// <summary>
    /// Keşif/Teklif statüsünde henüz kesinleşmemiş olabileceği için
    /// opsiyonel; Aktif'e geçerken zorunlu hale gelir (bkz.
    /// ProjectsController).
    /// </summary>
    public Guid? EmployerCurrentAccountId { get; set; }
    public CurrentAccount? EmployerCurrentAccount { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public string? ContractNumber { get; set; }
    public DateTime? ContractDate { get; set; }
    public decimal? ContractAmount { get; set; }

    /// <summary>
    /// Bu proje hangi kazanılan tekliften doğdu.
    ///
    /// Zincirin (teklif → proje → icmal → hakediş) ilk halkası. Boş
    /// olması normaldir: teklif modülü gelmeden önce açılmış projeler
    /// ve doğrudan açılan projeler bu bağı taşımaz.
    /// </summary>
    public Guid? SourceOfferId { get; set; }

    /// <summary>
    /// Hakediş periyodu. Nakit akışı takvimi buna bağlı.
    /// </summary>
    public ProjectProgressPaymentPeriod ProgressPaymentPeriod { get; set; }
        = ProjectProgressPaymentPeriod.Unspecified;

    /// <summary>
    /// Ödeme koşulları (vade, kesinti, teminat iadesi gibi sözleşme
    /// metninden gelen serbest hükümler). Sayılabilir bir şey
    /// olmadığı için serbest metin; periyot ayrı alanda tutuluyor.
    /// </summary>
    public string? PaymentTerms { get; set; }

    public string CurrencyCode { get; set; } = "TRY";
    public decimal VatRate { get; set; }

    /// <summary>
    /// Sözleşme/proje artış yüzdesi.
    /// Örnek: yüzde 10 için 10.00.
    /// </summary>
    public decimal IncreaseRate { get; set; }

    /// <summary>
    /// Nakit teminat kesintisi yüzdesi.
    /// Örnek: yüzde 5 için 5.00.
    /// </summary>
    public decimal CashRetentionRate { get; set; }

    /// <summary>
    /// Stopaj kesintisi yüzdesi.
    /// Örnek: yüzde 3 için 3.00.
    /// </summary>
    public decimal WithholdingTaxRate { get; set; }

    /// <summary>
    /// Malzeme kesintisi yüzdesi.
    /// Örnek: yüzde 10 için 10.00.
    /// </summary>
    public decimal MaterialDeductionRate { get; set; }

    /// <summary>
    /// Sözleşme tipi. Keşif–gerçekleşen sapmasının nasıl yorumlanacağını
    /// belirler: birim fiyatlı işte keşif üstü gerçekleşme ilave hakediş
    /// fırsatıdır, anahtar teslimde aynı sapma doğrudan kâr erozyonudur.
    ///
    /// Mevcut projeler <see cref="ProjectContractType.Undetermined"/>
    /// olarak açılır; tip seçilene kadar sapma yorumlanmaz — yanlış
    /// varsayım yanlış renk ve yanlış alarm üretirdi.
    /// </summary>
    public ProjectContractType ContractType { get; set; }
        = ProjectContractType.Undetermined;

    /// <summary>
    /// Proje sözleşme icmaliyle mi yürüyor?
    ///
    /// GERİYE UYUM: varsayılan false. İcmalsiz projede hakediş satırları
    /// bugünkü gibi elle girilir, saha günlük raporu icmale bağlanmaz ve
    /// portalda ilerleme yüzdesi gösterilmez. Açılan projede icmal
    /// zorunlu hale gelmez; yalnızca icmale dayalı akışlar devreye
    /// girer.
    /// </summary>
    public bool UsesContractSummary { get; set; }

    /// <summary>
    /// Anahtar teslimde toplam sapmanın kâr erozyon alarmı üreteceği
    /// eşik (%). Birim fiyatlı projede kullanılmaz.
    /// </summary>
    public decimal DeviationAlertThresholdRate { get; set; } = 5m;


    /// <summary>
    /// Projenin varsayılan barter oranı (%). Şantiyede oran tanımlıysa
    /// o öncelikli; hakedişte de düzeltilebilir.
    /// </summary>
    public string? WithholdingRate { get; set; }

    public DateTime? PlannedStartDate { get; set; }
    public DateTime? PlannedEndDate { get; set; }
    public DateTime? ActualStartDate { get; set; }
    public DateTime? ActualEndDate { get; set; }

    /// <summary>
    /// İŞVERENİN DAYATTIĞI sözleşme termini. İş programının referansı
    /// budur.
    ///
    /// <see cref="PlannedEndDate"/>'den ayrı tutuluyor: planlanan bitiş
    /// bizim takvimimizdir ve iş programı düzenlendikçe kayar; termin
    /// ise sözleşmede yazar ve kaymaz. İkisini tek alanda tutmak,
    /// planı öteleyen her düzenlemenin gecikme cezası hesabını da
    /// sessizce sıfırlaması demekti.
    ///
    /// Boşsa planlanan bitiş termin kabul edilir.
    /// </summary>
    public DateTime? ContractDeadlineDate { get; set; }

    /// <summary>
    /// Sözleşmedeki gecikme cezasının biçimi. Tanımsızsa ceza HİÇ
    /// hesaplanmaz — sıfır TL göstermek "ceza yok" demek olurdu.
    /// </summary>
    public Services.Schedule.DelayPenaltyKind DelayPenaltyKind { get; set; }
        = Services.Schedule.DelayPenaltyKind.None;

    /// <summary>
    /// Oransal cezada günlük YÜZDE (binde 1 için 0,1), sabit cezada
    /// günlük tutar. Yüzde seçildi çünkü bu kod tabanındaki bütün
    /// oranlar (teminat, stopaj, KDV) yüzde tutuluyor; tek bir alanın
    /// binde olması sessiz on kat hatası üretirdi.
    /// </summary>
    public decimal DelayPenaltyValue { get; set; }

    /// <summary>
    /// Ceza tavanı, sözleşme bedelinin yüzdesi olarak (yaygın: 10).
    /// Boşsa tavan yok.
    /// </summary>
    public decimal? DelayPenaltyCapRate { get; set; }

    public string? City { get; set; }
    public string? District { get; set; }
    public string? Address { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Kesif;
    public ProjectHealthStatus HealthStatus { get; set; } = ProjectHealthStatus.Green;

    public Guid? ProjectManagerUserId { get; set; }

    /// <summary>
    /// Arşiv (yumuşak silme) bayrağı. Kesinleşmiş kaydı olan proje kalıcı
    /// silinemez; bunun yerine arşive alınır. Bilinçli olarak
    /// <see cref="BaseEntity.IsDeleted"/> kullanılmaz: global sorgu filtresi
    /// projeyi tamamen görünmez yapar ve başka ekranlardaki
    /// <c>x.Project.Code</c> projeksiyonları sessizce null'a düşer.
    /// Arşivli proje yalnızca aktif listelerden ve seçim kutularından düşer;
    /// mali raporlar arşivli projenin kayıtlarını göstermeye devam eder.
    /// </summary>
    public bool IsArchived { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }

    public string? ArchiveReason { get; set; }

    public ICollection<Warehouse> Warehouses { get; set; } = new List<Warehouse>();
}
