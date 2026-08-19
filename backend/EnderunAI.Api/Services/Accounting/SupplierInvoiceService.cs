using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.DocumentNumbers;
using Microsoft.EntityFrameworkCore;
using EnderunAI.Api.Formatting;

namespace EnderunAI.Api.Services.Accounting;

public interface ISupplierInvoiceService
{
    Task<IReadOnlyCollection<SupplierInvoiceListItemResponse>> GetAllAsync(
        Guid? companyId, int? status, Guid? projectId, Guid? supplierId,
        string? search, CancellationToken cancellationToken);

    Task<SupplierInvoiceDetailResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<SupplierInvoiceDetailResponse> CreateAsync(
        CreateSupplierInvoiceRequest request, CancellationToken cancellationToken);

    Task<SupplierInvoiceDetailResponse> UpdateAsync(
        Guid id, UpdateSupplierInvoiceRequest request, CancellationToken cancellationToken);

    Task<SupplierInvoiceActionResponse> SubmitAsync(Guid id, CancellationToken cancellationToken);
    Task<SupplierInvoiceActionResponse> ApproveAsync(Guid id, CancellationToken cancellationToken);
    Task<SupplierInvoiceActionResponse> RejectAsync(Guid id, string reason, CancellationToken cancellationToken);
    Task<SupplierInvoiceActionResponse> CancelAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Gerekçeli iptal. Onaylanmış faturada gerekçe zorunludur ve ters
    /// fiş üretilir.
    /// </summary>
    Task<SupplierInvoiceActionResponse> CancelAsync(
        Guid id, string? reason, CancellationToken cancellationToken);

    /// <summary>
    /// Tedarikçiye mal iadesi. Orijinal faturaya bağlı yeni bir iade
    /// faturası (taslak) üretir; onaylandığında ters fiş kesilir ve
    /// stok depodan çıkar. Kısmi iade desteklenir.
    /// </summary>
    Task<SupplierInvoiceDetailResponse> CreateReturnAsync(
        Guid originalInvoiceId,
        CreateInvoiceReturnRequest request,
        CancellationToken cancellationToken);
}

