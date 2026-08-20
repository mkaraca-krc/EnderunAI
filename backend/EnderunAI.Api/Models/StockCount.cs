namespace EnderunAI.Api.Models;

/// <summary>
/// Sayım oturumunun durumu.
///
/// <see cref="Counting"/> ve <see cref="PendingApproval"/> AKTİF
/// sayılır: bu iki durumda sayılan bölge KİLİTLİDİR. Onay bekleyen
/// oturumda da kilit sürüyor çünkü sayılan miktarlar henüz stoğa
/// işlenmedi; araya bir hareket girerse onay anında düzeltilecek fark
/// gerçeği yansıtmaz.
/// </summary>
public enum StockCountStatus
{
    Counting = 0,
    PendingApproval = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4
}

/// <summary>
/// FARK GEREKÇESİ — sayım farkı olan her satırda ZORUNLU.
///
/// Serbest metin değil, sayılabilir bir liste: "hangi depoda ne kadar
/// fire var" sorusu ancak gerekçe sayılabilirse cevaplanır. Metin
/// olsaydı aynı sebep on farklı şekilde yazılır ve hiçbir rapor
/// üretilemezdi.
/// </summary>
public enum StockCountVarianceReason
{
    /// <summary>Kullanım/işlem sırasında doğal kayıp.</summary>
    Wastage = 0,

    /// <summary>Nerede olduğu bilinmiyor.</summary>
    Loss = 1,

    /// <summary>Kayıt hatalı; malzeme aslında hiç eksilmemiş/artmamış.</summary>
    CountingError = 2,

    /// <summary>Fiziken kırılmış/hasar görmüş.</summary>
    Breakage = 3
}

/// <summary>
/// SAYIM OTURUMU — dönemsel fiziki sayımın belgesi.
///
/// Tek seferlik `POST adjustments` ucu DURUYOR ve kaldırılmadı: o,
/// tek bir kalemin anlık düzeltmesi için. Oturum ise bir dönemin
/// tamamını kapsıyor — sistem miktarları DONDURULUYOR, fiziki miktar
/// ayrıca giriliyor, fark gerekçeleniyor ve yetkili onayından geçiyor.
/// İkisi aynı uca sıkıştırılsaydı ya anlık düzeltme onay kapısına
/// takılır ya dönemsel sayım onaysız stok değiştirirdi.
///
/// SİSTEM MİKTARI OTURUM AÇILIRKEN DONDURULUYOR: sayım sırasında
/// stok değişirse fark "sayım anındaki" gerçeği yansıtmaz. Zaten
/// bölge kilitli olduğu için değişmemesi gerekiyor; dondurma o kilidin
/// ikinci savunma hattı.
/// </summary>
public sealed class StockCountSession : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    /// <summary>
    /// Sayılan bölge. BOŞSA deponun TAMAMI sayılıyor demektir ve kilit
    /// tüm depoyu kapsar.
    ///
    /// Konum stok satırında değil KARTTA duruyor
    /// (<see cref="InventoryItem.WarehouseZoneId"/>), bölge filtresi
    /// oradan uygulanıyor.
    /// </summary>
    public Guid? WarehouseZoneId { get; set; }
    public WarehouseZone? WarehouseZone { get; set; }

    /// <summary>Belge numarası — DocumentNumberService (STOCK_COUNT / SAYIM).</summary>
    public string DocumentNumber { get; set; } = string.Empty;

    /// <summary>Dönem etiketi — "2026 1. Yarıyıl" gibi.</summary>
    public string Name { get; set; } = string.Empty;

    public DateTime CountDate { get; set; }

    public StockCountStatus Status { get; set; } = StockCountStatus.Counting;

    public Guid? StartedByUserId { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? SubmittedByUserId { get; set; }

    public DateTime? DecidedAtUtc { get; set; }
    public Guid? DecidedByUserId { get; set; }

    /// <summary>Ret ve iptalde ZORUNLU.</summary>
    public string? DecisionReason { get; set; }

    /// <summary>
    /// Onayda üretilen düzeltme fişi. OTURUM BAŞINA TEK FİŞ: sayım tek
    /// bir olaydır; satır başına fiş kesilseydi mizan yüzlerce satırlık
    /// anlamsız bir yığına dönerdi.
    /// </summary>
    public Guid? AccountingVoucherId { get; set; }

    public ICollection<StockCountLine> Lines { get; set; } = new List<StockCountLine>();
}

/// <summary>
/// Oturumun tek satırı — bir stok kartının sistemdeki ve fiilen
/// sayılan miktarı.
/// </summary>
public sealed class StockCountLine : BaseEntity
{
    public Guid StockCountSessionId { get; set; }
    public StockCountSession StockCountSession { get; set; } = null!;

    public Guid InventoryItemId { get; set; }
    public InventoryItem InventoryItem { get; set; } = null!;

    /// <summary>Oturum açılırken dondurulan sistem miktarı.</summary>
    public decimal SystemQuantity { get; set; }

    /// <summary>
    /// Fiziki sayım sonucu. BOŞ = HENÜZ SAYILMADI.
    ///
    /// KULLANICI KARARI: sayılmayan satır onayda ATLANIR, stoğu
    /// değişmez. Sıfır sayılsaydı unutulan tek bir satır o malzemenin
    /// tüm stoğunu siler ve karşılığında gider yazardı. Kaç satırın
    /// sayılmadığı raporda açıkça bildiriliyor — atlanması sessiz
    /// olmuyor.
    /// </summary>
    public decimal? CountedQuantity { get; set; }

    /// <summary>
    /// Sayım anındaki ağırlıklı ortalama maliyet — dondurulur. Fark
    /// tutarı ve muhasebe fişi bundan hesaplanır.
    /// </summary>
    public decimal UnitCostAtCount { get; set; }

    /// <summary>Fark varsa ZORUNLU; fark yoksa boş kalır.</summary>
    public StockCountVarianceReason? VarianceReason { get; set; }

    /// <summary>Sayan kişinin serbest notu — gerekçenin yerine geçmez.</summary>
    public string? Note { get; set; }

    /// <summary>Fiziki miktar girilmişse fark, girilmemişse null.</summary>
    public decimal? Difference =>
        CountedQuantity is decimal counted ? counted - SystemQuantity : null;
}
