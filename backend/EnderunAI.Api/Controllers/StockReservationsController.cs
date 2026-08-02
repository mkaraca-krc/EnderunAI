using EnderunAI.Api.Data;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/stock-reservations")]
public sealed class StockReservationsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.InventoryView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? warehouseId,
        [FromQuery] int? status,
        [FromQuery] string? search,
        [FromQuery] bool? activeOnly,
        [FromQuery] bool? expiredOnly,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var query = db.StockReservations
            .AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.Project)
            .Include(x => x.Warehouse)
            .Include(x => x.InventoryItem)
            .Include(x => x.PurchaseRequest)
            .AsQueryable();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (projectId.HasValue)
            query = query.Where(x => x.ProjectId == projectId.Value);

        if (warehouseId.HasValue)
            query = query.Where(x => x.WarehouseId == warehouseId.Value);

        if (status.HasValue)
            query = query.Where(x => (int)x.Status == status.Value);

        if (activeOnly == true)
            query = query.Where(x => x.IsActive);

        if (expiredOnly == true)
        {
            query = query.Where(
                x => x.ExpirationDate.HasValue && x.ExpirationDate.Value < now);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(
                x => x.ReservationNumber.Contains(term) ||
                     x.InventoryItem.Name.Contains(term) ||
                     x.InventoryItem.Code.Contains(term));
        }

        var requestedQuantities = await db.PurchaseRequestItems
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.Quantity, cancellationToken);

        var items = await query
            .OrderByDescending(x => x.ReservationDate)
            .ToListAsync(cancellationToken);

        return Ok(items.Select(x => new
        {
            x.Id,
            x.ReservationNumber,

            x.CompanyId,
            CompanyName = x.Company.Name,

            x.ProjectId,
            ProjectCode = x.Project.Code,
            ProjectName = x.Project.Name,

            x.WarehouseId,
            WarehouseCode = x.Warehouse.Code,
            WarehouseName = x.Warehouse.Name,

            x.InventoryItemId,
            InventoryItemCode = x.InventoryItem.Code,
            InventoryItemName = x.InventoryItem.Name,
            Unit = x.InventoryItem.Unit,

            x.PurchaseRequestId,
            RequestNumber = x.PurchaseRequest.RequestNumber,
            x.PurchaseRequestItemId,
            PurchaseRequestStatus = (int)x.PurchaseRequest.Status,

            RequestedQuantity = requestedQuantities.GetValueOrDefault(x.PurchaseRequestItemId),
            x.ReservedQuantity,
            x.ConsumedQuantity,
            RemainingQuantity = x.ReservedQuantity - x.ConsumedQuantity,

            x.ReservationDate,
            x.ExpirationDate,

            x.Status,
            StatusName = StatusName(x.Status),

            x.Description,
            IsExpired = x.ExpirationDate.HasValue && x.ExpirationDate.Value < now,
            x.IsActive
        }));
    }

    private static string StatusName(Models.StockReservationStatus status) => status switch
    {
        Models.StockReservationStatus.Active => "Aktif",
        Models.StockReservationStatus.PartiallyConsumed => "Kısmen Kullanıldı",
        Models.StockReservationStatus.Consumed => "Kullanıldı",
        Models.StockReservationStatus.Cancelled => "İptal Edildi",
        Models.StockReservationStatus.Expired => "Süresi Doldu",
        _ => status.ToString()
    };
}
