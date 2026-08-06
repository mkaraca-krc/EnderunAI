using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.DocumentNumbers;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Accounting;

public interface ISalesInvoiceService
{
    Task<IReadOnlyCollection<SalesInvoiceListItemResponse>> GetAllAsync(
        Guid? companyId, int? status, Guid? projectId, Guid? customerId,
        string? search, CancellationToken cancellationToken);

    Task<SalesInvoiceDetailResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<SalesInvoiceDetailResponse> CreateAsync(
        CreateSalesInvoiceRequest request, CancellationToken cancellationToken);

    Task<SalesInvoiceDetailResponse> UpdateAsync(
        Guid id, UpdateSalesInvoiceRequest request, CancellationToken cancellationToken);

    Task<SalesInvoiceActionResponse> PostAsync(Guid id, CancellationToken cancellationToken);

    Task<SalesInvoiceActionResponse> CancelAsync(
        Guid id, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Müşteriden mal iadesi. Orijinal faturaya bağlı taslak iade
    /// faturası üretir; kesinleştiğinde fiş 610 Satıştan İadeler + 391
    /// borç / 120 alacak olarak aynalanır. Kısmi iade desteklenir.
    /// </summary>
    Task<SalesInvoiceDetailResponse> CreateReturnAsync(
        Guid originalInvoiceId,
        CreateInvoiceReturnRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Hakediş dışı satış faturaları. Taslak olarak açılır, kesinleştirmede
/// gelir fişi (120/600/391) üretir ve müşteri carisine borç yazar.
/// </summary>
public sealed class SalesInvoiceService(
    AppDbContext db,
    IDocumentNumberService documentNumberService,
    IAccountingIntegrationService accountingIntegration,
    ICurrentUserService currentUser,
    Market.IInvoiceExchangeRateResolver rateResolver) : ISalesInvoiceService
{
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

    public async Task<IReadOnlyCollection<SalesInvoiceListItemResponse>> GetAllAsync(
        Guid? companyId, int? status, Guid? projectId, Guid? customerId,
        string? search, CancellationToken cancellationToken)
    {
        var query = db.SalesInvoices.AsNoTracking();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);
        if (status.HasValue)
            query = query.Where(x => (int)x.Status == status.Value);
        if (projectId.HasValue)
            query = query.Where(x => x.ProjectId == projectId.Value);
        if (customerId.HasValue)
            query = query.Where(x => x.CustomerCurrentAccountId == customerId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                EF.Functions.ILike(x.InternalNumber, $"%{term}%") ||
                (x.OfficialInvoiceNumber != null &&
                 EF.Functions.ILike(x.OfficialInvoiceNumber, $"%{term}%")) ||
                EF.Functions.ILike(x.CustomerCurrentAccount.Title, $"%{term}%"));
        }

        return await query
            .OrderByDescending(x => x.InvoiceDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new SalesInvoiceListItemResponse(
                x.Id, x.InternalNumber, x.OfficialInvoiceNumber, x.InvoiceDate,
                x.CustomerCurrentAccountId, x.CustomerCurrentAccount.Title,
                x.ProjectId,
                x.Project != null ? x.Project.Code : null,
                x.Project != null ? x.Project.Name : null,
                x.CurrencyCode, x.Subtotal, x.VatTotal, x.WithholdingAmount,
                x.GrandTotal, x.NetReceivableAmount,
                (int)x.Status, x.RequiresManualReview,
                x.ParseSource != null ? (int)x.ParseSource : null,
                x.AccountingVoucher != null ? x.AccountingVoucher.VoucherNumber : null,
                x.IsReturn))
            .ToListAsync(cancellationToken);
    }

    public async Task<SalesInvoiceDetailResponse> GetByIdAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var invoice = await LoadDetailAsync(id, cancellationToken);

        return invoice is null
            ? throw new KeyNotFoundException("Satış faturası bulunamadı.")
            : MapDetail(invoice);
    }

