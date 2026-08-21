using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.DocumentNumbers;
using Microsoft.EntityFrameworkCore;
using EnderunAI.Api.Formatting;
using EnderunAI.Api.Services.Market;

namespace EnderunAI.Api.Services.Accounting;

public interface IChequeService
{
    Task<IReadOnlyCollection<ChequeListItemResponse>> GetAllAsync(
        Guid? companyId,
        int? direction,
        int? status,
        Guid? currentAccountId,
        Guid? projectId,
        /// <summary>
        /// MERKEZ SÜZGECİ. Proje seçimi projeye, bu kod merkeze
        /// (ya da proje dışı bir masraf merkezine) işlenmiş çekleri
        /// getiriyor — merkeze işlenen çekler yalnız "projesi yok"
        /// diye süzgeçsiz kalıyordu.
        /// </summary>
        string? costCenterCode,
        string? search,
        /// <summary>İptal edilen çekler varsayılan olarak listelenmez.</summary>
        bool includeVoided,
        CancellationToken cancellationToken);

    Task<ChequeDetailResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<ChequeSummaryResponse> GetSummaryAsync(
        Guid? companyId, CancellationToken cancellationToken);

    Task<ChequeDetailResponse> CreateAsync(
        CreateChequeRequest request, CancellationToken cancellationToken);

    Task<ChequeDetailResponse> UpdateAsync(
        Guid id, UpdateChequeRequest request, Guid? userId,
        CancellationToken cancellationToken);

    Task<ChequeDetailResponse> ChangeStatusAsync(
        Guid id, ChequeStatusChangeRequest request, CancellationToken cancellationToken);

    Task<ChequeDetailResponse> ReverseLastMovementAsync(
        Guid id, ChequeReversalRequest request, Guid? userId,
        /// <summary>Kapanmış durumdan geri alma yetkisi (cheque.void-closed).</summary>
        bool hasClosedReversePermission,
        CancellationToken cancellationToken);

    Task<ChequeDetailResponse> VoidAsync(
        Guid id, ChequeReversalRequest request, Guid? userId,
        /// <summary>Kapanmış çek iptali yetkisi var mı (cheque.void-closed).</summary>
        bool hasClosedVoidPermission,
        CancellationToken cancellationToken);

