using EnderunAI.Api.Contracts.Rfq;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.GoodsReceipt;
using EnderunAI.Api.Models.PurchaseOrder;
using EnderunAI.Api.Models.Rfq;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.DocumentNumbers;
using EnderunAI.Api.Services.Procurement;
using Microsoft.EntityFrameworkCore;
using RfqEntity = EnderunAI.Api.Models.Rfq.Rfq;

namespace EnderunAI.Api.Services.Rfq;

public sealed class RfqService(
    AppDbContext db,
    IDocumentNumberService documentNumbers,
    ICurrentDataScopeService dataScope) : IRfqService
{
    public async Task<IReadOnlyList<RfqListItemResponse>> GetAllAsync(
        Guid? companyId,
        int? status,
        CancellationToken cancellationToken)
    {
        var scope = await GetScopeAsync(cancellationToken);
        var query = db.Rfqs.AsNoTracking().ApplyScope(scope);

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (status.HasValue)
        {
            if (!Enum.IsDefined(typeof(RfqStatus), status.Value))
                throw new ProcurementValidationException("Geçersiz RFQ durumu.");

            query = query.Where(x => x.Status == (RfqStatus)status.Value);
        }

        return await query
            .OrderByDescending(x => x.IssueDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new RfqListItemResponse(
                x.Id,
                x.CompanyId,
                x.PurchaseRequestId,
                x.PurchaseRequest.RequestNumber,
                x.RfqNumber,
                x.Title,
                x.IssueDate,
                x.ResponseDeadline,
                (int)x.Status,
                x.Currency,
                x.Items.Count,
                x.Suppliers.Count,
                x.Suppliers.Count(s => s.Quotations.Any())))
            .ToListAsync(cancellationToken);
    }

    public async Task<RfqDetailResponse> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var scope = await GetScopeAsync(cancellationToken);

        var result = await db.Rfqs
            .AsNoTracking()
            .AsSplitQuery()
            .ApplyScope(scope)
            .Where(x => x.Id == id)
            .Select(x => new RfqDetailResponse(
                x.Id,
                x.CompanyId,
                x.PurchaseRequestId,
                x.PurchaseRequest.RequestNumber,
                x.RfqNumber,
                x.Title,
                x.IssueDate,
                x.ResponseDeadline,
                (int)x.Status,
                x.Currency,
                x.Description,
                x.Notes,
                x.Items
                    .OrderBy(i => i.LineNumber)
                    .Select(i => new RfqItemResponse(
                        i.Id,
                        i.LineNumber,
                        i.MaterialDescription,
                        i.Quantity,
                        i.Unit,
                        i.RequestedDeliveryDate,
                        i.Notes))
                    .ToList(),
                x.Suppliers
                    .OrderBy(s => s.SupplierCurrentAccount.Title)
                    .Select(s => new RfqSupplierResponse(
                        s.Id,
                        s.SupplierCurrentAccountId,
                        s.SupplierCurrentAccount.Code,
                        s.SupplierCurrentAccount.Title,
                        (int)s.Status,
                        s.SentAtUtc,
                        s.RespondedAtUtc,
                        s.ContactName,
                        s.ContactEmail,
                        s.Quotations
                            .OrderByDescending(q => q.QuotationDate)
                            .Select(q => (Guid?)q.Id)
                            .FirstOrDefault(),
                        s.Quotations
                            .OrderByDescending(q => q.QuotationDate)
                            .Select(q => (decimal?)q.GrandTotal)
                            .FirstOrDefault(),
                        s.Quotations
                            .OrderByDescending(q => q.QuotationDate)
                            .Select(q => q.DeliveryDays)
                            .FirstOrDefault(),
                        s.Quotations
                            .OrderByDescending(q => q.QuotationDate)
                            .Select(q => q.PaymentTerm)
                            .FirstOrDefault()))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);

        return result ?? throw new ProcurementNotFoundException("RFQ bulunamadı.");
    }

    public async Task<CreateRfqResponse> CreateFromPurchaseRequestAsync(
        Guid purchaseRequestId,
        CreateRfqRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ProcurementValidationException("RFQ başlığı zorunludur.");

        if (request.Title.Trim().Length > 300)
            throw new ProcurementValidationException("RFQ başlığı 300 karakteri aşamaz.");

        var supplierIds = request.SupplierCurrentAccountIds
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToArray();

        if (supplierIds.Length == 0)
            throw new ProcurementValidationException("En az bir tedarikçi seçilmelidir.");

        var scope = await GetScopeAsync(cancellationToken);
        var purchaseRequest = await db.PurchaseRequests
            .ApplyScope(scope)
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == purchaseRequestId, cancellationToken);

        if (purchaseRequest is null)
            throw new ProcurementNotFoundException("Satın alma talebi bulunamadı.");

        if (purchaseRequest.Status != PurchaseRequestStatus.Approved)
            throw new ProcurementValidationException("Yalnız onaylı satın alma talebi RFQ'ya dönüştürülebilir.");

        if (purchaseRequest.Items.Count == 0)
            throw new ProcurementValidationException("Satın alma talebinde kalem bulunmuyor.");

        if (await db.Rfqs.AnyAsync(
                x => x.PurchaseRequestId == purchaseRequestId,
                cancellationToken))
        {
            throw new ProcurementValidationException("Bu satın alma talebi için daha önce RFQ oluşturulmuş.");
        }

        var suppliers = await db.CurrentAccounts
            .AsNoTracking()
            .Where(x =>
                supplierIds.Contains(x.Id) &&
                x.CompanyId == purchaseRequest.CompanyId &&
                x.Status == CurrentAccountStatus.Approved &&
                (x.Roles & CurrentAccountRoles.Supplier) != 0)
            .Select(x => new
            {
                x.Id,
                x.AuthorizedPerson,
                x.Email
            })
            .ToListAsync(cancellationToken);

        if (suppliers.Count != supplierIds.Length)
            throw new ProcurementValidationException("Seçilen tedarikçilerden biri geçersiz veya farklı şirkete ait.");

        var rfqNumber = await documentNumbers.GenerateAsync(
            purchaseRequest.CompanyId,
            "RFQ",
            "RFQ",
            cancellationToken);

        var entity = new RfqEntity
        {
            CompanyId = purchaseRequest.CompanyId,
            PurchaseRequestId = purchaseRequest.Id,
            RfqNumber = rfqNumber,
            Title = request.Title.Trim(),
            IssueDate = DateTime.UtcNow,
            ResponseDeadline = request.ResponseDeadline.AsUtc(),
            Status = RfqStatus.Draft,
            Currency = ProcurementServiceSupport.CurrencyOrTry(request.Currency, 3),
            Description = request.Description?.Trim(),
            Notes = request.Notes?.Trim(),
            Items = purchaseRequest.Items
                .OrderBy(x => x.LineNumber)
                .Select(x => new RfqItem
                {
                    PurchaseRequestItemId = x.Id,
                    LineNumber = x.LineNumber,
                    // Poz bağı talepten taşınır; stok kartı bağıyla
                    // aynı mantık.
                    EngineeringPositionId = x.EngineeringPositionId,
                    MaterialDescription = x.MaterialDescription,
                    // İSTENEN marka talepten taşınır. Tedarikçinin
                    // teklifte vereceği marka ayrı bir alan; ikisi
                    // karıştırılmaz.
                    RequestedBrand = x.RequestedBrand,
                    BrandIrrelevant = x.BrandIrrelevant,
                    Quantity = x.Quantity,
                    Unit = x.Unit,
                    RequestedDeliveryDate = x.RequestedDeliveryDate.AsUtc(),
                    Notes = x.Notes
                })
                .ToList(),
            Suppliers = suppliers
                .Select(x => new RfqSupplier
                {
                    SupplierCurrentAccountId = x.Id,
                    Status = RfqSupplierStatus.Pending,
                    ContactName = x.AuthorizedPerson,
                    ContactEmail = x.Email
                })
                .ToList()
        };

        purchaseRequest.Status = PurchaseRequestStatus.Quotation;
        db.Rfqs.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateRfqResponse(
            entity.Id,
            entity.RfqNumber,
            entity.Items.Count,
            entity.Suppliers.Count);
    }

    public async Task SendAsync(Guid id, CancellationToken cancellationToken)
    {
        var scope = await GetScopeAsync(cancellationToken);
        var entity = await db.Rfqs
            .ApplyScope(scope)
            .Include(x => x.Items)
            .Include(x => x.Suppliers)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            throw new ProcurementNotFoundException("RFQ bulunamadı.");

        if (entity.Status != RfqStatus.Draft)
            throw new ProcurementValidationException("Yalnız taslak RFQ gönderilebilir.");

        if (entity.Items.Count == 0 || entity.Suppliers.Count == 0)
            throw new ProcurementValidationException("RFQ kalem ve tedarikçi içermelidir.");

        var now = DateTime.UtcNow;
        entity.Status = RfqStatus.Sent;
        foreach (var supplier in entity.Suppliers)
        {
            supplier.Status = RfqSupplierStatus.Sent;
            supplier.SentAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveQuotationAsync(
        Guid rfqId,
        Guid rfqSupplierId,
        SaveQuotationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0)
            throw new ProcurementValidationException("Teklif en az bir kalem içermelidir.");

        if (request.Items.Any(x =>
                x.Quantity <= 0 ||
                x.UnitPrice < 0 ||
                x.DiscountRate < 0 ||
                x.DiscountRate > 100 ||
                x.DeliveryDays < 0))
        {
            throw new ProcurementValidationException("Teklif kalem değerleri geçersiz.");
        }

        var scope = await GetScopeAsync(cancellationToken);
        var scopedRfqIds = db.Rfqs.ApplyScope(scope).Select(x => x.Id);
        var supplier = await db.RfqSuppliers
            .Where(x =>
                x.Id == rfqSupplierId &&
                x.RfqId == rfqId &&
                scopedRfqIds.Contains(x.RfqId))
            .Include(x => x.Rfq)
                .ThenInclude(x => x.Items)
            .Include(x => x.Quotations)
                .ThenInclude(x => x.Items)
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);

        if (supplier is null)
            throw new ProcurementNotFoundException("RFQ tedarikçisi bulunamadı.");

        if (supplier.Rfq.Status is RfqStatus.Awarded or RfqStatus.Closed or RfqStatus.Cancelled)
            throw new ProcurementValidationException("Sonuçlanmış RFQ için teklif kaydedilemez.");

        var rfqItems = supplier.Rfq.Items.ToDictionary(x => x.Id);
        var requestedItemIds = request.Items.Select(x => x.RfqItemId).Distinct().ToArray();
        if (requestedItemIds.Length != request.Items.Count ||
            requestedItemIds.Any(x => !rfqItems.ContainsKey(x)))
        {
            throw new ProcurementValidationException("Teklifte RFQ'ya ait olmayan veya yinelenen kalem var.");
        }

        foreach (var oldQuotation in supplier.Quotations)
        {
            oldQuotation.IsDeleted = true;
            oldQuotation.IsActive = false;
            foreach (var oldItem in oldQuotation.Items)
            {
                oldItem.IsDeleted = true;
                oldItem.IsActive = false;
            }
        }

        var quotationItems = request.Items.Select(item =>
        {
            var netUnitPrice = decimal.Round(
                item.UnitPrice * (1m - item.DiscountRate / 100m),
                4,
                MidpointRounding.AwayFromZero);
            var totalPrice = decimal.Round(
                netUnitPrice * item.Quantity,
                2,
                MidpointRounding.AwayFromZero);

            return new RfqSupplierQuotationItem
            {
                RfqItemId = item.RfqItemId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                DiscountRate = item.DiscountRate,
                NetUnitPrice = netUnitPrice,
                TotalPrice = totalPrice,
                Brand = item.Brand?.Trim(),
                Model = item.Model?.Trim(),
                DeliveryDays = item.DeliveryDays,
                Notes = item.Notes?.Trim()
            };
        }).ToList();

        var subtotal = decimal.Round(
            request.Items.Sum(x => x.UnitPrice * x.Quantity),
            2,
            MidpointRounding.AwayFromZero);
        var grandTotal = quotationItems.Sum(x => x.TotalPrice);

        supplier.Quotations.Add(new RfqSupplierQuotation
        {
            SupplierQuotationNumber = request.SupplierQuotationNumber?.Trim(),
            QuotationDate = request.QuotationDate.AsUtc(),
            ValidUntil = request.ValidUntil.AsUtc(),
            Currency = ProcurementServiceSupport.CurrencyOrTry(request.Currency, 3),
            ExchangeRate = request.ExchangeRate > 0 ? request.ExchangeRate : 1m,
            DeliveryDays = request.DeliveryDays,
            PaymentTerm = request.PaymentTerm?.Trim(),
            Subtotal = subtotal,
            DiscountTotal = subtotal - grandTotal,
            GrandTotal = grandTotal,
            Notes = request.Notes?.Trim(),
            Items = quotationItems
        });

        supplier.Status = RfqSupplierStatus.Responded;
        supplier.RespondedAtUtc = DateTime.UtcNow;
        supplier.Rfq.Status = RfqStatus.ResponsesReceived;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<RfqComparisonResponse> GetComparisonAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var scope = await GetScopeAsync(cancellationToken);
        var header = await db.Rfqs
            .AsNoTracking()
            .ApplyScope(scope)
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.RfqNumber,
                x.Currency
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (header is null)
            throw new ProcurementNotFoundException("RFQ bulunamadı.");

        var supplierRows = await db.RfqSuppliers
            .AsNoTracking()
            .Where(x => x.RfqId == id)
            .OrderBy(x => x.SupplierCurrentAccount.Title)
            .Select(x => new
            {
                x.Id,
                x.SupplierCurrentAccountId,
                SupplierTitle = x.SupplierCurrentAccount.Title,
                Quotation = x.Quotations
                    .OrderByDescending(q => q.QuotationDate)
                    .Select(q => new
                    {
                        q.Id,
                        q.Currency,
                        q.ExchangeRate,
                        q.GrandTotal,
                        q.DeliveryDays,
                        q.PaymentTerm
                    })
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var quotationIds = supplierRows
            .Where(x => x.Quotation is not null)
            .Select(x => x.Quotation!.Id)
            .ToArray();

        var itemRows = await db.RfqSupplierQuotationItems
            .AsNoTracking()
            .Where(x => quotationIds.Contains(x.RfqSupplierQuotationId))
            .OrderBy(x => x.RfqItem.LineNumber)
            .Select(x => new
            {
                x.RfqSupplierQuotationId,
                x.RfqItemId,
                x.RfqItem.MaterialDescription,
                RequestedQuantity = x.RfqItem.Quantity,
                x.RfqItem.Unit,
                x.UnitPrice,
                x.NetUnitPrice,
                x.TotalPrice,
                x.Brand,
                x.Model,
                x.DeliveryDays,
                x.RfqSupplierQuotation.ExchangeRate
            })
            .ToListAsync(cancellationToken);

        var supplierIds = supplierRows
            .Select(x => x.SupplierCurrentAccountId)
            .Distinct()
            .ToArray();
        var histories = await LoadSupplierHistoriesAsync(
            supplierIds,
            scope,
            cancellationToken);

        var itemsByQuotation = itemRows
            .GroupBy(x => x.RfqSupplierQuotationId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<RfqComparisonItemResponse>)group
                    .Select(i => new RfqComparisonItemResponse(
                        i.RfqItemId,
                        i.MaterialDescription,
                        i.RequestedQuantity,
                        i.Unit,
                        i.UnitPrice,
                        i.NetUnitPrice,
                        i.TotalPrice,
                        i.Brand,
                        i.Model,
                        i.DeliveryDays,
                        ProcurementDecisionScoring.Normalize(
                            i.TotalPrice,
                            i.ExchangeRate)))
                    .ToList());

        var candidates = supplierRows.Select(s =>
        {
            var quotation = s.Quotation;
            var items = quotation is not null &&
                        itemsByQuotation.TryGetValue(quotation.Id, out var quotationItems)
                ? quotationItems
                : Array.Empty<RfqComparisonItemResponse>();
            var history = histories.GetValueOrDefault(
                s.SupplierCurrentAccountId,
                new SupplierHistoryMetrics(0, 0, 0, 0, 0, 0));

            return new
            {
                s.Id,
                s.SupplierCurrentAccountId,
                s.SupplierTitle,
                HasQuotation = quotation is not null,
                Currency = quotation?.Currency ?? header.Currency,
                GrandTotal = quotation?.GrandTotal ?? 0m,
                ExchangeRate = quotation?.ExchangeRate ?? 1m,
                NormalizedGrandTotal = quotation is null
                    ? 0m
                    : ProcurementDecisionScoring.Normalize(
                        quotation.GrandTotal,
                        quotation.ExchangeRate),
                DeliveryDays = quotation?.DeliveryDays,
                PaymentTerm = quotation?.PaymentTerm,
                History = history,
                Items = items
            };
        }).ToList();

        var quotedCandidates = candidates
            .Where(x => x.HasQuotation && x.NormalizedGrandTotal > 0m)
            .ToList();
        var lowest = quotedCandidates
            .OrderBy(x => x.NormalizedGrandTotal)
            .FirstOrDefault();
        var lowestNormalizedTotal = lowest?.NormalizedGrandTotal ?? 0m;
        var shortestDeliveryDays = quotedCandidates
            .Where(x => x.DeliveryDays.HasValue)
            .Select(x => x.DeliveryDays)
            .Min();

        var scoredCandidates = candidates
            .Select(candidate =>
            {
                var priceScore = candidate.HasQuotation
                    ? ProcurementDecisionScoring.PriceScore(
                        candidate.NormalizedGrandTotal,
                        lowestNormalizedTotal)
                    : 0m;
                var termScore = candidate.HasQuotation
                    ? ProcurementDecisionScoring.DeliveryTermScore(
                        candidate.DeliveryDays,
                        shortestDeliveryDays)
                    : 0m;
                var decisionScore = candidate.HasQuotation
                    ? ProcurementDecisionScoring.DecisionScore(
                        priceScore,
                        termScore,
                        candidate.History.HistoryScore)
                    : 0m;

                return new
                {
                    Candidate = candidate,
                    PriceScore = priceScore,
                    DeliveryTermScore = termScore,
                    DecisionScore = decisionScore
                };
            })
            .OrderByDescending(x => x.DecisionScore)
            .ThenBy(x => x.Candidate.NormalizedGrandTotal)
            .ThenBy(x => x.Candidate.SupplierTitle)
            .ToList();

        var recommended = scoredCandidates
            .FirstOrDefault(x => x.Candidate.HasQuotation);
        var quotedTotals = quotedCandidates
            .Select(x => x.NormalizedGrandTotal)
            .OrderBy(x => x)
            .ToList();
        var savingVsSecondLowest = quotedTotals.Count < 2
            ? 0m
            : quotedTotals[1] - quotedTotals[0];
        var savingRate = quotedTotals.Count < 2 || quotedTotals[1] <= 0m
            ? 0m
            : decimal.Round(
                savingVsSecondLowest / quotedTotals[1] * 100m,
                2,
                MidpointRounding.AwayFromZero);

        var suppliers = scoredCandidates
            .Select((scored, index) => new RfqComparisonSupplierResponse(
                scored.Candidate.Id,
                scored.Candidate.SupplierCurrentAccountId,
                scored.Candidate.SupplierTitle,
                scored.Candidate.HasQuotation,
                scored.Candidate.Currency,
                scored.Candidate.GrandTotal,
                scored.Candidate.ExchangeRate,
                scored.Candidate.NormalizedGrandTotal,
                scored.Candidate.DeliveryDays,
                scored.Candidate.PaymentTerm,
                scored.PriceScore,
                scored.DeliveryTermScore,
                scored.Candidate.History.HistoryScore,
                scored.DecisionScore,
                scored.Candidate.HasQuotation ? index + 1 : 0,
                recommended is not null &&
                scored.Candidate.Id == recommended.Candidate.Id,
                scored.Candidate.History.ResponseRate,
                scored.Candidate.History.OnTimeDeliveryRate,
                scored.Candidate.History.QualityRate,
                scored.Candidate.History.Confidence,
                scored.Candidate.Items))
            .ToList();

        return new RfqComparisonResponse(
            header.Id,
            header.RfqNumber,
            lowest?.GrandTotal ?? 0m,
            lowest?.Id,
            lowest?.SupplierTitle,
            ProcurementDecisionScoring.ComparisonCurrency,
            lowestNormalizedTotal,
            quotedTotals.Count == 0
                ? 0m
                : decimal.Round(
                    quotedTotals.Average(),
                    2,
                    MidpointRounding.AwayFromZero),
            savingVsSecondLowest,
            savingRate,
            recommended?.Candidate.Id,
            recommended?.Candidate.SupplierTitle,
            suppliers);
    }

    public async Task<AwardRfqResponse> AwardAsync(
        Guid id,
        Guid rfqSupplierId,
        CancellationToken cancellationToken)
    {
        var scope = await GetScopeAsync(cancellationToken);
        var entity = await db.Rfqs
            .ApplyScope(scope)
            .Include(x => x.Suppliers)
                .ThenInclude(x => x.SupplierCurrentAccount)
            .Include(x => x.Suppliers)
                .ThenInclude(x => x.Quotations)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            throw new ProcurementNotFoundException("RFQ bulunamadı.");

        if (entity.Status is RfqStatus.Awarded or RfqStatus.Closed or RfqStatus.Cancelled)
            throw new ProcurementValidationException("RFQ daha önce sonuçlandırılmış.");

        var selected = entity.Suppliers.SingleOrDefault(x => x.Id == rfqSupplierId);
        var quotation = selected?.Quotations
            .OrderByDescending(x => x.QuotationDate)
            .FirstOrDefault();

        if (selected is null || quotation is null)
            throw new ProcurementValidationException("Seçilen tedarikçinin geçerli teklifi bulunmuyor.");

        foreach (var supplier in entity.Suppliers)
        {
            supplier.Status = supplier.Id == selected.Id
                ? RfqSupplierStatus.Awarded
                : RfqSupplierStatus.Rejected;
        }

        entity.Status = RfqStatus.Awarded;
        await db.SaveChangesAsync(cancellationToken);

        return new AwardRfqResponse(
            entity.Id,
            selected.Id,
            selected.SupplierCurrentAccountId,
            selected.SupplierCurrentAccount.Title,
            quotation.GrandTotal);
    }

    public async Task CloseAsync(Guid id, CancellationToken cancellationToken)
    {
        var scope = await GetScopeAsync(cancellationToken);
        var entity = await db.Rfqs
            .ApplyScope(scope)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            throw new ProcurementNotFoundException("RFQ bulunamadı.");

        if (entity.Status == RfqStatus.Cancelled)
            throw new ProcurementValidationException("İptal edilen RFQ kapatılamaz.");

        entity.Status = RfqStatus.Closed;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyDictionary<Guid, SupplierHistoryMetrics>>
        LoadSupplierHistoriesAsync(
            IReadOnlyCollection<Guid> supplierIds,
            CurrentDataScopeSnapshot scope,
            CancellationToken cancellationToken)
    {
        if (supplierIds.Count == 0)
            return new Dictionary<Guid, SupplierHistoryMetrics>();

        var ids = supplierIds.ToArray();
        var scopedRfqIds = db.Rfqs
            .AsNoTracking()
            .ApplyScope(scope)
            .Select(x => x.Id);

        var invitationRows = await db.RfqSuppliers
            .AsNoTracking()
            .Where(x =>
                ids.Contains(x.SupplierCurrentAccountId) &&
                scopedRfqIds.Contains(x.RfqId))
            .GroupBy(x => x.SupplierCurrentAccountId)
            .Select(group => new
            {
                SupplierId = group.Key,
                InvitationCount = group.Count(),
                ResponseCount = group.Count(x => x.Quotations.Any())
            })
            .ToListAsync(cancellationToken);

        var orderRows = await db.PurchaseOrders
            .AsNoTracking()
            .ApplyScope(scope)
            .Where(x => ids.Contains(x.SupplierCurrentAccountId))
            .GroupBy(x => x.SupplierCurrentAccountId)
            .Select(group => new
            {
                SupplierId = group.Key,
                DeliveryMeasuredOrderCount = group.Count(x =>
                    x.Status == PurchaseOrderStatus.Completed &&
                    x.ExpectedDeliveryDate.HasValue &&
                    x.GoodsReceipts.Any(receipt =>
                        receipt.Status == GoodsReceiptStatus.Posted)),
                OnTimeDeliveryOrderCount = group.Count(x =>
                    x.Status == PurchaseOrderStatus.Completed &&
                    x.ExpectedDeliveryDate.HasValue &&
                    x.GoodsReceipts.Any(receipt =>
                        receipt.Status == GoodsReceiptStatus.Posted) &&
                    !x.GoodsReceipts.Any(receipt =>
                        receipt.Status == GoodsReceiptStatus.Posted &&
                        receipt.ReceiptDate > x.ExpectedDeliveryDate.Value))
            })
            .ToListAsync(cancellationToken);

        var qualityRows = await db.GoodsReceipts
            .AsNoTracking()
            .ApplyScope(scope)
            .Where(x =>
                x.Status == GoodsReceiptStatus.Posted &&
                ids.Contains(x.PurchaseOrder.SupplierCurrentAccountId))
            .SelectMany(x => x.Items)
            .GroupBy(x =>
                x.GoodsReceipt.PurchaseOrder.SupplierCurrentAccountId)
            .Select(group => new
            {
                SupplierId = group.Key,
                ReceiptLineCount = group.Count(),
                ExceptionLineCount = group.Count(x =>
                    x.RejectedQuantity > 0m ||
                    x.DamagedQuantity > 0m)
            })
            .ToListAsync(cancellationToken);

        var invitationsBySupplier = invitationRows.ToDictionary(x => x.SupplierId);
        var ordersBySupplier = orderRows.ToDictionary(x => x.SupplierId);
        var qualityBySupplier = qualityRows.ToDictionary(x => x.SupplierId);

        return ids.ToDictionary(
            supplierId => supplierId,
            supplierId =>
            {
                invitationsBySupplier.TryGetValue(supplierId, out var invitations);
                ordersBySupplier.TryGetValue(supplierId, out var orders);
                qualityBySupplier.TryGetValue(supplierId, out var quality);

                return new SupplierHistoryMetrics(
                    invitations?.InvitationCount ?? 0,
                    invitations?.ResponseCount ?? 0,
                    orders?.DeliveryMeasuredOrderCount ?? 0,
                    orders?.OnTimeDeliveryOrderCount ?? 0,
                    quality?.ReceiptLineCount ?? 0,
                    quality?.ExceptionLineCount ?? 0);
            });
    }

    private async Task<CurrentDataScopeSnapshot> GetScopeAsync(
        CancellationToken cancellationToken) =>
        await dataScope.GetAsync(cancellationToken) ??
        throw new UnauthorizedAccessException("Kullanıcı veri kapsamı bulunamadı.");
}
