namespace EnderunAI.Api.Models;

public enum ChequeDirection
{
    /// <summary>Alınan çek — işverenden/müşteriden tahsilat aracı (101).</summary>
    Received = 0,
    /// <summary>Verilen çek — tedarikçiye kendi vade yükümlülüğümüz (103).</summary>
    Issued = 1
}

/// <summary>
/// Çek durumları. 0-4 alınan çek akışı, 10-12 verilen çek akışı için
/// kullanılır; iki akış birbirine karışmaz (geçiş matrisi
/// ChequeService.AllowedTransitions içinde tanımlı).
/// </summary>
public enum ChequeStatus
{
    /// <summary>Portföyde — elimizde duruyor (101.01).</summary>
    Portfolio = 0,
    /// <summary>Bankaya tahsile/teminata verildi (101.02).</summary>
    AtBank = 1,
    /// <summary>Faktoring şirketine kırdırıldı (101.03).</summary>
    AtFactoring = 2,
    /// <summary>Tahsil edildi — para kasaya/bankaya girdi.</summary>
    Collected = 3,
    /// <summary>Karşılıksız çıktı — alacak cariye geri döndü.</summary>
    Bounced = 4,

    /// <summary>Tedarikçiye verildi, vadesi bekleniyor (103).</summary>
    Issued = 10,
    /// <summary>Vadesinde bankadan ödendi.</summary>
    Paid = 11,
    /// <summary>Tedarikçiden geri alındı / iptal edildi.</summary>
    Returned = 12,

    /// <summary>
    /// ERTELENDİ/DEĞİŞTİRİLDİ: çek yerine yeni vadeli bir çek verildi
    /// (ya da bizde alındı). Her iki yönde de kullanılır.
    ///
    /// Returned yeniden kullanılmadı: "iade alındı" ile "yenisiyle
    /// değiştirildi" farklı olaylardır ve erteleme sayısı bir risk
    /// sinyalidir — ikisi tek durumda toplanırsa o sinyal kaybolur.
    /// </summary>
    Replaced = 20,

    /// <summary>
    /// İPTAL EDİLDİ (void). Yanlış girilen çek defterden SİLİNMEZ,
    /// iptale çekilir: mali kayıt olduğu için geçmişin kaybolmaması
    /// gerekiyor. İptal, çekin ürettiği bütün muhasebe ve banka
    /// etkilerini ters kayıtla geri alır — çek kapanır ama izi durur.
    /// </summary>
    Voided = 90
}

/// <summary>
/// İPTAL NEDENİ — serbest metin DEĞİL, sayılabilir liste.
///
/// Serbest metin bırakıldığında "yanlış", "hata", "iptal" gibi on
/// farklı yazım doğuyor ve "kaç çek karşılıksız çıktı" sorusu hiç
/// cevaplanamıyor. Açıklama alanı DURUYOR ama nedenin yerine geçmiyor.
/// </summary>
public enum ChequeVoidReason
{
    /// <summary>
    /// YANLIŞ GİRİŞ — yalnız henüz işlem görmemiş çekte seçilebilir.
    ///
    /// Kapanmış bir çek yanlış giriş nedeniyle iptal edilmez: o çek
    /// gerçekten tahsil edilmiş/ödenmiştir. Yazım hatası varsa yol
    /// DÜZENLEMEDİR, iptal değil.
    /// </summary>
    DataEntryError = 0,

    /// <summary>Karşılıksız çıktı.</summary>
    Bounced = 1,

    /// <summary>Müşteriye/tedarikçiye iade edildi.</summary>
    ReturnedToParty = 2,

    /// <summary>Diğer — açıklama zorunlu.</summary>
    Other = 90
}

