using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Models;

namespace EnderunAI.Api.Services.Inventory;

/// <summary>
/// STOK ÇIKIŞ FİŞ SATIRLARI — TEK KAYNAK.
///
/// Stok depodan üç sebeple çıkıyor ve üçünde de ALACAK tarafı aynı
/// kuralla belirleniyor: kartın kategorisine göre 153 (ticari mal) ya
/// da 150 (sarf). Değişen yalnız BORÇ tarafı:
///
///   satış           → 621 Satılan Ticari Mallar Maliyeti
///   projeye çıkış   → 740.03.09 Kullanılan Malzemeler
///   projesiz çıkış  → 770 Genel Yönetim Giderleri
///   sayım noksanı   → 689.02 Stok Sayım Noksanları
///
/// Sayım FAZLASI ters yöndedir: stok artar, borç 150/153, alacak
/// 649.03.
///
/// Alacak kuralı her yola ayrı yazılsaydı, biri kategoriye göre
/// ayırırken diğeri sabit 153'e yazabilir ve aynı malzeme hangi
/// kapıdan çıktığına göre farklı hesaptan düşerdi. Fark mizanda değil,
/// ancak envanter sayımında görülürdü.
/// </summary>
public interface IStockOutflowLineBuilder
{
    /// <summary>
    /// SATIŞ: borç 621. Maliyeti sıfır olan kalem satır üretmez —
    /// sıfır tutarlı satır fişi şişirir, hiçbir şey anlatmaz.
    /// </summary>
    Task<IReadOnlyList<AccountingVoucherLineRequest>> BuildSaleCostAsync(
        Guid companyId,
        IReadOnlyList<StockSaleCost> costs,
        SaleCostLineContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// TÜKETİM: proje varsa borç 740, yoksa 770.
    ///
    /// Türden bağımsız — ticari mal da projede tüketilirse 740'a
    /// yazılır; satılmamıştır. Ayrılan yalnız alacak tarafıdır.
    /// </summary>
    Task<IReadOnlyList<AccountingVoucherLineRequest>> BuildConsumptionAsync(
        Guid companyId,
        IReadOnlyList<StockSaleCost> costs,
        SaleCostLineContext context,
        bool projectScoped,
        CancellationToken cancellationToken);

    /// <summary>
    /// SAYIM FARKI. Noksanda borç 689.02 / alacak stok; fazlada borç
    /// stok / alacak 649.03 — yön <paramref name="surplus"/> ile.
    /// </summary>
    Task<IReadOnlyList<AccountingVoucherLineRequest>> BuildVarianceAsync(
        Guid companyId,
        IReadOnlyList<StockSaleCost> costs,
        SaleCostLineContext context,
        bool surplus,
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

public sealed class StockOutflowLineBuilder(
    IInventoryAccountResolver accounts) : IStockOutflowLineBuilder
{
    public async Task<IReadOnlyList<AccountingVoucherLineRequest>> BuildSaleCostAsync(
        Guid companyId,
        IReadOnlyList<StockSaleCost> costs,
        SaleCostLineContext context,
        CancellationToken cancellationToken) =>
        await BuildAsync(
            companyId, costs, context,
            await accounts.ResolveCostOfGoodsSoldAccountAsync(companyId, cancellationToken),
            "Satılan malın maliyeti",
            surplus: false,
            cancellationToken);

    public async Task<IReadOnlyList<AccountingVoucherLineRequest>> BuildConsumptionAsync(
        Guid companyId,
        IReadOnlyList<StockSaleCost> costs,
        SaleCostLineContext context,
        bool projectScoped,
        CancellationToken cancellationToken) =>
        await BuildAsync(
            companyId, costs, context,
            projectScoped
                ? await accounts.ResolveProjectConsumptionAccountAsync(companyId, cancellationToken)
                : await accounts.ResolveGeneralAdminExpenseAccountAsync(companyId, cancellationToken),
            projectScoped ? "Projede kullanılan malzeme" : "Merkez sarfiyatı",
            surplus: false,
            cancellationToken);

    public async Task<IReadOnlyList<AccountingVoucherLineRequest>> BuildVarianceAsync(
        Guid companyId,
        IReadOnlyList<StockSaleCost> costs,
        SaleCostLineContext context,
        bool surplus,
        CancellationToken cancellationToken) =>
        await BuildAsync(
            companyId, costs, context,
            surplus
                ? await accounts.ResolveInventorySurplusAccountAsync(companyId, cancellationToken)
                : await accounts.ResolveInventoryShortageAccountAsync(companyId, cancellationToken),
            surplus ? "Sayım fazlası" : "Sayım noksanı",
            surplus,
            cancellationToken);

    /// <param name="surplus">
    /// true ise stok ARTIYOR: borç stok hesabı, alacak karşı hesap.
    /// Yön tek yerde dönüyor ki hiçbir yol yanlışlıkla ters
    /// yazılmasın.
    /// </param>
    private async Task<IReadOnlyList<AccountingVoucherLineRequest>> BuildAsync(
        Guid companyId,
        IReadOnlyList<StockSaleCost> costs,
        SaleCostLineContext context,
        Guid counterAccountId,
        string counterLabel,
        bool surplus,
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

        // Karşı taraf toplamı stok satırlarının TOPLAMINDAN türetiliyor;
        // ayrı ayrı yuvarlanıp toplansaydı fiş bir kuruş dengesiz
        // kalabilirdi.
        var total = decimal.Round(byKind.Values.Sum(), 2);

        lines.Add(new AccountingVoucherLineRequest(
            AccountingAccountId: counterAccountId,
            Description: $"{counterLabel} — {context.Reference}",
            DebitAmount: surplus ? 0m : total,
            CreditAmount: surplus ? total : 0m,
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
                    ? $"Ticari mal stok hareketi — {context.Reference}"
                    : $"Sarf malzeme stok hareketi — {context.Reference}",
                DebitAmount: surplus ? decimal.Round(amount, 2) : 0m,
                CreditAmount: surplus ? 0m : decimal.Round(amount, 2),
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
