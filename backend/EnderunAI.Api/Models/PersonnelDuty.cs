namespace EnderunAI.Api.Models;

/// <summary>
/// Görevlendirmenin amacı. Hangi MALİYET YOLUNUN çalışacağını bu
/// belirler — türler karıştırılırsa maliyet ya iki kez sayılır ya
/// hiç sayılmaz.
/// </summary>
public enum PersonnelDutyType
{
    /// <summary>
    /// Çalışma görevlendirmesi: personel başka bir projede FİİLEN
    /// çalışır. Gittiği gün kadarı hedef projeye sayılır — gün
    /// maliyeti puantaj üzerinden hedefe kayar ve mevcut işçilik
    /// dağıtıcısı (ProjectLaborCostAllocator) onu oradan alır.
    /// Ayrıca yol, konaklama ve harcırah masrafı yansır.
    /// </summary>
    Work = 0,

    /// <summary>
    /// Keşif görevi: hedef, henüz kazanılmamış bir iş. Aday proje
    /// ayrı bir kavram DEĞİL — keşif statüsündeki projenin kendisi.
    /// Yalnız masraf yansır; ortada yapılan bir imalat yok.
    /// </summary>
    Survey = 1,

    /// <summary>
    /// Ziyaret / denetim / görüşme: mevcut bir projeye ya da şantiyeye
    /// gidilir ama ORADA ÇALIŞILMAZ.
    ///
    /// İŞÇİLİK GÜNÜ YENİDEN ATANMAZ: ziyaretçi o gün kendi işini
    /// yapmaya devam ediyor, ziyaret edilen projede imalat üretmiyor.
    /// Yalnız yol, konaklama ve harcırah ziyaret edilen projeye düşer.
    /// Çalışma görevlendirmesiyle karıştırılırsa ziyaretçinin günlük
    /// ücreti gitmediği bir imalata maliyet olarak yazılırdı.
    /// </summary>
    Visit = 2
}

/// <summary>
/// Harcırah mahsup kararı. Fiş harcırahtan azsa fark bir yere
/// gitmek zorunda: ya personelden düşülür ya şirket gideri kabul
/// edilir. Sabit kural yok — karar GM/İK'nın, ama KAYIT ALTINA
/// alınıyor.
/// </summary>
public enum DutySettlementDecision
{
    /// <summary>Fark personelden kesilir; avans zincirine bağlanır.</summary>
    DeductFromPersonnel = 0,

    /// <summary>Fark şirket gideri kabul edilir.</summary>
    AcceptAsExpense = 1
}

/// <summary>
/// Görevlendirme durumu. Onaylanmadan hiçbir maliyet doğmaz.
/// </summary>
public enum PersonnelDutyStatus
{
    /// <summary>İK açtı, GM onayı bekliyor.</summary>
    Requested = 0,

    Approved = 1,
    Rejected = 2,

    /// <summary>Görev bitti; masraf ve mahsup kapandı.</summary>
    Completed = 3,

    Cancelled = 4
}

/// <summary>
/// Personel görevlendirmesi.
///
/// ONAY AKIŞI: İK talebi açar, GM onaylar. Onaylanmadan maliyet ve
/// harcırah yansımaz — talep aşamasındaki bir görev projenin kârını
/// değiştirmemeli. Açılış ve onay ayrı ayrı damgalanır.
///
/// MALİYET YOLU TÜRE BAĞLI (bkz. <see cref="PersonnelDutyType"/>):
/// yalnız Work türünde gün maliyeti hedefe kayar; Survey ve Visit
/// türünde SADECE masraf yansır.
/// </summary>
public sealed class PersonnelDuty : BaseEntity
{
    public Guid CompanyId { get; set; }

    public Guid PersonnelId { get; set; }
    public Personnel Personnel { get; set; } = null!;

    public PersonnelDutyType DutyType { get; set; }

    /// <summary>
    /// Görevin gideceği proje. Work'te çalışılacak proje, Survey'de
    /// keşif statüsündeki aday proje, Visit'te ziyaret edilen mevcut
    /// proje.
    /// </summary>
    public Guid TargetProjectId { get; set; }
    public Project TargetProject { get; set; } = null!;

    /// <summary>Ziyaret/çalışma belirli bir şantiyeyse.</summary>
    public Guid? TargetProjectSiteId { get; set; }
    public ProjectSite? TargetProjectSite { get; set; }

