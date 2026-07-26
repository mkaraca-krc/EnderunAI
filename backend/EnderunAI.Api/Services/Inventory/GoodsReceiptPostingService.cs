using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Inventory;

public sealed class GoodsReceiptPostingService(AppDbContext db) : IGoodsReceiptPostingService
{
    public async Task<GoodsReceipt> PostAsync(
        Guid goodsReceiptId,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var receipt = await db.GoodsReceipts
            .Include(x => x.Items)
            .Include(x => x.PurchaseOrder)
                .ThenInclude(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == goodsReceiptId, cancellationToken)
            ?? throw new InvalidOperationException("Mal kabul fişi bulunamadı.");

        if (receipt.Status == GoodsReceiptStatus.Posted)
            return receipt;

        if (receipt.Status == GoodsReceiptStatus.Cancelled)
            throw new InvalidOperationException("İptal edilmiş mal kabul fişi post edilemez.");

        if (receipt.Items.Count == 0)
            throw new InvalidOperationException("Mal kabul fişinde en az bir kalem olmalıdır.");

        foreach (var item in receipt.Items)
        {
            if (item.Quantity <= 0)
                throw new InvalidOperationException("Mal kabul miktarı sıfırdan büyük olmalıdır.");

            var orderItem = receipt.PurchaseOrder.Items
                .SingleOrDefault(x => x.Id == item.PurchaseOrderItemId)
                ?? throw new InvalidOperationException("Sipariş kalemi bulunamadı.");

            if (orderItem.MaterialId != item.MaterialId)
                throw new InvalidOperationException("Mal kabul malzemesi sipariş kalemiyle eşleşmiyor.");

            var remaining = orderItem.Quantity - orderItem.ReceivedQuantity;
            if (item.Quantity > remaining)
                throw new InvalidOperationException("Mal kabul miktarı kalan sipariş miktarını aşamaz.");

            var stock = await db.WarehouseStocks
                .SingleOrDefaultAsync(
                    x => x.WarehouseId == receipt.WarehouseId && x.MaterialId == item.MaterialId,
                    cancellationToken);

            if (stock is null)
            {
                stock = new WarehouseStock
                {
                    WarehouseId = receipt.WarehouseId,
                    MaterialId = item.MaterialId,
                    Quantity = 0m,
                    ReservedQuantity = 0m,
                    AverageUnitCost = 0m,
                    CreatedByUserId = userId
                };
                db.WarehouseStocks.Add(stock);
            }

            var oldValue = stock.Quantity * stock.AverageUnitCost;
            var incomingValue = item.Quantity * item.UnitCost;
            var newQuantity = stock.Quantity + item.Quantity;

            stock.Quantity = newQuantity;
            stock.AverageUnitCost = newQuantity == 0m
                ? 0m
                : (oldValue + incomingValue) / newQuantity;
            stock.UpdatedAtUtc = DateTime.UtcNow;
            stock.UpdatedByUserId = userId;

            orderItem.ReceivedQuantity += item.Quantity;
            orderItem.UpdatedAtUtc = DateTime.UtcNow;
            orderItem.UpdatedByUserId = userId;

            db.StockMovements.Add(new StockMovement
            {
                CompanyId = receipt.CompanyId,
                WarehouseId = receipt.WarehouseId,
                MaterialId = item.MaterialId,
                ProjectId = receipt.PurchaseOrder.ProjectId,
                MovementType = StockMovementType.Receipt,
                MovementDateUtc = receipt.ReceiptDateUtc,
                Quantity = item.Quantity,
                UnitCost = item.UnitCost,
                ReferenceType = nameof(GoodsReceipt),
                ReferenceId = receipt.Id,
                DocumentNumber = receipt.ReceiptNumber,
                Description = receipt.Description,
                CreatedByUserId = userId
            });
        }

        var allReceived = receipt.PurchaseOrder.Items.All(x => x.ReceivedQuantity >= x.Quantity);
        var anyReceived = receipt.PurchaseOrder.Items.Any(x => x.ReceivedQuantity > 0m);
        receipt.PurchaseOrder.Status = allReceived
            ? PurchaseOrderStatus.Completed
            : anyReceived
                ? PurchaseOrderStatus.PartiallyReceived
                : receipt.PurchaseOrder.Status;
        receipt.PurchaseOrder.UpdatedAtUtc = DateTime.UtcNow;
        receipt.PurchaseOrder.UpdatedByUserId = userId;

        receipt.Status = GoodsReceiptStatus.Posted;
        receipt.UpdatedAtUtc = DateTime.UtcNow;
        receipt.UpdatedByUserId = userId;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return receipt;
    }
}