/// <summary>
/// ÇEKİN DÜZENLENEBİLİRLİĞİ — TEK TANIM.
///
/// UI düğmesi, API doğrulaması ve toplu işlemler HEPSİ buradan
/// soruyor. İki ayrı yerde yazılsaydı biri gevşer, diğeri sıkı kalır
/// ve kullanıcı düğmeyi görüp tıkladığında reddedilirdi — ya da
/// daha kötüsü, UI kapatır API açık kalırdı.
///
/// `Reason` kullanıcıya SOMUT sebebi söylemek için: hangi işlem,
/// hangi tarih, hangi taraf. "Düzenlenemez" tek başına kullanıcıyı
/// ne yapacağını bilmeden bırakır.
/// </summary>
/// <remarks>
/// İKİ AYRI SORU, İKİ AYRI CEVAP (ÇEK/2 · K1/K2):
///
/// <c>CanEdit</c> — MALİ VE KİMLİK alanları açık mı (tutar, vade, çek
/// no, cari, banka, masraf merkezi). Kapanmış ya da işlem görmüş
/// çekte KAPALI; bu, bugünkü davranıştır ve değişmedi.
///
/// <c>CanEditDescriptive</c> — TANIMLAYICI alanlar açık mı (keşideci,
/// şube, açıklama). Bunlar deftere de bakiyeye de dokunmaz, o yüzden
/// kapanmış çekte de açıktır: bir yazım hatasını düzeltmek için mali
/// kaydı iptal edip yeniden üretmek, hatanın kendisinden zararlıdır.
///
/// Tek bir bayrak yetmiyordu; eskiden yetiyor sanılıyordu ve bedeli
/// "iptal edip yeniden girin"di.
/// </remarks>
public sealed record ChequeEditability(
    bool CanEdit, string? Reason, bool CanEditDescriptive)
{
    public static ChequeEditability Allowed() => new(true, null, true);

    /// <summary>
    /// HER ŞEY KAPALI. Yalnız kaydın kendisine güvenilemediği hâlde
    /// kullanılır (hareket geçmişi yok); mali kapanış için değil.
    /// </summary>
    public static ChequeEditability Blocked(string reason) => new(false, reason, false);

    /// <summary>
    /// MALİ ALANLAR KAPALI, TANIMLAYICI ALANLAR AÇIK. Kapanmış ya da
    /// işlem görmüş çeğin normal hâli.
    /// </summary>
    public static ChequeEditability DescriptiveOnly(string reason) =>
        new(false, reason, true);
}

