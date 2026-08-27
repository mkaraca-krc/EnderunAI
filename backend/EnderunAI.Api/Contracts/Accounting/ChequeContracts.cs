namespace EnderunAI.Api.Contracts.Accounting;

/// <summary>
/// Çekin bir payı. Fatura verilirse proje ve masraf merkezi faturadan
/// türetilir; gönderilen değerler yok sayılır (tek kaynak fatura olsun).
/// </summary>
public sealed record ChequeAllocationRequest(
    decimal Amount,
    Guid? ProjectId = null,
    string? CostCenterCode = null,
    Guid? SupplierInvoiceId = null,
    Guid? SalesInvoiceId = null,
    string? Description = null);

public sealed record ChequeAllocationsRequest(
    IReadOnlyCollection<ChequeAllocationRequest> Allocations,
    /// <summary>
    /// EŞZAMANLI DEĞİŞİKLİK DAMGASI — ZORUNLU.
    ///
    /// Çekin durumunu değiştiren HER uç bunu ister. Bir uçta eksik
    /// olması korumanın hiç olmaması demektir: iki kullanıcı aynı çeke
    /// aynı anda işlem yaparsa biri diğerininkini görmeden üzerine
    /// yazar ve çekte bu, aynı parayı iki kez işlemek anlamına gelir.
    /// </summary>
    DateTime? RowVersion = null);

public sealed record ChequeAllocationResponse(
    Guid Id,
    decimal Amount,
    Guid? ProjectId,
    string? ProjectCode,
    string? ProjectName,
    string? CostCenterCode,
    Guid? SupplierInvoiceId,
    string? SupplierInvoiceNumber,
    Guid? SalesInvoiceId,
    string? SalesInvoiceNumber,
    string? Description);

/// <summary>Bir faturayı ödeyen çekin özeti (fatura ekranında görünür).</summary>
public sealed record InvoiceChequePaymentResponse(
    Guid ChequeId,
    string InternalNumber,
    string ChequeNumber,
    DateTime DueDate,
    int Status,
    string StatusName,
    decimal AllocatedAmount);

public sealed record CreateChequeRequest(
    Guid CompanyId,
    int Direction,
    string ChequeNumber,
    string BankName,
    string? BankBranch,
    string? Drawer,
    Guid? CurrentAccountId,
    Guid? ProjectId,
    decimal Amount,
    string CurrencyCode,
    DateTime IssueDate,
    DateTime DueDate,
    Guid? ProgressPaymentId,
    Guid? SupplierInvoiceId,
    string? Description,
    /// <summary>Merkez ofis ya da şantiye kodu; boşsa proje kodu kullanılır.</summary>
    string? CostCenterCode = null,
    /// <summary>
    /// Keşide tarihindeki kur. Boş bırakılırsa TCMB arşivinden çözülür;
    /// arşivde de yoksa dövizli çek kaydedilmez.
    /// </summary>
    decimal? ExchangeRate = null,
    /// <summary>
    /// Proje/masraf merkezi dağılımı. Boş bırakılırsa çek tek parça
    /// işlenir (bugünkü davranış).
    /// </summary>
    IReadOnlyCollection<ChequeAllocationRequest>? Allocations = null);

public sealed record UpdateChequeRequest(
    string ChequeNumber,
    string BankName,
    string? BankBranch,
    string? Drawer,
    Guid? CurrentAccountId,
    Guid? ProjectId,
    decimal Amount,
    DateTime IssueDate,
    DateTime DueDate,
    Guid? ProgressPaymentId,
    Guid? SupplierInvoiceId,
    string? Description,
    string? CostCenterCode = null,
    /// <summary>
    /// EŞZAMANLI DEĞİŞİKLİK DAMGASI — ZORUNLU.
    ///
    /// Opsiyonel bırakılsaydı koruma fiilen olmazdı: atlatmak için
    /// alanı göndermemek yeterdi. Tek istemci kendi ön yüzümüz;
    /// korunacak eski istemci yok.
    /// </summary>
    DateTime? RowVersion = null,
    /// <summary>Düzeltme gerekçesi — denetim kaydına yazılır.</summary>
    string? EditReason = null,
    /// <summary>
    /// Para birimi. Değişirse kur YENİDEN ÇÖZÜLÜR ve giriş fişi ters
    /// kayıtla kapanıp yenisi kesilir — dövizli bir çeki eski kurla
    /// bırakmak defteri sessizce yanlışlardı.
    /// </summary>
    string? CurrencyCode = null,
    /// <summary>Elle kur; boşsa çözümleyici (belge/TCMB arşivi) kullanılır.</summary>
    decimal? ExchangeRate = null);

