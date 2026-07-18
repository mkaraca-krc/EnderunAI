using EnderunAI.Api.Contracts.Offers;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.DocumentNumbers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/offers")]
public sealed class OffersController(
    AppDbContext db,
    IDocumentNumberService documentNumbers) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] int? status,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var query = db.Offers.AsNoTracking();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (projectId.HasValue)
            query = query.Where(x => x.ProjectId == projectId.Value);

        if (status.HasValue)
        {
            if (!Enum.IsDefined(typeof(OfferStatus), status.Value))
                return BadRequest(new { message = "Geçersiz teklif durumu." });

            query = query.Where(x => x.Status == (OfferStatus)status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x =>
                x.OfferNumber.ToLower().Contains(term) ||
                x.Title.ToLower().Contains(term) ||
                (x.Project != null && x.Project.Name.ToLower().Contains(term)));
        }

        var rows = await query
            .OrderByDescending(x => x.OfferDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                CompanyName = x.Company.Name,
                x.ProjectId,
                ProjectName = x.Project != null ? x.Project.Name : null,
                x.CustomerId,
                x.OfferNumber,
                x.Title,
                x.OfferDate,
                x.ValidUntil,
                x.Currency,
                x.ExchangeRate,
                x.Status,
                x.Subtotal,
                x.DiscountTotal,
                x.CostTotal,
                x.ProfitTotal,
                x.GrandTotal,
                ItemCount = x.Items.Count
            })
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var row = await db.Offers
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                CompanyName = x.Company.Name,
                x.ProjectId,
                ProjectName = x.Project != null ? x.Project.Name : null,
                x.CustomerId,
                x.OfferNumber,
                x.Title,
                x.OfferDate,
                x.ValidUntil,
                x.Currency,
                x.ExchangeRate,
                x.Status,
                x.Description,
                x.Notes,
                x.Subtotal,
                x.DiscountTotal,
                x.CostTotal,
                x.ProfitTotal,
                x.GrandTotal,
                Items = x.Items
                    .OrderBy(i => i.LineNumber)
                    .Select(i => new
                    {
                        i.Id,
                        i.LineNumber,
                        i.PositionNumber,
                        i.Description,
                        i.ManufacturerPriceListItemId,
                        i.ManufacturerName,
                        i.ProductCode,
                        i.Brand,
                        i.Model,
                        i.Quantity,
                        i.Unit,
                        i.ListPrice,
                        i.DiscountRate,
                        i.NetPurchasePrice,
                        i.FreightRate,
                        i.WasteRate,
                        i.FinanceRate,
                        i.GeneralExpenseRate,
                        i.ProfitRate,
                        i.UnitCost,
                        i.UnitSalesPrice,
                        i.CostTotal,
                        i.SalesTotal,
                        i.Notes
                    })
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? NotFound(new { message = "Teklif bulunamadı." })
            : Ok(row);
    }

    [HttpPost("calculate-item")]
    public IActionResult CalculateItem(CalculateOfferItemRequest request)
    {
        var validation = ValidateRates(
            request.Quantity,
            request.ListPrice,
            request.DiscountRate,
            request.FreightRate,
            request.WasteRate,
            request.FinanceRate,
            request.GeneralExpenseRate,
            request.ProfitRate);

        if (validation is not null)
            return BadRequest(new { message = validation });

        var result = Calculate(
            request.Quantity,
            request.ListPrice,
            request.DiscountRate,
            request.FreightRate,
            request.WasteRate,
            request.FinanceRate,
            request.GeneralExpenseRate,
            request.ProfitRate);

        return Ok(new CalculateOfferItemResponse(
            result.NetPurchasePrice,
            result.UnitCost,
            result.UnitSalesPrice,
            result.CostTotal,
            result.SalesTotal,
            result.ProfitTotal));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateOfferRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { message = "Teklif başlığı zorunludur." });

        if (string.IsNullOrWhiteSpace(request.Currency))
            return BadRequest(new { message = "Para birimi zorunludur." });

        if (request.ExchangeRate <= 0)
            return BadRequest(new { message = "Kur sıfırdan büyük olmalıdır." });

        if (request.Items.Count == 0)
            return BadRequest(new { message = "Teklifte en az bir kalem bulunmalıdır." });

        var companyExists = await db.Companies.AnyAsync(
            x => x.Id == request.CompanyId && x.IsActive,
            cancellationToken);

        if (!companyExists)
            return BadRequest(new { message = "Geçerli bir şirket seçilmelidir." });

        if (request.ProjectId.HasValue)
        {
            var projectExists = await db.Projects.AnyAsync(
                x => x.Id == request.ProjectId.Value &&
                     x.CompanyId == request.CompanyId &&
                     x.IsActive,
                cancellationToken);

            if (!projectExists)
                return BadRequest(new { message = "Proje seçilen şirkete ait değil." });
        }

        var offerNumber = await documentNumbers.GenerateAsync(
            request.CompanyId,
            "OFFER",
            "TKL",
            cancellationToken);

        var entity = new Offer
        {
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            CustomerId = request.CustomerId,
            OfferNumber = offerNumber,
            Title = request.Title.Trim(),
            OfferDate = request.OfferDate.Date,
            ValidUntil = request.ValidUntil?.Date,
            Currency = request.Currency.Trim().ToUpperInvariant(),
            ExchangeRate = request.ExchangeRate,
            Description = request.Description?.Trim(),
            Notes = request.Notes?.Trim(),
            Status = OfferStatus.Draft
        };

        var lineNumber = 1;

        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Description) ||
                string.IsNullOrWhiteSpace(item.Unit))
            {
                return BadRequest(new
                {
                    message = "Her teklif kaleminde açıklama ve birim zorunludur."
                });
            }

            var validation = ValidateRates(
                item.Quantity,
                item.ListPrice,
                item.DiscountRate,
                item.FreightRate,
                item.WasteRate,
                item.FinanceRate,
                item.GeneralExpenseRate,
                item.ProfitRate);

            if (validation is not null)
                return BadRequest(new { message = validation });

            var calculation = Calculate(
                item.Quantity,
                item.ListPrice,
                item.DiscountRate,
                item.FreightRate,
                item.WasteRate,
                item.FinanceRate,
                item.GeneralExpenseRate,
                item.ProfitRate);

            entity.Items.Add(new OfferItem
            {
                LineNumber = lineNumber++,
                PositionNumber = item.PositionNumber?.Trim(),
                Description = item.Description.Trim(),
                ManufacturerPriceListItemId = item.ManufacturerPriceListItemId,
                ManufacturerName = item.ManufacturerName?.Trim(),
                ProductCode = item.ProductCode?.Trim(),
                Brand = item.Brand?.Trim(),
                Model = item.Model?.Trim(),
                Quantity = item.Quantity,
                Unit = item.Unit.Trim(),
                ListPrice = item.ListPrice,
                DiscountRate = item.DiscountRate,
                NetPurchasePrice = calculation.NetPurchasePrice,
                FreightRate = item.FreightRate,
                WasteRate = item.WasteRate,
                FinanceRate = item.FinanceRate,
                GeneralExpenseRate = item.GeneralExpenseRate,
                ProfitRate = item.ProfitRate,
                UnitCost = calculation.UnitCost,
                UnitSalesPrice = calculation.UnitSalesPrice,
                CostTotal = calculation.CostTotal,
                SalesTotal = calculation.SalesTotal,
                Notes = item.Notes?.Trim()
            });
        }

        entity.Subtotal = decimal.Round(
            entity.Items.Sum(x => x.ListPrice * x.Quantity), 2);

        entity.DiscountTotal = decimal.Round(
            entity.Items.Sum(x =>
                (x.ListPrice - x.NetPurchasePrice) * x.Quantity), 2);

        entity.CostTotal = decimal.Round(
            entity.Items.Sum(x => x.CostTotal), 2);

        entity.GrandTotal = decimal.Round(
            entity.Items.Sum(x => x.SalesTotal), 2);

        entity.ProfitTotal = decimal.Round(
            entity.GrandTotal - entity.CostTotal, 2);

        db.Offers.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Teklif taslak olarak oluşturuldu.",
            entity.Id,
            entity.OfferNumber,
            entity.GrandTotal,
            entity.Status
        });
    }

    private static string? ValidateRates(
        decimal quantity,
        decimal listPrice,
        params decimal[] rates)
    {
        if (quantity <= 0)
            return "Miktar sıfırdan büyük olmalıdır.";

        if (listPrice < 0)
            return "Liste fiyatı negatif olamaz.";

        if (rates.Any(x => x < 0 || x > 100))
            return "İskonto ve maliyet oranları 0 ile 100 arasında olmalıdır.";

        return null;
    }

    private static CalculationResult Calculate(
        decimal quantity,
        decimal listPrice,
        decimal discountRate,
        decimal freightRate,
        decimal wasteRate,
        decimal financeRate,
        decimal generalExpenseRate,
        decimal profitRate)
    {
        var netPurchasePrice =
            listPrice * (1 - discountRate / 100m);

        var unitCost = netPurchasePrice *
            (1 +
             freightRate / 100m +
             wasteRate / 100m +
             financeRate / 100m +
             generalExpenseRate / 100m);

        var unitSalesPrice =
            unitCost * (1 + profitRate / 100m);

        var costTotal = unitCost * quantity;
        var salesTotal = unitSalesPrice * quantity;

        return new CalculationResult(
            decimal.Round(netPurchasePrice, 6),
            decimal.Round(unitCost, 6),
            decimal.Round(unitSalesPrice, 6),
            decimal.Round(costTotal, 2),
            decimal.Round(salesTotal, 2),
            decimal.Round(salesTotal - costTotal, 2));
    }

    private sealed record CalculationResult(
        decimal NetPurchasePrice,
        decimal UnitCost,
        decimal UnitSalesPrice,
        decimal CostTotal,
        decimal SalesTotal,
        decimal ProfitTotal);
}