    public async Task<SalesInvoiceDetailResponse> CreateAsync(
        CreateSalesInvoiceRequest request, CancellationToken cancellationToken)
    {
        await ValidateHeaderAsync(
            request.CompanyId, request.CustomerCurrentAccountId, request.ProjectId,
            request.OfficialInvoiceNumber, request.CurrencyCode, request.ExchangeRate,
            null, cancellationToken);

        var items = BuildItems(request.Items);

        var internalNumber = await documentNumberService.GenerateAsync(
            request.CompanyId, "SALES_INVOICE", "SAT", cancellationToken);

        // Dövizli faturada kur girilmemişse fatura tarihinin TCMB döviz
        // alışı kullanılır; kur yoksa fatura kaydedilmez.
        var rate = await ResolveRateAsync(
            request.CurrencyCode, AsUtc(request.InvoiceDate), request.ExchangeRate,
            cancellationToken);

        var invoice = new SalesInvoice
        {
            CompanyId = request.CompanyId,
            CustomerCurrentAccountId = request.CustomerCurrentAccountId,
            ProjectId = request.ProjectId,
            InternalNumber = internalNumber,
            OfficialInvoiceNumber = Normalize(request.OfficialInvoiceNumber),
            InvoiceDate = AsUtc(request.InvoiceDate),
            DueDate = request.DueDate.HasValue ? AsUtc(request.DueDate.Value) : null,
            CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant(),
            ExchangeRate = rate,
            Description = Normalize(request.Description),
            Notes = Normalize(request.Notes),
            ParseSource = EInvoiceParseSource.Manual,
            Status = SalesInvoiceStatus.Draft
        };

        ApplyItemsAndTotals(invoice, items, request.WithholdingAmount);

        db.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(invoice.Id, cancellationToken);
    }

    public async Task<SalesInvoiceDetailResponse> UpdateAsync(
        Guid id, UpdateSalesInvoiceRequest request, CancellationToken cancellationToken)
    {
        var invoice = await db.SalesInvoices
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Satış faturası bulunamadı.");

        if (invoice.Status != SalesInvoiceStatus.Draft)
            throw new InvalidOperationException(
                "Yalnızca taslak satış faturaları güncellenebilir.");

        await ValidateHeaderAsync(
            invoice.CompanyId, request.CustomerCurrentAccountId, request.ProjectId,
            request.OfficialInvoiceNumber, request.CurrencyCode, request.ExchangeRate,
            invoice.Id, cancellationToken);

        var items = BuildItems(request.Items);

        invoice.CustomerCurrentAccountId = request.CustomerCurrentAccountId;
        invoice.ProjectId = request.ProjectId;
        invoice.OfficialInvoiceNumber = Normalize(request.OfficialInvoiceNumber);
        invoice.InvoiceDate = AsUtc(request.InvoiceDate);
        invoice.DueDate = request.DueDate.HasValue ? AsUtc(request.DueDate.Value) : null;
        invoice.CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant();
        invoice.ExchangeRate = await ResolveRateAsync(
            request.CurrencyCode, invoice.InvoiceDate, request.ExchangeRate,
            cancellationToken);
        invoice.Description = Normalize(request.Description);
        invoice.Notes = Normalize(request.Notes);
        invoice.UpdatedAtUtc = DateTime.UtcNow;

        db.SalesInvoiceItems.RemoveRange(invoice.Items);
        invoice.Items.Clear();
        ApplyItemsAndTotals(invoice, items, request.WithholdingAmount);

        await db.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(invoice.Id, cancellationToken);
    }

    public async Task<SalesInvoiceActionResponse> PostAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var invoice = await db.SalesInvoices
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Satış faturası bulunamadı.");

        if (invoice.Status != SalesInvoiceStatus.Draft)
            throw new InvalidOperationException(
                "Yalnızca taslak satış faturaları kesinleştirilebilir.");

        if (invoice.Items.Count == 0)
            throw new InvalidOperationException("Faturada kalem yok.");

        if (string.IsNullOrWhiteSpace(invoice.OfficialInvoiceNumber))
            throw new InvalidOperationException(
                "Resmi fatura numarası girilmeden fatura kesinleştirilemez.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var voucherId = await accountingIntegration.CreateSalesInvoiceVoucherAsync(
            invoice, cancellationToken, reverse: invoice.IsReturn);

