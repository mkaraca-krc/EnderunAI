using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Accounting;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Inventory;

/// <summary>
/// SAYIM DÜZELTME FİŞİ — oturum başına TEK fiş.
///
/// Noksan ve fazla AYNI fişte ama ayrı satırlarda: bir sayımda hem
/// eksik hem fazla çıkabiliyor ve ikisi netleştirilseydi "ne kadar
/// fire var" sorusu cevapsız kalırdı. Net 100 TL fark, 500 kayıp ve
/// 400 fazlanın toplamı da olabilir; bu iki tablo aynı şey değil.
///
/// FARK HESAPLARI FİNANS AYARINDAN (kullanıcı isteği). Boş bırakılırsa
/// S6c'de açılan 689.02 / 649.03 kullanılır — sistem durmuyor ama mali
/// müşavir isterse başka hesaba (ör. 157) yönlendirebiliyor.
/// </summary>
public interface IStockCountVoucherPoster
{
    Task<Guid?> PostAsync(
        StockCountSession session,
        IReadOnlyList<StockCountLine> adjustedLines,
        CancellationToken cancellationToken);
}

public sealed class StockCountVoucherPoster(
    AppDbContext db,
    IInventoryAccountResolver accounts,
    IAccountingIntegrationService integration,
    IAccountingVoucherService vouchers) : IStockCountVoucherPoster
{
    public async Task<Guid?> PostAsync(
        StockCountSession session,
        IReadOnlyList<StockCountLine> adjustedLines,
        CancellationToken cancellationToken)
    {
        // Maliyeti sıfır olan satır fişe girmez: kart hiç faturalı
        // girmemiş demektir, maliyeti bilinmiyordur. Sıfır tutarlı
        // satır bilgi üretmez.
        var shortage = new Dictionary<InventoryAccountingKind, decimal>();
        var surplus = new Dictionary<InventoryAccountingKind, decimal>();

        foreach (var line in adjustedLines)
        {
            var difference = line.Difference ?? 0m;
            var value = decimal.Round(Math.Abs(line.UnitCostAtCount * difference), 2);

            if (value <= 0m) continue;

            var kind = await accounts.ResolveKindAsync(line.InventoryItemId, cancellationToken);
            var bucket = difference < 0m ? shortage : surplus;

            bucket[kind] = bucket.TryGetValue(kind, out var running) ? running + value : value;
        }

        if (shortage.Count == 0 && surplus.Count == 0) return null;

        var settings = await integration.GetOrCreateFinanceSettingsAsync(
            session.CompanyId, cancellationToken);

        var reference = session.DocumentNumber;
        var lines = new List<AccountingVoucherLineRequest>();

        AccountingVoucherLineRequest Line(
            Guid accountId, string description, decimal debit, decimal credit) =>
            new(
                AccountingAccountId: accountId,
                Description: $"{description} — {reference}",
                DebitAmount: debit,
                CreditAmount: credit,
                CurrencyCode: "TRY",
                ExchangeRate: 1m,
                CurrentAccountId: null,
                // Sayım farkı bir projenin maliyeti değil; proje
                // etiketi konsaydı fire, hiç ilgisi olmayan bir
                // projenin maliyetine yazılırdı.
                ProjectId: null,
                CostCenterCode: null,
                DocumentNumber: reference,
                DocumentDate: session.CountDate,
                DueDate: null);

        if (shortage.Count > 0)
        {
            var total = decimal.Round(shortage.Values.Sum(), 2);

            var shortageAccountId = settings.StockCountShortageAccountId
                ?? await accounts.ResolveInventoryShortageAccountAsync(
                    session.CompanyId, cancellationToken);

            lines.Add(Line(shortageAccountId, "Sayım noksanı", total, 0m));

            foreach (var (kind, amount) in shortage.OrderBy(x => x.Key))
            {
                lines.Add(Line(
                    await accounts.ResolveStockAccountAsync(session.CompanyId, kind, cancellationToken),
                    kind == InventoryAccountingKind.TradeGood
                        ? "Ticari mal sayım noksanı"
                        : "Sarf malzeme sayım noksanı",
                    0m, decimal.Round(amount, 2)));
            }
        }

        if (surplus.Count > 0)
        {
            var total = decimal.Round(surplus.Values.Sum(), 2);

            var surplusAccountId = settings.StockCountSurplusAccountId
                ?? await accounts.ResolveInventorySurplusAccountAsync(
                    session.CompanyId, cancellationToken);

            foreach (var (kind, amount) in surplus.OrderBy(x => x.Key))
            {
                lines.Add(Line(
                    await accounts.ResolveStockAccountAsync(session.CompanyId, kind, cancellationToken),
                    kind == InventoryAccountingKind.TradeGood
                        ? "Ticari mal sayım fazlası"
                        : "Sarf malzeme sayım fazlası",
                    decimal.Round(amount, 2), 0m));
            }

            lines.Add(Line(surplusAccountId, "Sayım fazlası", 0m, total));
        }

        var zoneName = session.WarehouseZoneId is null
            ? "tüm depo"
            : await db.WarehouseZones
                .Where(x => x.Id == session.WarehouseZoneId)
                .Select(x => x.Name)
                .SingleOrDefaultAsync(cancellationToken) ?? "bölge";

        var created = await vouchers.CreateAsync(
            new CreateAccountingVoucherRequest(
                CompanyId: session.CompanyId,
                VoucherType: (int)AccountingVoucherType.Journal,
                VoucherDate: session.CountDate,
                CurrencyCode: "TRY",
                ExchangeRate: 1m,
                Description: $"Sayım düzeltmesi {reference} — {session.Name} ({zoneName})",
                ReferenceNumber: reference,
                SourceModule: "StockCount",
                SourceEntityId: session.Id,
                Lines: lines),
            cancellationToken);

        await vouchers.PostAsync(created.Id, cancellationToken);

        return created.Id;
    }
}
