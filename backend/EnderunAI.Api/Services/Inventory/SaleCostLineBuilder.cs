using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Models;

namespace EnderunAI.Api.Services.Inventory;

/// <summary>
/// SATILAN MALIN MALİYET FİŞ SATIRLARI — TEK KAYNAK.
///
/// Perakende fişi ve stoklu satış faturası aynı muhasebe kaydını
/// üretir: borç 621 Satılan Ticari Mallar Maliyeti, alacak kartın
/// kategorisine göre 153 (ticari mal) ya da 150 (sarf).
///
/// Kural iki belgeye ayrı ayrı yazılsaydı, biri kategoriye göre
/// ayırırken diğeri sabit 153'e yazabilir ve aynı malzeme hangi
/// ekrandan satıldığına göre farklı hesaptan düşerdi. Fark mizanda
/// değil, ancak envanter sayımında görülürdü.
///
/// BORÇ TARAFI HER ZAMAN 621: sarf malzeme de satıldığında 740'a
/// değil 621'e yazılır. 740 projede TÜKETİLEN malzemenin üretim
/// maliyetidir; satılan mal tüketilmemiştir. Ayrım
/// <see cref="IInventoryAccountResolver.ResolveCostOfGoodsSoldAccountAsync"/>
/// içinde gerekçesiyle duruyor.
/// </summary>
public interface ISaleCostLineBuilder
{
    /// <summary>
    /// Verilen maliyetlerden fiş satırlarını üretir. Maliyeti sıfır
    /// olan kalem satır üretmez — sıfır tutarlı satır fişi şişirir,
    /// hiçbir şey anlatmaz.
    /// </summary>
    Task<IReadOnlyList<AccountingVoucherLineRequest>> BuildAsync(
        Guid companyId,
        IReadOnlyList<StockSaleCost> costs,
        SaleCostLineContext context,
        CancellationToken cancellationToken);
}

/// <summary>Fiş satırlarının taşıyacağı ortak künye.</summary>
public sealed record SaleCostLineContext(
    string Reference,
    DateTime DocumentDate,
    string CurrencyCode,
    decimal ExchangeRate,
    Guid? ProjectId,
    string? CostCenterCode);

public sealed class SaleCostLineBuilder(
    IInventoryAccountResolver accounts) : ISaleCostLineBuilder
{
    public async Task<IReadOnlyList<AccountingVoucherLineRequest>> BuildAsync(
        Guid companyId,
        IReadOnlyList<StockSaleCost> costs,
        SaleCostLineContext context,
        CancellationToken cancellationToken)
    {
        var lines = new List<AccountingVoucherLineRequest>();

        if (costs.Count == 0) return lines;

        // Aynı fişte hem sarf hem ticari mal olabilir; ALACAK tarafı
        // türe göre GRUPLANIR, borç tarafı tek 621 satırında toplanır.
        var byKind = new Dictionary<InventoryAccountingKind, decimal>();

        foreach (var cost in costs)
        {
            if (cost.TotalCost <= 0m) continue;

            var kind = await accounts.ResolveKindAsync(
                cost.InventoryItemId, cancellationToken);

            byKind[kind] = byKind.TryGetValue(kind, out var running)
                ? running + cost.TotalCost
                : cost.TotalCost;
        }

        if (byKind.Count == 0) return lines;

        // Borç toplamı alacakların TOPLAMINDAN türetiliyor; ayrı ayrı
        // yuvarlanıp toplansaydı fiş bir kuruş dengesiz kalabilirdi.
        var total = decimal.Round(byKind.Values.Sum(), 2);

        var costAccountId = await accounts.ResolveCostOfGoodsSoldAccountAsync(
            companyId, cancellationToken);

        lines.Add(new AccountingVoucherLineRequest(
            AccountingAccountId: costAccountId,
            Description: $"Satılan malın maliyeti — {context.Reference}",
            DebitAmount: total,
            CreditAmount: 0m,
            CurrencyCode: context.CurrencyCode,
            ExchangeRate: context.ExchangeRate,
            CurrentAccountId: null,
            ProjectId: context.ProjectId,
            CostCenterCode: context.CostCenterCode,
            DocumentNumber: context.Reference,
            DocumentDate: context.DocumentDate,
            DueDate: null));

        foreach (var (kind, amount) in byKind.OrderBy(x => x.Key))
        {
            var stockAccountId = await accounts.ResolveStockAccountAsync(
                companyId, kind, cancellationToken);

            lines.Add(new AccountingVoucherLineRequest(
                AccountingAccountId: stockAccountId,
                Description: kind == InventoryAccountingKind.TradeGood
                    ? $"Ticari mal stok çıkışı — {context.Reference}"
                    : $"Sarf malzeme stok çıkışı — {context.Reference}",
                DebitAmount: 0m,
                CreditAmount: decimal.Round(amount, 2),
                CurrencyCode: context.CurrencyCode,
                ExchangeRate: context.ExchangeRate,
                CurrentAccountId: null,
                ProjectId: context.ProjectId,
                CostCenterCode: context.CostCenterCode,
                DocumentNumber: context.Reference,
                DocumentDate: context.DocumentDate,
                DueDate: null));
        }

        return lines;
    }
}
