using System.Security.Claims;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Route("api/goods-receipts")]
[Authorize]
public sealed class GoodsReceiptsController(
    AppDbContext db,
    IGoodsReceiptPostingService postingService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GoodsReceipt>>> GetAll(
        [FromQuery] Guid? purchaseOrderId,
        CancellationToken cancellationToken)
    {
        var query = db.GoodsReceipts
            .AsNoTracking()
            .Include(x => x.Items)
            .OrderByDescending(x => x.ReceiptDateUtc)
            .AsQueryable();

        if (purchaseOrderId.HasValue)
            query = query.Where(x => x.PurchaseOrderId == purchaseOrderId.Value);

        return Ok(await query.ToListAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GoodsReceipt>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var receipt = await db.GoodsReceipts
            .AsNoTracking()
            .Include(x => x.Items)
                .ThenInclude(x => x.Material)
            .Include(x => x.PurchaseOrder)
                .ThenInclude(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return receipt is null ? NotFound() : Ok(receipt);
    }

    [HttpPost("{id:guid}/post")]
    public async Task<ActionResult<GoodsReceipt>> Post(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            Guid? userId = null;
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(claim, out var parsedUserId))
                userId = parsedUserId;

            var receipt = await postingService.PostAsync(id, userId, cancellationToken);
            return Ok(receipt);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
