namespace EnderunAI.Api.Models.Fleet;

public enum VehicleType
{
    Car = 0,
    Pickup = 1,
    Van = 2,
    Truck = 3,
    Bus = 4,
    /// <summary>İş makinesi — kepçe, forklift, vinç.</summary>
    ConstructionMachine = 5,
    Other = 99
}

/// <summary>
/// Aracın bize ait mi kiralık mı olduğu. Masrafın yansıtılmasını
/// DEĞİŞTİRMEZ (ikisinin de cari masrafı projeye düşer); kira bedeli
/// yalnız kiralıkta vardır.
/// </summary>
public enum VehicleOwnership
{
    /// <summary>Öz mal.</summary>
    Owned = 0,

    /// <summary>Kiralık.</summary>
    Rented = 1
}

public enum VehicleFuelType
{
    Diesel = 0,
    Gasoline = 1,
    Lpg = 2,
    Electric = 3,
    Hybrid = 4,
    Other = 99
}

/// <summary>Kira bedelinin hangi aralıkla ödendiği.</summary>
public enum VehicleRentPeriod
{
    Monthly = 0,
    Quarterly = 1,
    Yearly = 2
}

/// <summary>
/// ARAÇ KARTI — elle girilir (toplu aktarım yok; araç sayısı elle
/// yönetilebilecek kadar azdır ve plaka hatası pahalıdır).
///
/// MALİYET DEFTERİ DEĞİL. Araç masrafı ayrı bir tabloda tutulmaz;
/// mevcut gider kaydına (<see cref="Expenses.ExpenseEntry.VehicleId"/>)
/// "bu gider şu araca ait" bağı eklenir. Araç kartındaki masraf
/// dökümü o kayıtların FİLTRELENMİŞ görünümüdür — ikinci bir toplama
/// kaynağı olsaydı aynı masraf iki kez sayılırdı.
///
/// AMORTİSMAN YOK: öz malda alış bilgisi yalnızca "bu araca ne verdik"
/// sorusu için tutulur, maliyete dönmez. Amortisman resmî muhasebenin
/// işidir ve buradan üretilseydi gider merkezi raporunda gerçek
/// ödemelerle karışırdı.
/// </summary>
public sealed class Vehicle : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>
    /// Plaka. Şirket içinde benzersiz — ama YALNIZ silinmemişler
    /// arasında (kısmi index): satılan bir araç kaydı denetim izi
    /// olarak kalır, aynı plaka yeniden alınırsa yeni kart açılabilir.
    /// </summary>
    public string PlateNumber { get; set; } = string.Empty;

    public VehicleType Type { get; set; }
    public VehicleOwnership Ownership { get; set; }

    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? ChassisNumber { get; set; }
    public int? ModelYear { get; set; }
    public VehicleFuelType? FuelType { get; set; }

    // ---- Kiralık ----

    /// <summary>Kiralayan firma — tedarikçi carisi.</summary>
    public Guid? LessorCurrentAccountId { get; set; }
    public CurrentAccount? LessorCurrentAccount { get; set; }

    public decimal? RentAmount { get; set; }
    public VehicleRentPeriod? RentPeriod { get; set; }

    /// <summary>
    /// Kira vadesi — ayın kaçı. Nakit akışa kira bu günden düşer;
    /// tarihi olmayan bir borç projeksiyonda görünmez.
    /// </summary>
    public int? RentDueDay { get; set; }

    // ---- Öz mal ----

    public DateTime? PurchaseDate { get; set; }
    public decimal? PurchaseCost { get; set; }

    // ---- Takip tarihleri (hatırlatma kaynağı bunları okur) ----

    public DateTime? InspectionDueDate { get; set; }
    public DateTime? InsuranceRenewalDate { get; set; }
    public DateTime? CascoRenewalDate { get; set; }
    public DateTime? MotorTaxDueDate { get; set; }
    public DateTime? NextMaintenanceDate { get; set; }

    public string? Notes { get; set; }

    public ICollection<VehicleAssignment> Assignments { get; set; }
        = new List<VehicleAssignment>();
}

/// <summary>
/// ARACIN NEREDE OLDUĞU: bir projede ya da MERKEZ HAVUZUNDA.
///
/// Merkez havuzu ayrı bir bayrakla değil, <see cref="ProjectId"/>'nin
/// BOŞ olmasıyla ifade edilir — iki alan olsaydı "proje dolu ama
/// merkez işaretli" gibi çelişkili satırlar kurulabilirdi.
///
/// AYNI ANDA TEK AÇIK ATAMA: veritabanı düzeyinde kısmi benzersiz
/// index ile garanti (bkz. AppDbContext). Uygulama katmanı yeni atama
/// açarken öncekini kapatır; kural yalnız kodda dursaydı iki eşzamanlı
/// istek aracı iki projede gösterebilirdi.
///
/// GEÇMİŞ KORUNUR: atama değişince eski satır silinmez, bitiş tarihi
/// yazılır. Masraf yansıtması "o tarihte araç neredeydi" sorusunu
/// geçmişe dönük sorabilmeli.
/// </summary>
public sealed class VehicleAssignment : BaseEntity
{
    public Guid VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;

    /// <summary>Boşsa araç merkez havuzundadır.</summary>
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public Guid? ProjectSiteId { get; set; }
    public ProjectSite? ProjectSite { get; set; }

    /// <summary>Sürücü — opsiyonel; havuzdaki araçta sürücü olmayabilir.</summary>
    public Guid? DriverPersonnelId { get; set; }
    public Personnel? DriverPersonnel { get; set; }

    public DateTime StartDate { get; set; }

    /// <summary>Boşsa atama AÇIK — araç hâlâ orada.</summary>
    public DateTime? EndDate { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// Çağıranın verdiği tekrar anahtarı. Aynı anahtarla ikinci kez
    /// atama açılmaz, mevcut kayıt döner: ağ tekrarında ya da çift
    /// tıklamada araç iki kez atanmış görünmesin.
    /// </summary>
    public string? ReferenceKey { get; set; }
}