    /// <summary>
    /// Personelin görev öncesi bağlı olduğu proje. Work türünde gün
    /// maliyeti buradan hedefe kayar; boşsa merkez kadrosudur.
    /// </summary>
    public Guid? SourceProjectId { get; set; }
    public Project? SourceProject { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    /// <summary>Şehir dışı görev: konaklama ve harcırah beklenir.</summary>
    public bool IsOutOfCity { get; set; }

    /// <summary>
    /// Günlük harcırah tutarı. Görev kartında SABİT tutulur: sonradan
    /// değişen bir parametreden okunsaydı kapanmış görevin tutarı
    /// geriye dönük oynardı.
    ///
    /// Elle düzeltilebilir ama düzeltme İZ BIRAKIR (aşağıdaki damga):
    /// tutar geriye dönük değiştiğinde defterdeki satır da değişir,
    /// bunun kim tarafından ve neden yapıldığı kayıtsız kalmamalı.
    /// </summary>
    public decimal DailyAllowance { get; set; }

    // --- Harcırah düzeltme izi ---

    public DateTime? AllowanceRevisedAtUtc { get; set; }
    public Guid? AllowanceRevisedByUserId { get; set; }

    /// <summary>Düzeltme gerekçesi; gerekçesiz tutar değişimi denetlenemez.</summary>
    public string? AllowanceRevisionNote { get; set; }

    public string Purpose { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public PersonnelDutyStatus Status { get; set; } = PersonnelDutyStatus.Requested;

    // --- Denetim izi ---
    public Guid? RequestedByUserId { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;

    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }

    /// <summary>Reddedildiyse gerekçe; gerekçesiz ret bilgi vermez.</summary>
    public string? DecisionNote { get; set; }

    // --- Masraf kalemleri ---
    //
    // Yol, konaklama ve harcırah AYRI AYRI tutuluyor; tek bir toplama
    // çökertilmiyor. Gider merkezi tarafı "şantiyeye ne kadar yol / ne
    // kadar konaklama / ne kadar harcırah" diye kategori bazında
    // soracak; kırılım sonradan ayrıştırılamaz.

    public decimal TravelCost { get; set; }
    public decimal AccommodationCost { get; set; }

    /// <summary>
    /// Getirilen fişlerin toplamı. Harcırahın karşılığı: fiş belge
    /// olduğu için resmî gider tarafında durur.
    /// </summary>
    public decimal ReceiptAmount { get; set; }

    // --- Mahsup ---

    public DutySettlementDecision? SettlementDecision { get; set; }
    public string? SettlementNote { get; set; }
    public Guid? SettlementByUserId { get; set; }
    public DateTime? SettlementAtUtc { get; set; }

    /// <summary>
    /// "Personelden düş" kararında açılan avans kaydı. Kesinti yeni
    /// bir yoldan değil, bordroda zaten çalışan avans zincirinden
    /// yürür; bağ tutuluyor ki ikinci kez avans açılmasın.
    /// </summary>
    public Guid? SettlementAdvanceId { get; set; }

    /// <summary>Görevin kapsadığı gün sayısı (uçlar sınır dahil sayar).</summary>
    public int DayCount => EndDate.Date < StartDate.Date
        ? 0
        : (EndDate.Date - StartDate.Date).Days + 1;

    /// <summary>
    /// Hak edilen toplam harcırah. Fiş mahsubu ayrı izlenir; bu tutar
    /// "ne verildi" değil "ne hak edildi" sorusunun cevabı.
    /// </summary>
    public decimal TotalAllowance => DailyAllowance * DayCount;

    /// <summary>
    /// Gün maliyeti hedefe kayar mı. Yalnız çalışma görevlendirmesinde
    /// evet — ziyaret ve keşifte kişi orada imalat üretmiyor.
    /// </summary>
    public bool ShiftsLaborCost => DutyType == PersonnelDutyType.Work;

    /// <summary>Hedef projeye yansıyan toplam masraf.</summary>
    public decimal TotalExpense => TravelCost + AccommodationCost + TotalAllowance;

    /// <summary>
    /// Mahsup farkı: hak edilen harcırahın fişle karşılanmayan kısmı.
    /// Eksi çıkmaz — fiş harcırahı aşarsa fark sıfırdır, fazlası ayrı
    /// bir masraf kalemidir, mahsup konusu değil.
    /// </summary>
    public decimal SettlementGap => Math.Max(0m, TotalAllowance - ReceiptAmount);

    /// <summary>
    /// Mahsup bekliyor mu: fark var ve karar verilmemiş. Onaysız
    /// görevde mahsup da yok.
    /// </summary>
    public bool SettlementPending =>
        Status == PersonnelDutyStatus.Approved &&
        SettlementGap > 0m &&
        SettlementDecision is null;
}
