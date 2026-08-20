using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Inventory;

public sealed record StockAccountingLine(
    string Kind,
    string StockAccountCode,
    decimal StockValue,
    decimal AccountBalance,
    decimal Difference);

public sealed record StockAccountingConsistencyReport(
    DateTime AsOfUtc,
    IReadOnlyList<StockAccountingLine> Lines,
    decimal PendingInvoiceBalance,
    bool IsConsistent,
    string Summary);

/// <summary>
/// STOK ↔ MUHASEBE TUTARLILIK RAPORU.
///
/// İki bağımsız kaynağı karşılaştırır:
///   (a) DEPODAKİ değer — miktar × ağırlıklı ortalama maliyet,
///   (b) MİZANDAKİ bakiye — 150 ve 153 hesaplarının borç-alacak farkı.
///
/// İkisi tutmuyorsa bir yerde stok muhasebeye yazılmadan hareket
/// etmiştir. Bu rapor olmasaydı fark ancak dönem sonunda, kimsenin
/// sebebini hatırlamadığı bir tutarsızlık olarak çıkardı.
///
/// 379.01 bakiyesi AYRICA gösteriliyor: o bir tutarsızlık değil,
/// "malı aldık faturası gelmedi" demek. Kalıcı bakiye eksik fatura
/// takibinin kendisidir.
/// </summary>
public interface IStockAccountingConsistencyService
{
    Task<StockAccountingConsistencyReport> BuildAsync(
        Guid companyId, CancellationToken cancellationToken);
}

public sealed class StockAccountingConsistencyService(
    AppDbContext db, IInventoryAccountResolver accounts)
    : IStockAccountingConsistencyService
{
    public async Task<StockAccountingConsistencyReport> BuildAsync(
        Guid companyId, CancellationToken cancellationToken)
    {
        // Depodaki değer: kartın kategorisine göre ayrılıyor ki
        // mizandaki iki ayrı hesapla karşılaştırılabilsin.
        var stockRows = await db.WarehouseStocks
            .Where(x => x.Warehouse.CompanyId == companyId)
            .Select(x => new
            {
                Kind = x.InventoryItem.InventoryCategory != null
                    ? x.InventoryItem.InventoryCategory.AccountingKind
                    : InventoryAccountingKind.Consumable,
                Value = x.Quantity * x.InventoryItem.AverageUnitCost
            })
            .ToListAsync(cancellationToken);

        var stockValueByKind = stockRows
            .GroupBy(x => x.Kind)
            .ToDictionary(x => x.Key, x => decimal.Round(x.Sum(v => v.Value), 2));

        var lines = new List<StockAccountingLine>();

        foreach (var kind in new[]
        {
            InventoryAccountingKind.Consumable,
            InventoryAccountingKind.TradeGood
        })
        {
            var accountId = await TryResolveAsync(companyId, kind, cancellationToken);
            var stockValue = stockValueByKind.GetValueOrDefault(kind, 0m);

            var balance = accountId is Guid id
                ? await BalanceAsync(id, cancellationToken)
                : 0m;

            lines.Add(new StockAccountingLine(
                Kind: kind == InventoryAccountingKind.TradeGood
                    ? "Ticari mal"
                    : "Sarf malzeme",
                StockAccountCode: kind == InventoryAccountingKind.TradeGood
                    ? InventoryAccountResolver.TradeGoodStockCode
                    : InventoryAccountResolver.ConsumableStockCode,
                StockValue: stockValue,
                AccountBalance: balance,
                Difference: decimal.Round(stockValue - balance, 2)));
        }

        var pending = 0m;

        try
        {
            var grir = await accounts.ResolveGoodsReceivedNotInvoicedAccountAsync(
                companyId, cancellationToken);

            // 379.01 pasif karakterli: alacak bakiye borcu gösterir.
            pending = -await BalanceAsync(grir, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Hesap henüz açılmamışsa rapor yine üretilir; eksik hesap
            // tutarsızlık DEĞİL, yapılandırma eksiğidir.
        }

        var consistent = lines.All(x => x.Difference == 0m);

        var summary = consistent
            ? "Stok değeri ile muhasebe bakiyesi birebir tutuyor."
            : "TUTARSIZLIK: " + string.Join(" · ", lines
                .Where(x => x.Difference != 0m)
                .Select(x =>
                    $"{x.Kind} ({x.StockAccountCode}) fark "
                    + $"{Formatting.TurkishFormat.Amount(x.Difference)} TL"));

        return new StockAccountingConsistencyReport(
            DateTime.UtcNow, lines, decimal.Round(pending, 2), consistent, summary);
    }

    private async Task<Guid?> TryResolveAsync(
        Guid companyId, InventoryAccountingKind kind, CancellationToken cancellationToken)
    {
        try
        {
            return await accounts.ResolveStockAccountAsync(companyId, kind, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<decimal> BalanceAsync(
        Guid accountId, CancellationToken cancellationToken)
    {
        // Yalnız KESİNLEŞMİŞ fişler: taslak fiş mizanda yoktur, onu
        // saymak raporu gerçekte olmayan bir farkla kirletirdi.
        var sums = await db.AccountingVoucherLines
            .Where(x => x.AccountingAccountId == accountId
                && x.AccountingVoucher.Status == AccountingVoucherStatus.Posted)
            .GroupBy(x => 1)
            .Select(g => new
            {
                Debit = g.Sum(x => x.DebitAmount),
                Credit = g.Sum(x => x.CreditAmount)
            })
            .SingleOrDefaultAsync(cancellationToken);

        return sums is null ? 0m : decimal.Round(sums.Debit - sums.Credit, 2);
    }
}
