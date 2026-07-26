using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Procurement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Route("api/rfqs")]
[Authorize]
public sealed class RfqsController(
    ProcurementDbContext procurementDb,
    AppDbContext appDb,
    IOfferEvaluationService evaluationService) : ControllerBase
{
    public sealed record CreateRfqItemRequest(
        Guid MaterialId,
        decimal Quantity,
        string Unit,
        DateTime? RequiredDateUtc,
        string? Description);

    public sealed record CreateRfqRequest(
        Guid CompanyId,
        Guid ProjectId,
        Guid? PurchaseRequestId,
        string? RfqNumber,
        DateTime? OfferDeadlineUtc,
        string CurrencyCode,
        string? Description,
        IReadOnlyList<CreateRfqItemRequest> Items);

    public sealed record CreateOfferItemRequest(
        Guid RfqItemId,
        Guid MaterialId,
        decimal OfferedQuantity,
        decimal AvailableStockQuantity,
        decimal UnitPrice,
        int ItemDeliveryDays);

    public sealed record CreateCheckTermRequest(
        DateTime DueDateUtc,
        decimal Amount,
        int SequenceNo);

    public sealed record CreateSupplierOfferRequest(
        Guid SupplierCurrentAccountId,
        string? OfferNumber,
        DateTime? OfferDateUtc,
        string CurrencyCode,
        decimal ExchangeRate,
        decimal DiscountRate,
        decimal FreightAmount,
        FreightResponsibility FreightResponsibility,
        int PaymentTermDays,
        int DeliveryTermDays,
        bool AllowsPartialShipment,
        decimal SupplierPerformanceScore,
        string? Notes,
        IReadOnlyList<CreateOfferItemRequest> Items,
        IReadOnlyList<CreateCheckTermRequest> CheckTerms);

    public sealed record AwardOfferRequest(
        Guid OfferId,
        string? OrderNumber,
        decimal VatRate,
        DateTime? DeliveryDateUtc,
        string? Description);

    [HttpGet]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await procurementDb.Rfqs
            .AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Offers)
            .OrderByDescending(x => x.RfqDateUtc)
            .Select(x => new
            {
                x.Id,
                x.RfqNumber,
                x.CompanyId,
                x.ProjectId,
                x.PurchaseRequestId,
                x.RfqDateUtc,
                x.OfferDeadlineUtc,
                x.Status,
                x.CurrencyCode,
                ItemCount = x.Items.Count,
                OfferCount = x.Offers.Count
            })
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var rfq = await procurementDb.Rfqs
            .AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Offers)
                .ThenInclude(x => x.Items)
            .Include(x => x.Offers)
                .ThenInclude(x => x.CheckTerms)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return rfq is null ? NotFound() : Ok(rfq);
    }

    [HttpPost]
    public async Task<IActionResult> CreateAsync(
        CreateRfqRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0)
            return BadRequest("RFQ en az bir kalem içermelidir.");

        if (request.Items.Any(x => x.Quantity <= 0))
            return BadRequest("RFQ kalem miktarları sıfırdan büyük olmalıdır.");

        var materialIds = request.Items.Select(x => x.MaterialId).Distinct().ToList();
        var materialCount = await appDb.Materials.CountAsync(x => materialIds.Contains(x.Id), cancellationToken);
        if (materialCount != materialIds.Count)
            return BadRequest("RFQ içinde geçersiz malzeme kartı bulunuyor.");

        PurchaseRequest? purchaseRequest = null;
        if (request.PurchaseRequestId.HasValue)
        {
            purchaseRequest = await appDb.PurchaseRequests
                .Include(x => x.Items)
                .SingleOrDefaultAsync(x => x.Id == request.PurchaseRequestId.Value, cancellationToken);

            if (purchaseRequest is null)
                return BadRequest("Satın alma talebi bulunamadı.");

            if (purchaseRequest.Status != PurchaseRequestStatus.Approved)
                return BadRequest("Yalnızca onaylı satın alma talepleri RFQ'ya dönüştürülebilir.");
        }

        var rfq = new Rfq
        {
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            PurchaseRequestId = request.PurchaseRequestId,
            RfqNumber = string.IsNullOrWhiteSpace(request.RfqNumber)
                ? $"RFQ-{DateTime.UtcNow:yyyyMMddHHmmss}"
                : request.RfqNumber.Trim(),
            OfferDeadlineUtc = request.OfferDeadlineUtc,
            CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode)
                ? "TRY"
                : request.CurrencyCode.Trim().ToUpperInvariant(),
            Description = request.Description,
            Status = RfqStatus.Draft,
            Items = request.Items.Select(x => new RfqItem
            {
                MaterialId = x.MaterialId,
                Quantity = x.Quantity,
                Unit = string.IsNullOrWhiteSpace(x.Unit) ? "Adet" : x.Unit.Trim(),
                RequiredDateUtc = x.RequiredDateUtc,
                Description = x.Description
            }).ToList()
        };

        procurementDb.Rfqs.Add(rfq);
        await procurementDb.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetAsync), new { id = rfq.Id }, rfq);
    }

    [HttpPost("{id:guid}/publish")]
    public async Task<IActionResult> PublishAsync(Guid id, CancellationToken cancellationToken)
    {
        var rfq = await procurementDb.Rfqs.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (rfq is null)
            return NotFound();

        if (rfq.Status != RfqStatus.Draft)
            return BadRequest("Yalnızca taslak RFQ yayınlanabilir.");

        rfq.Status = RfqStatus.CollectingOffers;
        rfq.UpdatedAtUtc = DateTime.UtcNow;
        await procurementDb.SaveChangesAsync(cancellationToken);

        return Ok(rfq);
    }

    [HttpPost("{id:guid}/offers")]
    public async Task<IActionResult> AddOfferAsync(
        Guid id,
        CreateSupplierOfferRequest request,
        CancellationToken cancellationToken)
    {
        var rfq = await procurementDb.Rfqs
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (rfq is null)
            return NotFound();

        if (rfq.Status is RfqStatus.Awarded or RfqStatus.Cancelled)
            return BadRequest("Sonuçlandırılmış veya iptal edilmiş RFQ'ya teklif eklenemez.");

        if (request.Items.Count == 0)
            return BadRequest("Teklif en az bir kalem içermelidir.");

        if (request.Items.Any(x => x.OfferedQuantity <= 0 || x.UnitPrice < 0))
            return BadRequest("Teklif miktarı sıfırdan büyük, fiyat ise negatif olmamalıdır.");

        var validRfqItemIds = rfq.Items.Select(x => x.Id).ToHashSet();
        if (request.Items.Any(x => !validRfqItemIds.Contains(x.RfqItemId)))
            return BadRequest("Teklif içinde RFQ'ya ait olmayan kalem bulunuyor.");

        var supplierExists = await appDb.CurrentAccounts
            .AnyAsync(x => x.Id == request.SupplierCurrentAccountId, cancellationToken);
        if (!supplierExists)
            return BadRequest("Tedarikçi cari kartı bulunamadı.");

        var offer = new SupplierOffer
        {
            RfqId = id,
            SupplierCurrentAccountId = request.SupplierCurrentAccountId,
            OfferNumber = string.IsNullOrWhiteSpace(request.OfferNumber)
                ? $"TKL-{DateTime.UtcNow:yyyyMMddHHmmss}"
                : request.OfferNumber.Trim(),
            OfferDateUtc = request.OfferDateUtc ?? DateTime.UtcNow,
            CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode)
                ? rfq.CurrencyCode
                : request.CurrencyCode.Trim().ToUpperInvariant(),
            ExchangeRate = request.ExchangeRate <= 0 ? 1m : request.ExchangeRate,
            DiscountRate = Math.Clamp(request.DiscountRate, 0m, 100m),
            FreightAmount = Math.Max(0m, request.FreightAmount),
            FreightResponsibility = request.FreightResponsibility,
            PaymentTermDays = Math.Max(0, request.PaymentTermDays),
            DeliveryTermDays = Math.Max(0, request.DeliveryTermDays),
            AllowsPartialShipment = request.AllowsPartialShipment,
            SupplierPerformanceScore = Math.Clamp(request.SupplierPerformanceScore, 0m, 100m),
            Notes = request.Notes,
            Items = request.Items.Select(x => new SupplierOfferItem
            {
                RfqItemId = x.RfqItemId,
                MaterialId = x.MaterialId,
                OfferedQuantity = x.OfferedQuantity,
                AvailableStockQuantity = Math.Max(0m, x.AvailableStockQuantity),
                UnitPrice = x.UnitPrice,
                ItemDeliveryDays = Math.Max(0, x.ItemDeliveryDays)
            }).ToList(),
            CheckTerms = request.CheckTerms.Select(x => new SupplierOfferCheckTerm
            {
                DueDateUtc = x.DueDateUtc,
                Amount = Math.Max(0m, x.Amount),
                SequenceNo = x.SequenceNo
            }).ToList()
        };

        procurementDb.SupplierOffers.Add(offer);
        rfq.Status = RfqStatus.CollectingOffers;
        await procurementDb.SaveChangesAsync(cancellationToken);

        return Created($"/api/rfqs/{id}/offers/{offer.Id}", offer);
    }

    [HttpGet("{id:guid}/evaluation")]
    public async Task<IActionResult> EvaluateAsync(Guid id, CancellationToken cancellationToken)
    {
        var exists = await procurementDb.Rfqs.AnyAsync(x => x.Id == id, cancellationToken);
        if (!exists)
            return NotFound();

        var result = await evaluationService.EvaluateAsync(id, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/award")]
    public async Task<IActionResult> AwardAsync(
        Guid id,
        AwardOfferRequest request,
        CancellationToken cancellationToken)
    {
        var rfq = await procurementDb.Rfqs
            .Include(x => x.Items)
            .Include(x => x.Offers)
                .ThenInclude(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (rfq is null)
            return NotFound();

        if (rfq.Status == RfqStatus.Awarded)
            return BadRequest("RFQ daha önce sonuçlandırılmış.");

        var offer = rfq.Offers.SingleOrDefault(x => x.Id == request.OfferId);
        if (offer is null)
            return BadRequest("Seçilen teklif bu RFQ'ya ait değil.");

        var order = new PurchaseOrder
        {
            CompanyId = rfq.CompanyId,
            ProjectId = rfq.ProjectId,
            PurchaseRequestId = rfq.PurchaseRequestId,
            SupplierCurrentAccountId = offer.SupplierCurrentAccountId,
            OrderNumber = string.IsNullOrWhiteSpace(request.OrderNumber)
                ? $"SAS-{DateTime.UtcNow:yyyyMMddHHmmss}"
                : request.OrderNumber.Trim(),
            OrderDateUtc = DateTime.UtcNow,
            DeliveryDateUtc = request.DeliveryDateUtc,
            Status = PurchaseOrderStatus.Draft,
            CurrencyCode = offer.CurrencyCode,
            ExchangeRate = offer.ExchangeRate <= 0 ? 1m : offer.ExchangeRate,
            VatRate = Math.Clamp(request.VatRate, 0m, 100m),
            Description = request.Description,
            Items = offer.Items.Select(x => new PurchaseOrderItem
            {
                MaterialId = x.MaterialId,
                Quantity = x.OfferedQuantity,
                ReceivedQuantity = 0m,
                Unit = rfq.Items.First(i => i.Id == x.RfqItemId).Unit,
                UnitPrice = x.UnitPrice,
                DiscountRate = offer.DiscountRate,
                Description = rfq.Items.First(i => i.Id == x.RfqItemId).Description
            }).ToList()
        };

        if (order.Items.Count == 0)
            return BadRequest("Seçilen teklif sipariş kalemi içermiyor.");

        appDb.PurchaseOrders.Add(order);

        if (rfq.PurchaseRequestId.HasValue)
        {
            var purchaseRequest = await appDb.PurchaseRequests
                .SingleOrDefaultAsync(x => x.Id == rfq.PurchaseRequestId.Value, cancellationToken);

            if (purchaseRequest is not null)
                purchaseRequest.Status = PurchaseRequestStatus.ConvertedToOrder;
        }

        await appDb.SaveChangesAsync(cancellationToken);

        rfq.Status = RfqStatus.Awarded;
        rfq.UpdatedAtUtc = DateTime.UtcNow;
        await procurementDb.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            RfqId = rfq.Id,
            SelectedOfferId = offer.Id,
            PurchaseOrderId = order.Id,
            order.OrderNumber,
            order.Status
        });
    }
}