/// <summary>
/// Çift yönlü çek defteri. Alınan çekler işverenden gelen tahsilat
/// aracı (101 Alınan Çekler), verilen çekler tedarikçiye karşı vade
/// yükümlülüğümüz (103 Verilen Çekler). Her durum geçişi bir
/// ChequeMovement satırı ve — muhasebe etkisi varsa — dengeli, doğrudan
/// Posted bir fiş üretir.
/// </summary>
public sealed class Cheque : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public ChequeDirection Direction { get; set; }
    public ChequeStatus Status { get; set; } = ChequeStatus.Portfolio;

    /// <summary>Sistem içi takip numarası (ACK-2026-000001 / VCK-2026-000001).</summary>
    public string InternalNumber { get; set; } = string.Empty;

    private string _chequeNumber = string.Empty;

    /// <summary>
    /// Çekin üzerindeki seri/çek numarası — kullanıcının yazdığı hâli.
    ///
    /// NORMALİZE DEĞER BURADAN TÜRÜYOR. İki ayrı atama olsaydı bir
    /// çağrı yeri birini yazıp diğerini unutabilir, kayıt normalize
    /// değeri BOŞ kalırdı — o hâlde kısmi tekil indekste bütün boşlar
    /// çakışır ve ikinci çek hiç kaydedilemezdi. Setter'a bağlamak bu
    /// sınıf hatayı tümden kapatıyor.
    /// </summary>
    public string ChequeNumber
    {
        get => _chequeNumber;
        set
        {
            _chequeNumber = value ?? string.Empty;
            NormalizedChequeNumber = NormalizeChequeNumber(_chequeNumber);
        }
    }

    /// <summary>
    /// MÜKERRER ENGELİNİN DAYANDIĞI NORMALİZE NUMARA.
    ///
    /// Boşluklar (aradakiler dahil) atılır ve büyük harfe çevrilir;
    /// böylece "12 345", "12345" ve "12345 " aynı çek sayılır — canlıda
    /// aynı çekin iki kez girilmesinin en sık yolu buydu.
    ///
    /// BAŞTAKİ SIFIRLAR KORUNUR: "0012345" ile "12345" FARKLI çeklerdir.
    /// Sayıya çevirip karşılaştırmak (ya da TrimStart('0')) iki ayrı
    /// çeki tek çek sanmaya yol açardı.
    ///
    /// AYRI KOLON, ifade indeksi değil: uygulamanın ön kontrolü ile
    /// veritabanı kısıtı AYNI değeri kullanabilsin diye. İfadeye
    /// gömülseydi ikisi zamanla ayrışabilirdi.
    /// </summary>
    public string NormalizedChequeNumber { get; set; } = string.Empty;

    /// <summary>
    /// Çek numarasını mükerrer engeli için normalize eder.
    ///
    /// TEK TANIM: kaydetme, düzenleme, erteleme ve ön kontrol hepsi
    /// buradan geçiyor. İkinci bir kopya yazılsaydı kurallar zamanla
    /// ayrışır ve kısıt "bazen" çalışırdı.
    /// </summary>
    public static string NormalizeChequeNumber(string? value) =>
        new string((value ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray())
            .ToUpperInvariant();

    /// <summary>Çekin ait olduğu banka (keşide bankası).</summary>
    public string BankName { get; set; } = string.Empty;
    public string? BankBranch { get; set; }

    /// <summary>Keşideci — alınan çekte çeki yazan taraf, verilen çekte biziz.</summary>
    public string? Drawer { get; set; }

    /// <summary>Alınan çekte çeki veren cari, verilen çekte çeki alan cari.</summary>
    public Guid? CurrentAccountId { get; set; }
    public CurrentAccount? CurrentAccount { get; set; }

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>
    /// Masraf merkezi kodu — Merkez ofis (şube kodu) ya da şantiye.
    /// Ofis kirası gibi projesi olmayan çekler için var: boş bırakılırsa
    /// fişte proje kodu, o da yoksa şirket kodu kullanılır.
    /// </summary>
    public string? CostCenterCode { get; set; }

    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "TRY";

    /// <summary>
    /// Keşide tarihindeki kur — çekin DEFTER DEĞERİ bununla belirlenir.
    ///
    /// Dövizli çekte 1 bırakılamaz: geçmişte fiş satırları sabit 1 kuru
    /// ile kesiliyordu ve 10.000 dolarlık bir çek deftere 10.000 TL
    /// olarak giriyordu. Kur, faturalarla aynı çözümleyiciden gelir
    /// (belge/elle → TCMB arşivi); bulunamazsa çek kaydedilmez.
    /// </summary>
    public decimal ExchangeRate { get; set; } = 1m;

    /// <summary>
    /// Keşide tarihindeki TL karşılığı (<c>Amount × ExchangeRate</c>).
    ///
    /// Saklanıyor çünkü tahsilat/ödeme anındaki kur farkı bu değere göre
    /// hesaplanıyor; kuru sonradan yeniden çözmek, arşiv değiştiğinde
    /// geçmiş fişle tutmayan bir fark üretirdi.
    /// </summary>
    public decimal AmountTry { get; set; }

    /// <summary>Yerel para birimi mi — dövizli mantığın anahtarı.</summary>
    public bool IsLocalCurrency =>
        string.Equals(CurrencyCode, "TRY", StringComparison.OrdinalIgnoreCase);

    /// <summary>Keşide tarihi.</summary>
    public DateTime IssueDate { get; set; }
    /// <summary>Vade — nakit akışı bu tarihe göre hesaplanır.</summary>
    public DateTime DueDate { get; set; }

    /// <summary>Bu çek hangi hakedişin tahsilatı (alınan çek).</summary>
    public Guid? ProgressPaymentId { get; set; }
    public ProgressPayment? ProgressPayment { get; set; }

    /// <summary>Bu çek hangi tedarikçi faturasının ödemesi (verilen çek).</summary>
    public Guid? SupplierInvoiceId { get; set; }
    public SupplierInvoice? SupplierInvoice { get; set; }

    /// <summary>Bankaya/faktoringe verildiğinde ilgili banka hesabı.</summary>
    public Guid? CashAccountId { get; set; }
    public CashAccount? CashAccount { get; set; }

    // --- İptal izi ---

    public DateTime? VoidedAtUtc { get; set; }
    public Guid? VoidedByUserId { get; set; }

    /// <summary>İptal gerekçesi (serbest açıklama); gerekçesiz iptal denetlenemez.</summary>
    public string? VoidReason { get; set; }

    /// <summary>
    /// İptal nedeni — sayılabilir. Eski kayıtlarda boş olabilir.
    /// </summary>
    public ChequeVoidReason? VoidReasonKind { get; set; }

    /// <summary>
    /// KAPANMIŞ ÇEK İPTALİ Mİ.
    ///
    /// Tahsil edilmiş, ödenmiş, bankada/faktoringde olan, karşılıksız
    /// çıkan ya da iade alınmış bir çekin iptali portföydeki bir çeki
    /// iptal etmekle aynı şey değil: gerçekleşmiş bir para hareketini
    /// storno ile geri alır VE numarayı yeniden kullanıma açar.
    ///
    /// Ayrı bayrak, listede rozetle gösterilebilsin ve "bu para nereye
    /// gitti" sorusu geldiğinde tek bakışta görülebilsin diye.
    /// </summary>
    public bool VoidedFromClosedState { get; set; }

    public string? Description { get; set; }

    /// <summary>
    /// Bu çek ertelendiyse yerine geçen yeni çek.
    /// </summary>
    public Guid? ReplacedByChequeId { get; set; }
    public Cheque? ReplacedByCheque { get; set; }

    /// <summary>
    /// Bu çek bir ertelemenin sonucuysa yerine geçtiği eski çek.
    /// Zincir buradan geriye doğru izlenir; uzunluğu kaç kez
    /// ertelendiğini verir.
    /// </summary>
    public Guid? ReplacesChequeId { get; set; }
    public Cheque? ReplacesCheque { get; set; }

    public ICollection<ChequeMovement> Movements { get; set; }
        = new List<ChequeMovement>();

    /// <summary>
    /// Proje/masraf merkezi dağılımı. Boşsa çek tek parça işlenir ve
    /// yukarıdaki ProjectId/CostCenterCode geçerlidir.
    /// </summary>
    public ICollection<ChequeAllocation> Allocations { get; set; }
        = new List<ChequeAllocation>();
}