        invoice.Status = SalesInvoiceStatus.Posted;
        invoice.AccountingVoucherId = voucherId;
        invoice.PostedByUserId = currentUser.UserId;
        invoice.PostedAtUtc = DateTime.UtcNow;
        invoice.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new SalesInvoiceActionResponse(
            invoice.Id, invoice.InternalNumber, (int)invoice.Status,
            invoice.IsReturn
                ? "İade faturası kesinleşti; 610 Satıştan İadeler fişi oluşturuldu ve " +
                  "müşteri carisindeki alacak azaltıldı."
                : "Satış faturası kesinleşti; gelir fişi oluşturuldu ve müşteri carisine borç yazıldı.");
    }

    public async Task<SalesInvoiceActionResponse> CancelAsync(
        Guid id, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("İptal gerekçesi zorunludur.");

        var invoice = await db.SalesInvoices
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Satış faturası bulunamadı.");

        if (invoice.Status == SalesInvoiceStatus.Cancelled)
            throw new InvalidOperationException("Fatura zaten iptal edilmiş.");

        // Kesinleşmiş faturanın fişi SİLİNMEZ; ters kaydı üretilir ve
        // ikisi de defterde durur. Elle ters kayıt beklemek, iptalin
        // muhasebeye hiç yansımadan unutulmasına yol açıyordu.
        if (invoice.Status == SalesInvoiceStatus.Posted)
        {
            if (invoice.AccountingVoucherId is null)
            {
                throw new InvalidOperationException(
                    "Kesinleşmiş faturanın fişi bulunamadı; iptal ters kayıt " +
                    "üretemeyeceği için durduruldu.");
            }

            var hasReturn = await db.SalesInvoices.AnyAsync(
                x => x.OriginalInvoiceId == invoice.Id &&
                     x.Status != SalesInvoiceStatus.Cancelled,
                cancellationToken);

            if (hasReturn)
            {
                throw new InvalidOperationException(
                    "Bu faturaya bağlı iade faturası var. Önce iadeyi iptal edin.");
            }

            var hasChequeAllocation = await db.ChequeAllocations.AnyAsync(
                x => x.SalesInvoiceId == invoice.Id, cancellationToken);

            if (hasChequeAllocation)
            {
                throw new InvalidOperationException(
                    "Bu faturaya bağlı çek tahsilatı var. Önce çek dağılımından " +
                    "faturayı çıkarın.");
            }

            invoice.ReversalVoucherId = await accountingIntegration.CreateReversalVoucherAsync(
                invoice.AccountingVoucherId.Value,
                reason.Trim(),
                DateTime.UtcNow.Date,
                cancellationToken);
        }

        invoice.Status = SalesInvoiceStatus.Cancelled;
        invoice.CancelledAtUtc = DateTime.UtcNow;
        invoice.CancellationReason = reason.Trim();
        invoice.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return new SalesInvoiceActionResponse(
            invoice.Id, invoice.InternalNumber, (int)invoice.Status,
            "Satış faturası iptal edildi.");
    }

    private async Task ValidateHeaderAsync(
        Guid companyId,
        Guid customerId,
        Guid? projectId,
        string? officialNumber,
        string currencyCode,
        decimal exchangeRate,
        Guid? excludeInvoiceId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
            throw new ArgumentException("Para birimi zorunludur.");

        if (exchangeRate <= 0m)
            throw new ArgumentException("Kur sıfırdan büyük olmalıdır.");

        var customerExists = await db.CurrentAccounts.AnyAsync(
            x => x.Id == customerId && x.CompanyId == companyId, cancellationToken);

        if (!customerExists)
            throw new ArgumentException("Müşteri carisi bulunamadı.");

        if (projectId is Guid project)
        {
            var projectExists = await db.Projects.AnyAsync(
                x => x.Id == project && x.CompanyId == companyId, cancellationToken);

            if (!projectExists)
                throw new ArgumentException("Proje bulunamadı.");
        }

        if (string.IsNullOrWhiteSpace(officialNumber))
            return;

        // Aynı müşteriye aynı resmi numarayla ikinci fatura girilmemeli;
        // e-fatura içe aktarma ile elle giriş çakışabilir.
        var number = officialNumber.Trim();

        var duplicate = await db.SalesInvoices.AnyAsync(
            x => x.CompanyId == companyId &&
                 x.CustomerCurrentAccountId == customerId &&
                 x.OfficialInvoiceNumber == number &&
                 (excludeInvoiceId == null || x.Id != excludeInvoiceId),
            cancellationToken);

        if (duplicate)
            throw new ArgumentException(
                $"Bu müşteriye '{number}' numaralı fatura zaten kayıtlı.");
    }

    private static List<SalesInvoiceItem> BuildItems(
        IReadOnlyCollection<SalesInvoiceItemRequest> requests)
    {
        if (requests is null || requests.Count == 0)
            throw new ArgumentException("En az bir fatura kalemi girilmelidir.");

        var items = new List<SalesInvoiceItem>();
        var lineNumber = 1;

        foreach (var request in requests)
        {
            if (string.IsNullOrWhiteSpace(request.Description))
                throw new ArgumentException("Kalem açıklaması zorunludur.");

            if (request.Quantity <= 0m)
                throw new ArgumentException(
                    $"'{request.Description}' kaleminde miktar sıfırdan büyük olmalıdır.");

            if (request.UnitPrice < 0m)
                throw new ArgumentException(
                    $"'{request.Description}' kaleminde birim fiyat negatif olamaz.");

            if (request.VatRate is < 0m or > 100m)
                throw new ArgumentException(
                    $"'{request.Description}' kaleminde KDV oranı 0-100 aralığında olmalıdır.");

            var subtotal = decimal.Round(
                request.Quantity * request.UnitPrice, 2, MidpointRounding.AwayFromZero);
            var vat = decimal.Round(
                subtotal * request.VatRate / 100m, 2, MidpointRounding.AwayFromZero);

            items.Add(new SalesInvoiceItem
            {
                LineNumber = lineNumber++,
                Description = request.Description.Trim(),
                Unit = (request.Unit ?? string.Empty).Trim(),
                Quantity = request.Quantity,
                UnitPrice = request.UnitPrice,
                VatRate = request.VatRate,
                LineSubtotal = subtotal,
                VatAmount = vat,
                LineTotal = subtotal + vat
            });
        }

        return items;
    }

    public async Task<SalesInvoiceDetailResponse> CreateReturnAsync(
        Guid originalInvoiceId,
        CreateInvoiceReturnRequest request,
        CancellationToken cancellationToken)
    {
        var original = await db.SalesInvoices
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == originalInvoiceId, cancellationToken)
            ?? throw new KeyNotFoundException("İade edilecek satış faturası bulunamadı.");

        // Kesinleşmemiş faturanın geliri de alacağı da oluşmamıştır;
        // tersine çevrilecek bir kayıt yok. O fatura düzeltilir ya da
        // iptal edilir.
        if (original.Status != SalesInvoiceStatus.Posted)
        {
            throw new InvalidOperationException(
                "Yalnızca kesinleşmiş faturadan iade yapılabilir. " +
                "Kesinleşmemiş fatura için iade değil düzeltme/iptal kullanın.");
        }

        if (original.IsReturn)
            throw new InvalidOperationException("İade faturasının iadesi alınamaz.");

        if (request.Items is null || request.Items.Count == 0)
            throw new ArgumentException("İade edilecek en az bir kalem seçilmelidir.");

        var alreadyReturned = await db.SalesInvoiceItems
            .AsNoTracking()
            .Where(x => x.OriginalItemId != null &&
                        x.SalesInvoice.OriginalInvoiceId == originalInvoiceId &&
                        x.SalesInvoice.Status != SalesInvoiceStatus.Cancelled)
            .GroupBy(x => x.OriginalItemId!.Value)
            .Select(g => new { ItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Quantity, cancellationToken);

        var returnItems = new List<SalesInvoiceItem>();
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
                    $"{source.Description}: en fazla {returnable:N4} {source.Unit} " +
                    $"iade edilebilir (faturada {source.Quantity:N4}, " +
                    $"daha önce iade edilen {alreadyReturned.GetValueOrDefault(source.Id, 0m):N4}).");
            }

            var lineSubtotal = decimal.Round(requested.Quantity * source.UnitPrice, 2);
            var vatAmount = decimal.Round(lineSubtotal * source.VatRate / 100m, 2);

            returnItems.Add(new SalesInvoiceItem
            {
                LineNumber = lineNumber++,
                OriginalItemId = source.Id,
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
            original.CompanyId, "SALES_INVOICE_RETURN", "SIF", cancellationToken);

        var returnInvoice = new SalesInvoice
        {
            CompanyId = original.CompanyId,
            CustomerCurrentAccountId = original.CustomerCurrentAccountId,
            ProjectId = original.ProjectId,
            InternalNumber = internalNumber,
            OfficialInvoiceNumber = Normalize(request.InvoiceNumber),
            InvoiceDate = AsUtc(request.InvoiceDate),
            CurrencyCode = original.CurrencyCode,
            ExchangeRate = original.ExchangeRate,
            Description = Normalize(request.Description)
                ?? $"{original.OfficialInvoiceNumber ?? original.InternalNumber} " +
                   "numaralı faturanın iadesi",
            Status = SalesInvoiceStatus.Draft,
            ParseSource = EInvoiceParseSource.Manual,
            IsReturn = true,
            OriginalInvoiceId = original.Id
        };

        // Tevkifat, iade edilen paya oranla taşınır: orijinalde tevkifat
        // varsa iadede de olmalı, yoksa alacak tutarı tutmaz.
        var returnedRatio = original.Subtotal == 0m
            ? 0m
            : returnItems.Sum(x => x.LineSubtotal) / original.Subtotal;

        var withholding = decimal.Round(
            original.WithholdingAmount * returnedRatio, 2, MidpointRounding.AwayFromZero);

        ApplyItemsAndTotals(returnInvoice, returnItems, withholding);

        db.SalesInvoices.Add(returnInvoice);
        await db.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(returnInvoice.Id, cancellationToken);
    }

    private static void ApplyItemsAndTotals(
        SalesInvoice invoice, List<SalesInvoiceItem> items, decimal withholdingAmount)
    {
        foreach (var item in items)
            invoice.Items.Add(item);

        invoice.Subtotal = items.Sum(x => x.LineSubtotal);
        invoice.VatTotal = items.Sum(x => x.VatAmount);
        invoice.GrandTotal = invoice.Subtotal + invoice.VatTotal;

        var withholding = decimal.Round(withholdingAmount, 2, MidpointRounding.AwayFromZero);

        if (withholding < 0m)
            throw new ArgumentException("Tevkifat tutarı negatif olamaz.");

        // Tevkifat KDV'nin bir kısmıdır; KDV'yi aşamaz.
        if (withholding > invoice.VatTotal)
            throw new ArgumentException(
                $"Tevkifat ({withholding:N2}) hesaplanan KDV'den ({invoice.VatTotal:N2}) büyük olamaz.");

        invoice.WithholdingAmount = withholding;
        invoice.NetReceivableAmount = invoice.GrandTotal - withholding;
    }

    private async Task<SalesInvoice?> LoadDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        return await db.SalesInvoices
            .AsNoTracking()
            .Include(x => x.CustomerCurrentAccount)
            .Include(x => x.Project)
            .Include(x => x.AccountingVoucher)
            .Include(x => x.ReversalVoucher)
            .Include(x => x.OriginalInvoice)
            .Include(x => x.Items.OrderBy(item => item.LineNumber))
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    private static SalesInvoiceDetailResponse MapDetail(SalesInvoice invoice) =>
        new(
            invoice.Id,
            invoice.CompanyId,
            invoice.InternalNumber,
            invoice.OfficialInvoiceNumber,
            invoice.InvoiceDate,
            invoice.DueDate,
            invoice.CustomerCurrentAccountId,
            invoice.CustomerCurrentAccount.Title,
            invoice.ProjectId,
            invoice.Project?.Code,
            invoice.Project?.Name,
            invoice.CurrencyCode,
            invoice.ExchangeRate,
            invoice.Subtotal,
            invoice.VatTotal,
            invoice.WithholdingAmount,
            invoice.GrandTotal,
            invoice.NetReceivableAmount,
            invoice.Description,
            invoice.Notes,
            (int)invoice.Status,
            invoice.PostedAtUtc,
            invoice.CancelledAtUtc,
            invoice.CancellationReason,
            invoice.RequiresManualReview,
            invoice.ParseSource is null ? null : (int)invoice.ParseSource,
            !string.IsNullOrWhiteSpace(invoice.SourceXmlPath),
            invoice.AccountingVoucherId,
            invoice.AccountingVoucher?.VoucherNumber,
            invoice.Items
                .OrderBy(x => x.LineNumber)
                .Select(x => new SalesInvoiceItemResponse(
                    x.Id, x.LineNumber, x.Description, x.Quantity, x.Unit,
                    x.UnitPrice, x.VatRate, x.LineSubtotal, x.VatAmount, x.LineTotal))
                .ToList(),
            invoice.IsReturn,
            invoice.OriginalInvoiceId,
            invoice.OriginalInvoice?.OfficialInvoiceNumber
                ?? invoice.OriginalInvoice?.InternalNumber,
            invoice.ReversalVoucherId,
            invoice.ReversalVoucher?.VoucherNumber);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
}
