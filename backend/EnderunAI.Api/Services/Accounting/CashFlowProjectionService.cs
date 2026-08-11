using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Models.PurchaseOrder;
using EnderunAI.Api.Services.HumanResources;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Accounting;

public interface ICashFlowProjectionService
{
    Task<CashFlowProjectionResponse> GetAsync(
        Guid companyId, int months, DateTime? targetDate,
        CancellationToken cancellationToken);
}

/// <summary>
/// Likidite takvimi: TARİH BAZLI yürüyen nakit bakiyesi.
///
/// Mevcut <see cref="CashFlowService"/> 30/60/90 KOVASI üretiyor —
/// "önümüzdeki 60 günde ne olur" sorusunu cevaplıyor ama "hangi GÜN
/// açığa düşüyoruz" sorusunu cevaplayamıyor. İki tahsilat arasındaki
/// çukur kovanın içinde kayboluyor. Bu servis her hareketi kendi
/// gününe koyar ve bakiyeyi gün gün yürütür.
///
/// KESİN ↔ TAHMİNİ AYRI: çek ve vergi vadesi kesin, hakediş ve bordro
/// tahmin. İkisi aynı renkte gösterilirse tahmini bir gecikme kesin
/// bir borç gibi okunur.
///
/// ELDEN DAHİL: bordro çıkışı resmî net + manuel elden + mesai elden
/// olarak TAM hesaplanır. Okuma anı maskelemesi (TotalTakeHome)
/// KULLANILMAZ — cashflow.view'ı olup extra_payment.view'ı olmayan bir
/// kullanıcıda maskeleme eldeni gizler ve projeksiyon sessizce eksik
/// çıkardı. Yetki KAPIDA çözülüyor (cashflow.view), tablo içeride tek
/// ve eksiksiz.
///
/// İPTAL EDİLEN HİÇBİR ŞEY SAYILMAZ: iptal çek, ertelenen çek,
/// karşılıksız çek ve iptal görevlendirme ne giriş ne çıkış.
/// </summary>
public sealed class CashFlowProjectionService(
    AppDbContext db,
    HrDbContext hrDb,
    SalaryTakeHomeService takeHome,
    Tax.ITaxObligationService taxObligations,
    Expenses.RecurringExpenseService recurringExpenses)
    : ICashFlowProjectionService
{
    private static readonly string[] MonthNames =
    [
        "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran",
        "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"
    ];

    private sealed record Movement(
        DateTime Date,
        string Kind,
        string KindName,
        string Title,
        string? Reference,
        Guid? ProjectId,
        string? ProjectCode,
        decimal Amount,
        bool IsInflow,
        CashFlowCertainty Certainty);

    public async Task<CashFlowProjectionResponse> GetAsync(
        Guid companyId, int months, DateTime? targetDate,
        CancellationToken cancellationToken)
    {
        // Ufuk 3/6/12 dışında bir değere zorlanmıyor: 6 varsayılan.
        var horizon = months is 3 or 6 or 12 ? months : 6;

        var today = DateTime.UtcNow.Date;
        var until = today.AddMonths(horizon);

        var notes = new List<string>();
        var movements = new List<Movement>();

        var opening = await GetOpeningBalanceAsync(companyId, cancellationToken);

        movements.AddRange(await GetChequeMovementsAsync(
            companyId, today, until, cancellationToken));

        movements.AddRange(await GetProgressPaymentMovementsAsync(
            companyId, today, until, notes, cancellationToken));

        movements.AddRange(await GetSupplierInvoiceMovementsAsync(
            companyId, today, until, cancellationToken));

        movements.AddRange(await GetSubcontractorMovementsAsync(
            companyId, today, until, cancellationToken));

        movements.AddRange(await GetPurchaseOrderMovementsAsync(
            companyId, today, until, cancellationToken));

        movements.AddRange(await GetDutyMovementsAsync(
            companyId, today, until, cancellationToken));

        movements.AddRange(await GetTaxMovementsAsync(
            companyId, today, until, cancellationToken));

        movements.AddRange(await GetPayrollMovementsAsync(
            companyId, today, until, horizon, notes, cancellationToken));

        // GİDER MERKEZİ DEVRALDI: tekrarlayan giderler ve elle
        // girilen gider kayıtları artık oradan geliyor.
        movements.AddRange(await GetRecurringExpenseMovementsAsync(
            companyId, today, until, cancellationToken));

        movements.AddRange(await GetExpenseEntryMovementsAsync(
            companyId, today, until, cancellationToken));

        // Stopgap tablosunda kalan satırlar SAYILMAYA DEVAM EDİYOR:
        // okunmasaydı, taşınmamış bir kira sessizce takvimden düşer ve
        // tablo yeniden iyimser olurdu. Ama artık yeni satır
        // açılamıyor (uç 410 dönüyor), yani R6 çift sayımı ancak eski
        // satırlar taşınırken doğabilir — bu yüzden uyarı düşüyor.
        var legacy = await GetEstimatedExpenseMovementsAsync(
            companyId, today, until, cancellationToken);

        movements.AddRange(legacy);

        if (legacy.Count > 0)
            notes.Add(
                "Nakit akışın eski \"tahmini gider\" satırları hâlâ sayılıyor. " +
                "Bunları Gider Merkezi'nde tekrarlayan gider olarak tanımlayıp " +
                "eskilerini silin; iki yerde birden dururlarsa çift sayılırlar.");

        if (!await db.RecurringExpenseTemplates
                .AnyAsync(x => x.CompanyId == companyId && !x.IsStopped,
                    cancellationToken) &&
            legacy.Count == 0)
        {
            notes.Add(
                "Genel gider (kira, elektrik, sigorta) takvimde yok: Gider " +
                "Merkezi'nde tekrarlayan gider tanımlanmamış. Tanımlayana " +
                "kadar bakiye olduğundan iyimser.");
        }

        return Build(companyId, today, until, horizon, opening,
            movements, targetDate, notes);
    }

    // ---------------- Takvimin kurulması ----------------

    private static CashFlowProjectionResponse Build(
        Guid companyId,
        DateTime today,
        DateTime until,
        int horizon,
        decimal opening,
        List<Movement> movements,
        DateTime? targetDate,
        List<string> notes)
    {
        var days = new List<CashFlowProjectionDay>();
        var running = opening;

        CashFlowShortfall? shortfall = null;
        DateTime? firstNegative = null;
        decimal firstNegativeBalance = 0m;
        var peakBalance = opening;
        var peakDate = today;

        foreach (var group in movements
                     .Where(x => x.Date.Date >= today && x.Date.Date <= until)
                     .GroupBy(x => x.Date.Date)
                     .OrderBy(x => x.Key))
        {
            var inflow = group.Where(x => x.IsInflow).Sum(x => x.Amount);
            var outflow = group.Where(x => !x.IsInflow).Sum(x => x.Amount);

            running += inflow - outflow;

            if (running < peakBalance)
            {
                peakBalance = running;
                peakDate = group.Key;
            }

            if (running < 0m && firstNegative is null)
            {
                firstNegative = group.Key;
                firstNegativeBalance = running;
            }

            days.Add(new CashFlowProjectionDay(
                group.Key,
                inflow,
                outflow,
                inflow - outflow,
                running,
                group
                    .OrderByDescending(x => x.IsInflow)
                    .ThenByDescending(x => x.Amount)
                    .Select(x => new CashFlowProjectionItem(
                        x.Date, x.Kind, x.KindName, x.Title, x.Reference,
                        x.ProjectId, x.ProjectCode, x.Amount, x.IsInflow,
                        (int)x.Certainty, CertaintyName(x.Certainty)))
                    .ToList()));
        }

        if (firstNegative is DateTime negativeDate)
        {
            shortfall = new CashFlowShortfall(
                negativeDate,
                firstNegativeBalance,
                peakDate,
                peakBalance,
                // Gereken finansman EN DERİN noktaya göre: ilk negatif
                // günü kapatmak yetmez, çukurun dibi kadar para lazım.
                decimal.Round(Math.Abs(Math.Min(peakBalance, 0m)), 2));
        }

        var monthly = days
            .GroupBy(x => new { x.Date.Year, x.Date.Month })
            .OrderBy(x => x.Key.Year).ThenBy(x => x.Key.Month)
            .Select(g =>
            {
                var lowest = g.OrderBy(x => x.RunningBalance).First();

                return new CashFlowProjectionMonth(
                    g.Key.Year,
                    g.Key.Month,
                    $"{MonthNames[g.Key.Month - 1]} {g.Key.Year}",
                    g.Sum(x => x.Inflow),
                    g.Sum(x => x.Outflow),
                    g.Sum(x => x.Net),
                    g.OrderBy(x => x.Date).Last().RunningBalance,
                    lowest.RunningBalance,
                    lowest.Date);
            })
            .ToList();

        CashFlowTargetSummary? target = null;

        if (targetDate is DateTime wanted)
        {
            var limit = wanted.Date;

            var reached = days.Where(x => x.Date <= limit).ToList();

            var closing = reached.Count > 0
                ? reached[^1].RunningBalance
                : opening;

            target = new CashFlowTargetSummary(
                limit,
                reached.Sum(x => x.Inflow),
                reached.Sum(x => x.Outflow),
                closing,
                // Hedefe kadar en derin çukur: o güne "eksi bakiyeyle"
                // varmak yetmez, yol boyunca da batmamak gerekir.
                decimal.Round(
                    Math.Abs(Math.Min(
                        reached.Count > 0
                            ? reached.Min(x => x.RunningBalance)
                            : opening,
                        0m)),
                    2));
        }

        return new CashFlowProjectionResponse(
            companyId,
            today,
            until,
            horizon,
            opening,
            days.Count > 0 ? days[^1].RunningBalance : opening,
            monthly,
            days,
            shortfall,
            target,
            notes);
    }

    private static string CertaintyName(CashFlowCertainty certainty) =>
        certainty == CashFlowCertainty.Confirmed ? "Kesin" : "Tahmini";

    private async Task<decimal> GetOpeningBalanceAsync(
        Guid companyId, CancellationToken cancellationToken)
    {
        var opening = await db.CashAccounts
            .Where(x => x.CompanyId == companyId && x.IsActive)
            .SumAsync(x => (decimal?)x.OpeningBalance, cancellationToken) ?? 0m;

        var movements = await db.CashTransactions
            .Where(x => x.CashAccount.CompanyId == companyId && x.CashAccount.IsActive)
            .GroupBy(x => x.Direction)
            .Select(g => new { Direction = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        return opening
            + movements.Where(x => x.Direction == CashTransactionDirection.In)
                .Sum(x => x.Total)
            - movements.Where(x => x.Direction == CashTransactionDirection.Out)
                .Sum(x => x.Total);
    }

    // ---------------- Kaynaklar ----------------

    /// <summary>
    /// Çek: tek KESİN kaynak — vade günü sözleşmeyle sabit.
    /// İptal, ertelenen ve karşılıksız çek elenir.
    /// </summary>
    private async Task<List<Movement>> GetChequeMovementsAsync(
        Guid companyId, DateTime today, DateTime until,
        CancellationToken cancellationToken)
    {
        var rows = await db.Cheques
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.DueDate >= today && x.DueDate <= until &&
                        (x.Status == ChequeStatus.Portfolio ||
                         x.Status == ChequeStatus.AtBank ||
                         x.Status == ChequeStatus.Issued))
            .Select(x => new
            {
                x.Id, x.Direction, x.DueDate, x.AmountTry, x.ChequeNumber,
                x.ProjectId,
                ProjectCode = x.Project != null ? x.Project.Code : null,
                CurrentAccountTitle = x.CurrentAccount != null
                    ? x.CurrentAccount.Title
                    : null
            })
            .ToListAsync(cancellationToken);

        return rows.Select(x => new Movement(
            x.DueDate,
            x.Direction == ChequeDirection.Received ? "ReceivedCheque" : "IssuedCheque",
            x.Direction == ChequeDirection.Received ? "Alınan çek" : "Verilen çek",
            x.CurrentAccountTitle ?? "Çek",
            x.ChequeNumber,
            x.ProjectId,
            x.ProjectCode,
            x.AmountTry,
            x.Direction == ChequeDirection.Received,
            CashFlowCertainty.Confirmed)).ToList();
    }

    /// <summary>
    /// Hakediş: tahsil edilmemiş bakiye.
    ///
    /// TARİH SIRASI: hakedişteki ezme → projedeki vade günü → hakediş
    /// tarihi. Ezme varsa KESİN (işverenle konuşulmuş), yoksa TAHMİNİ.
    /// Vade tanımlı değilse hakediş tarihi kullanılır ve bu durum
    /// nota yazılır — para girişini olduğundan erken gösterir.
    /// </summary>
    private async Task<List<Movement>> GetProgressPaymentMovementsAsync(
        Guid companyId, DateTime today, DateTime until,
        List<string> notes, CancellationToken cancellationToken)
    {
        var rows = await db.ProgressPayments
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.Status == ProgressPaymentStatus.Posted)
            .Select(x => new
            {
                x.Id, x.ProgressPaymentNumber, x.ProjectId,
                ProjectCode = x.Project.Code,
                ProjectName = x.Project.Name,
                x.Project.CollectionTermDays,
                x.ProgressPaymentDate,
                x.ExpectedCollectionDate,
                x.NetPayableAmount
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return [];

        var ids = rows.Select(x => x.Id).ToList();

        var collected = (await db.CashTransactions.AsNoTracking()
            .Where(x => x.SourceModule == "ProgressPayment" &&
                        x.SourceEntityId != null &&
                        ids.Contains(x.SourceEntityId!.Value) &&
                        x.Direction == CashTransactionDirection.In)
            .GroupBy(x => x.SourceEntityId!.Value)
            .Select(g => new { Id = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken))
            .ToDictionary(x => x.Id, x => x.Total);

        // Çekle karşılanan kısım ayrı satır olarak zaten takvimde:
        // burada da sayılsa aynı tahsilat iki kez görünürdü.
        var covered = (await db.Cheques.AsNoTracking()
            .Where(x => x.Direction == ChequeDirection.Received &&
                        x.ProgressPaymentId != null &&
                        ids.Contains(x.ProgressPaymentId!.Value) &&
                        x.Status != ChequeStatus.Bounced &&
                        x.Status != ChequeStatus.Replaced &&
                        x.Status != ChequeStatus.Voided)
            .GroupBy(x => x.ProgressPaymentId!.Value)
            .Select(g => new { Id = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken))
            .ToDictionary(x => x.Id, x => x.Total);

        var missingTerm = 0;
        var items = new List<Movement>();

        foreach (var row in rows)
        {
            var remaining = decimal.Round(
                row.NetPayableAmount
                - collected.GetValueOrDefault(row.Id)
                - covered.GetValueOrDefault(row.Id), 2);

            if (remaining <= 0m)
                continue;

            DateTime date;
            CashFlowCertainty certainty;

            if (row.ExpectedCollectionDate is DateTime expected)
            {
                date = expected.Date;
                certainty = CashFlowCertainty.Confirmed;
            }
            else if (row.CollectionTermDays is int term && term > 0)
            {
                date = row.ProgressPaymentDate.Date.AddDays(term);
                certainty = CashFlowCertainty.Estimated;
            }
            else
            {
                date = row.ProgressPaymentDate.Date;
                certainty = CashFlowCertainty.Estimated;
                missingTerm++;
            }

            // Vadesi geçmiş tahsilat bugüne çekiliyor: geçmişte kalan
            // bir satır takvimde görünmez ama para da gelmemiştir.
            if (date < today)
                date = today;

            if (date > until)
                continue;

            items.Add(new Movement(
                date, "ProgressPayment", "Hakediş",
                $"{row.ProjectCode} — {row.ProjectName}",
                row.ProgressPaymentNumber,
                row.ProjectId, row.ProjectCode,
                remaining, true, certainty));
        }

        if (missingTerm > 0)
        {
            notes.Add(
                $"{missingTerm} hakedişte tahsilat vadesi tanımlı değil; " +
                "hakediş tarihi kullanıldı. Proje kartına tahsilat vade günü " +
                "girilmeden para girişi olduğundan erken görünür.");
        }

        return items;
    }

    /// <summary>
    /// Tedarikçi faturası: ödenmemiş bakiye. Vade doluysa KESİN,
    /// yoksa fatura tarihi ve TAHMİNİ.
    /// </summary>
    private async Task<List<Movement>> GetSupplierInvoiceMovementsAsync(
        Guid companyId, DateTime today, DateTime until,
        CancellationToken cancellationToken)
    {
        var rows = await db.SupplierInvoices
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        (x.Status == SupplierInvoiceStatus.Approved ||
                         x.Status == SupplierInvoiceStatus.PendingApproval))
            .Select(x => new
            {
                x.Id, x.InvoiceNumber, x.InvoiceDate, x.DueDate,
                x.GrandTotal, x.ProjectId,
                ProjectCode = x.Project != null ? x.Project.Code : null,
                SupplierTitle = x.SupplierCurrentAccount.Title
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return [];

        var ids = rows.Select(x => x.Id).ToList();

        var paid = (await db.CashTransactions.AsNoTracking()
            .Where(x => x.SourceModule == "SupplierInvoice" &&
                        x.SourceEntityId != null &&
                        ids.Contains(x.SourceEntityId!.Value) &&
                        x.Direction == CashTransactionDirection.Out)
            .GroupBy(x => x.SourceEntityId!.Value)
            .Select(g => new { Id = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken))
            .ToDictionary(x => x.Id, x => x.Total);

        var covered = (await db.Cheques.AsNoTracking()
            .Where(x => x.Direction == ChequeDirection.Issued &&
                        x.SupplierInvoiceId != null &&
                        ids.Contains(x.SupplierInvoiceId!.Value) &&
                        x.Status != ChequeStatus.Returned &&
                        x.Status != ChequeStatus.Replaced &&
                        x.Status != ChequeStatus.Voided)
            .GroupBy(x => x.SupplierInvoiceId!.Value)
            .Select(g => new { Id = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken))
            .ToDictionary(x => x.Id, x => x.Total);

        var items = new List<Movement>();

        foreach (var row in rows)
        {
            var remaining = decimal.Round(
                row.GrandTotal
                - paid.GetValueOrDefault(row.Id)
                - covered.GetValueOrDefault(row.Id), 2);

            if (remaining <= 0m)
                continue;

            var date = row.DueDate?.Date ?? row.InvoiceDate.Date;

            var certainty = row.DueDate is not null
                ? CashFlowCertainty.Confirmed
                : CashFlowCertainty.Estimated;

            if (date < today) date = today;
            if (date > until) continue;

            items.Add(new Movement(
                date, "SupplierInvoice", "Tedarikçi faturası",
                row.SupplierTitle ?? "Tedarikçi",
                row.InvoiceNumber, row.ProjectId, row.ProjectCode,
                remaining, false, certainty));
        }

        return items;
    }

    /// <summary>
    /// Taşeron hakedişi: onaylanmış ama ödenmemiş. Vade alanı yok —
    /// hakediş tarihi kullanılıyor ve TAHMİNİ işaretleniyor.
    /// </summary>
    private async Task<List<Movement>> GetSubcontractorMovementsAsync(
        Guid companyId, DateTime today, DateTime until,
        CancellationToken cancellationToken)
    {
        var rows = await db.SubcontractorProgressPayments
            .AsNoTracking()
            .Where(x => x.Status == SubcontractorProgressPaymentStatus.Approved &&
                        x.SubcontractorContract.Project.CompanyId == companyId)
            .Select(x => new
            {
                x.Id,
                x.ProgressPaymentDate,
                x.NetPayableAmount,
                ProjectId = (Guid?)x.SubcontractorContract.ProjectId,
                ProjectCode = x.SubcontractorContract.Project.Code,
                Subcontractor = x.SubcontractorContract.CurrentAccount.Title
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(x => x.NetPayableAmount > 0m)
            .Select(x =>
            {
                var date = x.ProgressPaymentDate.Date < today
                    ? today
                    : x.ProgressPaymentDate.Date;

                return new Movement(
                    date, "SubcontractorPayment", "Taşeron hakedişi",
                    x.Subcontractor, null, x.ProjectId, x.ProjectCode,
                    x.NetPayableAmount, false, CashFlowCertainty.Estimated);
            })
            .Where(x => x.Date <= until)
            .ToList();
    }

    /// <summary>
    /// Satın alma siparişi: onaylı ama HENÜZ FATURALANMAMIŞ olanlar.
    ///
    /// Faturası kesilmiş sipariş zaten fatura satırı olarak takvimde;
    /// ikisi birden sayılsa aynı ödeme iki kez çıkardı.
    /// </summary>
    private async Task<List<Movement>> GetPurchaseOrderMovementsAsync(
        Guid companyId, DateTime today, DateTime until,
        CancellationToken cancellationToken)
    {
        var invoicedOrderIds = await db.SupplierInvoices
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.PurchaseOrderId != null)
            .Select(x => x.PurchaseOrderId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var rows = await db.PurchaseOrders
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        (x.Status == PurchaseOrderStatus.Approved ||
                         x.Status == PurchaseOrderStatus.PartiallyReceived) &&
                        !invoicedOrderIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id, x.OrderNumber, x.OrderDate, x.ExpectedDeliveryDate,
                x.GrandTotal, x.ProjectId,
                ProjectCode = x.Project != null ? x.Project.Code : null,
                Supplier = x.SupplierCurrentAccount.Title
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(x => x.GrandTotal > 0m)
            .Select(x =>
            {
                var date = (x.ExpectedDeliveryDate ?? x.OrderDate).Date;
                if (date < today) date = today;

                return new Movement(
                    date, "PurchaseOrder", "Satın alma siparişi",
                    x.Supplier ?? "Tedarikçi", x.OrderNumber,
                    x.ProjectId, x.ProjectCode,
                    x.GrandTotal, false, CashFlowCertainty.Estimated);
            })
            .Where(x => x.Date <= until)
            .ToList();
    }

    /// <summary>
    /// Görevlendirme masrafı: onaylı görevin yol + konaklama +
    /// harcırahı. İPTAL EDİLEN GÖREV SAYILMAZ.
    /// </summary>
    private async Task<List<Movement>> GetDutyMovementsAsync(
        Guid companyId, DateTime today, DateTime until,
        CancellationToken cancellationToken)
    {
        var rows = await db.PersonnelDuties
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.Status == PersonnelDutyStatus.Approved &&
                        x.StartDate >= today && x.StartDate <= until)
            .Select(x => new
            {
                x.Id, x.StartDate, x.EndDate, x.DailyAllowance,
                x.TravelCost, x.AccommodationCost,
                x.TargetProjectId,
                ProjectCode = x.TargetProject.Code,
                PersonnelName = x.Personnel.FirstName + " " + x.Personnel.LastName
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x =>
            {
                var dayCount = x.EndDate.Date < x.StartDate.Date
                    ? 0
                    : (x.EndDate.Date - x.StartDate.Date).Days + 1;

                var total = x.TravelCost + x.AccommodationCost
                    + x.DailyAllowance * dayCount;

                return new Movement(
                    x.StartDate.Date, "PersonnelDuty", "Görevlendirme masrafı",
                    x.PersonnelName, null, x.TargetProjectId, x.ProjectCode,
                    total, false, CashFlowCertainty.Estimated);
            })
            .Where(x => x.Amount > 0m)
            .ToList();
    }

    private async Task<List<Movement>> GetTaxMovementsAsync(
        Guid companyId, DateTime today, DateTime until,
        CancellationToken cancellationToken)
    {
        var obligations = await taxObligations.GetObligationsAsync(
            companyId, today, until, cancellationToken);

        // ÖDENMİŞ yükümlülük çıkış değil: parası zaten gitti ve
        // başlangıç bakiyesine yansıdı.
        return obligations
            .Where(x => !x.IsPaid &&
                        x.DueDate.Date >= today && x.DueDate.Date <= until &&
                        x.EstimatedAmount > 0m)
            .Select(x => new Movement(
                x.DueDate.Date, "Tax", "Vergi / SGK",
                $"{x.KindName} — {x.PeriodLabel}", null,
                null, null, x.EstimatedAmount, false,
                CashFlowCertainty.Confirmed))
            .ToList();
    }

    /// <summary>
    /// Bordro çıkışı — ELDEN DAHİL TAM TUTAR.
    ///
    /// Üç parçadan toplanıyor:
    ///   1) resmî net (son kesinleşmiş dönemin toplamı)
    ///   2) manuel elden (yürürlükteki aylık ek ödemeler)
    ///   3) mesai eldeni (dönemin mesai saatleri × saatlik × katsayı)
    ///
    /// Bordro kaydındaki ActualPayableAmount OKUNMUYOR: adı "fiili"
    /// olsa da hesaplama sırasında resmî netin birebir eşdeğerine
    /// set ediliyor, elden içermiyor.
    ///
    /// Okuma anı maskelemesi (TotalTakeHome) da KULLANILMIYOR:
    /// cashflow.view'ı olup extra_payment.view'ı olmayan kullanıcıda
    /// maskeleme eldeni gizler ve projeksiyon sessizce eksik çıkardı.
    /// Yetki kapıda çözülüyor.
    /// </summary>
    private async Task<List<Movement>> GetPayrollMovementsAsync(
        Guid companyId, DateTime today, DateTime until, int horizon,
        List<string> notes, CancellationToken cancellationToken)
    {
        var latest = await hrDb.PayrollRecords
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.Status != PayrollStatus.Draft)
            .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
            .Select(x => new { x.Year, x.Month })
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            notes.Add(
                "Bordro çıkışı takvimde yok: kesinleşmiş bordro dönemi " +
                "bulunamadı. Şirketin en büyük düzenli çıkışı eksik olduğu " +
                "için bakiye olduğundan iyimser.");

            return [];
        }

        var officialNet = await hrDb.PayrollRecords
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.Year == latest.Year && x.Month == latest.Month &&
                        x.Status != PayrollStatus.Draft)
            .SumAsync(x => (decimal?)x.OfficialNetPayableAmount, cancellationToken)
            ?? 0m;

        var personnelIds = await db.Personnel
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive &&
                        x.Status == PersonnelStatus.Active)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var extras = await takeHome.LoadEffectiveExtraPaymentsAsync(
            personnelIds, cancellationToken);

        var manualExtra = extras.Values.Sum();

        var overtimeExtra = await GetOvertimeExtraAsync(
            companyId, latest.Year, latest.Month, personnelIds, extras,
            cancellationToken);

        var total = decimal.Round(officialNet + manualExtra + overtimeExtra, 2);

        if (total <= 0m)
            return [];

        var paymentDay = await db.CompanyFinanceSettings
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => (int?)x.PayrollPaymentDay)
            .FirstOrDefaultAsync(cancellationToken) ?? 5;

        var items = new List<Movement>();

        for (var offset = 0; offset <= horizon; offset++)
        {
            var month = new DateTime(today.Year, today.Month, 1, 0, 0, 0,
                DateTimeKind.Utc).AddMonths(offset);

            var day = Math.Min(paymentDay, DateTime.DaysInMonth(month.Year, month.Month));
            var date = new DateTime(month.Year, month.Month, day, 0, 0, 0,
                DateTimeKind.Utc);

            if (date < today || date > until)
                continue;

            items.Add(new Movement(
                date, "Payroll", "Bordro (elden dahil)",
                $"{MonthNames[month.Month - 1]} {month.Year} maaş ödemesi",
                null, null, null, total, false, CashFlowCertainty.Estimated));
        }

        return items;
    }

    /// <summary>
    /// Dönemin mesai eldeni. Saatlik ücret ORTAK YARDIMCIDAN geliyor —
    /// personel kartındaki mesai paneliyle aynı formül.
    /// </summary>
    private async Task<decimal> GetOvertimeExtraAsync(
        Guid companyId, int year, int month,
        List<Guid> personnelIds,
        IReadOnlyDictionary<Guid, decimal> extras,
        CancellationToken cancellationToken)
    {
        if (personnelIds.Count == 0)
            return 0m;

        var periodStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);

        var hours = await db.AttendanceRecords
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.WorkDate >= periodStart && x.WorkDate <= periodEnd &&
                        (x.OvertimeHours > 0m || x.SundayHours > 0m ||
                         x.PublicHolidayHours > 0m))
            .GroupBy(x => x.PersonnelId)
            .Select(g => new
            {
                PersonnelId = g.Key,
                Overtime = g.Sum(x => x.OvertimeHours),
                Sunday = g.Sum(x => x.SundayHours),
                Holiday = g.Sum(x => x.PublicHolidayHours)
            })
            .ToListAsync(cancellationToken);

        if (hours.Count == 0)
            return 0m;

        var dailyWorkHours = await db.CompanyPayrollSettings
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Year == year)
            .Select(x => (decimal?)x.DailyWorkHours)
            .FirstOrDefaultAsync(cancellationToken);

        var cards = await hrDb.SalaryDefinitions
            .AsNoTracking()
            .Where(x => personnelIds.Contains(x.PersonnelId) &&
                        x.EffectiveStartDate <= periodEnd &&
                        (x.EffectiveEndDate == null || x.EffectiveEndDate >= periodStart))
            .ToListAsync(cancellationToken);

        var parameters = await takeHome.TryLoadPayrollParametersAsync(
            companyId, year, cancellationToken);

        var total = 0m;

        foreach (var row in hours)
        {
            var card = cards
                .Where(x => x.PersonnelId == row.PersonnelId)
                .OrderByDescending(x => x.EffectiveStartDate)
                .FirstOrDefault();

            if (card is null)
                continue;

            var hourly = SalaryTakeHomeService.ResolveOvertimeHourlyRate(
                SalaryTakeHomeService.ResolveOfficialNet(card, parameters),
                extras.GetValueOrDefault(row.PersonnelId),
                dailyWorkHours);

            if (hourly is not decimal rate)
                continue;

            total += row.Overtime * rate * card.OvertimeMultiplier
                + row.Sunday * rate * card.SundayMultiplier
                + row.Holiday * rate * card.PublicHolidayMultiplier;
        }

        return decimal.Round(total, 2);
    }

    /// <summary>
    /// Gider merkezindeki tekrarlayan giderler.
    ///
    /// YALNIZ GERÇEKLEŞMEMİŞ DÖNEMLER: bir ayın gerçekleşeni
    /// girilmişse o ay gider kaydı olarak zaten akıyor; tahmini de
    /// eklenseydi aynı kira iki kez çıkardı (R5/R6). Kural
    /// RecurringExpenseService'te tek yerde duruyor, burada
    /// tekrarlanmıyor.
    ///
    /// ELDEN MASKESİ YOK: projeksiyon zaten cashflow.view kapısında
    /// ve tablo tek/eksiksiz olmak zorunda — gerçek nakit ihtiyacı
    /// elden kalemleri de içerir.
    /// </summary>
    private async Task<List<Movement>> GetRecurringExpenseMovementsAsync(
        Guid companyId, DateTime today, DateTime until,
        CancellationToken cancellationToken)
    {
        var states = await recurringExpenses.GetPeriodStatesAsync(
            companyId, today, until, cancellationToken);

        var pending = states
            .Where(x => x.ActualEntryId is null &&
                        x.DueDate >= today && x.DueDate <= until &&
                        x.EstimatedAmount > 0m)
            .ToList();

        if (pending.Count == 0)
            return [];

        var templateIds = pending.Select(x => x.TemplateId).Distinct().ToList();

        var templates = await db.RecurringExpenseTemplates
            .AsNoTracking()
            .Where(x => templateIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.Description,
                x.ProjectId,
                ProjectCode = x.Project != null ? x.Project.Code : null
            })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        return pending
            .Select(x =>
            {
                var template = templates[x.TemplateId];

                return new Movement(
                    x.DueDate, "RecurringExpense", "Tekrarlayan gider",
                    template.Description, null,
                    template.ProjectId, template.ProjectCode,
                    x.EstimatedAmount, false, CashFlowCertainty.Estimated);
            })
            .ToList();
    }

    /// <summary>
    /// Elle girilen gider kayıtlarının GELECEK tarihli olanları.
    ///
    /// Geçmiş tarihli gider zaten ödenmiş kabul ediliyor ve açılış
    /// bakiyesinin içinde: yeniden çıkış yazılsaydı aynı para iki kez
    /// düşerdi.
    ///
    /// Gider kaydı muhasebeye ve kasaya yazmıyor; burada OKUNUYOR.
    /// Okuma, resmî deftere postalama değil.
    /// </summary>
    private async Task<List<Movement>> GetExpenseEntryMovementsAsync(
        Guid companyId, DateTime today, DateTime until,
        CancellationToken cancellationToken)
    {
        var rows = await db.ExpenseEntries
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.ExpenseDate >= today && x.ExpenseDate <= until &&
                        x.Amount > 0m)
            .Select(x => new
            {
                x.ExpenseDate,
                x.Description,
                x.Amount,
                x.ProjectId,
                ProjectCode = x.Project != null ? x.Project.Code : null
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new Movement(
                x.ExpenseDate, "ExpenseEntry", "Gider kaydı",
                x.Description, null, x.ProjectId, x.ProjectCode,
                x.Amount, false, CashFlowCertainty.Confirmed))
            .ToList();
    }

    /// <summary>
    /// ESKİ STOPGAP: gider merkezi gelmeden önce elle girilen
    /// tekrarlayan tahmini gider. Yeni satır açılamıyor; kalanlar
    /// taşınana kadar sayılmaya devam ediyor.
    /// </summary>
    private async Task<List<Movement>> GetEstimatedExpenseMovementsAsync(
        Guid companyId, DateTime today, DateTime until,
        CancellationToken cancellationToken)
    {
        var rows = await db.CashFlowEstimatedExpenses
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive && x.Amount > 0m)
            .Select(x => new
            {
                x.Description, x.Amount, x.StartYear, x.StartMonth,
                x.RecurrenceCount, x.PaymentDay, x.ProjectId,
                ProjectCode = x.Project != null ? x.Project.Code : null
            })
            .ToListAsync(cancellationToken);

        var items = new List<Movement>();

        foreach (var row in rows)
        {
            var start = new DateTime(row.StartYear, row.StartMonth, 1, 0, 0, 0,
                DateTimeKind.Utc);

            for (var index = 0; index < row.RecurrenceCount; index++)
            {
                var month = start.AddMonths(index);

                var day = Math.Min(
                    row.PaymentDay,
                    DateTime.DaysInMonth(month.Year, month.Month));

                var date = new DateTime(month.Year, month.Month, day, 0, 0, 0,
                    DateTimeKind.Utc);

                if (date < today || date > until)
                    continue;

                items.Add(new Movement(
                    date, "EstimatedExpense", "Tahmini gider",
                    row.Description, null, row.ProjectId, row.ProjectCode,
                    row.Amount, false, CashFlowCertainty.Estimated));
            }
        }

        return items;
    }
}