/// <summary>
/// Çekin durum geçmişi. Her satır bir durum geçişini ve (varsa) o
/// geçişte üretilen muhasebe fişini taşır.
/// </summary>
public sealed class ChequeMovement : BaseEntity
{
    public Guid ChequeId { get; set; }
    public Cheque Cheque { get; set; } = null!;

    public DateTime MovementDate { get; set; }

    /// <summary>Geçiş öncesi durum; ilk kayıtta null.</summary>
    public ChequeStatus? FromStatus { get; set; }
    public ChequeStatus ToStatus { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>Geçişte para hareketi olduysa ilgili kasa/banka hesabı.</summary>
    public Guid? CashAccountId { get; set; }
    public CashAccount? CashAccount { get; set; }

    /// <summary>Geçişte üretilen (Posted) muhasebe fişi; etkisiz geçişlerde null.</summary>
    public Guid? AccountingVoucherId { get; set; }
    public AccountingVoucher? AccountingVoucher { get; set; }

    // --- Geri alma izi ---
    //
    // Geri alınan hareket SİLİNMEZ, damgalanır: "bu geçiş yapıldı ve
    // sonra geri alındı" ile "bu geçiş hiç olmadı" farklı olaylardır
    // ve yanlış ödeme sayısı denetimde aranan bir sinyaldir.

    public DateTime? ReversedAtUtc { get; set; }
    public Guid? ReversedByUserId { get; set; }
    public string? ReversalReason { get; set; }

    /// <summary>Bu geçişin fişini kapatan ters kayıt.</summary>
    public Guid? ReversalVoucherId { get; set; }

    /// <summary>Geri alındı mı — sorgularda tek yerden okunuyor.</summary>
    public bool IsReversed => ReversedAtUtc is not null;
}