public sealed class SupplierInvoiceService(
    AppDbContext db,
    IDocumentNumberService documentNumberService,
    IAccountingIntegrationService accountingIntegration,
    Inventory.ISupplierInvoiceStockPoster stockPoster,
    ICurrentUserService currentUser,
    Market.IInvoiceExchangeRateResolver rateResolver) : ISupplierInvoiceService
{
    private static readonly string[] GmRoleNames = ["Admin", "Genel Müdür"];

    /// <summary>
    /// Faturaya uygulanacak kuru çözer. Kur bulunamıyorsa kaydetmeyi
    /// engeller; TRY faturalarda her zaman 1 döner, dolayısıyla mevcut
    /// TL akışların davranışı değişmez.
    /// </summary>
    private async Task<decimal> ResolveRateAsync(
        string? currencyCode,
        DateTime invoiceDate,
        decimal requestedRate,
        CancellationToken cancellationToken)
    {
        var resolution = await rateResolver.ResolveAsync(
            currencyCode, invoiceDate, requestedRate, cancellationToken);

        if (!resolution.Success)
            throw new ArgumentException(resolution.Error);

        return resolution.Rate;
    }

    public async Task<IReadOnlyCollection<SupplierInvoiceListItemResponse>> GetAllAsync(
        Guid? companyId, int? status, Guid? projectId, Guid? supplierId,
        string? search, CancellationToken cancellationToken)
    {
        var query = db.SupplierInvoices.AsNoTracking();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);
        if (status.HasValue)
            query = query.Where(x => (int)x.Status == status.Value);
        if (projectId.HasValue)
            query = query.Where(x => x.ProjectId == projectId.Value);
        if (supplierId.HasValue)
            query = query.Where(x => x.SupplierCurrentAccountId == supplierId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                EF.Functions.ILike(x.InvoiceNumber, $"%{term}%") ||
                EF.Functions.ILike(x.InternalNumber, $"%{term}%") ||
                EF.Functions.ILike(x.SupplierCurrentAccount.Title, $"%{term}%"));
        }

        return await query
            .OrderByDescending(x => x.InvoiceDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new SupplierInvoiceListItemResponse(
                x.Id, x.InternalNumber, x.InvoiceNumber, x.InvoiceDate,
                x.SupplierCurrentAccountId, x.SupplierCurrentAccount.Title,
                x.ProjectId,
                x.Project != null ? x.Project.Code : null,
                x.Project != null ? x.Project.Name : null,
                (int)x.InvoiceType,
                x.InvoiceType == SupplierInvoiceType.Expense ? "Gider" : "Alış (Stok)",
                x.CostCenterCode,
                x.CurrencyCode, x.Subtotal, x.VatTotal, x.GrandTotal,
                (int)x.Status, (int)x.MatchStatus, x.RequiresGmApproval,
                x.PurchaseOrder != null ? x.PurchaseOrder.OrderNumber : null,
                x.AccountingVoucher != null ? x.AccountingVoucher.VoucherNumber : null,
                x.IsReturn))
            .ToListAsync(cancellationToken);
    }

    public async Task<SupplierInvoiceDetailResponse> GetByIdAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var invoice = await LoadDetailAsync(id, cancellationToken);

        if (invoice is null)
            throw new KeyNotFoundException("Tedarikçi faturası bulunamadı.");

        return MapDetail(
            invoice,
            await LoadChequePaymentsAsync(id, cancellationToken),
            await LoadReturnableItemsAsync(invoice, cancellationToken));
    }

    public async Task<SupplierInvoiceDetailResponse> CreateAsync(
        CreateSupplierInvoiceRequest request, CancellationToken cancellationToken)
    {
        await ValidateHeaderAsync(
            request.CompanyId, request.SupplierCurrentAccountId, request.ProjectId,
            request.PurchaseOrderId, request.GoodsReceiptId,
            request.InvoiceNumber, request.CurrencyCode, request.ExchangeRate,
            cancellationToken);

        var invoiceType = ParseInvoiceType(request.InvoiceType);
        var items = BuildItems(request.Items);

        await ValidateTypeRulesAsync(
            request.CompanyId, invoiceType, request.ProjectId, request.WarehouseId,
            request.GoodsReceiptId, items, cancellationToken);

        var internalNumber = await documentNumberService.GenerateAsync(
            request.CompanyId, "SUPPLIER_INVOICE", "SFT", cancellationToken);

        // Dövizli faturada kur girilmemişse fatura tarihinin TCMB döviz
        // alışı kullanılır. Arşivde kur yoksa fatura kaydedilmez —
        // uydurma kurla defterlenmiş fatura geri dönülmesi zor bir hata.
        var rate = await ResolveRateAsync(
            request.CurrencyCode, AsUtc(request.InvoiceDate), request.ExchangeRate,
            cancellationToken);

        var invoice = new SupplierInvoice
        {
            CompanyId = request.CompanyId,
            SupplierCurrentAccountId = request.SupplierCurrentAccountId,
            InvoiceType = invoiceType,
            ProjectId = request.ProjectId,
            WarehouseId = invoiceType == SupplierInvoiceType.Stock
                ? request.WarehouseId
                : null,
            CostCenterCode = Normalize(request.CostCenterCode),
            PurchaseOrderId = request.PurchaseOrderId,
            GoodsReceiptId = request.GoodsReceiptId,
            InternalNumber = internalNumber,
            InvoiceNumber = request.InvoiceNumber.Trim(),
            InvoiceDate = AsUtc(request.InvoiceDate),
            DueDate = request.DueDate.HasValue ? AsUtc(request.DueDate.Value) : null,
            CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant(),
            ExchangeRate = rate,
            Description = Normalize(request.Description),
            Status = SupplierInvoiceStatus.Draft
        };

        ApplyItemsAndTotals(invoice, items);

        db.SupplierInvoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(invoice.Id, cancellationToken);
    }

    public async Task<SupplierInvoiceDetailResponse> UpdateAsync(
        Guid id, UpdateSupplierInvoiceRequest request, CancellationToken cancellationToken)
    {
        var invoice = await db.SupplierInvoices
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Tedarikçi faturası bulunamadı.");

        if (invoice.Status != SupplierInvoiceStatus.Draft)
            throw new InvalidOperationException("Yalnızca taslak faturalar güncellenebilir.");

        if (string.IsNullOrWhiteSpace(request.InvoiceNumber))
            throw new ArgumentException("Tedarikçi fatura numarası zorunludur.");

        var invoiceType = ParseInvoiceType(request.InvoiceType);
        var items = BuildItems(request.Items);

        await ValidateTypeRulesAsync(
            invoice.CompanyId, invoiceType, request.ProjectId, request.WarehouseId,
            invoice.GoodsReceiptId, items, cancellationToken);

        invoice.InvoiceType = invoiceType;
        invoice.ProjectId = request.ProjectId;
        invoice.WarehouseId = invoiceType == SupplierInvoiceType.Stock
            ? request.WarehouseId
            : null;
        invoice.CostCenterCode = Normalize(request.CostCenterCode);
        invoice.InvoiceNumber = request.InvoiceNumber.Trim();
        invoice.InvoiceDate = AsUtc(request.InvoiceDate);
        invoice.DueDate = request.DueDate.HasValue ? AsUtc(request.DueDate.Value) : null;
        invoice.CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant();
        invoice.ExchangeRate = await ResolveRateAsync(
            request.CurrencyCode, invoice.InvoiceDate, request.ExchangeRate,
            cancellationToken);
        invoice.Description = Normalize(request.Description);
        invoice.UpdatedAtUtc = DateTime.UtcNow;

        db.SupplierInvoiceItems.RemoveRange(invoice.Items);
        invoice.Items.Clear();
        ApplyItemsAndTotals(invoice, items);

        await db.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(invoice.Id, cancellationToken);
    }

    public async Task<SupplierInvoiceActionResponse> SubmitAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var invoice = await db.SupplierInvoices
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Tedarikçi faturası bulunamadı.");

        if (invoice.Status != SupplierInvoiceStatus.Draft)
            throw new InvalidOperationException("Yalnızca taslak faturalar onaya gönderilebilir.");

        var settings = await accountingIntegration.GetOrCreateFinanceSettingsAsync(
            invoice.CompanyId, cancellationToken);

        await RunThreeWayMatchAsync(invoice, settings, cancellationToken);

        var grandTotalTry = decimal.Round(invoice.GrandTotal * invoice.ExchangeRate, 2);
        if (grandTotalTry > settings.GmApprovalThresholdTry)
        {
            invoice.RequiresGmApproval = true;
            invoice.MatchNote = AppendNote(invoice.MatchNote,
                $"Tutar ({TurkishFormat.Amount(grandTotalTry)} TL) GM onay eşiğini ({TurkishFormat.Amount(settings.GmApprovalThresholdTry)} TL) aşıyor.");
        }

        invoice.Status = SupplierInvoiceStatus.PendingApproval;
        invoice.SubmittedAtUtc = DateTime.UtcNow;
        invoice.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return new SupplierInvoiceActionResponse(
            invoice.Id, invoice.InternalNumber, (int)invoice.Status,
            invoice.RequiresGmApproval
                ? "Fatura onaya gönderildi — Genel Müdür onayı gerekiyor."
                : "Fatura onaya gönderildi.");
    }

    public async Task<SupplierInvoiceActionResponse> ApproveAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var invoice = await db.SupplierInvoices
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Tedarikçi faturası bulunamadı.");

        if (invoice.Status != SupplierInvoiceStatus.PendingApproval)
            throw new InvalidOperationException("Yalnızca onay bekleyen faturalar onaylanabilir.");

        if (invoice.RequiresGmApproval &&
            !currentUser.Roles.Any(role => GmRoleNames.Contains(role, StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Bu fatura (tolerans dışı fark veya tutar eşiği nedeniyle) yalnızca Genel Müdür/Admin tarafından onaylanabilir.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var posting = await accountingIntegration.CreateSupplierInvoiceVoucherAsync(
            invoice, cancellationToken, reverse: invoice.IsReturn);

        // Mal kabulsuz ALIŞ faturasında stok depoya girer ve ağırlıklı
        // ortalama maliyet güncellenir. Mal kabullü faturada bu adım
        // hiç çalışmaz: stok orada zaten girmiştir.
        //
        // İade faturasında ters yön: mal depodan çıkar ve ortalama
        // maliyet geri sarılır.
        var postedStockLines = invoice.IsReturn
            ? await stockPoster.PostReturnAsync(invoice, cancellationToken)
            : await stockPoster.PostAsync(invoice, cancellationToken);

        invoice.Status = SupplierInvoiceStatus.Approved;
        invoice.AccountingVoucherId = posting.VoucherId;
        invoice.ApprovedByUserId = currentUser.UserId;
        invoice.ApprovedAtUtc = DateTime.UtcNow;
        invoice.UpdatedAtUtc = DateTime.UtcNow;

        // Mal kabul (depo) üzerinden gelen faturalarda proje maliyeti,
        // stok çıkışında (InventoryController → StockMovement) oluşur;
        // burada da yazılırsa maliyet çift sayılır. Yalnız depoya
        // uğramayan doğrudan hizmet/masraf faturaları projeye işlenir.
        //
        // Projesiz (Merkez) gider faturasında proje maliyeti oluşmaz:
        // ofis elektriğinin projesi yoktur, rastgele bir projeye
        // yazılsaydı o projenin kârlılığı yanlış görünürdü.
        if (invoice.GoodsReceiptId is null && invoice.ProjectId is Guid costProjectId)
        {
            var supplierTitle = await db.CurrentAccounts
                .Where(x => x.Id == invoice.SupplierCurrentAccountId)
                .Select(x => x.Title)
                .SingleAsync(cancellationToken);

            // Maliyet SINIFI kalemin yazıldığı hesaptan çıkar. Gider
            // faturasında kalemler farklı sınıflara düşebilir (bir
            // faturada hem taşeron işçiliği hem nakliye olabilir); bu
            // yüzden sınıf başına ayrı kayıt yazılır. Hepsi aynı fiş
            // satırına bağlanır, mutabakat toplam üzerinden yürüdüğü
            // için bu bağ bozulmaz.
            var sign = invoice.IsReturn ? -1m : 1m;

            var classAmounts = await ResolveCostClassAmountsAsync(
                invoice, cancellationToken);

            foreach (var allocation in classAmounts)
            {
                db.ProjectCostTransactions.Add(new ProjectCostTransaction
                {
                    ProjectId = costProjectId,
                    CostType = invoice.PurchaseOrderId is null
                        ? ProjectCostType.Other
                        : ProjectCostType.Material,
                    CostClass = allocation.CostClass,
                    ProjectBoqItemId = allocation.ProjectBoqItemId,
                    CostDate = invoice.InvoiceDate,
                    // İade projenin maliyetini AZALTIR; eksi tutar yazılmazsa
                    // iade edilen mal projede maliyet olarak durmaya devam
                    // eder ve kârlılık olduğundan düşük görünürdü.
                    Amount = decimal.Round(allocation.Amount * invoice.ExchangeRate, 2) * sign,
                    Description = invoice.IsReturn
                        ? $"Alış iadesi {invoice.InternalNumber} — {supplierTitle}"
                        : $"Tedarikçi faturası {invoice.InternalNumber} — {supplierTitle}",
                    ReferenceType = "SupplierInvoice",
                    ReferenceId = invoice.Id,
                    // Muhasebedeki maliyet satırına bağla — proje maliyeti ile
                    // gider hesapları arasında iki ayrı "doğru" rakam oluşmasın.
                    AccountingVoucherLineId = posting.ExpenseLineId
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var message = invoice.IsReturn
            ? postedStockLines > 0
                ? $"İade faturası onaylandı; ters fiş kesinleştirildi ve " +
                  $"{postedStockLines} kalem depodan çıkarıldı."
                : "İade faturası onaylandı; ters fiş oluşturuldu ve kesinleştirildi."
            : postedStockLines > 0
                ? $"Fatura onaylandı; muhasebe fişi kesinleştirildi ve " +
                  $"{postedStockLines} kalem depoya girildi."
                : "Fatura onaylandı; muhasebe fişi otomatik oluşturuldu ve kesinleştirildi.";

        return new SupplierInvoiceActionResponse(
            invoice.Id, invoice.InternalNumber, (int)invoice.Status, message);
    }

    public async Task<SupplierInvoiceActionResponse> RejectAsync(
        Guid id, string reason, CancellationToken cancellationToken)
    {
        var invoice = await db.SupplierInvoices
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Tedarikçi faturası bulunamadı.");

        if (invoice.Status != SupplierInvoiceStatus.PendingApproval)
            throw new InvalidOperationException("Yalnızca onay bekleyen faturalar reddedilebilir.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Ret gerekçesi zorunludur.");

        invoice.Status = SupplierInvoiceStatus.Rejected;
        invoice.RejectedByUserId = currentUser.UserId;
        invoice.RejectedAtUtc = DateTime.UtcNow;
        invoice.RejectionReason = reason.Trim();
        invoice.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return new SupplierInvoiceActionResponse(
            invoice.Id, invoice.InternalNumber, (int)invoice.Status, "Fatura reddedildi.");
    }

    public async Task<SupplierInvoiceActionResponse> CancelAsync(
        Guid id, CancellationToken cancellationToken) =>
        await CancelAsync(id, null, cancellationToken);

    /// <summary>
    /// Fatura iptali.
    ///
    /// Kesinleşmemiş faturada kayıt yalnızca iptal işaretlenir. ONAYLANMIŞ
    /// faturada ise iz bırakmak şart: fiş silinmez, ters kaydı üretilir;
    /// stok girmişse depodan geri çıkar ve proje maliyeti eksiyle
    /// dengelenir.
    ///
    /// İptal ile İADE farklı şeylerdir: iade gerçekten olmuş bir alışın
    /// malının geri gönderilmesidir ve KDV beyanına iade olarak girer;
    /// iptal ise "bu fatura hiç olmamalıydı" demektir. Bu yüzden faturaya
    /// bağlı iade ya da ödeme varsa iptal reddedilir — önce onlar
    /// çözülmeli.
    /// </summary>
    public async Task<SupplierInvoiceActionResponse> CancelAsync(
        Guid id, string? reason, CancellationToken cancellationToken)
    {
        var invoice = await db.SupplierInvoices
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Tedarikçi faturası bulunamadı.");

        if (invoice.Status == SupplierInvoiceStatus.Cancelled)
            throw new InvalidOperationException("Fatura zaten iptal edilmiş.");

        if (invoice.Status == SupplierInvoiceStatus.Approved)
            return await CancelApprovedAsync(invoice, reason, cancellationToken);

        if (invoice.Status is not (SupplierInvoiceStatus.Draft
            or SupplierInvoiceStatus.PendingApproval))
        {
            throw new InvalidOperationException(
                "Yalnızca taslak, onay bekleyen veya onaylanmış faturalar iptal edilebilir.");
        }

        invoice.CancellationReason = Normalize(reason);
        invoice.CancelledAtUtc = DateTime.UtcNow;
        invoice.Status = SupplierInvoiceStatus.Cancelled;
        invoice.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return new SupplierInvoiceActionResponse(
            invoice.Id, invoice.InternalNumber, (int)invoice.Status, "Fatura iptal edildi.");
    }

    private async Task<SupplierInvoiceActionResponse> CancelApprovedAsync(
        SupplierInvoice invoice, string? reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "Onaylanmış faturanın iptalinde gerekçe zorunludur; " +
                "ters fişte ve denetim izinde görünür.");
        }

        var hasReturn = await db.SupplierInvoices.AnyAsync(
            x => x.OriginalInvoiceId == invoice.Id &&
                 x.Status != SupplierInvoiceStatus.Cancelled &&
                 x.Status != SupplierInvoiceStatus.Rejected,
            cancellationToken);

        if (hasReturn)
        {
            throw new InvalidOperationException(
                "Bu faturaya bağlı iade faturası var. Önce iadeyi iptal edin; " +
                "iade duruyorken fatura iptal edilirse iade dayanaksız kalır.");
        }

        var hasChequeAllocation = await db.ChequeAllocations.AnyAsync(
            x => x.SupplierInvoiceId == invoice.Id, cancellationToken);

        if (hasChequeAllocation)
        {
            throw new InvalidOperationException(
                "Bu faturaya bağlı çek ödemesi var. Önce çek dağılımından " +
                "faturayı çıkarın; ödenmiş faturanın iptali cari bakiyesini bozar.");
        }

        if (invoice.AccountingVoucherId is null)
        {
            throw new InvalidOperationException(
                "Onaylı faturanın muhasebe fişi bulunamadı; iptal ters kayıt " +
                "üretemeyeceği için durduruldu.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var reversalVoucherId = await accountingIntegration.CreateReversalVoucherAsync(
            invoice.AccountingVoucherId.Value,
            reason.Trim(),
            DateTime.UtcNow.Date,
            cancellationToken);

        // Stok bu faturayla girdiyse geri çıkar. Mal kabullü faturada
        // stok mal kabulde girmiştir; oraya dokunulmaz.
        var removedStockLines = invoice.GoodsReceiptId is null
            ? await stockPoster.PostReturnAsync(invoice, cancellationToken, "Fatura iptali")
            : 0;

        // Proje maliyeti eksiyle dengelenir; silinmez ki maliyetin
        // oluşup sonra iptal edildiği geçmişte görünsün.
        //
        // Bir fatura sınıf başına birden fazla maliyet satırı üretebilir
        // (gider faturasında hem taşeron işçiliği hem nakliye olabilir);
        // yalnız ilki dengelenirse kalanı projede maliyet olarak durur.
        // Sınıf bazında NET tutar dengelenir: iptal ikinci kez çalışsa
        // bile net sıfır olduğu için yeni satır yazılmaz.
        var costRows = await db.ProjectCostTransactions
            .Where(x => x.ReferenceType == "SupplierInvoice" && x.ReferenceId == invoice.Id)
            .ToListAsync(cancellationToken);

        foreach (var group in costRows.GroupBy(x => new { x.ProjectId, x.CostType, x.CostClass }))
        {
            var net = group.Sum(x => x.Amount);

            if (net == 0m)
                continue;

            db.ProjectCostTransactions.Add(new ProjectCostTransaction
            {
                ProjectId = group.Key.ProjectId,
                CostType = group.Key.CostType,
                CostClass = group.Key.CostClass,
                CostDate = DateTime.UtcNow.Date,
                Amount = -net,
                Description = $"İPTAL — {group.First().Description}",
                ReferenceType = "SupplierInvoice",
                ReferenceId = invoice.Id
            });
        }

        invoice.Status = SupplierInvoiceStatus.Cancelled;
        invoice.ReversalVoucherId = reversalVoucherId;
        invoice.CancellationReason = reason.Trim();
        invoice.CancelledAtUtc = DateTime.UtcNow;
        invoice.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var message = removedStockLines > 0
            ? $"Fatura iptal edildi; ters fiş kesildi ve {removedStockLines} kalem " +
              "depodan geri çıkarıldı."
            : "Fatura iptal edildi; ters fiş kesildi.";

        return new SupplierInvoiceActionResponse(
            invoice.Id, invoice.InternalNumber, (int)invoice.Status, message);
    }

    /// <summary>
    /// 3 yönlü kontrol: fatura ara toplamı (KDV hariç) ↔ sipariş tutarı ↔
    /// mal kabul edilen miktar × sipariş birim fiyatı. En büyük mutlak
    /// fark tolerans yüzdesiyle karşılaştırılır.
    /// </summary>
    private async Task RunThreeWayMatchAsync(
        SupplierInvoice invoice,
        CompanyFinanceSettings settings,
        CancellationToken cancellationToken)
    {
        if (invoice.PurchaseOrderId is null)
        {
            invoice.MatchStatus = SupplierInvoiceMatchStatus.NotApplicable;
            invoice.MatchDifferenceAmount = 0m;
            invoice.MatchNote = null;
            invoice.RequiresGmApproval = false;
            return;
        }

        var poNet = await db.PurchaseOrders
            .Where(x => x.Id == invoice.PurchaseOrderId.Value)
            .Select(x => x.GrandTotal)
            .SingleAsync(cancellationToken);

        decimal? grNet = null;
        if (invoice.GoodsReceiptId is not null)
        {
            grNet = await db.GoodsReceiptItems
                .Where(x => x.GoodsReceiptId == invoice.GoodsReceiptId.Value)
                .SumAsync(x => x.AcceptedQuantity * x.PurchaseOrderItem.NetUnitPrice, cancellationToken);
            grNet = decimal.Round(grNet.Value, 2);
        }

        var invoiceNet = invoice.Subtotal;
        var diffToPo = invoiceNet - poNet;
        var diffToGr = grNet.HasValue ? invoiceNet - grNet.Value : 0m;
        var maxDiff = Math.Abs(diffToPo) >= Math.Abs(diffToGr) ? diffToPo : diffToGr;

        var baseAmount = poNet != 0m ? poNet : invoiceNet;
        var diffPercent = baseAmount != 0m
            ? Math.Abs(maxDiff) / Math.Abs(baseAmount) * 100m
            : 0m;

        invoice.MatchDifferenceAmount = decimal.Round(maxDiff, 2);

        var summary =
            $"Fatura (KDV hariç): {TurkishFormat.Amount(invoiceNet)} | Sipariş: {TurkishFormat.Amount(poNet)}" +
            (grNet.HasValue ? $" | Mal kabul: {TurkishFormat.Amount(grNet.Value)}" : "");

        if (diffPercent > settings.ThreeWayTolerancePercent)
        {
            invoice.MatchStatus = SupplierInvoiceMatchStatus.ToleranceExceeded;
            invoice.RequiresGmApproval = true;
            invoice.MatchNote =
                $"{summary} — fark %{TurkishFormat.Rate(diffPercent)}, tolerans %{TurkishFormat.Rate(settings.ThreeWayTolerancePercent)} aşıldı.";
        }
        else
        {
            invoice.MatchStatus = SupplierInvoiceMatchStatus.Matched;
            invoice.RequiresGmApproval = false;
            invoice.MatchNote = $"{summary} — tolerans içinde (%{TurkishFormat.Rate(diffPercent)}).";
        }
    }

    private async Task ValidateHeaderAsync(
        Guid companyId, Guid supplierId, Guid? projectId,
        Guid? purchaseOrderId, Guid? goodsReceiptId,
        string invoiceNumber, string currencyCode, decimal exchangeRate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            throw new ArgumentException("Tedarikçi fatura numarası zorunludur.");
        if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Trim().Length != 3)
            throw new ArgumentException("Para birimi 3 karakter olmalıdır.");
        if (exchangeRate <= 0)
            throw new ArgumentException("Kur sıfırdan büyük olmalıdır.");

        var supplier = await db.CurrentAccounts
            .SingleOrDefaultAsync(x => x.Id == supplierId && x.CompanyId == companyId, cancellationToken)
            ?? throw new ArgumentException("Tedarikçi cari kartı bulunamadı.");

        if (supplier.Status != CurrentAccountStatus.Approved)
            throw new ArgumentException("Fatura yalnızca onaylı cari karta kesilebilir.");

        if (projectId is Guid project)
        {
            var projectExists = await db.Projects
                .AnyAsync(x => x.Id == project && x.CompanyId == companyId, cancellationToken);
            if (!projectExists)
                throw new ArgumentException("Proje bulunamadı.");
        }

        if (purchaseOrderId is not null)
        {
            var poValid = await db.PurchaseOrders.AnyAsync(x =>
                x.Id == purchaseOrderId.Value &&
                x.CompanyId == companyId &&
                x.SupplierCurrentAccountId == supplierId, cancellationToken);
            if (!poValid)
                throw new ArgumentException("Sipariş bulunamadı veya bu tedarikçiye ait değil.");
        }

        if (goodsReceiptId is not null)
        {
            if (purchaseOrderId is null)
                throw new ArgumentException("Mal kabul bağlantısı için sipariş de seçilmelidir.");

            var grValid = await db.GoodsReceipts.AnyAsync(x =>
                x.Id == goodsReceiptId.Value &&
                x.PurchaseOrderId == purchaseOrderId.Value, cancellationToken);
            if (!grValid)
                throw new ArgumentException("Mal kabul bulunamadı veya seçilen siparişe ait değil.");
        }
    }

    private static SupplierInvoiceType ParseInvoiceType(int value) =>
        Enum.IsDefined(typeof(SupplierInvoiceType), value)
            ? (SupplierInvoiceType)value
            : throw new ArgumentException("Geçersiz fatura tipi.");

    /// <summary>
    /// Tipe göre iş kuralları.
    ///
    /// ALIŞ: stok girişi isteniyorsa her kalemde stok kartı ve depo
    /// gerekir — stok neyin, nereye gireceği belli olmalı. Hiçbiri
    /// verilmemişse fatura stoğa hiç uğramaz (bkz. aşağıdaki not).
    /// Mal kabule bağlı faturada stok zaten girdiği için depo İSTENMEZ
    /// ve girilse bile yok sayılır; yoksa aynı malzeme iki kez stoğa
    /// eklenirdi.
    ///
    /// GİDER: her kalemde gider hesabı gerekir; stok ve depo hiç
    /// istenmez. Hesap fişe kayıt kabul etmeli ve gider/maliyet
    /// grubunda olmalı — 320 Satıcılar'a gider yazılması engellenir.
    /// </summary>
    private async Task ValidateTypeRulesAsync(
        Guid companyId,
        SupplierInvoiceType invoiceType,
        Guid? projectId,
        Guid? defaultWarehouseId,
        Guid? goodsReceiptId,
        IReadOnlyList<SupplierInvoiceItem> items,
        CancellationToken cancellationToken)
    {
        if (invoiceType == SupplierInvoiceType.Stock)
        {
            // Stok girişi İSTENİYORSA (depo verilmiş ya da kalemlerde
            // stok kartı seçilmiş) ikisi de eksiksiz olmalı: kart
            // olmadan neyin, depo olmadan nereye gireceği belli olmaz.
            //
            // İkisi de boşsa fatura stok tarafına hiç girmez ve bugünkü
            // gibi düz bir maliyet faturası olarak işlenir. Bu kasıtlı:
            // stok kartı zorunlu tutulsaydı, hizmet/nakliye gibi
            // depoya uğramayan alışlar ve yeni ekran yayına çıkana
            // kadarki mevcut girişler tamamen bloke olurdu.
            var hasWarehouse = defaultWarehouseId is not null ||
                               items.Any(x => x.WarehouseId is not null);

            var hasInventoryItem = items.Any(x => x.InventoryItemId is not null);

            if (!hasWarehouse && !hasInventoryItem)
                return;

            var postsStock = goodsReceiptId is null;

            foreach (var item in items)
            {
                if (item.InventoryItemId is null)
                {
                    throw new ArgumentException(
                        $"Kalem {item.LineNumber}: stok girişi yapılan faturada " +
                        "her kalemde stok kartı seçilmelidir.");
                }

                if (postsStock && item.WarehouseId is null && defaultWarehouseId is null)
                {
                    throw new ArgumentException(
                        $"Kalem {item.LineNumber}: stok girişi için depo seçilmelidir.");
                }
            }

            await ValidateInventoryItemsAsync(companyId, items, cancellationToken);

            if (postsStock)
                await ValidateWarehousesAsync(companyId, defaultWarehouseId, items, cancellationToken);

            return;
        }

        // --- GİDER ---
        foreach (var item in items)
        {
            if (item.ExpenseAccountId is null)
            {
                throw new ArgumentException(
                    $"Kalem {item.LineNumber}: gider faturasında gider hesabı seçilmelidir.");
            }

            if (item.InventoryItemId is not null || item.WarehouseId is not null)
            {
                throw new ArgumentException(
                    $"Kalem {item.LineNumber}: gider faturasında stok kartı ve depo seçilemez.");
            }
        }

        await ValidateExpenseAccountsAsync(companyId, items, cancellationToken);
    }

    private async Task ValidateInventoryItemsAsync(
        Guid companyId,
        IReadOnlyList<SupplierInvoiceItem> items,
        CancellationToken cancellationToken)
    {
        var ids = items.Select(x => x.InventoryItemId).OfType<Guid>().Distinct().ToList();

        var validCount = await db.InventoryItems
            .CountAsync(x => x.CompanyId == companyId && ids.Contains(x.Id), cancellationToken);

        if (validCount != ids.Count)
            throw new ArgumentException("Kalemlerden biri bu şirkete ait olmayan bir stok kartına bağlı.");

        /*
         * ARŞİVLENMİŞ KARTA YENİ FATURA KALEMİ BAĞLANAMAZ.
         *
         * Şirket kontrolü tek başına yetmiyordu: arşivden çıkarılmış
         * bir kart aynı şirkete ait olduğu için geçiyordu. Arşivin
         * anlamı "yeni belgede kullanılmaz"; kontrol edilmezse bayrak
         * süs olur.
         *
         * MEVCUT faturalar etkilenmez: bu doğrulama yalnız kalem
         * YAZILIRKEN çalışır, işlenirken değil.
         */
        var archived = await db.InventoryItems
            .Where(x => ids.Contains(x.Id) && !x.IsActive)
            .Select(x => x.Code + " " + x.Name)
            .ToListAsync(cancellationToken);

        if (archived.Count > 0)
            throw new ArgumentException(
                "Arşivlenmiş stok kartına fatura kalemi bağlanamaz: "
                + string.Join(", ", archived));
    }

    private async Task ValidateWarehousesAsync(
        Guid companyId,
        Guid? defaultWarehouseId,
        IReadOnlyList<SupplierInvoiceItem> items,
        CancellationToken cancellationToken)
    {
        var ids = items.Select(x => x.WarehouseId).OfType<Guid>().ToList();

        if (defaultWarehouseId is Guid fallback)
            ids.Add(fallback);

        ids = ids.Distinct().ToList();

        if (ids.Count == 0)
            return;

        var validCount = await db.Warehouses
            .CountAsync(x => x.CompanyId == companyId && x.IsActive && ids.Contains(x.Id),
                cancellationToken);

        if (validCount != ids.Count)
            throw new ArgumentException("Seçilen depolardan biri bulunamadı veya aktif değil.");
    }

    /// <summary>
    /// Gider hesabı doğrulaması. Kayıt kabul etmeyen (grup) hesap ve
    /// gider/maliyet dışındaki hesaplar reddedilir: 320'ye gider yazmak
    /// borcu iki kez kaydeder ve mizanı bozar.
    /// </summary>
    private async Task ValidateExpenseAccountsAsync(
        Guid companyId,
        IReadOnlyList<SupplierInvoiceItem> items,
        CancellationToken cancellationToken)
    {
        var ids = items.Select(x => x.ExpenseAccountId).OfType<Guid>().Distinct().ToList();

        var accounts = await db.AccountingAccounts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name, x.IsPostingAllowed, x.IsActive })
            .ToListAsync(cancellationToken);

        if (accounts.Count != ids.Count)
            throw new ArgumentException("Seçilen gider hesaplarından biri bulunamadı.");

        foreach (var account in accounts)
        {
            if (!account.IsActive || !account.IsPostingAllowed)
            {
                throw new ArgumentException(
                    $"{account.Code} {account.Name} hesabına kayıt yapılamaz " +
                    "(grup hesabı veya pasif). Alt kırılımlardan birini seçin.");
            }

            if (!IsExpenseAccountCode(account.Code))
            {
                throw new ArgumentException(
                    $"{account.Code} {account.Name} bir gider/maliyet hesabı değil. " +
                    "Gider faturasında 6xx/7xx grubundan bir hesap seçilmelidir.");
            }
        }
    }

    /// <summary>
    /// Tek düzen hesap planında maliyet ve gider hesapları 6 ve 7 ile
    /// başlar (62 satışların maliyeti, 63 faaliyet giderleri, 65/66
    /// diğer gider ve finansman, 7xx maliyet hesapları).
    /// </summary>
    private static bool IsExpenseAccountCode(string code) =>
        code.StartsWith('6') || code.StartsWith('7');

    private static List<SupplierInvoiceItem> BuildItems(
        IReadOnlyCollection<SupplierInvoiceItemRequest> requests)
    {
        if (requests is null || requests.Count == 0)
            throw new ArgumentException("Faturada en az bir kalem bulunmalıdır.");

        var items = new List<SupplierInvoiceItem>();
        var lineNumber = 1;

        foreach (var request in requests)
        {
            if (string.IsNullOrWhiteSpace(request.Description))
                throw new ArgumentException($"Kalem {lineNumber}: açıklama zorunludur.");
            if (request.Quantity <= 0)
                throw new ArgumentException($"Kalem {lineNumber}: miktar sıfırdan büyük olmalıdır.");
            if (request.UnitPrice < 0)
                throw new ArgumentException($"Kalem {lineNumber}: birim fiyat negatif olamaz.");
            if (request.VatRate is < 0 or > 100)
                throw new ArgumentException($"Kalem {lineNumber}: KDV oranı 0-100 arasında olmalıdır.");

            var lineSubtotal = decimal.Round(request.Quantity * request.UnitPrice, 2);
            var vatAmount = decimal.Round(lineSubtotal * request.VatRate / 100m, 2);

            items.Add(new SupplierInvoiceItem
            {
                LineNumber = lineNumber++,
                InventoryItemId = request.InventoryItemId,
                WarehouseId = request.WarehouseId,
                ExpenseAccountId = request.ExpenseAccountId,
                CostCenterCode = Normalize(request.CostCenterCode),
                ProjectBoqItemId = request.ProjectBoqItemId,
                Description = request.Description.Trim(),
                Quantity = request.Quantity,
                Unit = string.IsNullOrWhiteSpace(request.Unit) ? "adet" : request.Unit.Trim(),
                UnitPrice = request.UnitPrice,
                VatRate = request.VatRate,
                LineSubtotal = lineSubtotal,
                VatAmount = vatAmount,
                LineTotal = lineSubtotal + vatAmount,
                PurchaseOrderItemId = request.PurchaseOrderItemId
            });
        }

        return items;
    }

    private static void ApplyItemsAndTotals(SupplierInvoice invoice, List<SupplierInvoiceItem> items)
    {
        foreach (var item in items)
            invoice.Items.Add(item);

        invoice.Subtotal = decimal.Round(items.Sum(x => x.LineSubtotal), 2);
        invoice.VatTotal = decimal.Round(items.Sum(x => x.VatAmount), 2);
        invoice.GrandTotal = invoice.Subtotal + invoice.VatTotal;
    }

    private async Task<SupplierInvoice?> LoadDetailAsync(Guid id, CancellationToken cancellationToken) =>
        await db.SupplierInvoices
            .AsNoTracking()
            .Include(x => x.Items.OrderBy(i => i.LineNumber))
                .ThenInclude(i => i.InventoryItem)
            .Include(x => x.Items)
                .ThenInclude(i => i.Warehouse)
            .Include(x => x.Items)
                .ThenInclude(i => i.ExpenseAccount)
            .Include(x => x.SupplierCurrentAccount)
            .Include(x => x.Warehouse)
            .Include(x => x.Project)
            .Include(x => x.PurchaseOrder)
            .Include(x => x.GoodsReceipt)
            .Include(x => x.AccountingVoucher)
            .Include(x => x.ReversalVoucher)
            .Include(x => x.OriginalInvoice)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<SupplierInvoiceDetailResponse> CreateReturnAsync(
        Guid originalInvoiceId,
        CreateInvoiceReturnRequest request,
        CancellationToken cancellationToken)
    {
        var original = await db.SupplierInvoices
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == originalInvoiceId, cancellationToken)
            ?? throw new KeyNotFoundException("İade edilecek fatura bulunamadı.");

        // İade ancak muhasebeleşmiş bir alıştan yapılabilir: onaylanmamış
        // faturanın ne borcu ne de stoğu oluşmuştur, tersine çevrilecek
        // bir kayıt yoktur — o faturayı düzeltmek ya da iptal etmek gerekir.
        if (original.Status != SupplierInvoiceStatus.Approved)
        {
            throw new InvalidOperationException(
                "Yalnızca onaylanmış faturadan iade yapılabilir. " +
                "Onaylanmamış fatura için iade değil düzeltme/iptal kullanın.");
        }

        if (original.IsReturn)
            throw new InvalidOperationException("İade faturasının iadesi alınamaz.");

        if (string.IsNullOrWhiteSpace(request.InvoiceNumber))
            throw new ArgumentException("İade fatura numarası zorunludur.");

        if (request.Items is null || request.Items.Count == 0)
            throw new ArgumentException("İade edilecek en az bir kalem seçilmelidir.");

        // Daha önce iade edilmiş miktarlar: aynı mal iki kez iade
        // edilemesin. Reddedilen/iptal edilen iadeler sayılmaz.
        var alreadyReturned = await db.SupplierInvoiceItems
            .AsNoTracking()
            .Where(x => x.OriginalItemId != null &&
                        x.SupplierInvoice.OriginalInvoiceId == originalInvoiceId &&
                        x.SupplierInvoice.Status != SupplierInvoiceStatus.Cancelled &&
                        x.SupplierInvoice.Status != SupplierInvoiceStatus.Rejected)
            .GroupBy(x => x.OriginalItemId!.Value)
            .Select(g => new { ItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Quantity, cancellationToken);

        var returnItems = new List<SupplierInvoiceItem>();
        var lineNumber = 1;

        foreach (var requested in request.Items)
        {
            if (requested.Quantity <= 0m)
                continue;

            var source = original.Items.SingleOrDefault(x => x.Id == requested.OriginalItemId)
                ?? throw new ArgumentException(
                    "İade edilen kalem orijinal faturada bulunamadı.");

            var returnable = source.Quantity -
                             alreadyReturned.GetValueOrDefault(source.Id, 0m);

            if (requested.Quantity > returnable)
            {
                throw new ArgumentException(
                    $"{source.Description}: en fazla {TurkishFormat.Quantity(returnable)} {source.Unit} " +
                    $"iade edilebilir (faturada {TurkishFormat.Quantity(source.Quantity)}, " +
                    $"daha önce iade edilen {TurkishFormat.Quantity(alreadyReturned.GetValueOrDefault(source.Id, 0m))}).");
            }

            // Birim fiyat ve KDV oranı orijinalden kopyalanır; iade
            // farklı fiyattan yapılsaydı ters fiş orijinali kapatmaz ve
            // cari bakiyesinde kalıntı borç kalırdı.
            var lineSubtotal = decimal.Round(requested.Quantity * source.UnitPrice, 2);
            var vatAmount = decimal.Round(lineSubtotal * source.VatRate / 100m, 2);

            returnItems.Add(new SupplierInvoiceItem
            {
                LineNumber = lineNumber++,
                OriginalItemId = source.Id,
                InventoryItemId = source.InventoryItemId,
                WarehouseId = source.WarehouseId,
                ExpenseAccountId = source.ExpenseAccountId,
                CostCenterCode = source.CostCenterCode,
                Description = source.Description,
                Quantity = requested.Quantity,
                Unit = source.Unit,
                UnitPrice = source.UnitPrice,
                VatRate = source.VatRate,
                LineSubtotal = lineSubtotal,
                VatAmount = vatAmount,
                LineTotal = lineSubtotal + vatAmount
            });
        }

        if (returnItems.Count == 0)
            throw new ArgumentException("İade edilecek en az bir kalem seçilmelidir.");

        var internalNumber = await documentNumberService.GenerateAsync(
            original.CompanyId, "SUPPLIER_INVOICE_RETURN", "AIF", cancellationToken);

        var returnInvoice = new SupplierInvoice
        {
            CompanyId = original.CompanyId,
            SupplierCurrentAccountId = original.SupplierCurrentAccountId,
            InvoiceType = original.InvoiceType,
            ProjectId = original.ProjectId,
            WarehouseId = original.WarehouseId,
            CostCenterCode = original.CostCenterCode,
            InternalNumber = internalNumber,
            InvoiceNumber = request.InvoiceNumber.Trim(),
            InvoiceDate = AsUtc(request.InvoiceDate),
            CurrencyCode = original.CurrencyCode,
            ExchangeRate = original.ExchangeRate,
            Description = Normalize(request.Description)
                ?? $"{original.InvoiceNumber} numaralı faturanın iadesi",
            Status = SupplierInvoiceStatus.Draft,
            IsReturn = true,
            OriginalInvoiceId = original.Id,
            // Sipariş/mal kabul bağlantısı taşınmaz: 3 yönlü kontrol
            // iadeye uygulanmaz, uygulanırsa fatura sipariş tutarını
            // tutmadığı için tolerans dışı görünürdü.
            MatchStatus = SupplierInvoiceMatchStatus.NotApplicable
        };

        ApplyItemsAndTotals(returnInvoice, returnItems);

        db.SupplierInvoices.Add(returnInvoice);
        await db.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(returnInvoice.Id, cancellationToken);
    }

    /// <summary>
    /// Faturanın maliyet sınıfı kırılımı: sınıf → KDV hariç tutar.
    ///
    /// ALIŞ faturası bütünüyle malzemedir. GİDER faturasında her kalem
    /// kendi hesabına göre sınıflanır; hesabı olmayan kalem genel gider
    /// sayılır (bilinmeyeni malzeme ya da işçilik saymak karşılaştırmayı
    /// sessizce yanıltırdı).
    /// </summary>
    private async Task<IReadOnlyCollection<CostAllocationLine>>
        ResolveCostClassAmountsAsync(
            SupplierInvoice invoice, CancellationToken cancellationToken)
    {
        // Kalemler her iki fatura tipinde de okunur: stok faturasında
        // sınıf sabit malzemedir ama icmal satırı kalem bazında
        // değişebilir, tek satıra indirgemek etiketi kaybettirirdi.
        var lines = await db.SupplierInvoiceItems
            .AsNoTracking()
            .Where(x => x.SupplierInvoiceId == invoice.Id)
            .Select(x => new
            {
                x.LineSubtotal,
                x.ProjectBoqItemId,
                AccountCode = x.ExpenseAccount != null ? x.ExpenseAccount.Code : null
            })
            .ToListAsync(cancellationToken);

        if (lines.Count == 0)
        {
            var fallbackClass = invoice.InvoiceType == SupplierInvoiceType.Stock
                ? ProjectCostClass.Material
                : ProjectCostClass.Overhead;

            return [new CostAllocationLine(fallbackClass, null, invoice.Subtotal)];
        }

        return lines
            .GroupBy(x => new
            {
                CostClass = invoice.InvoiceType == SupplierInvoiceType.Stock
                    ? ProjectCostClass.Material
                    : Projects.ProjectCostClassifier.ForExpenseAccount(x.AccountCode),
                x.ProjectBoqItemId
            })
            .Select(g => new CostAllocationLine(
                g.Key.CostClass, g.Key.ProjectBoqItemId, g.Sum(x => x.LineSubtotal)))
            .Where(x => x.Amount != 0m)
            .ToList();
    }

    /// <summary>
    /// Faturadan çıkan tek bir maliyet satırı: sınıf, varsa icmal
    /// satırı ve tutar.
    /// </summary>
    private sealed record CostAllocationLine(
        ProjectCostClass CostClass,
        Guid? ProjectBoqItemId,
        decimal Amount);

    /// <summary>
    /// Kalem bazında iade edilebilir kalan miktar. İade faturasında ve
    /// onaylanmamış faturada boş döner: ikisinden de iade yapılamaz.
    /// </summary>
    private async Task<IReadOnlyCollection<InvoiceReturnableItemResponse>>
        LoadReturnableItemsAsync(SupplierInvoice invoice, CancellationToken cancellationToken)
    {
        if (invoice.IsReturn || invoice.Status != SupplierInvoiceStatus.Approved)
            return [];

        var returned = await db.SupplierInvoiceItems
            .AsNoTracking()
            .Where(x => x.OriginalItemId != null &&
                        x.SupplierInvoice.OriginalInvoiceId == invoice.Id &&
                        x.SupplierInvoice.Status != SupplierInvoiceStatus.Cancelled &&
                        x.SupplierInvoice.Status != SupplierInvoiceStatus.Rejected)
            .GroupBy(x => x.OriginalItemId!.Value)
            .Select(g => new { ItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Quantity, cancellationToken);

        return invoice.Items
            .OrderBy(x => x.LineNumber)
            .Select(item =>
            {
                var returnedQuantity = returned.GetValueOrDefault(item.Id, 0m);

                return new InvoiceReturnableItemResponse(
                    item.Id,
                    item.Description,
                    item.Unit,
                    item.Quantity,
                    returnedQuantity,
                    item.Quantity - returnedQuantity);
            })
            .ToList();
    }

    /// <summary>
    /// Faturayı ödeyen çek payları. Ayrı bir "ödeme" defteri tutulmuyor;
    /// tek kaynak çek dağılımı — böylece çekte gördüğünüzle faturada
    /// gördüğünüz birbirini tutmak zorunda kalır.
    /// </summary>
    private async Task<IReadOnlyCollection<InvoiceChequePaymentResponse>>
        LoadChequePaymentsAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        await db.ChequeAllocations
            .AsNoTracking()
            .Where(x => x.SupplierInvoiceId == invoiceId)
            .OrderBy(x => x.Cheque.DueDate)
            .Select(x => new InvoiceChequePaymentResponse(
                x.ChequeId,
                x.Cheque.InternalNumber,
                x.Cheque.ChequeNumber,
                x.Cheque.DueDate,
                (int)x.Cheque.Status,
                ChequeService.StatusName(x.Cheque.Status),
                x.Amount))
            .ToListAsync(cancellationToken);

    private static SupplierInvoiceDetailResponse MapDetail(
        SupplierInvoice invoice,
        IReadOnlyCollection<InvoiceChequePaymentResponse>? chequePayments = null,
        IReadOnlyCollection<InvoiceReturnableItemResponse>? returnableItems = null) =>
        new(
            invoice.Id, invoice.CompanyId, invoice.InternalNumber, invoice.InvoiceNumber,
            invoice.InvoiceDate, invoice.DueDate,
            invoice.SupplierCurrentAccountId, invoice.SupplierCurrentAccount.Title,
            invoice.ProjectId, invoice.Project?.Code, invoice.Project?.Name,
            (int)invoice.InvoiceType,
            invoice.InvoiceType == SupplierInvoiceType.Expense ? "Gider" : "Alış (Stok)",
            invoice.CostCenterCode,
            invoice.WarehouseId, invoice.Warehouse?.Name,
            invoice.PurchaseOrderId, invoice.PurchaseOrder?.OrderNumber,
            invoice.GoodsReceiptId, invoice.GoodsReceipt?.ReceiptNumber,
            invoice.CurrencyCode, invoice.ExchangeRate,
            invoice.Subtotal, invoice.VatTotal, invoice.GrandTotal,
            invoice.Description,
            (int)invoice.Status, (int)invoice.MatchStatus,
            invoice.MatchDifferenceAmount, invoice.MatchNote, invoice.RequiresGmApproval,
            invoice.SubmittedAtUtc, invoice.ApprovedAtUtc, invoice.RejectedAtUtc,
            invoice.RejectionReason,
            invoice.AccountingVoucherId, invoice.AccountingVoucher?.VoucherNumber,
            invoice.Items.Select(item => new SupplierInvoiceItemResponse(
                item.Id, item.LineNumber, item.Description, item.Quantity, item.Unit,
                item.UnitPrice, item.VatRate, item.LineSubtotal, item.VatAmount,
                item.LineTotal, item.PurchaseOrderItemId,
                item.InventoryItemId,
                item.InventoryItem?.Code,
                item.InventoryItem?.Name,
                item.WarehouseId,
                item.Warehouse?.Name,
                item.ExpenseAccountId,
                item.ExpenseAccount?.Code,
                item.ExpenseAccount?.Name,
                item.CostCenterCode)).ToList(),
            chequePayments ?? [],
            chequePayments?.Sum(x => x.AllocatedAmount) ?? 0m,
            invoice.GrandTotal - (chequePayments?.Sum(x => x.AllocatedAmount) ?? 0m),
            invoice.IsReturn,
            invoice.OriginalInvoiceId,
            invoice.OriginalInvoice?.InvoiceNumber,
            invoice.ReversalVoucherId,
            invoice.ReversalVoucher?.VoucherNumber,
            returnableItems ?? []);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? AppendNote(string? existing, string addition) =>
        string.IsNullOrWhiteSpace(existing) ? addition : $"{existing} {addition}";

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