    /// <summary>
    /// Çekin proje/masraf merkezi dağılımını baştan yazar ve giriş fişini
    /// dağılıma göre yeniden üretir. Boş liste dağılımı kaldırır.
    /// </summary>
    Task<ChequeDetailResponse> ReplaceAllocationsAsync(
        Guid id, ChequeAllocationsRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Çek erteleme/değişim: eski çek "Ertelendi" durumuna geçip ters
    /// kaydı kesilir, yerine aynı tutarda yeni vadeli çek açılır ve
    /// zincire bağlanır. Verilen ve alınan çeklerin ikisinde de çalışır.
    /// </summary>
    Task<ChequeDetailResponse> ReplaceAsync(
        Guid id, ReplaceChequeRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Çift yönlü çek defteri. Durum geçişleri tek bir matristen yürür;
/// her geçiş bir hareket satırı, gerekiyorsa dengeli bir muhasebe fişi
/// ve para hareketi doğuran geçişlerde bir kasa/banka hareketi üretir.
/// Fiş üretilemezse durum da değişmez (tek işlem).
/// </summary>
public sealed class ChequeService(
    AppDbContext db,
    IAccountingIntegrationService accountingIntegration,
    IAccountingVoucherService voucherService,
    IDocumentNumberService documentNumberService,
    IInvoiceExchangeRateResolver exchangeRateResolver) : IChequeService
{
    /// <summary>
    /// İzin verilen durum geçişleri. Portföy → Faktoringde geçişi
    /// bilinçli olarak yok: kırdırma yalnızca faktoring modülünden,
    /// kesinti matematiğiyle birlikte yapılır.
    /// </summary>
    public static readonly IReadOnlyDictionary<ChequeStatus, IReadOnlyCollection<ChequeStatus>>
        AllowedTransitions = new Dictionary<ChequeStatus, IReadOnlyCollection<ChequeStatus>>
        {
            [ChequeStatus.Portfolio] = new[]
            {
                ChequeStatus.AtBank, ChequeStatus.Collected, ChequeStatus.Bounced,
                ChequeStatus.Replaced
            },
            [ChequeStatus.AtBank] = new[]
            {
                ChequeStatus.Portfolio, ChequeStatus.Collected, ChequeStatus.Bounced,
                ChequeStatus.Replaced
            },
            [ChequeStatus.AtFactoring] = new[]
            {
                ChequeStatus.Collected, ChequeStatus.Bounced
            },
            [ChequeStatus.Collected] = Array.Empty<ChequeStatus>(),
            [ChequeStatus.Bounced] = Array.Empty<ChequeStatus>(),
            [ChequeStatus.Issued] = new[]
            {
                ChequeStatus.Paid, ChequeStatus.Returned, ChequeStatus.Replaced
            },
            [ChequeStatus.Paid] = Array.Empty<ChequeStatus>(),
            [ChequeStatus.Returned] = Array.Empty<ChequeStatus>(),
            [ChequeStatus.Replaced] = Array.Empty<ChequeStatus>()
        };

    /// <summary>Banka hareketlerinin çek modülü etiketi.</summary>
    public const string ChequeSourceModule = "Cheque";

    /// <summary>
    /// Geri alma hareketlerinin etiketi. Ayrı tutuluyor ki bir geri
    /// almanın kendisi yeniden geri alınmaya çalışılmasın ve mükerrer
    /// kontrolü kaynak işlemin kimliğinden yürüsün.
    /// </summary>
    public const string ChequeReversalSourceModule = "ChequeReversal";

    /// <summary>Kasa/banka hesabı seçimi zorunlu olan geçişler.</summary>
    public static bool RequiresCashAccount(ChequeStatus from, ChequeStatus to) =>
        (from, to) switch
        {
            (ChequeStatus.Portfolio, ChequeStatus.AtBank) => true,
            (ChequeStatus.Portfolio, ChequeStatus.Collected) => true,
            (ChequeStatus.AtBank, ChequeStatus.Collected) => true,
            (ChequeStatus.AtFactoring, ChequeStatus.Bounced) => true,
            (ChequeStatus.Issued, ChequeStatus.Paid) => true,
            _ => false
        };

    /// <summary>
    /// Geçişin kasa/banka bakiyesine etkisi. null ise para hareketi yok
    /// (ör. bankaya tahsile verme yalnızca çekin yerini değiştirir).
    /// </summary>
    public static (CashTransactionType Type, CashTransactionDirection Direction)?
        CashEffect(ChequeStatus from, ChequeStatus to) => (from, to) switch
        {
            (ChequeStatus.Portfolio, ChequeStatus.Collected) =>
                (CashTransactionType.ChequeCollection, CashTransactionDirection.In),
            (ChequeStatus.AtBank, ChequeStatus.Collected) =>
                (CashTransactionType.ChequeCollection, CashTransactionDirection.In),
            (ChequeStatus.Issued, ChequeStatus.Paid) =>
                (CashTransactionType.ChequePayment, CashTransactionDirection.Out),
            (ChequeStatus.AtFactoring, ChequeStatus.Bounced) =>
                (CashTransactionType.Factoring, CashTransactionDirection.Out),
            _ => null
        };

    public static IReadOnlyCollection<ChequeStatus> NextStatuses(ChequeStatus status) =>
        AllowedTransitions.TryGetValue(status, out var next)
            ? next
            : Array.Empty<ChequeStatus>();

    public static string StatusName(ChequeStatus status) => status switch
    {
        ChequeStatus.Portfolio => "Portföyde",
        ChequeStatus.AtBank => "Bankada (tahsilde)",
        ChequeStatus.AtFactoring => "Faktoringde",
        ChequeStatus.Collected => "Tahsil edildi",
        ChequeStatus.Bounced => "Karşılıksız",
        ChequeStatus.Issued => "Verildi",
        ChequeStatus.Paid => "Ödendi",
        ChequeStatus.Returned => "İade alındı",
        ChequeStatus.Replaced => "Ertelendi (değiştirildi)",
        ChequeStatus.Voided => "İptal edildi",
        _ => status.ToString()
    };

    /// <summary>
    /// ÇEK DÜZENLENEBİLİR Mİ — TEK KARAR NOKTASI.
    ///
    /// Hem API doğrulaması hem detay yanıtındaki "düzenle düğmesi açık
    /// mı" bilgisi buradan geliyor. UI'da düğmeyi gizlemek yetmez;
    /// aynı kural API'de de çalışmalı ve İKİSİ AYNI METODU sormalı,
    /// yoksa zamanla ayrışırlar.
    ///
    /// KURAL: çek yalnız kendi ilk durumundayken ve hiçbir alt işleme
    /// girmemişken düzenlenebilir. Bankaya verilmiş, faktoringe
    /// kırdırılmış, tahsil edilmiş, ödenmiş, karşılıksız çıkmış, iade
    /// alınmış, ertelenmiş ya da iptal edilmiş çekte düzeltme yolu
    /// kapalıdır — o noktadan sonra gerçekleşmiş bir para hareketi
    /// vardır ve onu "düzeltmek" defteri sessizce değiştirmek olurdu.
    /// </summary>
    public async Task<ChequeEditability> GetEditabilityAsync(
        Cheque cheque, CancellationToken cancellationToken)
    {
        /*
         * HAREKETLERİ METOT KENDİSİ YÜKLÜYOR (F-çek/2/D).
         *
         * Önce dışarıdan parametre alıyordu ve bu sessiz bir delikti:
         * bir çağrı yeri boş liste geçse metot "düzenlenebilir" der,
         * kural hiçbir uyarı vermeden delinirdi. Yükleme burada olunca
         * yanlış çağrı ihtimali ortadan kalkıyor.
         */
        var movements = await db.ChequeMovements
            .AsNoTracking()
            .Where(x => x.ChequeId == cheque.Id)
            .ToListAsync(cancellationToken);

        /*
         * HAREKETSİZ ÇEK OLMAZ: her çek doğduğunda bir giriş hareketi
         * yazılıyor. Liste boşsa ya kayıt bozuk ya da yanlış çek
         * yüklenmiş demektir; "düzenlenebilir" demek yerine duruyoruz.
         */
        if (movements.Count == 0)
        {
            return ChequeEditability.Blocked(
                "Çekin hareket geçmişi bulunamadı; düzenleme güvenli değil. " +
                "Kaydı inceleyin.");
        }

        return EvaluateEditability(cheque, movements);
    }

    /// <summary>Saf karar — hareketler yüklenmiş hâlde verilir.</summary>
    private static ChequeEditability EvaluateEditability(
        Cheque cheque, IReadOnlyCollection<ChequeMovement> movements)
    {
        var openStatus = cheque.Direction == ChequeDirection.Received
            ? ChequeStatus.Portfolio
            : ChequeStatus.Issued;

        if (cheque.Status != openStatus)
        {
            // Durumu hangi hareket getirdiyse onu söylüyoruz: kullanıcı
            // "neden düzenleyemiyorum" sorusunun cevabını tarihiyle
            // birlikte görmeli.
            var cause = movements
                .Where(x => x.ToStatus == cheque.Status)
                .OrderByDescending(x => x.MovementDate)
                .FirstOrDefault();

            var when = cause is not null
                ? $"{cause.MovementDate:dd.MM.yyyy} tarihinde "
                : string.Empty;

            return ChequeEditability.Blocked(
                $"Bu çek {when}\"{StatusName(cheque.Status)}\" durumuna geçtiği için " +
                "düzenlenemez. İptal edip yeniden girin.");
        }

        /*
         * DURUM AÇIK OLSA BİLE HAREKET GÖRMÜŞ OLABİLİR: bankaya verilip
         * geri alınan bir çek yeniden portföye döner. O çekin geçmişinde
         * gerçekleşmiş fişler vardır; alanlarını değiştirmek onları
         * sessizce tutarsız bırakırdı.
         *
         * İlk kayıt hareketi (FromStatus == null) sayılmıyor — o çekin
         * doğuşu, bir alt işlem değil.
         */
        /*
         * GERİ ALINMIŞ İŞLEM "İŞLEM GÖRMÜŞ" SAYILMAZ.
         *
         * "Durum geri al" tam da düzeltmeye izin vermek için var:
         * yanlış işaretlenen "Ödendi" geri alınıyor, çek açık duruma
         * dönüyor ve düzeltiliyor. İlk yazımda bu akışı kırmıştım —
         * geri almanın KENDİ hareketini de alt işlem sayıyordum ve
         * mevcut test bunu yakaladı.
         *
         * İki eleme:
         *   - ters kaydı alınmış hareketler (ReversedAtUtc dolu),
         *   - çeki AÇIK duruma geri getiren hareketler (ToStatus ==
         *     openStatus) — bunlar bir alt işlem değil, geri dönüştür.
         */
        var processed = movements
            .Where(x => x.FromStatus != null
                && x.ReversedAtUtc == null
                && x.ToStatus != openStatus)
            .OrderByDescending(x => x.MovementDate)
            .FirstOrDefault();

        if (processed is not null)
        {
            return ChequeEditability.Blocked(
                $"Bu çek {processed.MovementDate:dd.MM.yyyy} tarihinde " +
                $"\"{StatusName(processed.ToStatus)}\" işlemi gördüğü için düzenlenemez. " +
                "İptal edip yeniden girin.");
        }

        return ChequeEditability.Allowed();
    }

    /// <summary>
    /// Kısmi tekil çek indeksinin ihlali mi.
    ///
    /// İNDEKS ADINA BAKILIYOR: başka bir tekil kısıt (ör. iç numara)
    /// da 23505 üretir ve onu "bu çek zaten kayıtlı" diye çevirmek
    /// kullanıcıyı yanlış yere bakmaya gönderirdi.
    /// </summary>
    private static bool IsChequeUniquenessViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException
        {
            SqlState: Npgsql.PostgresErrorCodes.UniqueViolation,
            ConstraintName: ChequeUniquenessIndexName
        };

    /// <summary>Kısmi tekil indeksin adı — migration ile aynı olmalı.</summary>
    public const string ChequeUniquenessIndexName = "IX_cheques_aktif_benzersizlik";

    /// <summary>
    /// Çakışan AKTİF çeki bulup kullanıcıya ne olduğunu söyleyen bir
    /// istisna üretir. Yarışı kaybeden istek buraya düşüyor; kayıt
    /// artık var olduğu için mesaj somut olabiliyor.
    /// </summary>
    private async Task<InvalidOperationException> DescribeChequeClashAsync(
        Guid companyId, ChequeDirection direction, string bankName, string? bankBranch,
        string normalizedNumber, CancellationToken cancellationToken)
    {
        try
        {
            await EnsureChequeNumberAvailableAsync(
                companyId, direction, bankName, bankBranch,
                normalizedNumber, excludeChequeId: null, cancellationToken);
        }
        catch (InvalidOperationException described)
        {
            return described;
        }

        // Kayıt arada silinmiş/iptal edilmiş olabilir; yine de ham hata
        // dönmüyoruz.
        return new InvalidOperationException(
            "Bu çek aynı anda başka bir yerden kaydedildi. " +
            "Sayfayı yenileyip tekrar deneyin.");
    }

    /// <summary>
    /// Aynı çek zaten AKTİF olarak kayıtlı mı — kullanıcı dostu mesaj
    /// üretmek için. Asıl güvence veritabanındaki kısmi tekil indeks.
    /// </summary>
    private async Task EnsureChequeNumberAvailableAsync(
        Guid companyId,
        ChequeDirection direction,
        string bankName,
        string? bankBranch,
        string normalizedNumber,
        Guid? excludeChequeId,
        CancellationToken cancellationToken)
    {
        /*
         * ANAHTAR VERİTABANINDAKİ İNDEKSLE BİREBİR AYNI OLMAK ZORUNDA:
         * şirket + yön + banka + şube + normalize no. İkisi ayrışırsa
         * uygulama "boş" der, indeks "dolu" der ve kullanıcı ham 500
         * görür — bu kontrolün tek işi zaten anlaşılır mesaj üretmek.
         *
         * Keşideci anahtarda YOK: çek numarası banka ve şube bazında
         * zaten tekil; keşideciyi eklemek aynı çekin, o alan farklı
         * yazılarak ikinci kez girilmesine kapı açardı.
         */
        var bank = (bankName ?? string.Empty).Trim().ToUpperInvariant();
        var branch = (bankBranch ?? string.Empty).Trim().ToUpperInvariant();

        var candidates = await db.Cheques
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId
                && x.Direction == direction
                && x.NormalizedChequeNumber == normalizedNumber
                && x.Status != ChequeStatus.Voided
                && (excludeChequeId == null || x.Id != excludeChequeId.Value))
            .Select(x => new
            {
                x.Id,
                x.InternalNumber,
                x.ChequeNumber,
                x.Status,
                x.DueDate,
                x.BankName,
                x.BankBranch
            })
            .ToListAsync(cancellationToken);

        var clash = candidates.FirstOrDefault(x =>
            (x.BankName ?? string.Empty).Trim().ToUpperInvariant() == bank
            && (x.BankBranch ?? string.Empty).Trim().ToUpperInvariant() == branch);

        if (clash is null)
            return;

        /*
         * MESAJ ANAHTARIN TAMAMINI SÖYLÜYOR.
         *
         * Önce yalnız kayıt no + durum + vade yazıyordu. Eksik olan
         * YÖN ve BANKA tam da kullanıcının "ama ben bunu girmedim ki"
         * dediği yerdi: aynı numara alınan ve verilen çekte ayrı ayrı
         * kaydedilebiliyor, farklı bankada da öyle. Hangi kaydın
         * engellediği söylenmezse kullanıcı doğru kaydı arayamıyor.
         */
        var bankLabel = string.IsNullOrWhiteSpace(clash.BankBranch)
            ? clash.BankName
            : $"{clash.BankName} / {clash.BankBranch}";

        throw new InvalidOperationException(
            $"Bu çek zaten kayıtlı — {DirectionName(direction)}, " +
            $"No: {clash.ChequeNumber}, Banka: {bankLabel}, " +
            $"Kayıt No: {clash.InternalNumber}, " +
            $"Durum: {StatusName(clash.Status)}, Vade: {clash.DueDate:dd.MM.yyyy}");
    }

    /// <summary>
    /// EŞZAMANLI DEĞİŞİKLİK KORUMASI — DAMGA ZORUNLU.
    ///
    /// Opsiyonel bırakılsaydı koruma fiilen olmazdı: atlatmak için
    /// alanı göndermemek yeterdi. Tek istemci kendi ön yüzümüz, geriye
    /// dönük uyumluluk kaygısı yok.
    ///
    /// KARŞILAŞTIRMA MİLİSANİYEYE YUVARLANARAK yapılıyor, tolerans
    /// aralığıyla değil. Önce 1 saniyelik tolerans vardı ve GERÇEK BİR
    /// DELİKTİ: art arda yapılan iki düzenleme aynı saniyeye düşünce
    /// bayat damga geçerli sayılıyordu — test bunu yakaladı. Yuvarlama,
    /// JSON gidiş-dönüşündeki mikrosaniye kaybını tolere ederken
    /// milisaniye farkını görüyor.
    /// </summary>
    /// <summary>
    /// Damga kontrolü TEK KAYNAK: faktoring gibi çekin durumunu
    /// değiştiren diğer servisler de buradan çağırıyor. Her servis
    /// kendi kontrolünü yazsaydı biri milisaniye, diğeri saniye
    /// karşılaştırır ve aynı istek bir uçta geçip diğerinde
    /// reddedilirdi.
    /// </summary>
    public static void EnsureRowVersionMatches(Cheque cheque, DateTime? rowVersion)
    {
        if (rowVersion is not DateTime expected)
        {
            throw new ArgumentException(
                "İstek geçersiz, sayfayı yenileyin. (Değişiklik damgası eksik.)");
        }

        var current = cheque.UpdatedAtUtc ?? cheque.CreatedAtUtc;

        static long ToMilliseconds(DateTime value) =>
            value.Ticks / TimeSpan.TicksPerMillisecond;

        if (ToMilliseconds(current) != ToMilliseconds(expected))
        {
            throw new InvalidOperationException(
                "Bu çek siz düzenlerken başka bir kullanıcı tarafından " +
                "güncellendi. Sayfayı yenileyip tekrar deneyin.");
        }
    }

    /// <summary>İptal nedeninin insan diliyle adı — açıklama boşsa yerine geçer.</summary>
    public static string VoidReasonName(ChequeVoidReason kind) => kind switch
    {
        ChequeVoidReason.DataEntryError => "Yanlış giriş",
        ChequeVoidReason.Bounced => "Karşılıksız",
        ChequeVoidReason.ReturnedToParty => "Müşteriye iade",
        ChequeVoidReason.Other => "Diğer",
        _ => kind.ToString()
    };

    public static string DirectionName(ChequeDirection direction) =>
        direction == ChequeDirection.Received ? "Alınan çek" : "Verilen çek";

    public async Task<IReadOnlyCollection<ChequeListItemResponse>> GetAllAsync(
        Guid? companyId,
        int? direction,
        int? status,
        Guid? currentAccountId,
        Guid? projectId,
        string? costCenterCode,
        string? search,
        bool includeVoided,
        CancellationToken cancellationToken)
    {
        var query = db.Cheques.AsNoTracking().AsQueryable();

        /*
         * İPTAL EDİLEN ÇEKLER VARSAYILAN OLARAK GİZLİ.
         *
         * Denetim izi için defterde duruyorlar ama günlük listede
         * gürültü yapıyorlardı: kullanıcı iptal ettiği çeki her açılışta
         * yeniden görüyor ve "silinmemiş mi" diye tereddüt ediyordu.
         * Açıkça istenirse geliyorlar ve ekranda üstü çizili/soluk
         * gösteriliyorlar — gizlemek yok saymak değil.
         *
         * DURUM SÜZGECİ AÇIKÇA İPTAL SEÇİLDİYSE bu kural devreye
         * girmiyor: kullanıcı zaten iptalleri istemiştir.
         */
        var voidedRequested = status == (int)ChequeStatus.Voided;

        if (!includeVoided && !voidedRequested)
            query = query.Where(x => x.Status != ChequeStatus.Voided);

        if (companyId is not null)
            query = query.Where(x => x.CompanyId == companyId.Value);
        if (direction is not null)
            query = query.Where(x => (int)x.Direction == direction.Value);
        if (status is not null)
            query = query.Where(x => (int)x.Status == status.Value);
        if (currentAccountId is not null)
            query = query.Where(x => x.CurrentAccountId == currentAccountId.Value);
        if (projectId is not null)
            query = query.Where(x => x.ProjectId == projectId.Value);

        if (!string.IsNullOrWhiteSpace(costCenterCode))
        {
            var center = costCenterCode.Trim();
            query = query.Where(x => x.CostCenterCode == center);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.ChequeNumber.ToLower().Contains(term) ||
                x.InternalNumber.ToLower().Contains(term) ||
                x.BankName.ToLower().Contains(term) ||
                (x.Drawer != null && x.Drawer.ToLower().Contains(term)));
        }

        var rows = await query
            .OrderBy(x => x.DueDate)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                x.Direction,
                x.Status,
                x.InternalNumber,
                x.ChequeNumber,
                x.BankName,
                x.Drawer,
                x.CurrentAccountId,
                CurrentAccountTitle = x.CurrentAccount != null ? x.CurrentAccount.Title : null,
                x.ProjectId,
                ProjectCode = x.Project != null ? x.Project.Code : null,
                x.CostCenterCode,
                x.Amount,
                x.CurrencyCode,
                x.ExchangeRate,
                x.AmountTry,
                x.IssueDate,
                x.DueDate
            })
            .ToListAsync(cancellationToken);

        var today = DateTime.UtcNow.Date;

