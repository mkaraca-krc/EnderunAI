using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Retail;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

public sealed record RetailSaleLineRequest(
    Guid InventoryItemId,
    decimal Quantity,
    decimal DiscountRate);

public sealed record CreateRetailSaleRequest(
    Guid CompanyId,
    Guid WarehouseId,
    DateTime SaleDate,
    Guid? CustomerCurrentAccountId,
    string? WalkInCustomerName,
    int PaymentMethod,
    DateTime? DueDate,
    decimal OverallDiscountRate,
    decimal CashAmount,
    Guid? CashAccountId,
    List<RetailSaleLineRequest> Items);

public sealed record RejectRetailSaleRequest(string Reason);

/// <summary>
/// Perakende satış uçları.
///
/// SATIŞ EKRANI MALİYETİ HİÇ GÖRMEZ: ürün araması bu controller'daki
/// dar uçtan besleniyor ve o uç AverageUnitCost okumuyor. Mevcut stok
/// uçları (InventoryController) maliyeti döndürmeye devam ediyor —
/// oradaki davranış değiştirilmedi, çünkü onu okuyan satın alma ve
/// muhasebe ekranları maliyeti görmek zorunda.
/// </summary>
[ApiController]
[Route("api/perakende")]
public sealed class RetailSalesController(
    AppDbContext db,
    IRetailSaleService sales,
    IExtraPaymentVisibilityService cashVisibility) : ControllerBase
{
    /// <summary>
    /// Satış ekranının ürün araması. Kod, ad ve barkodla arar.
    ///
    /// DÖNDÜRÜLMEYEN ALAN: maliyet. Satış personeli fiyatı ve tavanı
    /// görür, malın kaça alındığını görmez.
    /// </summary>
    [HttpGet("urunler")]
    [RequirePermission(PermissionCatalog.Keys.SalesView)]
    public async Task<IActionResult> SearchProducts(
        [FromQuery] Guid warehouseId,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var term = search?.Trim();

        var query = db.InventoryItems
            .AsNoTracking()
            .Where(x => x.SalesPrice != null);

        if (!string.IsNullOrWhiteSpace(term))
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.Code, $"%{term}%")
                || EF.Functions.ILike(x.Name, $"%{term}%")
                || (x.Barcode != null && EF.Functions.ILike(x.Barcode, $"%{term}%")));
        }

        var cards = await query
            .OrderBy(x => x.Name)
            .Take(50)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                x.Unit,
                x.Barcode,
                SalesPrice = x.SalesPrice!.Value,
                x.MaxDiscountRate,
                VatRate = x.VatRate ?? 0m,
                OnHand = x.WarehouseStocks
                    .Where(s => s.WarehouseId == warehouseId)
                    .Sum(s => (decimal?)s.Quantity) ?? 0m
            })
            .ToListAsync(cancellationToken);

        // Satılabilir adet, onay bekleyen fişlerdeki miktar düşülerek
        // hesaplanıyor; ekranda görünen sayı budur.
        var ids = cards.Select(x => x.Id).ToArray();

        var reserved = await db.RetailSaleItems
            .AsNoTracking()
            .Where(x => ids.Contains(x.InventoryItemId)
                && x.RetailSale.WarehouseId == warehouseId
                && (x.RetailSale.Status == RetailSaleStatus.Draft
                    || x.RetailSale.Status == RetailSaleStatus.PendingApproval))
            .GroupBy(x => x.InventoryItemId)
            .Select(g => new { g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Quantity, cancellationToken);

        return Ok(cards.Select(x => new
        {
            x.Id,
            x.Code,
            x.Name,
            x.Unit,
            x.Barcode,
            x.SalesPrice,
            x.MaxDiscountRate,
            x.VatRate,
            Available = x.OnHand - (reserved.TryGetValue(x.Id, out var held) ? held : 0m)
        }));
    }

    /// <summary>
    /// Satışın yapılabileceği merkez depolar ve tahsilat hesapları.
    ///
    /// AYRI UÇ, ÇÜNKÜ SATIŞ PERSONELİNDE `inventory.view` YOK: genel
    /// depo ucu stok değeri ve maliyet taşıyan ekranlara hizmet ediyor.
    /// Burada yalnız seçim için gereken kimlik ve ad dönüyor.
    /// </summary>
    [HttpGet("kaynaklar")]
    [RequirePermission(PermissionCatalog.Keys.SalesView)]
    public async Task<IActionResult> GetResources(CancellationToken cancellationToken)
    {
        var warehouses = await db.Warehouses
            .AsNoTracking()
            .Where(x => x.Type == WarehouseType.Central)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Code, x.Name, x.CompanyId })
            .ToListAsync(cancellationToken);

        var cashAccounts = await db.CashAccounts
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Code, x.Name, Type = (int)x.Type, x.CompanyId })
            .ToListAsync(cancellationToken);

        var customers = await db.CurrentAccounts
            .AsNoTracking()
            .Where(x => x.Roles.HasFlag(CurrentAccountRoles.Customer)
                && x.Status == CurrentAccountStatus.Approved)
            .OrderBy(x => x.Title)
            .Take(500)
            .Select(x => new { x.Id, x.Code, x.Title })
            .ToListAsync(cancellationToken);

        return Ok(new { warehouses, cashAccounts, customers });
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.SalesView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? status,
        CancellationToken cancellationToken)
    {
        var canSeeCash = await cashVisibility.CanViewExtraPaymentAsync(cancellationToken);

        var query = db.RetailSales.AsNoTracking();

        if (status.HasValue)
            query = query.Where(x => (int)x.Status == status.Value);

        var rows = await query
            .OrderByDescending(x => x.SaleDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.DocumentNumber,
                x.SaleDate,
                Status = (int)x.Status,
                PaymentMethod = (int)x.PaymentMethod,
                x.DueDate,
                CustomerTitle = x.CustomerCurrentAccount != null
                    ? x.CustomerCurrentAccount.Title
                    : x.WalkInCustomerName,
                x.GrandTotal,
                x.RecordedAmount,
                x.CashAmount,
                x.ApprovalReason,
                x.DecisionReason,
                x.SalesInvoiceId
            })
            .ToListAsync(cancellationToken);

        // ELDEN MASKESİ: yetkisiz kullanıcıya elden tutar null döner ve
        // kaç kayıtta gizlendiği ayrıca bildirilir — tutar sızmaz ama
        // eksik olduğu belli olur. Desen VehiclesController ile aynı.
        var hiddenCount = canSeeCash ? 0 : rows.Count(x => x.CashAmount > 0);

        return Ok(new
        {
            items = rows.Select(x => new
            {
                x.Id,
                x.DocumentNumber,
                x.SaleDate,
                x.Status,
                x.PaymentMethod,
                x.DueDate,
                x.CustomerTitle,
                x.GrandTotal,
                x.RecordedAmount,
                CashAmount = canSeeCash ? x.CashAmount : (decimal?)null,
                x.ApprovalReason,
                x.DecisionReason,
                x.SalesInvoiceId
            }),
            hiddenCount
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.SalesCreate)]
    public async Task<IActionResult> Create(
        CreateRetailSaleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var sale = await sales.CreateAsync(
                new RetailSaleInput(
                    request.CompanyId,
                    request.WarehouseId,
                    request.SaleDate,
                    request.CustomerCurrentAccountId,
                    request.WalkInCustomerName,
                    (RetailPaymentMethod)request.PaymentMethod,
                    request.DueDate,
                    request.OverallDiscountRate,
                    request.CashAmount,
                    request.CashAccountId,
                    request.Items.Select(x => new RetailSaleLineInput(
                        x.InventoryItemId, x.Quantity, x.DiscountRate)).ToList()),
                cancellationToken);

            return Ok(new { sale.Id, sale.DocumentNumber, sale.GrandTotal });
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary>
    /// Fişi sonuçlandırmaya gönderir. Tavan aşımı ya da vade varsa
    /// finans onayına düşer; yoksa satış anında tamamlanır.
    /// </summary>
    [HttpPost("{id:guid}/gonder")]
    [RequirePermission(PermissionCatalog.Keys.SalesCreate)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var sale = await sales.SubmitAsync(id, cancellationToken);

            return Ok(new
            {
                Status = (int)sale.Status,
                sale.ApprovalReason,
                sale.SalesInvoiceId
            });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary>
    /// Finans onayı. AYRI İZİN: satışı hazırlayan personel kendi
    /// iskontosunu onaylayamaz.
    /// </summary>
    [HttpPost("{id:guid}/onayla")]
    [RequirePermission(PermissionCatalog.Keys.SalesApprove)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var sale = await sales.ApproveAsync(id, cancellationToken);
            return Ok(new { Status = (int)sale.Status, sale.SalesInvoiceId });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("{id:guid}/reddet")]
    [RequirePermission(PermissionCatalog.Keys.SalesApprove)]
    public async Task<IActionResult> Reject(
        Guid id, RejectRetailSaleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var sale = await sales.RejectAsync(id, request.Reason, cancellationToken);
            return Ok(new { Status = (int)sale.Status, sale.DecisionReason });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary>
    /// Satış fiyatı ve iskonto tavanı güncelleme — tek tek ya da toplu.
    /// Yönetim işi olduğu için stok düzenleme izni aranıyor.
    /// </summary>
    [HttpPut("fiyatlar")]
    [RequirePermission(PermissionCatalog.Keys.InventoryEdit)]
    public async Task<IActionResult> UpdatePricing(
        List<RetailPricingRequest> request, CancellationToken cancellationToken)
    {
        var ids = request.Select(x => x.InventoryItemId).ToArray();

        var cards = await db.InventoryItems
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        foreach (var row in request)
        {
            if (!cards.TryGetValue(row.InventoryItemId, out var card))
                continue;

            if (row.MaxDiscountRate is < 0 or > 100)
            {
                return BadRequest(new
                {
                    message = $"{card.Name}: iskonto tavanı 0 ile 100 arasında olmalıdır."
                });
            }

            card.SalesPrice = row.SalesPrice;
            card.MaxDiscountRate = row.MaxDiscountRate;
            card.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { updated = request.Count });
    }
}

public sealed record RetailPricingRequest(
    Guid InventoryItemId,
    decimal? SalesPrice,
    decimal MaxDiscountRate);