/// <summary>Geri alma / iptal isteği — gerekçe her ikisinde de zorunlu.</summary>
public sealed record ChequeReversalRequest(
    string? Reason,
    /// <summary>Eşzamanlı değişiklik damgası — ZORUNLU (bkz. UpdateChequeRequest).</summary>
    DateTime? RowVersion = null,
    /// <summary>
    /// İptal nedeni — 0 Yanlış giriş, 1 Karşılıksız, 2 Müşteriye iade,
    /// 90 Diğer (açıklama zorunlu). Serbest metin nedenin YERİNE geçmez:
    /// "kaç çek karşılıksız çıktı" ancak sayılabilir nedenle cevaplanır.
    /// </summary>
    int? ReasonKind = null);

/// <summary>
/// Durum geçişi. CashAccountId yalnızca para hareketi doğuran
/// geçişlerde zorunlu (bankaya verme, tahsil, ödeme, rücu).
/// </summary>
public sealed record ChequeStatusChangeRequest(
    int ToStatus,
    DateTime MovementDate,
    Guid? CashAccountId,
    string? Description,
    /// <summary>
    /// EŞZAMANLI DEĞİŞİKLİK DAMGASI — ZORUNLU.
    ///
    /// Çekin durumunu değiştiren HER uç bunu ister. Bir uçta eksik
    /// olması korumanın hiç olmaması demektir: iki kullanıcı aynı çeke
    /// aynı anda işlem yaparsa biri diğerininkini görmeden üzerine
    /// yazar ve çekte bu, aynı parayı iki kez işlemek anlamına gelir.
    /// </summary>
    DateTime? RowVersion = null);

/// <summary>
/// Çek erteleme/değişim. Tutar YENİDEN ALINMAZ: yeni çek eski çekle
/// aynı tutarda olmak zorunda. Vade farkı ayrı bir belgeyle (fatura ya
/// da dekont) kaydedilir; burada otomatik bir gider hesabı uydurulmaz.
/// </summary>
public sealed record ReplaceChequeRequest(
    string ChequeNumber,
    DateTime DueDate,
    DateTime MovementDate,
    string? BankName = null,
    string? BankBranch = null,
    string? Drawer = null,
    DateTime? IssueDate = null,
    string? Description = null,
    /// <summary>
    /// EŞZAMANLI DEĞİŞİKLİK DAMGASI — ZORUNLU.
    ///
    /// Çekin durumunu değiştiren HER uç bunu ister. Bir uçta eksik
    /// olması korumanın hiç olmaması demektir: iki kullanıcı aynı çeke
    /// aynı anda işlem yaparsa biri diğerininkini görmeden üzerine
    /// yazar ve çekte bu, aynı parayı iki kez işlemek anlamına gelir.
    /// </summary>
    DateTime? RowVersion = null);

public sealed record ChequeMovementResponse(
    Guid Id,
    DateTime MovementDate,
    int? FromStatus,
    string? FromStatusName,
    int ToStatus,
    string ToStatusName,
    string Description,
    Guid? CashAccountId,
    string? CashAccountName,
    Guid? AccountingVoucherId,
    string? AccountingVoucherNumber);

public sealed record ChequeListItemResponse(
    Guid Id,
    Guid CompanyId,
    int Direction,
    string DirectionName,
    int Status,
    string StatusName,
    string InternalNumber,
    string ChequeNumber,
    string BankName,
    string? Drawer,
    Guid? CurrentAccountId,
    string? CurrentAccountTitle,
    Guid? ProjectId,
    string? ProjectCode,
    string? CostCenterCode,
    decimal Amount,
    string CurrencyCode,
    /// <summary>Keşide kuru; TL çekte 1.</summary>
    decimal ExchangeRate,
    /// <summary>Keşide tarihindeki TL karşılığı — defter değeri.</summary>
    decimal AmountTry,
    DateTime IssueDate,
    DateTime DueDate,
    int DaysToDue,
    bool IsOverdue,
    /// <summary>
    /// BU SATIR TUTAR TOPLAMINA GİRER Mİ — kararı SUNUCU verir.
    ///
    /// Ekran eskiden kendi kuralını yazıyordu (`status !== Voided`) ve
    /// liste ucundaki süzgeçten AYRI karar veriyordu; ÇEK/1'deki hata
    /// tam olarak bu ayrışmaydı. Bayrak buraya konuldu ki ekranda
    /// ikinci bir karar yeri kalmasın.
    /// </summary>
    bool CountsTowardTotals);

