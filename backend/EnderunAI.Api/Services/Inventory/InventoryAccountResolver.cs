using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Inventory;

/// <summary>
/// KATEGORİ → MUHASEBE HESABI. Stokun hangi hesapta durduğu ve
/// çıkışta nereye yazıldığı YALNIZ buradan çözülür.
///
/// Tek kaynak olması şart: eşleme iki yere yazılsaydı, biri
/// güncellenip diğeri unutulduğunda stok bir hesaba girip başka bir
/// hesaptan çıkardı ve mizan sessizce şişerdi. Fark ancak dönem
/// sonunda, sayım tutmadığında görülürdü.
/// </summary>
public interface IInventoryAccountResolver
{
    /// <summary>Stokun durduğu hesap: sarf → 150, ticari mal → 153.</summary>
    Task<Guid> ResolveStockAccountAsync(
        Guid companyId, InventoryAccountingKind kind, CancellationToken cancellationToken);

    /// <summary>Çıkışta yazılan hesap: sarf → 740, ticari mal → 621.</summary>
    Task<Guid> ResolveConsumptionAccountAsync(
        Guid companyId, InventoryAccountingKind kind, CancellationToken cancellationToken);

    /// <summary>Bir kartın kategorisinden muhasebe karşılığı.</summary>
    Task<InventoryAccountingKind> ResolveKindAsync(
        Guid inventoryItemId, CancellationToken cancellationToken);
}

public sealed class InventoryAccountResolver(AppDbContext db) : IInventoryAccountResolver
{
    /// <summary>
    /// Tekdüzen hesap planı kodları. Kategori sistem geneli olduğu için
    /// eşleme de kod bazında; kimliğe çevirme şirket bazında yapılır.
    /// </summary>
    public const string ConsumableStockCode = "150";
    public const string TradeGoodStockCode = "153";
    public const string ConsumableExpenseCode = "740";
    public const string TradeGoodCostCode = "621";

    public Task<Guid> ResolveStockAccountAsync(
        Guid companyId, InventoryAccountingKind kind, CancellationToken cancellationToken) =>
        FindAsync(
            companyId,
            kind == InventoryAccountingKind.TradeGood ? TradeGoodStockCode : ConsumableStockCode,
            kind == InventoryAccountingKind.TradeGood
                ? "Ticari mal stok hesabı (153)"
                : "Sarf malzeme stok hesabı (150)",
            cancellationToken);

    public Task<Guid> ResolveConsumptionAccountAsync(
        Guid companyId, InventoryAccountingKind kind, CancellationToken cancellationToken) =>
        FindAsync(
            companyId,
            kind == InventoryAccountingKind.TradeGood ? TradeGoodCostCode : ConsumableExpenseCode,
            kind == InventoryAccountingKind.TradeGood
                ? "Satılan ticari mallar maliyeti (621)"
                : "Hizmet üretim maliyeti (740)",
            cancellationToken);

    public async Task<InventoryAccountingKind> ResolveKindAsync(
        Guid inventoryItemId, CancellationToken cancellationToken)
    {
        var kind = await db.InventoryItems
            .Where(x => x.Id == inventoryItemId)
            .Select(x => x.InventoryCategory != null
                ? (InventoryAccountingKind?)x.InventoryCategory.AccountingKind
                : null)
            .SingleOrDefaultAsync(cancellationToken);

        // Kategorisiz kart (S1 öncesinden kalanlar) SARF sayılır:
        // taahhüt işinin varsayılanı bu ve yanlış tarafa düşmek
        // istenirse ticari mal tarafı olmamalı — 153'e yazılan bir
        // sarf malzeme mali tabloda satılabilir mal gibi görünür.
        return kind ?? InventoryAccountingKind.Consumable;
    }

    private async Task<Guid> FindAsync(
        Guid companyId, string code, string label, CancellationToken cancellationToken)
    {
        // Önce tam kod, sonra o kodun altındaki ilk alt hesap: hesap
        // planında "150" ana hesabı bazen hareket görmez, kayıtlar
        // "150.01.02" gibi alt hesaplara yazılır.
        var id = await db.AccountingAccounts
            .Where(x => x.CompanyId == companyId && x.Code == code)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (id is not null) return id.Value;

        id = await db.AccountingAccounts
            .Where(x => x.CompanyId == companyId && x.Code.StartsWith(code + "."))
            .OrderBy(x => x.Code)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return id ?? throw new InvalidOperationException(
            $"{label} hesap planında bulunamadı ({code}). "
            + "Muhasebe → Hesap Planı'ndan açılmalı.");
    }
}
