using EnderunAI.Api.Contracts.Pricing;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/manufacturer-price-lists")]
public sealed class ManufacturerPriceListsController(AppDbContext db)
    : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.EngineeringView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] string? manufacturer,
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var query = db.ManufacturerPriceLists.AsNoTracking();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (!string.IsNullOrWhiteSpace(manufacturer))
        {
            var term = manufacturer.Trim().ToLower();
            query = query.Where(x =>
                x.ManufacturerName.ToLower().Contains(term));
        }

        if (activeOnly)
        {
            var today = DateTime.UtcNow.Date;
            query = query.Where(x =>
                x.IsActive &&
                (!x.ValidUntil.HasValue || x.ValidUntil.Value.Date >= today));
        }

        var items = await query
            .OrderByDescending(x => x.ListDate)
            .ThenBy(x => x.ManufacturerName)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                CompanyName = x.Company.Name,
                x.ManufacturerName,
                x.ListName,
                x.ListDate,
                x.ValidUntil,
                x.Currency,
                x.IsActive,
                ItemCount = x.Items.Count
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringView)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var item = await db.ManufacturerPriceLists
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                CompanyName = x.Company.Name,
                x.ManufacturerName,
                x.ListName,
                x.ListDate,
                x.ValidUntil,
                x.Currency,
                x.IsActive,
                Items = x.Items
                    .OrderBy(i => i.ProductDescription)
                    .Select(i => new
                    {
                        i.Id,
                        i.ProductCode,
                        i.ProductDescription,
                        i.Unit,
                        i.ListPrice,
                        i.Category,
                        i.Brand,
                        i.Model,
                        i.IsActive
                    })
            })
            .SingleOrDefaultAsync(cancellationToken);

        return item is null
            ? NotFound(new { message = "Fiyat listesi bulunamadı." })
            : Ok(item);
    }

    [HttpGet("search-products")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringView)]
    public async Task<IActionResult> SearchProducts(
        [FromQuery] Guid companyId,
        [FromQuery] string search,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(search))
            return BadRequest(new { message = "Arama metni zorunludur." });

        take = Math.Clamp(take, 1, 200);
        var term = search.Trim().ToLower();
        var today = DateTime.UtcNow.Date;

        var rows = await db.ManufacturerPriceListItems
            .AsNoTracking()
            .Where(x =>
                x.ManufacturerPriceList.CompanyId == companyId &&
                x.ManufacturerPriceList.IsActive &&
                (!x.ManufacturerPriceList.ValidUntil.HasValue ||
                 x.ManufacturerPriceList.ValidUntil.Value.Date >= today) &&
                (x.ProductCode.ToLower().Contains(term) ||
                 x.ProductDescription.ToLower().Contains(term) ||
                 (x.Brand != null && x.Brand.ToLower().Contains(term)) ||
                 (x.Model != null && x.Model.ToLower().Contains(term))))
            .OrderByDescending(x => x.ManufacturerPriceList.ListDate)
            .ThenBy(x => x.ManufacturerPriceList.ManufacturerName)
            .Take(take)
            .Select(x => new
            {
                x.Id,
                PriceListId = x.ManufacturerPriceListId,
                Manufacturer = x.ManufacturerPriceList.ManufacturerName,
                x.ManufacturerPriceList.ListName,
                x.ManufacturerPriceList.ListDate,
                x.ManufacturerPriceList.ValidUntil,
                x.ManufacturerPriceList.Currency,
                x.ProductCode,
                x.ProductDescription,
                x.Unit,
                x.ListPrice,
                x.Category,
                x.Brand,
                x.Model
            })
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.EngineeringManage)]
    public async Task<IActionResult> Create(
        CreateManufacturerPriceListRequest request,
        CancellationToken cancellationToken)
    {
        var validation = Validate(
            request.ManufacturerName,
            request.ListName,
            request.Currency,
            request.Items);

        if (validation is not null)
            return BadRequest(new { message = validation });

        var companyExists = await db.Companies.AnyAsync(
            x => x.Id == request.CompanyId && x.IsActive,
            cancellationToken);

        if (!companyExists)
            return BadRequest(new { message = "Geçerli bir şirket seçilmelidir." });

        var entity = new ManufacturerPriceList
        {
            CompanyId = request.CompanyId,
            ManufacturerName = request.ManufacturerName.Trim(),
            ListName = request.ListName.Trim(),
            ListDate = request.ListDate.Date,
            ValidUntil = request.ValidUntil?.Date,
            Currency = request.Currency.Trim().ToUpperInvariant()
        };

        foreach (var item in request.Items)
        {
            entity.Items.Add(ToEntity(item));
        }

        db.ManufacturerPriceLists.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Üretici fiyat listesi oluşturuldu.",
            entity.Id,
            entity.ManufacturerName,
            entity.ListName
        });
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringManage)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateManufacturerPriceListRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await db.ManufacturerPriceLists
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            return NotFound(new { message = "Fiyat listesi bulunamadı." });

        var validation = Validate(
            request.ManufacturerName,
            request.ListName,
            request.Currency,
            request.Items);

        if (validation is not null)
            return BadRequest(new { message = validation });

        entity.ManufacturerName = request.ManufacturerName.Trim();
        entity.ListName = request.ListName.Trim();
        entity.ListDate = request.ListDate.Date;
        entity.ValidUntil = request.ValidUntil?.Date;
        entity.Currency = request.Currency.Trim().ToUpperInvariant();
        entity.UpdatedAtUtc = DateTime.UtcNow;

        db.ManufacturerPriceListItems.RemoveRange(entity.Items);

        foreach (var item in request.Items)
        {
            entity.Items.Add(ToEntity(item));
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Fiyat listesi güncellendi." });
    }

    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringManage)]
    public async Task<IActionResult> Deactivate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var entity = await db.ManufacturerPriceLists
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
            return NotFound(new { message = "Fiyat listesi bulunamadı." });

        entity.IsActive = false;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Fiyat listesi pasife alındı." });
    }

    private static ManufacturerPriceListItem ToEntity(
        CreateManufacturerPriceListItemRequest item)
    {
        return new ManufacturerPriceListItem
        {
            ProductCode = item.ProductCode.Trim().ToUpperInvariant(),
            ProductDescription = item.ProductDescription.Trim(),
            Unit = item.Unit.Trim(),
            ListPrice = item.ListPrice,
            Category = item.Category?.Trim(),
            Brand = item.Brand?.Trim(),
            Model = item.Model?.Trim()
        };
    }

    private static string? Validate(
        string manufacturerName,
        string listName,
        string currency,
        IReadOnlyCollection<CreateManufacturerPriceListItemRequest> items)
    {
        if (string.IsNullOrWhiteSpace(manufacturerName))
            return "Üretici adı zorunludur.";

        if (string.IsNullOrWhiteSpace(listName))
            return "Liste adı zorunludur.";

        if (string.IsNullOrWhiteSpace(currency))
            return "Para birimi zorunludur.";

        if (items.Count == 0)
            return "Fiyat listesinde en az bir ürün bulunmalıdır.";

        if (items.Any(x =>
            string.IsNullOrWhiteSpace(x.ProductCode) ||
            string.IsNullOrWhiteSpace(x.ProductDescription) ||
            string.IsNullOrWhiteSpace(x.Unit) ||
            x.ListPrice < 0))
        {
            return "Ürün kodu, açıklaması, birimi ve geçerli liste fiyatı zorunludur.";
        }

        return null;
    }
}