        return rows.Select(x =>
        {
            var daysToDue = (int)(x.DueDate.Date - today).TotalDays;
            var isOpen = x.Status is ChequeStatus.Portfolio or ChequeStatus.AtBank
                or ChequeStatus.AtFactoring or ChequeStatus.Issued;

            return new ChequeListItemResponse(
                x.Id,
                x.CompanyId,
                (int)x.Direction,
                DirectionName(x.Direction),
                (int)x.Status,
                StatusName(x.Status),
                x.InternalNumber,
                x.ChequeNumber,
                x.BankName,
                x.Drawer,
                x.CurrentAccountId,
                x.CurrentAccountTitle,
                x.ProjectId,
                x.ProjectCode,
                x.CostCenterCode,
                x.Amount,
                x.CurrencyCode,
                x.ExchangeRate,
                x.AmountTry,
                x.IssueDate,
                x.DueDate,
                daysToDue,
                isOpen && daysToDue < 0);
        }).ToList();
    }

    public async Task<ChequeDetailResponse> GetByIdAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var cheque = await db.Cheques
            .AsNoTracking()
            .Include(x => x.CurrentAccount)
            .Include(x => x.Project)
            .Include(x => x.ProgressPayment)
            .Include(x => x.SupplierInvoice)
            .Include(x => x.CashAccount)
            .Include(x => x.ReplacedByCheque)
            .Include(x => x.ReplacesCheque)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (cheque is null)
            throw new KeyNotFoundException("Çek bulunamadı.");

        var movements = await db.ChequeMovements
            .AsNoTracking()
            .Where(x => x.ChequeId == id)
            .OrderBy(x => x.MovementDate).ThenBy(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.MovementDate,
                x.FromStatus,
                x.ToStatus,
                x.Description,
                x.CashAccountId,
                CashAccountName = x.CashAccount != null ? x.CashAccount.Name : null,
                x.AccountingVoucherId,
                AccountingVoucherNumber = x.AccountingVoucher != null
                    ? x.AccountingVoucher.VoucherNumber
                    : null
            })
            .ToListAsync(cancellationToken);

        var allocations = await db.ChequeAllocations
            .AsNoTracking()
            .Where(x => x.ChequeId == id)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new ChequeAllocationResponse(
                x.Id,
                x.Amount,
                x.ProjectId,
                x.Project != null ? x.Project.Code : null,
                x.Project != null ? x.Project.Name : null,
                x.CostCenterCode,
                x.SupplierInvoiceId,
                x.SupplierInvoice != null ? x.SupplierInvoice.InvoiceNumber : null,
                x.SalesInvoiceId,
                x.SalesInvoice != null ? x.SalesInvoice.InternalNumber : null,
                x.Description))
            .ToListAsync(cancellationToken);

        /*
         * DÜZENLENEBİLİRLİK VE GEÇMİŞ DETAYLA BİRLİKTE GELİYOR.
         *
         * Ekran ayrı bir uç çağırmıyor: düğmenin durumu ile kaydın
         * kendisi tek yanıtta gelmezse ikisi arasında yarış doğar —
         * kullanıcı açık düğmeyi tıklar, API reddeder.
         */
        var editability = await GetEditabilityAsync(cheque, cancellationToken);

        var changeLog = await db.ChequeChangeLogs
            .AsNoTracking()
            .Where(x => x.ChequeId == cheque.Id)
            .OrderByDescending(x => x.ChangedAtUtc)
            .Select(x => new ChequeChangeLogResponse(
                x.Id,
                x.FieldName,
                x.FieldLabel,
                x.OldValue,
                x.NewValue,
                x.AffectsAccounting,
                x.ChangedAtUtc,
                x.ChangedByUserId,
                db.Users.Where(u => u.Id == x.ChangedByUserId)
                    .Select(u => u.FullName).FirstOrDefault(),
                x.Reason))
            .ToListAsync(cancellationToken);

        var restores = await DescribeVoidRestoreAsync(cheque, cancellationToken);

        return new ChequeDetailResponse(
            cheque.Id,
            cheque.CompanyId,
            (int)cheque.Direction,
            DirectionName(cheque.Direction),
            (int)cheque.Status,
            StatusName(cheque.Status),
            cheque.InternalNumber,
            cheque.ChequeNumber,
            cheque.BankName,
            cheque.BankBranch,
            cheque.Drawer,
            cheque.CurrentAccountId,
            cheque.CurrentAccount?.Title,
            cheque.ProjectId,
            cheque.Project?.Code,
            cheque.Project?.Name,
            cheque.CostCenterCode,
            cheque.Amount,
            cheque.CurrencyCode,
            cheque.ExchangeRate,
            cheque.AmountTry,
            cheque.IssueDate,
            cheque.DueDate,
            cheque.ProgressPaymentId,
            cheque.ProgressPayment?.ProgressPaymentNumber,
            cheque.SupplierInvoiceId,
            cheque.SupplierInvoice?.InvoiceNumber,
            cheque.CashAccountId,
            cheque.CashAccount?.Name,
            cheque.Description,
            NextStatuses(cheque.Status).Select(x => (int)x).ToList(),
            movements.Select(x => new ChequeMovementResponse(
                x.Id,
                x.MovementDate,
                x.FromStatus is null ? null : (int)x.FromStatus.Value,
                x.FromStatus is null ? null : StatusName(x.FromStatus.Value),
                (int)x.ToStatus,
                StatusName(x.ToStatus),
                x.Description,
                x.CashAccountId,
                x.CashAccountName,
                x.AccountingVoucherId,
                x.AccountingVoucherNumber)).ToList(),
            allocations,
            cheque.ReplacedByChequeId,
            cheque.ReplacedByCheque?.ChequeNumber,
            cheque.ReplacesChequeId,
            cheque.ReplacesCheque?.ChequeNumber,
            await CountRenewalsAsync(cheque.Id, cancellationToken),

            // Damga: ekran bunu alıp düzenleme/iptal isteğinde geri
            // yolluyor. Kayıt hiç güncellenmediyse doğuş zamanı.
            cheque.UpdatedAtUtc ?? cheque.CreatedAtUtc,

            editability.CanEdit,
            editability.Reason,
            cheque.VoidedFromClosedState,
            cheque.VoidReasonKind is ChequeVoidReason vk ? (int)vk : null,
            cheque.VoidReasonKind is ChequeVoidReason vk2 ? VoidReasonName(vk2) : null,
            changeLog,
            restores?.Number,
            restores?.StatusName);
    }

    /// <summary>
    /// Bu çek iptal edilirse hangi orijinal çek, hangi duruma dönecek.
    /// Ekran iptalden ÖNCE uyarabilsin diye hesaplanıyor; kural
    /// <see cref="RestoreReplacedOriginalAsync"/> ile AYNI olmak
    /// zorunda — ayrışırsa ekran bir şey vaat eder, sunucu başkasını
    /// yapar.
    /// </summary>
    private async Task<(string Number, string StatusName)?> DescribeVoidRestoreAsync(
        Cheque cheque, CancellationToken cancellationToken)
    {
        if (cheque.Status == ChequeStatus.Voided) return null;
        if (cheque.ReplacesChequeId is not Guid originalId) return null;

        var original = await db.Cheques
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == originalId, cancellationToken);

        if (original is null ||
            original.Status != ChequeStatus.Replaced ||
            original.ReplacedByChequeId != cheque.Id)
        {
            return null;
        }

        var previous = await db.ChequeMovements
            .AsNoTracking()
            .Where(x => x.ChequeId == original.Id &&
                        x.ToStatus == ChequeStatus.Replaced &&
                        x.ReversedAtUtc == null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => x.FromStatus)
            .FirstOrDefaultAsync(cancellationToken);

        return previous is ChequeStatus status
            ? (original.ChequeNumber, StatusName(status))
            : null;
    }

    public async Task<ChequeSummaryResponse> GetSummaryAsync(
        Guid? companyId, CancellationToken cancellationToken)
    {
        var query = db.Cheques.AsNoTracking().AsQueryable();

        if (companyId is not null)
            query = query.Where(x => x.CompanyId == companyId.Value);

        // İPTAL EDİLEN ÇEK TOPLAMLARA GİRMEZ. Durum kırılımı zaten
        // yalnız belirli durumları topluyor ve İptal onların dışında —
        // ama filtre AÇIKÇA yazılıyor ki ileride buraya "toplam çek"
        // gibi bir alan eklendiğinde iptalliler sessizce geri
        // sızmasın.
        query = query.Where(x => x.Status != ChequeStatus.Voided);

        var groups = await query
            .GroupBy(x => x.Status)
            .Select(g => new
            {
                Status = g.Key,
                Total = g.Sum(x => x.Amount),
                Count = g.Count()
            })
            .ToListAsync(cancellationToken);

        decimal Total(ChequeStatus status) =>
            groups.Where(x => x.Status == status).Sum(x => x.Total);
        int Count(ChequeStatus status) =>
            groups.Where(x => x.Status == status).Sum(x => x.Count);

        return new ChequeSummaryResponse(
            Total(ChequeStatus.Portfolio),
            Total(ChequeStatus.AtBank),
            Total(ChequeStatus.AtFactoring),
            Total(ChequeStatus.Collected),
            Total(ChequeStatus.Bounced),
            Total(ChequeStatus.Issued),
            Total(ChequeStatus.Paid),
            Count(ChequeStatus.Portfolio) + Count(ChequeStatus.AtBank)
                + Count(ChequeStatus.AtFactoring),
            Count(ChequeStatus.Issued));
    }

    public async Task<ChequeDetailResponse> CreateAsync(
        CreateChequeRequest request, CancellationToken cancellationToken)
    {
        var direction = (ChequeDirection)request.Direction;
        if (direction is not (ChequeDirection.Received or ChequeDirection.Issued))
            throw new ArgumentException("Geçersiz çek yönü.");

        if (request.Amount <= 0m)
            throw new ArgumentException("Çek tutarı sıfırdan büyük olmalıdır.");

        if (string.IsNullOrWhiteSpace(request.ChequeNumber))
            throw new ArgumentException("Çek numarası zorunludur.");

        if (string.IsNullOrWhiteSpace(request.BankName))
            throw new ArgumentException("Banka adı zorunludur.");

        if (request.CurrentAccountId is null)
        {
            throw new ArgumentException(direction == ChequeDirection.Received
                ? "Çeki veren cari seçilmelidir."
                : "Çekin verildiği cari seçilmelidir.");
        }

        if (!await db.Companies.AnyAsync(x => x.Id == request.CompanyId, cancellationToken))
            throw new ArgumentException("Şirket bulunamadı.");

        if (!await db.CurrentAccounts.AnyAsync(
                x => x.Id == request.CurrentAccountId.Value, cancellationToken))
        {
            throw new ArgumentException("Cari bulunamadı.");
        }

        if (request.ProjectId is not null && !await db.Projects.AnyAsync(
                x => x.Id == request.ProjectId.Value, cancellationToken))
        {
            throw new ArgumentException("Proje bulunamadı.");
        }

        RequireAttribution(request.ProjectId, request.CostCenterCode,
            request.Allocations is { Count: > 0 });

        /*
         * MÜKERRER ENGELİ — İPTAL EDİLENLER HARİÇ.
         *
         * Eski kontrol `(şirket, yön, çek no)` bakıyordu ve DURUM
         * SÜZGECİ YOKTU: yanlış girilip iptal edilen bir çek numarayı
         * kalıcı olarak bloke ediyordu, aynı numara bir daha
         * girilemiyordu. Bildirilen hata buydu.
         *
         * Anahtar da genişledi: aynı çek numarası FARKLI bankada,
         * farklı şubede ya da farklı keşidecide gerçekten farklı bir
         * çektir. Dar anahtar, meşru kayıtları da reddediyordu.
         *
         * BU KONTROL YALNIZ KULLANICI DOSTU MESAJ İÇİN. Asıl savunma
         * veritabanındaki kısmi tekil indeks (bkz. migration); iki
         * eşzamanlı istek bu sorguyu da geçebilir, indeks geçemez.
         */
        await EnsureChequeNumberAvailableAsync(
            request.CompanyId, direction, request.BankName, request.BankBranch,
            Cheque.NormalizeChequeNumber(request.ChequeNumber),
            excludeChequeId: null, cancellationToken);

        var internalNumber = await documentNumberService.GenerateAsync(
            request.CompanyId,
            direction == ChequeDirection.Received ? "CHEQUE_RECEIVED" : "CHEQUE_ISSUED",
            direction == ChequeDirection.Received ? "ACK" : "VCK",
            cancellationToken);

        var currencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode)
            ? "TRY"
            : request.CurrencyCode.Trim().ToUpperInvariant();

        // Kur, faturalarla AYNI çözümleyiciden geliyor: belge/elle kur
        // varsa o, yoksa TCMB arşivi. Bulunamazsa çek kaydedilmez —
        // dövizli bir çeki kursuz deftere yazmak, 10.000 doları 10.000
        // TL göstermek demekti ve tam olarak bu oluyordu.
        var rate = await exchangeRateResolver.ResolveAsync(
            currencyCode,
            AsUtc(request.IssueDate),
            request.ExchangeRate,
            cancellationToken);

        if (!rate.Success)
            throw new InvalidOperationException(rate.Error ?? "Çek kuru belirlenemedi.");

        var amount = decimal.Round(request.Amount, 2);

        var cheque = new Cheque
        {
            CompanyId = request.CompanyId,
            Direction = direction,
            Status = direction == ChequeDirection.Received
                ? ChequeStatus.Portfolio
                : ChequeStatus.Issued,
            InternalNumber = internalNumber,
            ChequeNumber = request.ChequeNumber.Trim(),
            NormalizedChequeNumber = Cheque.NormalizeChequeNumber(request.ChequeNumber),
            BankName = request.BankName.Trim(),
            BankBranch = Normalize(request.BankBranch),
            Drawer = Normalize(request.Drawer),
            CurrentAccountId = request.CurrentAccountId,
            ProjectId = request.ProjectId,
            CostCenterCode = Normalize(request.CostCenterCode),
            Amount = amount,
            CurrencyCode = currencyCode,
            ExchangeRate = rate.Rate,
            AmountTry = decimal.Round(amount * rate.Rate, 2),
            IssueDate = AsUtc(request.IssueDate),
            DueDate = AsUtc(request.DueDate),
            ProgressPaymentId = request.ProgressPaymentId,
            SupplierInvoiceId = request.SupplierInvoiceId,
            Description = Normalize(request.Description)
        };

        var ownsTransaction = db.Database.CurrentTransaction is null;
        var dbTransaction = ownsTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            db.Cheques.Add(cheque);

            /*
             * YARIŞ KORUMASI — ASIL SAVUNMA VERİTABANI (stok modülündeki
             * desenin aynısı).
             *
             * Yukarıdaki ön kontrol iki eşzamanlı isteğin İKİSİNE DE
             * "yok" diyebilir; kısmi tekil indeks diyemez. Kaybeden
             * istek burada yakalanıp anlaşılır Türkçe mesaja çevriliyor
             * — kullanıcıya ham 500 dönmüyor.
             */
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
                when (IsChequeUniquenessViolation(ex))
            {
                throw await DescribeChequeClashAsync(
                    cheque.CompanyId, cheque.Direction, cheque.BankName,
                    cheque.BankBranch, cheque.NormalizedChequeNumber, cancellationToken);
            }

            // Dağılım fişten ÖNCE yazılır: fiş cari tarafını bu satırlara
            // göre böler, sonradan eklenirse fiş dağılımı görmezdi.
            var allocations = await BuildAllocationsAsync(
                cheque, request.Allocations, cancellationToken);

            if (allocations.Count > 0)
            {
                db.ChequeAllocations.AddRange(allocations);
                await db.SaveChangesAsync(cancellationToken);
            }

            var voucherId = await accountingIntegration.CreateChequeVoucherAsync(
                cheque, null, cheque.Status, cheque.IssueDate, null, cancellationToken);

            db.ChequeMovements.Add(new ChequeMovement
            {
                ChequeId = cheque.Id,
                MovementDate = cheque.IssueDate,
                FromStatus = null,
                ToStatus = cheque.Status,
                Description = cheque.Direction == ChequeDirection.Received
                    ? "Çek alındı, portföye girdi"
                    : "Çek düzenlendi ve tedarikçiye verildi",
                AccountingVoucherId = voucherId
            });

            await db.SaveChangesAsync(cancellationToken);

            if (dbTransaction is not null)
                await dbTransaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (dbTransaction is not null)
                await dbTransaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            dbTransaction?.Dispose();
        }

        return await GetByIdAsync(cheque.Id, cancellationToken);
    }

    public async Task<ChequeDetailResponse> UpdateAsync(
        Guid id, UpdateChequeRequest request, Guid? userId,
        CancellationToken cancellationToken)
    {
        var cheque = await db.Cheques.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (cheque is null)
            throw new KeyNotFoundException("Çek bulunamadı.");

        /*
         * DÜZENLENEBİLİRLİK TEK YERDEN SORULUYOR (F-çek/2).
         *
         * Eskiden burada yalnız duruma bakan bir kontrol vardı ve
         * mesajı genel bir cümleydi. Artık `GetEditability` karar
         * veriyor — aynı metot detay yanıtındaki "düzenle düğmesi açık
         * mı" bilgisini de üretiyor, yani UI ile API ayrışamıyor.
         * Reddedilirse kullanıcı SOMUT sebebi görüyor: hangi işlem,
         * hangi tarih.
         */
        var editability = await GetEditabilityAsync(cheque, cancellationToken);

        if (!editability.CanEdit)
            throw new InvalidOperationException(editability.Reason!);

        /*
         * EŞZAMANLI DEĞİŞİKLİK KORUMASI.
         *
         * Ekran çeki açtığında satırın son güncelleme damgasını alıyor;
         * kaydederken damga değişmişse başka biri araya girmiş demektir.
         * Sessizce üzerine yazmak, o kullanıcının değişikliğini iz
         * bırakmadan siler — çek gibi mali bir kayıtta kabul edilemez.
         *
         * DAMGA ZORUNLU: yollanmayan istek reddediliyor. Opsiyonel
         * bırakılsaydı koruma fiilen olmazdı — atlatmak için alanı
         * hiç göndermemek yeterdi. Tek istemci kendi ön yüzümüz.
         */
        EnsureRowVersionMatches(cheque, request.RowVersion);

        /*
         * DENETİM KAYDI — DEĞİŞİKLİKLER ESKİ HÂLLERİ SİLİNMEDEN ÖNCE
         * TOPLANIYOR. Aşağıdaki atamalardan sonra eski değerler
         * kaybolur; buradan sonra "ne değişti" sorusu cevapsız kalırdı.
         */
        var changes = new List<ChequeChangeLog>();

        void Track(string field, string label, string? before, string? after,
            bool affectsAccounting = false)
        {
            if (string.Equals(before, after, StringComparison.Ordinal))
                return;

            changes.Add(new ChequeChangeLog
            {
                ChequeId = cheque.Id,
                FieldName = field,
                FieldLabel = label,
                OldValue = before,
                NewValue = after,
                AffectsAccounting = affectsAccounting,
                Reason = Normalize(request.EditReason)
            });
        }

        Track("ChequeNumber", "Çek numarası",
            cheque.ChequeNumber, request.ChequeNumber.Trim());
        Track("BankName", "Banka", cheque.BankName,
            string.IsNullOrWhiteSpace(request.BankName) ? cheque.BankName : request.BankName.Trim());
        Track("BankBranch", "Şube", cheque.BankBranch, Normalize(request.BankBranch));
        Track("Drawer", "Keşideci", cheque.Drawer, Normalize(request.Drawer));
        Track("Description", "Açıklama", cheque.Description, Normalize(request.Description));
        // MASRAF MERKEZİ DE MUHASEBEYİ ETKİLER (kullanıcı kararı):
        // fişin masraf merkezi kırılımı bu iki alandan çözülüyor.
        Track("ProjectId", "Proje",
            cheque.ProjectId?.ToString(), request.ProjectId?.ToString(),
            affectsAccounting: true);
        Track("CostCenterCode", "Masraf merkezi",
            cheque.CostCenterCode, Normalize(request.CostCenterCode),
            affectsAccounting: true);

        // MUHASEBEYİ ETKİLEYEN ALANLAR ayrıca işaretleniyor: bunlar
        // değişince bağlı fiş ters kayıtla kapanıp yenisi kesiliyor.
        // Rapor bu bayrakla süzülüyor.
        //
        // VADE İSTİSNA: işaretli ama fişi yenilemiyor — giriş fişi vade
        // taşımıyor (ölçüldü), vade takibi çekin kendi alanından
        // besleniyor. Yine de mali sonucu olan bir düzeltme olduğu için
        // denetim süzgecinde görünüyor.
        Track("Amount", "Tutar",
            cheque.Amount.ToString("0.00"), decimal.Round(request.Amount, 2).ToString("0.00"),
            affectsAccounting: true);
        Track("DueDate", "Vade",
            cheque.DueDate.ToString("yyyy-MM-dd"), AsUtc(request.DueDate).ToString("yyyy-MM-dd"),
            affectsAccounting: true);
        Track("CurrentAccountId", "Cari",
            cheque.CurrentAccountId?.ToString(), request.CurrentAccountId?.ToString(),
            affectsAccounting: true);

        // TUTAR VE CARİ DÜZELTİLEBİLİR ama bedeli var: ikisi de giriş
        // fişine yazıldığı için fiş ters kayıtla kapatılıp yenisi
        // kesilir. Eskiden bu alanlar tamamen kapalıydı ve tek çare
        // çeki silmekti — banka hareketi ve fiş ortada kalıyordu.
        //
        // ÇEK İŞLEM GÖRDÜYSE (ödendi/tahsil edildi) buraya hiç
        // gelinmiyor: yukarıdaki kapı yalnız açık durumlara izin
        // veriyor, yani önce durumu geri almak gerekiyor.
        var amountChanged =
            decimal.Round(request.Amount, 2) != decimal.Round(cheque.Amount, 2);

        var accountChanged = request.CurrentAccountId != cheque.CurrentAccountId;

        /*
         * PARA BİRİMİ DE DÜZENLENEBİLİR (F-çek/2).
         *
         * Eskiden hiç düzenlenemiyordu: yanlış para biriminde girilen
         * çekin tek çaresi iptal + yeniden girişti. Değişince KUR
         * YENİDEN ÇÖZÜLÜYOR (belge/elle → TCMB arşivi) ve defter değeri
         * yeniden hesaplanıyor; eski kurla bırakmak 10.000 doları
         * 10.000 TL göstermek olurdu.
         */
        var requestedCurrency = string.IsNullOrWhiteSpace(request.CurrencyCode)
            ? cheque.CurrencyCode
            : request.CurrencyCode.Trim().ToUpperInvariant();

        var currencyChanged = !string.Equals(
            requestedCurrency, cheque.CurrencyCode, StringComparison.OrdinalIgnoreCase);

        /*
         * MASRAF MERKEZİ DEĞİŞİMİ DE FİŞİ YENİLETİR (kullanıcı kararı,
         * 2026-08-21).
         *
         * Fiş satırlarındaki masraf merkezi kodu çekin `ProjectId` ve
         * `CostCenterCode` alanlarından çözülüyor
         * (`ResolveChequeCostCenterAsync`). Yenilenmeseydi çek yeni
         * merkezi gösterirken defter eskisinde kalırdı: masraf merkezi
         * bazlı rapor ile çek listesi birbirini tutmaz, üstelik fark
         * hiçbir yerde görünmezdi. Bu paketin tamamı tam olarak bu
         * sınıf ayrışmayı kapatmak için yazıldı.
         */
        var costCenterChanged =
            request.ProjectId != cheque.ProjectId ||
            !string.Equals(
                Normalize(request.CostCenterCode), cheque.CostCenterCode,
                StringComparison.Ordinal);

        if (amountChanged && request.Amount <= 0m)
            throw new ArgumentException("Çek tutarı sıfırdan büyük olmalıdır.");

        if (accountChanged && request.CurrentAccountId is null)
            throw new ArgumentException("Çekin carisi boş bırakılamaz.");

        if ((amountChanged || accountChanged) &&
            request.CurrentAccountId is Guid targetAccount &&
            !await db.CurrentAccounts.AnyAsync(
                x => x.Id == targetAccount && x.CompanyId == cheque.CompanyId,
                cancellationToken))
        {
            throw new ArgumentException("Cari bulunamadı.");
        }

        if (string.IsNullOrWhiteSpace(request.ChequeNumber))
            throw new ArgumentException("Çek numarası zorunludur.");

        /*
         * DÜZENLEMEDE DE MÜKERRER KONTROLÜ (F-çek/1).
         *
         * Eskiden yoktu: çek numarası düzenleme yoluyla başka bir aktif
         * çekin numarasına çevrilebiliyordu ve engel yalnız YENİ kayıtta
         * çalışıyordu. Kaydın kendisi hariç tutuluyor, yoksa çek kendi
         * numarasıyla çakışır ve hiçbir düzenleme kaydedilemezdi.
         */
        await EnsureChequeNumberAvailableAsync(
            cheque.CompanyId, cheque.Direction,
            string.IsNullOrWhiteSpace(request.BankName) ? cheque.BankName : request.BankName,
            request.BankBranch,
            Cheque.NormalizeChequeNumber(request.ChequeNumber),
            excludeChequeId: cheque.Id, cancellationToken);

        cheque.ChequeNumber = request.ChequeNumber.Trim();
        cheque.NormalizedChequeNumber = Cheque.NormalizeChequeNumber(request.ChequeNumber);
        cheque.BankName = string.IsNullOrWhiteSpace(request.BankName)
            ? cheque.BankName
            : request.BankName.Trim();
        cheque.BankBranch = Normalize(request.BankBranch);
        cheque.Drawer = Normalize(request.Drawer);
        RequireAttribution(request.ProjectId, request.CostCenterCode,
            await db.ChequeAllocations.AnyAsync(
                x => x.ChequeId == cheque.Id, cancellationToken));

        cheque.ProjectId = request.ProjectId;
        cheque.CostCenterCode = Normalize(request.CostCenterCode);
        cheque.IssueDate = AsUtc(request.IssueDate);
        cheque.DueDate = AsUtc(request.DueDate);
        cheque.ProgressPaymentId = request.ProgressPaymentId;
        cheque.SupplierInvoiceId = request.SupplierInvoiceId;
        cheque.Description = Normalize(request.Description);

        /*
         * FİŞİ BOZAN ALANLAR: tutar, para birimi, cari, masraf merkezi.
         *
         * VADE FİŞİ BOZMUYOR — ölçüldü: çek giriş fişi vade taşımıyor.
         * Nakit projeksiyonu ve vade takibi `cheque.DueDate`'i canlı
         * okuduğu için vade değişimi oralarda kendiliğinden doğru
         * yansıyor. Vadeyi de fiş yenilemeye sokmak, hiçbir şeyi
         * düzeltmeyen ama defteri iki fişle şişiren bir işlem olurdu.
         */
        if (amountChanged || accountChanged || currencyChanged || costCenterChanged)
        {
            if (currencyChanged)
            {
                var newRate = await exchangeRateResolver.ResolveAsync(
                    requestedCurrency,
                    AsUtc(request.IssueDate),
                    request.ExchangeRate,
                    cancellationToken);

                if (!newRate.Success)
                    throw new InvalidOperationException(newRate.Error ?? "Çek kuru belirlenemedi.");

                cheque.CurrencyCode = requestedCurrency;
                cheque.ExchangeRate = newRate.Rate;
            }

            await ReissueEntryVoucherAsync(
                cheque, request, amountChanged, cancellationToken);
        }

        /*
         * DAMGA ELLE İLERLETİLİYOR — otomatik değil.
         *
         * `UpdatedAtUtc` bu kod tabanında `SaveChanges` tarafından
         * KENDİLİĞİNDEN yazılmıyor (ölçüldü: AppDbContext'te böyle bir
         * kanca yok). Yazılmasaydı damga hiç değişmez, eşzamanlılık
         * koruması sessizce hiçbir şey yapmazdı — test bunu yakaladı:
         * bayat damgayla ikinci istek sorunsuz geçiyordu.
         */
        cheque.UpdatedAtUtc = DateTime.UtcNow;
        cheque.UpdatedByUserId = userId;

        // DEĞİŞİKLİK YOKSA KAYIT DA YOK: her açıp kapatmada satır
        // yazılsaydı geçmiş, gerçek düzeltmelerin kaybolduğu bir
        // gürültüye dönerdi.
        if (changes.Count > 0)
        {
            foreach (var change in changes)
                change.ChangedByUserId = userId;

            db.ChequeChangeLogs.AddRange(changes);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsChequeUniquenessViolation(ex))
        {
            // Düzenleme sırasında da yarış olabilir: başka bir istek
            // aynı anda aynı numarayı almış olabilir.
            throw await DescribeChequeClashAsync(
                cheque.CompanyId, cheque.Direction, cheque.BankName,
                cheque.BankBranch, cheque.NormalizedChequeNumber, cancellationToken);
        }

        return await GetByIdAsync(cheque.Id, cancellationToken);
    }



    /// <summary>
    /// Tutar ya da cari düzeltildiğinde giriş fişini yeniler: eski fiş
    /// ters kayıtla kapanır, yeni tutarla yenisi kesilir.
    ///
    /// Fişi yerinde düzeltmek yerine ters kayıt üretiliyor çünkü
    /// kesinleşmiş bir fişin satırlarını değiştirmek defteri geriye
    /// dönük oynatır; kod tabanındaki fatura iptali de aynı deseni
    /// kullanıyor.
    /// </summary>
    private async Task ReissueEntryVoucherAsync(
        Cheque cheque,
        UpdateChequeRequest request,
        bool amountChanged,
        CancellationToken cancellationToken)
    {
        var entry = await db.ChequeMovements
            .Where(x => x.ChequeId == cheque.Id &&
                        x.FromStatus == null &&
                        x.ReversedAtUtc == null)
            .OrderBy(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (amountChanged)
        {
            cheque.Amount = decimal.Round(request.Amount, 2);
            cheque.AmountTry = decimal.Round(cheque.Amount * cheque.ExchangeRate, 2);
        }

        if (request.CurrentAccountId is Guid account)
            cheque.CurrentAccountId = account;

        /*
         * BAĞLI FİŞ YOKSA SESSİZCE GEÇMEZ.
         *
         * Eskiden burada `return` vardı: giriş fişi bulunamazsa
         * düzenleme sorunsuz görünüyor, çek yeni tutarla kaydediliyor
         * ama muhasebe eski tutarda kalıyordu. Çek toplamı ile mizan
         * sessizce ayrışırdı — bu programın en pahalı hata sınıfı.
         *
         * Fiş gerçekten yoksa bu bir veri tutarsızlığıdır ve
         * kullanıcının haberi olmalı.
         */
        if (entry is null || entry.AccountingVoucherId is not Guid voucherId)
        {
            throw new InvalidOperationException(
                $"{cheque.InternalNumber} numaralı çekin giriş muhasebe fişi bulunamadı; " +
                "tutar/para birimi/cari/masraf merkezi düzeltmesi yapılamaz. Kaydı " +
                "muhasebeyle birlikte inceleyin.");
        }

        /*
         * AÇIKLAMALAR ORİJİNAL FİŞİ REFERANS VERİYOR.
         *
         * Düzeltme mizanda ÜÇ fiş bırakıyor: orijinal, ters kayıt, yeni
         * fiş. Açıklamalar yalnız "çek düzeltmesi" deseydi altı ay sonra
         * bakan kişi hangisinin neyi kapattığını ayıramazdı. Numara
         * yazılınca zincir tek bakışta okunuyor.
         */
        var originalNumber = await db.AccountingVouchers
            .AsNoTracking()
            .Where(x => x.Id == voucherId)
            .Select(x => x.VoucherNumber)
            .SingleOrDefaultAsync(cancellationToken) ?? "?";

        entry.ReversalVoucherId = await accountingIntegration.CreateReversalVoucherAsync(
            voucherId,
            $"Çek düzeltmesi — {originalNumber} no'lu fişin iptali ({cheque.InternalNumber})",
            DateTime.UtcNow.Date,
            cancellationToken);

        entry.ReversedAtUtc = DateTime.UtcNow;
        entry.ReversalReason = "Tutar/para birimi/cari/masraf merkezi düzeltmesi";

        var replacementVoucherId = await accountingIntegration.CreateChequeVoucherAsync(
            cheque, null, cheque.Status, cheque.IssueDate, null, cancellationToken);

        db.ChequeMovements.Add(new ChequeMovement
        {
            ChequeId = cheque.Id,
            MovementDate = DateTime.UtcNow.Date,
            FromStatus = null,
            ToStatus = cheque.Status,
            Description = $"Çek düzeltmesi — {originalNumber} yerine yeniden giriş fişi",
            AccountingVoucherId = replacementVoucherId
        });
    }

    /// <summary>
    /// Son durum değişikliğini geri alır (ör. yanlış "Ödendi" →
    /// "Verildi").
    ///
    /// Durum matrisi bu geçişleri bilerek TEK YÖNLÜ tutuyor: "Ödendi"
    /// bir olaydır, geri sayılmaz. Ama yanlış işaretlenen ödeme
    /// gerçekten oluyor ve bugüne kadar tek çare çeki silmekti — hem
    /// banka hareketi hem fiş ortada kalıyordu.
    ///
    /// GERİ ALMA SİLMEZ, TERS KAYIT ÜRETİR:
    ///   - geçişin fişi ters kayıtla kapanır (ikisi de defterde kalır)
    ///   - banka hareketi ters yönlü bir hareketle dengelenir
    ///   - çek önceki durumuna döner
    ///   - hem geri alınan hareket damgalanır hem yeni bir hareket
    ///     satırı yazılır: kim, ne zaman, neden
    ///
    /// MÜKERRER ENGELİ: aynı hareket iki kez geri alınamaz ve ters
    /// banka hareketi kaynak işlemin kimliğiyle anahtarlanır — ikinci
    /// çağrı yeni satır üretmez.
    /// </summary>
    public async Task<ChequeDetailResponse> ReverseLastMovementAsync(
        Guid id, ChequeReversalRequest request, Guid? userId,
        bool hasClosedReversePermission,
        CancellationToken cancellationToken)
    {
        var reason = request.Reason?.Trim();

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Geri alma gerekçesi zorunludur.");

        var cheque = await db.Cheques.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Çek bulunamadı.");

        EnsureRowVersionMatches(cheque, request.RowVersion);

        if (cheque.Status == ChequeStatus.Voided)
            throw new InvalidOperationException("İptal edilmiş çekte geri alma yapılmaz.");

        /*
         * KAPANMIŞ DURUMDAN GERİ ALMA AYRI YETKİ — İPTALDEKİ AYRIMIN
         * AYNISI.
         *
         * Tahsil edilmiş ya da ödenmiş bir çeki geri almak, iptal etmek
         * kadar ağır: gerçekleşmiş bir para hareketini storno ediyor.
         * İptalde bu ayrım konmuş, geri almada unutulmuştu — yani aynı
         * mali etki daha düşük bir yetkiyle üretilebiliyordu. Kötü
         * niyet gerekmiyor, yanlış satıra tıklamak yetiyor.
         */
        var openStatus = cheque.Direction == ChequeDirection.Received
            ? ChequeStatus.Portfolio
            : ChequeStatus.Issued;

        if (cheque.Status != openStatus && !hasClosedReversePermission)
        {
            throw new UnauthorizedAccessException(
                $"Bu çek \"{StatusName(cheque.Status)}\" durumunda. Kapanmış bir " +
                "durumdan geri alma ayrı bir yetki gerektiriyor " +
                "(Çek — Kapanmış İptal).");
        }

        var movement = await db.ChequeMovements
            .Where(x => x.ChequeId == cheque.Id && x.ReversedAtUtc == null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "Geri alınacak bir durum değişikliği yok.");

        if (movement.FromStatus is not ChequeStatus previous)
        {
            throw new InvalidOperationException(
                "Çekin ilk kaydı geri alınamaz; çeki iptal edin.");
        }

        var ownsTransaction = db.Database.CurrentTransaction is null;
        var dbTransaction = ownsTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            await ReverseMovementAsync(cheque, movement, reason, userId, cancellationToken);

            cheque.Status = previous;
            cheque.UpdatedAtUtc = DateTime.UtcNow;
            cheque.UpdatedByUserId = userId;

            // GERİ ALMA DA DENETİM KAYDINA DÜŞER: mali etkisi olan bir
            // durum değişikliği gerekçesiyle birlikte kayda geçmeli.
            db.ChequeChangeLogs.Add(new ChequeChangeLog
            {
                ChequeId = cheque.Id,
                FieldName = "Status",
                FieldLabel = "Durum",
                OldValue = StatusName(movement.ToStatus),
                NewValue = StatusName(previous),
                AffectsAccounting = true,
                ChangedByUserId = userId,
                Reason = $"Durum geri alındı — {reason}"
            });

            db.ChequeMovements.Add(new ChequeMovement
            {
                ChequeId = cheque.Id,
                MovementDate = DateTime.UtcNow.Date,
                FromStatus = movement.ToStatus,
                ToStatus = previous,
                Description = $"Geri alma — {reason}",
                CashAccountId = movement.CashAccountId,
                CreatedByUserId = userId
            });

            await db.SaveChangesAsync(cancellationToken);

            if (dbTransaction is not null)
                await dbTransaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (dbTransaction is not null)
                await dbTransaction.RollbackAsync(cancellationToken);

            throw;
        }
        finally
        {
            if (dbTransaction is not null)
                await dbTransaction.DisposeAsync();
        }

        return await GetByIdAsync(cheque.Id, cancellationToken);
    }

    /// <summary>
    /// Çeki iptale çeker ve ürettiği BÜTÜN mali etkileri aynı işlemde
    /// geri alır.
    ///
    /// Hard delete yok: mali kayıt silinince geçmiş de gider ve
    /// "bu çek neden yoktu" sorusu cevapsız kalır. İptal edilen çek
    /// listede durur, durumu İptal'dir ve fişleri ters kayıtlarıyla
    /// birlikte defterdedir.
    ///
    /// ORPHAN BIRAKMAZ: her geri alınmamış hareketin fişi ters kayıtla
    /// kapanır ve her banka hareketi ters yönlü bir hareketle
    /// dengelenir. Bakiye çekin hiç girilmemiş halindeki değerine
    /// döner.
    /// </summary>
    public async Task<ChequeDetailResponse> VoidAsync(
        Guid id, ChequeReversalRequest request, Guid? userId,
        bool hasClosedVoidPermission,
        CancellationToken cancellationToken)
    {
        var reason = request.Reason?.Trim();

        var cheque = await db.Cheques.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Çek bulunamadı.");

        if (cheque.Status == ChequeStatus.Voided)
            throw new InvalidOperationException("Çek zaten iptal edilmiş.");

        // İPTALDE DE EŞZAMANLI DEĞİŞİKLİK KORUMASI: kullanıcı ekranı
        // açtıktan sonra çek ciro edilmiş olabilir; o hâlde iptal artık
        // "kapanmış çek iptali"dir ve farklı yetki ister.
        EnsureRowVersionMatches(cheque, request.RowVersion);

        /*
         * İPTALİN SINIRI — İKİ AYRI RİSK, İKİ AYRI YETKİ (F-çek/2).
         *
         * Portföydeki (ya da yeni verilmiş) çeki iptal etmek düşük
         * riskli: henüz para hareketi yok. Tahsil edilmiş, ödenmiş,
         * bankaya/faktoringe verilmiş, karşılıksız çıkmış ya da iade
         * alınmış çeki iptal etmek ise GERÇEKLEŞMİŞ bir hareketi storno
         * ile geri alır — üstelik artık numarayı da yeniden kullanıma
         * açar (kısmi tekil indeks iptalleri saymıyor). Kötü niyet
         * gerekmiyor, yanlış satıra tıklamak yetiyor.
         */
        var openStatus = cheque.Direction == ChequeDirection.Received
            ? ChequeStatus.Portfolio
            : ChequeStatus.Issued;

        var fromClosedState = cheque.Status != openStatus;

        if (fromClosedState && !hasClosedVoidPermission)
        {
            throw new UnauthorizedAccessException(
                $"Bu çek \"{StatusName(cheque.Status)}\" durumunda. Kapanmış çekin " +
                "iptali ayrı bir yetki gerektiriyor (Çek — Kapanmış İptal).");
        }

        /*
         * NEDEN SAYILABİLİR OLMAK ZORUNDA. Serbest metin bırakıldığında
         * "yanlış", "hata", "iptal" gibi on farklı yazım doğuyor ve
         * "kaç çek karşılıksız çıktı" sorusu hiç cevaplanamıyor.
         */
        if (request.ReasonKind is not int rawKind ||
            !Enum.IsDefined(typeof(ChequeVoidReason), rawKind))
        {
            throw new ArgumentException("İptal nedeni seçilmelidir.");
        }

        var kind = (ChequeVoidReason)rawKind;

        /*
         * "YANLIŞ GİRİŞ" KAPANMIŞ ÇEKTE SEÇİLEMEZ: o çek gerçekten
         * tahsil edilmiş/ödenmiştir. Yazım hatası varsa yol DÜZENLEME,
         * iptal değil. Seçenek zaten ekranda gösterilmiyor ama API de
         * kendi başına reddediyor — düğmeyi gizlemek yetmez.
         */
        if (fromClosedState && kind == ChequeVoidReason.DataEntryError)
        {
            throw new ArgumentException(
                "Kapanmış bir çek \"yanlış giriş\" nedeniyle iptal edilemez. " +
                "Yazım hatası için çeki düzenleyin.");
        }

        // "Diğer" seçildiyse açıklama zorunlu; yoksa neden yine
        // sayılabilir görünür ama içi boş kalır.
        if (kind == ChequeVoidReason.Other && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("\"Diğer\" seçildiğinde açıklama zorunludur.");

        if (string.IsNullOrWhiteSpace(reason))
            reason = VoidReasonName(kind);

        // Ertelenen çekin yerine yenisi açılmıştır; onu iptal etmek
        // zinciri kopuk bırakırdı.
        if (cheque.Status == ChequeStatus.Replaced)
        {
            throw new InvalidOperationException(
                "Ertelenmiş çek iptal edilemez; önce yerine geçen çeki iptal edin.");
        }

        var movements = await db.ChequeMovements
            .Where(x => x.ChequeId == cheque.Id && x.ReversedAtUtc == null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var ownsTransaction = db.Database.CurrentTransaction is null;
        var dbTransaction = ownsTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            foreach (var movement in movements)
            {
                await ReverseMovementAsync(
                    cheque, movement, $"Çek iptali — {reason}", userId, cancellationToken);
            }

            cheque.Status = ChequeStatus.Voided;
            cheque.VoidedAtUtc = DateTime.UtcNow;
            cheque.VoidedByUserId = userId;
            cheque.VoidReason = reason;
            cheque.VoidReasonKind = kind;
            cheque.VoidedFromClosedState = fromClosedState;

            // Damga iptalde de ilerliyor: aynı çeki iki kez iptal etmeye
            // çalışan ikinci istek bayat damgayla gelir ve reddedilir.
            cheque.UpdatedAtUtc = DateTime.UtcNow;
            cheque.UpdatedByUserId = userId;

            db.ChequeMovements.Add(new ChequeMovement
            {
                ChequeId = cheque.Id,
                MovementDate = DateTime.UtcNow.Date,
                FromStatus = movements.Count > 0 ? movements[0].ToStatus : null,
                ToStatus = ChequeStatus.Voided,
                Description = $"İptal — {reason}",
                CreatedByUserId = userId
            });

            // ERTELEME ZİNCİRİ: yerine geçen çek iptal ediliyorsa
            // orijinal çek açılır (bkz. RestoreReplacedOriginalAsync).
            await RestoreReplacedOriginalAsync(cheque, reason, userId, cancellationToken);

            await db.SaveChangesAsync(cancellationToken);

            if (dbTransaction is not null)
                await dbTransaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (dbTransaction is not null)
                await dbTransaction.RollbackAsync(cancellationToken);

            throw;
        }
        finally
        {
            if (dbTransaction is not null)
                await dbTransaction.DisposeAsync();
        }

        return await GetByIdAsync(cheque.Id, cancellationToken);
    }

    /// <summary>
    /// Tek bir hareketin mali etkisini geri alır: fişin ters kaydı ve
    /// banka hareketinin karşıt kaydı.
    ///
    /// Karşıt banka hareketi KAYNAK İŞLEMİN kimliğiyle anahtarlanıyor
    /// (SourceModule + SourceEntityId): aynı hareket iki kez geri
    /// alınmaya çalışılsa bile ikinci karşıt kayıt açılmıyor.
    /// </summary>
    /// <summary>
    /// ERTELENEN ÇEK, YERİNE GEÇEN İPTAL EDİLİNCE YENİDEN AÇILIR.
    ///
    /// NEDEN ŞART: erteleme, orijinal çeki "Ertelendi" durumuna alıp
    /// defterden ters kayıtla ÇIKARIYOR. Yerine geçen çek iptal
    /// edildiğinde ortada geçerli bir çek kalmıyor ama BORÇ DURUYOR.
    /// Orijinal "Ertelendi"de bırakılırsa alacak/borç portföyden,
    /// vade raporundan ve nakit projeksiyonundan birden düşer —
    /// üstelik defterde de yok, çünkü ertelemenin ters kaydı orada
    /// duruyor. Yani gerçek bir alacak sistemde tamamen görünmez olur
    /// ve kimse fark etmez. Çek numarası sorunundan daha tehlikeli.
    ///
    /// ÖNCEKİ DURUM TAHMİN EDİLMİYOR: erteleme hareketinin
    /// <c>FromStatus</c> alanı çekin ertelemeden önceki durumunu zaten
    /// taşıyor. Körlemesine "Portföyde" yazmak yanlış olurdu — çek
    /// bankada tahsilde ya da teminatta olabilirdi.
    ///
    /// ZİNCİRE DOKUNULMAZ: geri dönüş yalnızca orijinal HÂLÂ
    /// "Ertelendi" durumundaysa VE tam olarak bu iptal edilen çeki
    /// işaret ediyorsa yapılır. A→B→C zincirinde C iptal edilirse B
    /// açılır, A'ya dokunulmaz (A zaten B'yi işaret ediyor).
    ///
    /// SESSİZ DEĞİL: durum değişimi hareket kaydı ve denetim kaydı
    /// bırakır; defter de ertelemenin ters kaydı geri alınarak
    /// düzelir.
    /// </summary>
    private async Task RestoreReplacedOriginalAsync(
        Cheque voided, string reason, Guid? userId,
        CancellationToken cancellationToken)
    {
        if (voided.ReplacesChequeId is not Guid originalId)
            return;

        var original = await db.Cheques
            .SingleOrDefaultAsync(x => x.Id == originalId, cancellationToken);

        if (original is null)
            return;

        // ZİNCİR KORUMASI: orijinal başka bir çekle yeniden
        // ertelendiyse artık bu iptalin konusu değil.
        if (original.Status != ChequeStatus.Replaced ||
            original.ReplacedByChequeId != voided.Id)
        {
            return;
        }

        var replacementMovement = await db.ChequeMovements
            .Where(x => x.ChequeId == original.Id &&
                        x.ToStatus == ChequeStatus.Replaced &&
                        x.ReversedAtUtc == null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (replacementMovement?.FromStatus is not ChequeStatus previous)
        {
            // Hareket yoksa durum güvenilir şekilde türetilemiyor.
            // Sessizce "Portföyde" yazmak yerine kullanıcıya söylüyoruz:
            // bu bir veri tutarsızlığıdır ve elle incelenmeli.
            throw new InvalidOperationException(
                $"{voided.InternalNumber} numaralı çek {original.InternalNumber} " +
                "numaralı çekin ertelemesi ama ertelenme hareketi bulunamadı; " +
                "orijinal çekin hangi duruma döneceği belirlenemiyor. Kaydı " +
                "muhasebeyle birlikte inceleyin.");
        }

        // DEFTER DE GERİ GELİR: ertelemenin ters kaydı geri alınıyor,
        // yani orijinal çek yeniden defterde. Yalnız durumu
        // değiştirmek, çeki raporlarda gösterip mizanda göstermezdi.
        await ReverseMovementAsync(
            original, replacementMovement,
            $"Erteleme iptali — {voided.InternalNumber} iptal edildi ({reason})",
            userId, cancellationToken);

        original.Status = previous;
        original.ReplacedByChequeId = null;
        original.UpdatedAtUtc = DateTime.UtcNow;
        original.UpdatedByUserId = userId;

        db.ChequeMovements.Add(new ChequeMovement
        {
            ChequeId = original.Id,
            MovementDate = DateTime.UtcNow.Date,
            FromStatus = ChequeStatus.Replaced,
            ToStatus = previous,
            Description =
                $"Erteleme geri alındı — yerine geçen {voided.ChequeNumber} " +
                $"numaralı çek iptal edildi ({reason})",
            CreatedByUserId = userId
        });

        db.ChequeChangeLogs.Add(new ChequeChangeLog
        {
            ChequeId = original.Id,
            FieldName = "Status",
            FieldLabel = "Durum",
            OldValue = StatusName(ChequeStatus.Replaced),
            NewValue = StatusName(previous),
            AffectsAccounting = true,
            ChangedByUserId = userId,
            Reason =
                $"Yerine geçen {voided.ChequeNumber} numaralı çek iptal edildi"
        });
    }

    private async Task ReverseMovementAsync(
        Cheque cheque,
        ChequeMovement movement,
        string reason,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        if (movement.AccountingVoucherId is Guid voucherId)
        {
            movement.ReversalVoucherId =
                await accountingIntegration.CreateReversalVoucherAsync(
                    voucherId, reason, DateTime.UtcNow.Date, cancellationToken);
        }

        var transactions = await db.CashTransactions
            .Where(x => x.SourceModule == ChequeSourceModule &&
                        x.SourceEntityId == cheque.Id &&
                        x.AccountingVoucherId == movement.AccountingVoucherId)
            .ToListAsync(cancellationToken);

        foreach (var transaction in transactions)
        {
            var alreadyReversed = await db.CashTransactions.AnyAsync(
                x => x.SourceModule == ChequeReversalSourceModule &&
                     x.SourceEntityId == transaction.Id,
                cancellationToken);

            if (alreadyReversed)
                continue;

            db.CashTransactions.Add(new CashTransaction
            {
                CashAccountId = transaction.CashAccountId,
                TransactionDate = DateTime.UtcNow.Date,
                TransactionType = transaction.TransactionType,
                // Karşıt yön: bakiye çekin hiç işlem görmemiş haline
                // döner. Özgün hareket SİLİNMİYOR — banka defteri
                // geriye dönük değişmemeli.
                Direction = transaction.Direction == CashTransactionDirection.In
                    ? CashTransactionDirection.Out
                    : CashTransactionDirection.In,
                Amount = transaction.Amount,
                CurrencyCode = transaction.CurrencyCode,
                Description = $"Geri alma — {transaction.Description}",
                DocumentNumber = transaction.DocumentNumber,
                CurrentAccountId = transaction.CurrentAccountId,
                ProjectId = transaction.ProjectId,
                SourceModule = ChequeReversalSourceModule,
                SourceEntityId = transaction.Id,
                AccountingVoucherId = movement.ReversalVoucherId,
                CreatedByUserId = userId
            });
        }

        movement.ReversedAtUtc = DateTime.UtcNow;
        movement.ReversedByUserId = userId;
        movement.ReversalReason = reason;
    }

    public async Task<ChequeDetailResponse> ChangeStatusAsync(
        Guid id, ChequeStatusChangeRequest request, CancellationToken cancellationToken)
    {
        var cheque = await db.Cheques.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (cheque is null)
            throw new KeyNotFoundException("Çek bulunamadı.");

        // DURUM DEĞİŞTİREN HER UÇTA DAMGA. Bu uç ciro, bankaya verme,
        // tahsil, ödeme, karşılıksız ve iadenin HEPSİNİ taşıyor;
        // korumasız kalsaydı çekin en sık kullanılan yolu açıkta olurdu.
        EnsureRowVersionMatches(cheque, request.RowVersion);

        var toStatus = (ChequeStatus)request.ToStatus;
        var fromStatus = cheque.Status;

        if (!Enum.IsDefined(toStatus))
            throw new ArgumentException("Geçersiz çek durumu.");

        if (!NextStatuses(fromStatus).Contains(toStatus))
        {
            throw new InvalidOperationException(
                $"'{StatusName(fromStatus)}' durumundan '{StatusName(toStatus)}' " +
                "durumuna geçiş yapılamaz.");
        }

        // "Ertelendi" düz durum değişikliğiyle seçilemez: yerine geçen
        // çek açılmadan bu duruma geçilirse çek kapanır ama alacak/borç
        // ortadan kaybolur ve nakit akışında hiçbir yerde görünmez.
        if (toStatus == ChequeStatus.Replaced)
        {
            throw new InvalidOperationException(
                "Erteleme durum değişikliğiyle yapılamaz; " +
                "yerine geçecek yeni çeki de açan erteleme işlemini kullanın.");
        }

        CashAccount? cashAccount = null;
        if (request.CashAccountId is not null)
        {
            cashAccount = await db.CashAccounts
                .SingleOrDefaultAsync(
                    x => x.Id == request.CashAccountId.Value
                        && x.CompanyId == cheque.CompanyId,
                    cancellationToken);

            if (cashAccount is null)
                throw new ArgumentException("Kasa/banka hesabı bulunamadı.");
        }

        if (RequiresCashAccount(fromStatus, toStatus) && cashAccount is null)
            throw new ArgumentException("Bu geçiş için kasa/banka hesabı seçilmelidir.");

        var movementDate = AsUtc(request.MovementDate);

        var ownsTransaction = db.Database.CurrentTransaction is null;
        var dbTransaction = ownsTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var voucherId = await accountingIntegration.CreateChequeVoucherAsync(
                cheque, fromStatus, toStatus, movementDate, cashAccount, cancellationToken);

            var cashEffect = CashEffect(fromStatus, toStatus);
            if (cashEffect is not null && cashAccount is not null)
            {
                db.CashTransactions.Add(new CashTransaction
                {
                    CashAccountId = cashAccount.Id,
                    TransactionDate = movementDate,
                    TransactionType = cashEffect.Value.Type,
                    Direction = cashEffect.Value.Direction,
                    Amount = cheque.Amount,
                    CurrencyCode = cheque.CurrencyCode,
                    Description = $"{cheque.InternalNumber} — {StatusName(toStatus)} " +
                        $"(çek no {cheque.ChequeNumber})",
                    DocumentNumber = cheque.ChequeNumber,
                    CurrentAccountId = cheque.CurrentAccountId,
                    ProjectId = cheque.ProjectId,
                    SourceModule = ChequeSourceModule,
                    SourceEntityId = cheque.Id,
                    // Fiş çek modülünde üretildi; hareket aynı fişe bağlanır,
                    // ikinci bir fiş kesilmez.
                    AccountingVoucherId = voucherId
                });
            }

            cheque.Status = toStatus;

            // DAMGA HER DURUM DEĞİŞİKLİĞİNDE İLERLER. İlerlemeseydi
            // koruma fiilen çalışmazdı: aynı damgayla gelen ikinci
            // istek de geçerdi ve iki kullanıcı aynı çeki arka arkaya
            // işleyebilirdi.
            cheque.UpdatedAtUtc = DateTime.UtcNow;
            if (cashAccount is not null)
                cheque.CashAccountId = cashAccount.Id;

            db.ChequeMovements.Add(new ChequeMovement
            {
                ChequeId = cheque.Id,
                MovementDate = movementDate,
                FromStatus = fromStatus,
                ToStatus = toStatus,
                Description = string.IsNullOrWhiteSpace(request.Description)
                    ? $"{StatusName(fromStatus)} → {StatusName(toStatus)}"
                    : request.Description!.Trim(),
                CashAccountId = cashAccount?.Id,
                AccountingVoucherId = voucherId
            });

            await db.SaveChangesAsync(cancellationToken);

            if (dbTransaction is not null)
                await dbTransaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (dbTransaction is not null)
                await dbTransaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            dbTransaction?.Dispose();
        }

        return await GetByIdAsync(cheque.Id, cancellationToken);
    }

    public async Task<ChequeDetailResponse> ReplaceAsync(
        Guid id, ReplaceChequeRequest request, CancellationToken cancellationToken)
    {
        var cheque = await db.Cheques
            .Include(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Çek bulunamadı.");

        EnsureRowVersionMatches(cheque, request.RowVersion);

        if (!NextStatuses(cheque.Status).Contains(ChequeStatus.Replaced))
        {
            throw new InvalidOperationException(
                $"'{StatusName(cheque.Status)}' durumundaki çek ertelenemez.");
        }

        if (cheque.ReplacedByChequeId is not null)
            throw new InvalidOperationException("Bu çek zaten ertelenmiş.");

        if (string.IsNullOrWhiteSpace(request.ChequeNumber))
            throw new ArgumentException("Yeni çek numarası zorunludur.");

        var newChequeNumber = request.ChequeNumber.Trim();

        if (string.Equals(newChequeNumber, cheque.ChequeNumber, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Yeni çek numarası eskisiyle aynı olamaz; erteleme yeni bir çektir.");
        }

        // Erteleme de yeni bir çek açıyor; aynı kural geçerli
        // (iptal edilmişler engellemez, banka/şube/keşideci ayırır).
        await EnsureChequeNumberAvailableAsync(
            cheque.CompanyId, cheque.Direction, request.BankName ?? cheque.BankName,
            request.BankBranch ?? cheque.BankBranch,
            Cheque.NormalizeChequeNumber(newChequeNumber),
            excludeChequeId: null, cancellationToken);

        var movementDate = AsUtc(request.MovementDate);
        var newIssueDate = AsUtc(request.IssueDate ?? request.MovementDate);
        var newDueDate = AsUtc(request.DueDate);

        var internalNumber = await documentNumberService.GenerateAsync(
            cheque.CompanyId,
            cheque.Direction == ChequeDirection.Received ? "CHEQUE_RECEIVED" : "CHEQUE_ISSUED",
            cheque.Direction == ChequeDirection.Received ? "ACK" : "VCK",
            cancellationToken);

        // Tutar eski çekten kopyalanır, istekten ALINMAZ: vade farkı
        // ayrı bir belgenin konusudur ve burada sessizce bir gider
        // hesabına yazılmamalı.
        var replacement = new Cheque
        {
            CompanyId = cheque.CompanyId,
            Direction = cheque.Direction,
            Status = cheque.Direction == ChequeDirection.Received
                ? ChequeStatus.Portfolio
                : ChequeStatus.Issued,
            InternalNumber = internalNumber,
            ChequeNumber = newChequeNumber,
            NormalizedChequeNumber = Cheque.NormalizeChequeNumber(newChequeNumber),
            BankName = Normalize(request.BankName) ?? cheque.BankName,
            BankBranch = Normalize(request.BankBranch) ?? cheque.BankBranch,
            Drawer = Normalize(request.Drawer) ?? cheque.Drawer,
            CurrentAccountId = cheque.CurrentAccountId,
            ProjectId = cheque.ProjectId,
            CostCenterCode = cheque.CostCenterCode,
            Amount = cheque.Amount,
            CurrencyCode = cheque.CurrencyCode,
            IssueDate = newIssueDate,
            DueDate = newDueDate,
            ProgressPaymentId = cheque.ProgressPaymentId,
            SupplierInvoiceId = cheque.SupplierInvoiceId,
            Description = Normalize(request.Description)
                ?? $"{cheque.ChequeNumber} numaralı çekin ertelenmesi",
            ReplacesChequeId = cheque.Id
        };

        var ownsTransaction = db.Database.CurrentTransaction is null;
        var dbTransaction = ownsTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            db.Cheques.Add(replacement);
            await db.SaveChangesAsync(cancellationToken);

            // Dağılım aynen taşınır: yeni çek aynı faturaları/projeleri
            // karşılıyor. Taşınmasaydı yeni çekin fişi tek parça kesilir
            // ve masraf merkezi kırılımı kaybolurdu.
            foreach (var allocation in cheque.Allocations)
            {
                db.ChequeAllocations.Add(new ChequeAllocation
                {
                    ChequeId = replacement.Id,
                    Amount = allocation.Amount,
                    ProjectId = allocation.ProjectId,
                    CostCenterCode = allocation.CostCenterCode,
                    SupplierInvoiceId = allocation.SupplierInvoiceId,
                    SalesInvoiceId = allocation.SalesInvoiceId,
                    Description = allocation.Description
                });
            }

            if (cheque.Allocations.Count > 0)
                await db.SaveChangesAsync(cancellationToken);

            // Eski çekin ters kaydı.
            var oldStatus = cheque.Status;

            var reversalVoucherId = await accountingIntegration.CreateChequeVoucherAsync(
                cheque, oldStatus, ChequeStatus.Replaced, movementDate, null, cancellationToken);

            cheque.Status = ChequeStatus.Replaced;
            cheque.ReplacedByChequeId = replacement.Id;
            cheque.UpdatedAtUtc = DateTime.UtcNow;

            db.ChequeMovements.Add(new ChequeMovement
            {
                ChequeId = cheque.Id,
                MovementDate = movementDate,
                FromStatus = oldStatus,
                ToStatus = ChequeStatus.Replaced,
                Description =
                    $"Ertelendi — yerine {newChequeNumber} numaralı çek verildi " +
                    $"(yeni vade {newDueDate:dd.MM.yyyy})",
                AccountingVoucherId = reversalVoucherId
            });

            // Yeni çekin giriş fişi.
            var newVoucherId = await accountingIntegration.CreateChequeVoucherAsync(
                replacement, null, replacement.Status, newIssueDate, null, cancellationToken);

            db.ChequeMovements.Add(new ChequeMovement
            {
                ChequeId = replacement.Id,
                MovementDate = newIssueDate,
                FromStatus = null,
                ToStatus = replacement.Status,
                Description = $"{cheque.ChequeNumber} numaralı çekin yerine düzenlendi",
                AccountingVoucherId = newVoucherId
            });

            await db.SaveChangesAsync(cancellationToken);

            if (dbTransaction is not null)
                await dbTransaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (dbTransaction is not null)
                await dbTransaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            dbTransaction?.Dispose();
        }

        return await GetByIdAsync(replacement.Id, cancellationToken);
    }

    /// <summary>
    /// Çekin kaç kez ertelendiği: zincirde geriye doğru yürünerek
    /// bulunur. Sayaç alanı tutulsaydı zincirle tutarsız kalabilirdi.
    /// </summary>
    private async Task<int> CountRenewalsAsync(Guid chequeId, CancellationToken cancellationToken)
    {
        var count = 0;
        var currentId = chequeId;

        // Zincir uzunluğu için üst sınır: bozuk veri sonsuz döngüye
        // dönüşmesin.
        while (count < 100)
        {
            var previousId = await db.Cheques
                .AsNoTracking()
                .Where(x => x.Id == currentId)
                .Select(x => x.ReplacesChequeId)
                .SingleOrDefaultAsync(cancellationToken);

            if (previousId is not Guid previous)
                break;

            count++;
            currentId = previous;
        }

        return count;
    }

    public async Task<ChequeDetailResponse> ReplaceAllocationsAsync(
        Guid id, ChequeAllocationsRequest request, CancellationToken cancellationToken)
    {
        var cheque = await db.Cheques.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Çek bulunamadı.");

        EnsureRowVersionMatches(cheque, request.RowVersion);

        var movements = await db.ChequeMovements
            .Where(x => x.ChequeId == id)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        // Çek işlem gördükten sonra (tahsil, ödeme, karşılıksız...) dağılım
        // değiştirilemez: sonraki fişler ilk dağılıma göre kesilmiş olur ve
        // geriye dönük değişiklik onlarla tutarsız kalırdı.
        if (movements.Count > 1)
        {
            throw new InvalidOperationException(
                "Çek işlem gördüğü için dağılımı değiştirilemez. " +
                "Düzeltme gerekiyorsa muhasebede düzeltme fişi düzenleyin.");
        }

        var allocations = await BuildAllocationsAsync(
            cheque, request.Allocations, cancellationToken);

        var ownsTransaction = db.Database.CurrentTransaction is null;
        var dbTransaction = ownsTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var existing = await db.ChequeAllocations
                .Where(x => x.ChequeId == id)
                .ToListAsync(cancellationToken);

            db.ChequeAllocations.RemoveRange(existing);

            if (allocations.Count > 0)
                db.ChequeAllocations.AddRange(allocations);

            // Damga ilerliyor: dağılım da çekin mali kırılımını
        // değiştiriyor ve eşzamanlı ikinci bir isteğin üzerine
        // yazmasına izin verilmemeli.
        cheque.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

            // Giriş fişi dağılıma göre yeniden üretilir. Eskisi SİLİNMEZ,
            // iptal edilir: muhasebede yazılan fişin izi kalmalı.
            var entryMovement = movements.SingleOrDefault(x => x.FromStatus is null);

            if (entryMovement?.AccountingVoucherId is Guid oldVoucherId)
            {
                await voucherService.CancelAsync(
                    oldVoucherId,
                    $"{cheque.InternalNumber} — çek dağılımı değiştirildi, fiş yeniden kesildi.",
                    cancellationToken);
            }

            var newVoucherId = await accountingIntegration.CreateChequeVoucherAsync(
                cheque, null, cheque.Status, cheque.IssueDate, null, cancellationToken);

            if (entryMovement is not null)
            {
                entryMovement.AccountingVoucherId = newVoucherId;
                entryMovement.UpdatedAtUtc = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(cancellationToken);

            if (dbTransaction is not null)
                await dbTransaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (dbTransaction is not null)
                await dbTransaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            dbTransaction?.Dispose();
        }

        return await GetByIdAsync(cheque.Id, cancellationToken);
    }

    /// <summary>
    /// Dağılım satırlarını doğrular ve kurar.
    ///
    /// Fatura bağlantılı satırda proje ve masraf merkezi FATURADAN alınır;
    /// istemcinin gönderdiği değerler yok sayılır. Aksi halde aynı ödeme
    /// faturada bir projeye, çekte başka bir projeye yazılabilir ve iki
    /// rapor birbirini tutmazdı.
    /// </summary>
    private async Task<List<ChequeAllocation>> BuildAllocationsAsync(
        Cheque cheque,
        IReadOnlyCollection<ChequeAllocationRequest>? requests,
        CancellationToken cancellationToken)
    {
        if (requests is null || requests.Count == 0)
            return [];

        var result = new List<ChequeAllocation>();
        var lineNumber = 0;

        foreach (var request in requests)
        {
            lineNumber++;

            var amount = decimal.Round(request.Amount, 2);

            if (amount <= 0m)
                throw new ArgumentException($"Dağılım {lineNumber}: tutar sıfırdan büyük olmalıdır.");

            if (request.SupplierInvoiceId is not null && request.SalesInvoiceId is not null)
            {
                throw new ArgumentException(
                    $"Dağılım {lineNumber}: bir satır hem alış hem satış faturasına bağlanamaz.");
            }

            var allocation = new ChequeAllocation
            {
                ChequeId = cheque.Id,
                Amount = amount,
                Description = Normalize(request.Description)
            };

            if (request.SupplierInvoiceId is Guid supplierInvoiceId)
            {
                if (cheque.Direction != ChequeDirection.Issued)
                {
                    throw new ArgumentException(
                        $"Dağılım {lineNumber}: tedarikçi faturası yalnızca verilen çeke bağlanabilir.");
                }

                var invoice = await db.SupplierInvoices
                    .AsNoTracking()
                    .Where(x => x.Id == supplierInvoiceId && x.CompanyId == cheque.CompanyId)
                    .Select(x => new
                    {
                        x.Id,
                        x.InvoiceNumber,
                        x.SupplierCurrentAccountId,
                        x.ProjectId,
                        x.CostCenterCode,
                        x.GrandTotal,
                        x.Status,
                        ProjectCode = x.Project != null ? x.Project.Code : null
                    })
                    .SingleOrDefaultAsync(cancellationToken)
                    ?? throw new ArgumentException(
                        $"Dağılım {lineNumber}: tedarikçi faturası bulunamadı.");

                if (invoice.SupplierCurrentAccountId != cheque.CurrentAccountId)
                {
                    throw new ArgumentException(
                        $"Dağılım {lineNumber}: {invoice.InvoiceNumber} numaralı fatura " +
                        "çekin carisine ait değil.");
                }

                if (invoice.Status is SupplierInvoiceStatus.Cancelled
                    or SupplierInvoiceStatus.Rejected)
                {
                    throw new ArgumentException(
                        $"Dağılım {lineNumber}: iptal/reddedilmiş faturaya ödeme bağlanamaz.");
                }

                await EnsureInvoiceNotOverAllocatedAsync(
                    cheque.Id, supplierInvoiceId, null, invoice.GrandTotal,
                    requests, lineNumber, invoice.InvoiceNumber, cancellationToken);

                allocation.SupplierInvoiceId = supplierInvoiceId;
                allocation.ProjectId = invoice.ProjectId;
                allocation.CostCenterCode = invoice.CostCenterCode ?? invoice.ProjectCode;
                result.Add(allocation);
                continue;
            }

            if (request.SalesInvoiceId is Guid salesInvoiceId)
            {
                if (cheque.Direction != ChequeDirection.Received)
                {
                    throw new ArgumentException(
                        $"Dağılım {lineNumber}: satış faturası yalnızca alınan çeke bağlanabilir.");
                }

                var invoice = await db.SalesInvoices
                    .AsNoTracking()
                    .Where(x => x.Id == salesInvoiceId && x.CompanyId == cheque.CompanyId)
                    .Select(x => new
                    {
                        x.Id,
                        x.InternalNumber,
                        x.CustomerCurrentAccountId,
                        x.ProjectId,
                        x.GrandTotal,
                        x.Status,
                        ProjectCode = x.Project != null ? x.Project.Code : null
                    })
                    .SingleOrDefaultAsync(cancellationToken)
                    ?? throw new ArgumentException(
                        $"Dağılım {lineNumber}: satış faturası bulunamadı.");

                if (invoice.CustomerCurrentAccountId != cheque.CurrentAccountId)
                {
                    throw new ArgumentException(
                        $"Dağılım {lineNumber}: {invoice.InternalNumber} numaralı fatura " +
                        "çekin carisine ait değil.");
                }

                if (invoice.Status == SalesInvoiceStatus.Cancelled)
                {
                    throw new ArgumentException(
                        $"Dağılım {lineNumber}: iptal edilmiş faturaya tahsilat bağlanamaz.");
                }

                await EnsureInvoiceNotOverAllocatedAsync(
                    cheque.Id, null, salesInvoiceId, invoice.GrandTotal,
                    requests, lineNumber, invoice.InternalNumber, cancellationToken);

                allocation.SalesInvoiceId = salesInvoiceId;
                allocation.ProjectId = invoice.ProjectId;
                allocation.CostCenterCode = invoice.ProjectCode;
                result.Add(allocation);
                continue;
            }

            // Elle dağılım: fatura yoksa hedef birim mutlaka belirtilmeli;
            // yoksa pay hangi masraf merkezine gideceği belirsiz kalırdı.
            if (request.ProjectId is null && string.IsNullOrWhiteSpace(request.CostCenterCode))
            {
                throw new ArgumentException(
                    $"Dağılım {lineNumber}: proje, masraf merkezi ya da fatura seçilmelidir.");
            }

            string? projectCode = null;

            if (request.ProjectId is Guid projectId)
            {
                projectCode = await db.Projects
                    .Where(x => x.Id == projectId && x.CompanyId == cheque.CompanyId)
                    .Select(x => x.Code)
                    .SingleOrDefaultAsync(cancellationToken)
                    ?? throw new ArgumentException($"Dağılım {lineNumber}: proje bulunamadı.");
            }

            allocation.ProjectId = request.ProjectId;
            allocation.CostCenterCode = Normalize(request.CostCenterCode) ?? projectCode;
            result.Add(allocation);
        }

        var total = result.Sum(x => x.Amount);

        if (total != decimal.Round(cheque.Amount, 2))
        {
            throw new ArgumentException(
                $"Dağılım toplamı ({TurkishFormat.Amount(total)}) çek tutarına ({TurkishFormat.Amount(cheque.Amount)}) eşit olmalıdır.");
        }

        return result;
    }

    /// <summary>
    /// Bir faturaya bağlanan toplam ödeme fatura tutarını aşamaz; aşarsa
    /// cari kapatma yanlış olur ve fatura fazla ödenmiş görünürdü.
    /// Kontrol, bu çekin ESKİ satırları hariç tutularak yapılır.
    /// </summary>
    private async Task EnsureInvoiceNotOverAllocatedAsync(
        Guid chequeId,
        Guid? supplierInvoiceId,
        Guid? salesInvoiceId,
        decimal invoiceTotal,
        IReadOnlyCollection<ChequeAllocationRequest> requests,
        int lineNumber,
        string invoiceNumber,
        CancellationToken cancellationToken)
    {
        var otherCheques = await db.ChequeAllocations
            .AsNoTracking()
            .Where(x => x.ChequeId != chequeId &&
                        ((supplierInvoiceId != null && x.SupplierInvoiceId == supplierInvoiceId) ||
                         (salesInvoiceId != null && x.SalesInvoiceId == salesInvoiceId)))
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        var thisRequest = requests
            .Where(x => (supplierInvoiceId != null && x.SupplierInvoiceId == supplierInvoiceId) ||
                        (salesInvoiceId != null && x.SalesInvoiceId == salesInvoiceId))
            .Sum(x => decimal.Round(x.Amount, 2));

        if (otherCheques + thisRequest > decimal.Round(invoiceTotal, 2))
        {
            throw new ArgumentException(
                $"Dağılım {lineNumber}: {invoiceNumber} numaralı faturaya bağlanan toplam " +
                $"({TurkishFormat.Amount(otherCheques + thisRequest)}) fatura tutarını ({TurkishFormat.Amount(invoiceTotal)}) aşıyor.");
        }
    }

    /// <summary>
    /// Her çek bir yere yazılmalı: PROJE ya da MASRAF MERKEZİ.
    ///
    /// İkisi de boş bırakılabildiği sürece çek hiçbir kırılıma
    /// düşmüyordu; proje bazlı nakit akışı ve "bu projeye bu ay ne
    /// kadar çek verildi" sorusu o çekleri hiç görmezdi.
    ///
    /// Proje TEK BAŞINA zorunlu tutulmadı: ofis kirası gibi projesi
    /// olmayan çekler Merkez'e yazılıyor. Zorunlu tutulsaydı kullanıcı
    /// rastgele bir proje seçerdi ve tam da kurmaya çalıştığımız
    /// kırılım bozulurdu.
    /// </summary>
    private static void RequireAttribution(
        Guid? projectId, string? costCenterCode, bool hasAllocations)
    {
        // DAĞILIM DA SAYILIR: birden çok faturayı ödeyen çekte proje
        // başlıkta değil dağılım satırlarında durur ve faturadan
        // türetilir. Başlık zorunlu tutulsaydı bu tercih edilen
        // kullanım engellenirdi.
        if (hasAllocations)
            return;

        if (projectId is null && string.IsNullOrWhiteSpace(costCenterCode))
        {
            throw new ArgumentException(
                "Çek bir projeye ya da masraf merkezine bağlanmalıdır. " +
                "Projesi olmayan çekler için masraf merkezi seçin.");
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime AsUtc(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
}
