using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Accounting;

public interface ICashFlowService
{
    Task<CashFlowResponse> GetAsync(
        Guid companyId,
        Guid? projectId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Vade bazlı nakit akışı: beklenen tahsilatlar (portföydeki/bankadaki
/// alınan çekler + kesinleşmiş hakedişlerin tahsil edilmemiş bakiyesi
/// + kesinleşmiş satış faturalarının tahsil edilmemiş bakiyesi)
/// karşısında beklenen ödemeler (vadesi gelmemiş verilen çekler +
/// onaylı tedarikçi faturalarının ödenmemiş bakiyesi).
///
/// Hakediş/fatura bakiyesinden düşülenler: o belgeye bağlanmış kasa
/// hareketleri ve o belgeye bağlanmış çekler. Belgeye bağlanmadan
/// girilen serbest tahsilat/ödemeler bu kırılımda görünmez — kalem
/// bazlı takip için tahsilatın hakedişe/faturaya bağlanması gerekir.
/// </summary>
public sealed class CashFlowService(
    AppDbContext db,
    Tax.ITaxObligationService taxObligations) : ICashFlowService
{
    private static readonly int[] BucketDays = [30, 60, 90];

    /// <summary>
    /// Vergi çıkışları en uzak kova kadar ileriye bakılarak üretilir.
    /// </summary>
    private static readonly int TaxHorizonDays = BucketDays.Max();

    public async Task<CashFlowResponse> GetAsync(
        Guid companyId,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;

        var currentBalance = await GetCurrentCashBalanceAsync(companyId, cancellationToken);

        var inflows = new List<CashFlowItemResponse>();
        var outflows = new List<CashFlowItemResponse>();

        inflows.AddRange(await GetChequeItemsAsync(
            companyId, projectId, ChequeDirection.Received, today, cancellationToken));
        inflows.AddRange(await GetProgressPaymentItemsAsync(
            companyId, projectId, today, cancellationToken));
        inflows.AddRange(await GetSalesInvoiceItemsAsync(
            companyId, projectId, today, cancellationToken));

        outflows.AddRange(await GetChequeItemsAsync(
            companyId, projectId, ChequeDirection.Issued, today, cancellationToken));
        outflows.AddRange(await GetSupplierInvoiceItemsAsync(
            companyId, projectId, today, cancellationToken));

        // Vergi çıkışları: KDV, SGK, muhtasar ve geçici vergi. Proje
        // filtresi verildiğinde gösterilmez — vergi şirket düzeyinde bir
        // yükümlülüktür, tek projeye pay edilmesi yanıltıcı olurdu.
        if (projectId is null)
        {
            outflows.AddRange(await GetTaxItemsAsync(
                companyId, today, cancellationToken));
        }

        inflows = inflows.OrderBy(x => x.ExpectedDate).ToList();
        outflows = outflows.OrderBy(x => x.ExpectedDate).ToList();

        var overdueIn = inflows.Where(x => x.IsOverdue).Sum(x => x.Amount);
        var overdueOut = outflows.Where(x => x.IsOverdue).Sum(x => x.Amount);

        var buckets = new List<CashFlowBucketResponse>(BucketDays.Length);

        foreach (var days in BucketDays)
        {
            var limit = today.AddDays(days);

            // Kümülatif: "önümüzdeki N gün" bakiyesi, vadesi geçmişleri
            // dışarıda bırakarak bugünden N. güne kadar tüm hareketleri
            // kapsar.
            var inflow = inflows
                .Where(x => !x.IsOverdue && x.ExpectedDate.Date <= limit)
                .Sum(x => x.Amount);
            var outflow = outflows
                .Where(x => !x.IsOverdue && x.ExpectedDate.Date <= limit)
                .Sum(x => x.Amount);

            buckets.Add(new CashFlowBucketResponse(
                days,
                $"{days} gün",
                inflow,
                outflow,
                inflow - outflow,
                currentBalance + inflow - outflow));
        }

        return new CashFlowResponse(
            companyId,
            today,
            currentBalance,
            overdueIn,
            overdueOut,
            buckets,
            inflows,
            outflows);
    }

    private async Task<decimal> GetCurrentCashBalanceAsync(
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

        var inflow = movements
            .Where(x => x.Direction == CashTransactionDirection.In).Sum(x => x.Total);
        var outflow = movements
            .Where(x => x.Direction == CashTransactionDirection.Out).Sum(x => x.Total);

        return opening + inflow - outflow;
    }

    /// <summary>
    /// Tahmini vergi ödemeleri.
    ///
    /// Ödendi işaretlenen dönem listeye girmez: girseydi ödenmiş vergi
    /// nakit akışta durmaya devam eder ve şirket olduğundan daha dar
    /// görünürdü. Vadesi geçmiş ve hâlâ ödenmemiş olanlar gecikmiş
    /// olarak görünür — gerçekten ödenmişse işaretlenmelidir.
    /// </summary>
    private async Task<List<CashFlowItemResponse>> GetTaxItemsAsync(
        Guid companyId,
        DateTime today,
        CancellationToken cancellationToken)
    {
        // Geriye dönük 90 gün: vadesi geçmiş ama işaretlenmemiş
        // yükümlülükler de uyarı olarak görünmeli.
        var obligations = await taxObligations.GetObligationsAsync(
            companyId,
            today.AddDays(-TaxHorizonDays),
            today.AddDays(TaxHorizonDays),
            cancellationToken);

        return obligations
            .Where(x => !x.IsPaid && x.EstimatedAmount > 0m)
            .Select(x => new CashFlowItemResponse(
                $"Tax{x.Kind}",
                $"{x.KindName} (tahmini)",
                Guid.Empty,
                x.PeriodLabel,
                $"{x.KindName} — {x.PeriodLabel} dönemi",
                null,
                null,
                null,
                null,
                x.DueDate,
                (int)(x.DueDate.Date - today).TotalDays,
                x.DueDate.Date < today,
                x.EstimatedAmount,
                "TRY"))
            .ToList();
    }

    private async Task<List<CashFlowItemResponse>> GetChequeItemsAsync(
        Guid companyId,
        Guid? projectId,
        ChequeDirection direction,
        DateTime today,
        CancellationToken cancellationToken)
    {
        // Faktoringdeki çek beklenen tahsilat değildir: parası kırdırma
        // anında zaten alınmıştır.
        var openStatuses = direction == ChequeDirection.Received
            ? new[] { ChequeStatus.Portfolio, ChequeStatus.AtBank }
            : new[] { ChequeStatus.Issued };

        var query = db.Cheques
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId
                && x.Direction == direction
                && openStatuses.Contains(x.Status));

        if (projectId is not null)
            query = query.Where(x => x.ProjectId == projectId.Value);

        var rows = await query
            .Select(x => new
            {
                x.Id,
                x.InternalNumber,
                x.ChequeNumber,
                x.BankName,
                x.CurrentAccountId,
                CurrentAccountTitle = x.CurrentAccount != null ? x.CurrentAccount.Title : null,
                x.ProjectId,
                ProjectCode = x.Project != null ? x.Project.Code : null,
                x.Amount,
                x.CurrencyCode,
                x.DueDate
            })
            .ToListAsync(cancellationToken);

        var kind = direction == ChequeDirection.Received ? "ReceivedCheque" : "IssuedCheque";
        var kindName = direction == ChequeDirection.Received ? "Alınan çek" : "Verilen çek";

        return rows.Select(x => new CashFlowItemResponse(
            kind,
            kindName,
            x.Id,
            x.ChequeNumber,
            $"{x.BankName} — {x.InternalNumber}",
            x.CurrentAccountId,
            x.CurrentAccountTitle,
            x.ProjectId,
            x.ProjectCode,
            x.DueDate,
            (int)(x.DueDate.Date - today).TotalDays,
            x.DueDate.Date < today,
            x.Amount,
            x.CurrencyCode)).ToList();
    }

    private async Task<List<CashFlowItemResponse>> GetProgressPaymentItemsAsync(
        Guid companyId,
        Guid? projectId,
        DateTime today,
        CancellationToken cancellationToken)
    {
        var query = db.ProgressPayments
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId
                && x.Status == ProgressPaymentStatus.Posted);

        if (projectId is not null)
            query = query.Where(x => x.ProjectId == projectId.Value);

        var rows = await query
            .Select(x => new
            {
                x.Id,
                x.ProgressPaymentNumber,
                x.ProjectId,
                ProjectCode = x.Project.Code,
                ProjectName = x.Project.Name,
                EmployerCurrentAccountId = x.Project.EmployerCurrentAccountId,
                EmployerTitle = x.Project.EmployerCurrentAccount != null
                    ? x.Project.EmployerCurrentAccount.Title
                    : null,
                x.CurrencyCode,
                x.NetPayableAmount,
                x.ProgressPaymentDate
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return [];

        var ids = rows.Select(x => x.Id).ToList();

        var collections = await db.CashTransactions
            .AsNoTracking()
            .Where(x => x.SourceModule == "ProgressPayment"
                && x.SourceEntityId != null
                && ids.Contains(x.SourceEntityId!.Value)
                && x.Direction == CashTransactionDirection.In)
            .GroupBy(x => x.SourceEntityId!.Value)
            .Select(g => new { ProgressPaymentId = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        // Hakedişe karşılık alınan çekler: karşılıksız çıkanlar ve
        // ERTELENENLER hariç tahsilat sayılır. Ertelenen çek hariç
        // tutulmazsa yerine geçen yeni çekle birlikte aynı tahsilat iki
        // kez sayılır ve hakediş fazladan karşılanmış görünürdü.
        var chequeCoverage = await db.Cheques
            .AsNoTracking()
            .Where(x => x.Direction == ChequeDirection.Received
                && x.ProgressPaymentId != null
                && ids.Contains(x.ProgressPaymentId!.Value)
                && x.Status != ChequeStatus.Bounced
                && x.Status != ChequeStatus.Replaced
                // İPTAL EDİLEN ÇEK TAHSİLAT SAYILMAZ: mali etkileri
                // ters kayıtla geri alındı, hakedişi karşılamıyor.
                && x.Status != ChequeStatus.Voided)
            .GroupBy(x => x.ProgressPaymentId!.Value)
            .Select(g => new { ProgressPaymentId = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var items = new List<CashFlowItemResponse>();

        foreach (var row in rows)
        {
            var collected = collections
                .Where(x => x.ProgressPaymentId == row.Id).Sum(x => x.Total);
            var covered = chequeCoverage
                .Where(x => x.ProgressPaymentId == row.Id).Sum(x => x.Total);

            var remaining = decimal.Round(row.NetPayableAmount - collected - covered, 2);
            if (remaining <= 0m)
                continue;

            items.Add(new CashFlowItemResponse(
                "ProgressPayment",
                "Hakediş",
                row.Id,
                row.ProgressPaymentNumber,
                $"{row.ProjectCode} — {row.ProjectName}",
                row.EmployerCurrentAccountId,
                row.EmployerTitle,
                row.ProjectId,
                row.ProjectCode,
                row.ProgressPaymentDate,
                (int)(row.ProgressPaymentDate.Date - today).TotalDays,
                row.ProgressPaymentDate.Date < today,
                remaining,
                row.CurrencyCode));
        }

        return items;
    }

    private async Task<List<CashFlowItemResponse>> GetSupplierInvoiceItemsAsync(
        Guid companyId,
        Guid? projectId,
        DateTime today,
        CancellationToken cancellationToken)
    {
        var query = db.SupplierInvoices
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId
                && x.Status == SupplierInvoiceStatus.Approved);

        if (projectId is not null)
            query = query.Where(x => x.ProjectId == projectId.Value);

        var rows = await query
            .Select(x => new
            {
                x.Id,
                x.InternalNumber,
                x.InvoiceNumber,
                x.SupplierCurrentAccountId,
                SupplierTitle = x.SupplierCurrentAccount.Title,
                x.ProjectId,
                ProjectCode = x.Project.Code,
                x.CurrencyCode,
                x.GrandTotal,
                x.InvoiceDate,
                x.DueDate
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return [];

        var ids = rows.Select(x => x.Id).ToList();

        var payments = await db.CashTransactions
            .AsNoTracking()
            .Where(x => x.SourceModule == "SupplierInvoice"
                && x.SourceEntityId != null
                && ids.Contains(x.SourceEntityId!.Value)
                && x.Direction == CashTransactionDirection.Out)
            .GroupBy(x => x.SourceEntityId!.Value)
            .Select(g => new { SupplierInvoiceId = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        // Faturaya karşılık verilen çekler: iade alınanlar ve
        // ERTELENENLER hariç ödeme sayılır; ertelenen çek yerine geçen
        // yeni çekle birlikte aynı ödemeyi iki kez göstermemeli.
        var chequeCoverage = await db.Cheques
            .AsNoTracking()
            .Where(x => x.Direction == ChequeDirection.Issued
                && x.SupplierInvoiceId != null
                && ids.Contains(x.SupplierInvoiceId!.Value)
                && x.Status != ChequeStatus.Returned
                && x.Status != ChequeStatus.Replaced
                // İptal edilen çek ödeme de sayılmaz; faturayı açık
                // bırakır.
                && x.Status != ChequeStatus.Voided)
            .GroupBy(x => x.SupplierInvoiceId!.Value)
            .Select(g => new { SupplierInvoiceId = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var items = new List<CashFlowItemResponse>();

        foreach (var row in rows)
        {
            var paid = payments
                .Where(x => x.SupplierInvoiceId == row.Id).Sum(x => x.Total);
            var covered = chequeCoverage
                .Where(x => x.SupplierInvoiceId == row.Id).Sum(x => x.Total);

            var remaining = decimal.Round(row.GrandTotal - paid - covered, 2);
            if (remaining <= 0m)
                continue;

            var expectedDate = row.DueDate ?? row.InvoiceDate;

            items.Add(new CashFlowItemResponse(
                "SupplierInvoice",
                "Tedarikçi faturası",
                row.Id,
                row.InvoiceNumber,
                $"{row.InternalNumber} — {row.SupplierTitle}",
                row.SupplierCurrentAccountId,
                row.SupplierTitle,
                row.ProjectId,
                row.ProjectCode,
                expectedDate,
                (int)(expectedDate.Date - today).TotalDays,
                expectedDate.Date < today,
                remaining,
                row.CurrencyCode));
        }

        return items;
    }
    /// <summary>
    /// KESİNLEŞMİŞ SATIŞ FATURALARININ TAHSİL EDİLMEMİŞ BAKİYESİ.
    ///
    /// Bu kaynak sonradan eklendi: nakit akışı girişleri yıllarca
    /// yalnız çek portföyü ve hakediş bakiyesinden geliyordu, yani
    /// vadeli bir satış faturası kesildiğinde alacak projeksiyonda hiç
    /// görünmüyordu. Perakendede vadeli satış açılınca eksik ortaya
    /// çıktı; ama kusur perakendeye ait değil, satış faturasının
    /// tamamına aitti — bu yüzden düzeltme kaynağın kendisinde.
    ///
    /// ÇİFT SAYIM YOK: faturaya bağlanmış tahsilatlar (kasa hareketi)
    /// ve karşılığında alınan çekler bakiyeden düşülüyor. Aksi hâlde
    /// peşin satışta hem kasaya giren para hem de faturanın tamamı
    /// beklenen tahsilat sayılırdı.
    ///
    /// Yalnız POSTED fatura sayılır: taslak fatura henüz bir alacak
    /// doğurmamıştır. İADE FATURALARI HARİÇ (IsReturn) — onlar
    /// alacağı azaltan ters kayıtlar, ayrı bir tahsilat beklentisi
    /// değil.
    /// </summary>
    private async Task<List<CashFlowItemResponse>> GetSalesInvoiceItemsAsync(
        Guid companyId,
        Guid? projectId,
        DateTime today,
        CancellationToken cancellationToken)
    {
        var query = db.SalesInvoices
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId
                && x.Status == SalesInvoiceStatus.Posted
                && !x.IsReturn);

        if (projectId is not null)
            query = query.Where(x => x.ProjectId == projectId.Value);

        var rows = await query
            .Select(x => new
            {
                x.Id,
                x.InternalNumber,
                x.OfficialInvoiceNumber,
                x.CustomerCurrentAccountId,
                CustomerTitle = x.CustomerCurrentAccount.Title,
                x.ProjectId,
                ProjectCode = x.Project != null ? x.Project.Code : null,
                x.CurrencyCode,
                x.NetReceivableAmount,
                x.InvoiceDate,
                x.DueDate
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return [];

        var ids = rows.Select(x => x.Id).ToList();

        var collections = await db.CashTransactions
            .AsNoTracking()
            .Where(x => x.SourceModule == "SalesInvoice"
                && x.SourceEntityId != null
                && ids.Contains(x.SourceEntityId!.Value)
                && x.Direction == CashTransactionDirection.In)
            .GroupBy(x => x.SourceEntityId!.Value)
            .Select(g => new { SalesInvoiceId = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        // Perakende peşin/kart satışında tahsilat fişin kendisine
        // bağlanıyor (SourceModule = "RETAIL_SALE"), faturaya değil.
        // O tahsilatlar da bu faturanın bakiyesini kapatır; hesaba
        // katılmazsa peşin satış "açık alacak" gibi görünürdü.
        var retailCollections = await db.RetailSales
            .AsNoTracking()
            .Where(x => x.SalesInvoiceId != null
                && ids.Contains(x.SalesInvoiceId!.Value)
                && x.CashTransactionId != null)
            .Join(db.CashTransactions.AsNoTracking(),
                sale => sale.CashTransactionId!.Value,
                cash => cash.Id,
                (sale, cash) => new { sale.SalesInvoiceId, cash.Amount })
            .GroupBy(x => x.SalesInvoiceId!.Value)
            .Select(g => new { SalesInvoiceId = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        // ÇEK KARŞILIĞI BURADA DÜŞÜLEMİYOR: Cheque modelinde
        // SupplierInvoiceId ve ProgressPaymentId var ama SalesInvoiceId
        // YOK — alınan bir çek satış faturasına bağlanamıyor. Bu yüzden
        // çekle kapatılan bir satış faturası, çek tahsil edilip kasaya
        // girene kadar açık alacak olarak görünmeye devam eder.
        //
        // Uydurma bir bağ kurmak yerine eksik olduğu gibi bırakıldı:
        // yanlış eşleşen bir çek, alacağı olduğundan erken kapatır ve
        // nakit akışını olduğundan iyi gösterirdi. Çek↔satış faturası
        // bağı ayrı bir iş (TEMIZLIK-TARAMASI'na yazıldı).
        var items = new List<CashFlowItemResponse>();

        foreach (var row in rows)
        {
            var collected = collections
                .Where(x => x.SalesInvoiceId == row.Id).Sum(x => x.Total);
            var retail = retailCollections
                .Where(x => x.SalesInvoiceId == row.Id).Sum(x => x.Total);
            var remaining = decimal.Round(
                row.NetReceivableAmount - collected - retail, 2);

            if (remaining <= 0m)
                continue;

            var expectedDate = row.DueDate ?? row.InvoiceDate;

            items.Add(new CashFlowItemResponse(
                "SalesInvoice",
                "Satış faturası",
                row.Id,
                row.OfficialInvoiceNumber ?? row.InternalNumber,
                $"{row.InternalNumber} — {row.CustomerTitle}",
                row.CustomerCurrentAccountId,
                row.CustomerTitle,
                row.ProjectId,
                row.ProjectCode,
                expectedDate,
                (int)(expectedDate.Date - today).TotalDays,
                expectedDate.Date < today,
                remaining,
                row.CurrencyCode));
        }

        return items;
    }
}