public sealed record ChequeDetailResponse(
    Guid Id,
    Guid CompanyId,
    int Direction,
    string DirectionName,
    int Status,
    string StatusName,
    string InternalNumber,
    string ChequeNumber,
    string BankName,
    string? BankBranch,
    string? Drawer,
    Guid? CurrentAccountId,
    string? CurrentAccountTitle,
    Guid? ProjectId,
    string? ProjectCode,
    string? ProjectName,
    string? CostCenterCode,
    decimal Amount,
    string CurrencyCode,
    /// <summary>Keşide kuru; TL çekte 1.</summary>
    decimal ExchangeRate,
    /// <summary>Keşide tarihindeki TL karşılığı — defter değeri.</summary>
    decimal AmountTry,
    DateTime IssueDate,
    DateTime DueDate,
    Guid? ProgressPaymentId,
    string? ProgressPaymentNumber,
    Guid? SupplierInvoiceId,
    string? SupplierInvoiceNumber,
    Guid? CashAccountId,
    string? CashAccountName,
    string? Description,
    IReadOnlyCollection<int> AllowedNextStatuses,
    IReadOnlyCollection<ChequeMovementResponse> Movements,
    IReadOnlyCollection<ChequeAllocationResponse> Allocations,
    /// <summary>Bu çek ertelendiyse yerine geçen çek.</summary>
    Guid? ReplacedByChequeId,
    string? ReplacedByChequeNumber,
    /// <summary>Bu çek bir ertelemenin sonucuysa yerine geçtiği çek.</summary>
    Guid? ReplacesChequeId,
    string? ReplacesChequeNumber,
    /// <summary>
    /// Zincirde kaç kez ertelendiği. Risk sinyali: sürekli ertelenen
    /// çek tahsilat sorununun habercisidir.
    /// </summary>
    int RenewalCount,

    /// <summary>
    /// EŞZAMANLI DEĞİŞİKLİK DAMGASI. Ekran bunu alıp düzenleme ve
    /// iptal isteğinde geri yolluyor; arada başkası değiştirmişse
    /// istek reddediliyor.
    /// </summary>
    DateTime RowVersion,

    /// <summary>Düzenleme düğmesi açık mı — karar sunucudan gelir.</summary>
    bool CanEdit,

    /// <summary>
    /// Kapalıysa NEDEN kapalı. Ekran bu cümleyi AYNEN gösteriyor;
    /// kendi metnini uydursaydı API ile ekran zamanla ayrışırdı.
    /// </summary>
    string? EditBlockedReason,

    /// <summary>
    /// TANIMLAYICI ALANLAR AÇIK MI (ÇEK/2 · K2) — keşideci, şube,
    /// açıklama. <c>CanEdit</c> kapalıyken bile açık olabilir:
    /// kapanmış çekte yazım hatası düzeltmek mali kaydı iptal etmeyi
    /// gerektirmiyor. Ekran düzenleme formunda mali alanları bu
    /// bilgiye göre pasifleştiriyor.
    /// </summary>
    bool CanEditDescriptive,

    /// <summary>Çek kapanmış bir durumdan mı iptal edildi (rozet için).</summary>
    bool VoidedFromClosedState,

    /// <summary>İptal nedeni — sayılabilir; eski kayıtlarda boş.</summary>
    int? VoidReasonKind,
    string? VoidReasonName,

    /// <summary>Alan bazlı düzeltme geçmişi.</summary>
    IReadOnlyCollection<ChequeChangeLogResponse> ChangeLog,

    /// <summary>
    /// BU ÇEK İPTAL EDİLİRSE AÇILACAK ORİJİNAL ÇEK — yoksa null.
    ///
    /// Ekran iptalden ÖNCE uyarıyor: kullanıcı "yerine geçen çeki
    /// iptal ediyorum" derken orijinalin hangi duruma döneceğini
    /// görsün. Sonradan öğrenilen bir durum değişimi, iptal kararını
    /// bilerek almayı imkânsız kılardı.
    /// </summary>
    string? VoidRestoresChequeNumber = null,
    /// <summary>Orijinalin döneceği durumun adı — "Bankada (tahsilde)".</summary>
    string? VoidRestoresStatusName = null);

/// <summary>Tek alanın düzeltme kaydı — "Değişiklik geçmişi" sekmesi.</summary>
public sealed record ChequeChangeLogResponse(
    Guid Id,
    string FieldName,
    string FieldLabel,
    string? OldValue,
    string? NewValue,
    /// <summary>Muhasebeyi etkileyen alan mı (tutar, vade, cari) — süzgeç için.</summary>
    bool AffectsAccounting,
    DateTime ChangedAtUtc,
    Guid? ChangedByUserId,
    string? ChangedByUserName,
    string? Reason);

public sealed record ChequeSummaryResponse(
    decimal ReceivedPortfolioAmount,
    decimal ReceivedAtBankAmount,
    decimal ReceivedAtFactoringAmount,
    decimal ReceivedCollectedAmount,
    decimal ReceivedBouncedAmount,
    decimal IssuedOpenAmount,
    decimal IssuedPaidAmount,
    int ReceivedOpenCount,
    int IssuedOpenCount);
