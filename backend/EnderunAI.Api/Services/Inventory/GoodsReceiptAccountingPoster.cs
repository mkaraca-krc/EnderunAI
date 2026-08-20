using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Accounting;
using Microsoft.EntityFrameworkCore;
using GoodsReceiptEntity = EnderunAI.Api.Models.GoodsReceipt.GoodsReceipt;

namespace EnderunAI.Api.Services.Inventory;

/// <summary>
/// MAL KABUL → MUHASEBE. Stokun mali tabloya girdiği tek an.
///
///   BORÇ  150 İlk Madde ve Malzeme   (sarf kategorileri)
///   BORÇ  153 Ticari Mallar          (ticari mal kategorileri)
///   ALACAK 379.01 Faturası Gelmemiş Mal Alımları
///
/// KDV BURADA YOK — mal kabulde fatura da KDV de yoktur. KDV, fatura
/// geldiğinde 191'e yazılır; mal kabulde yazılsaydı beyan edilecek
/// KDV, elde belge olmadan doğardı.
///
/// Tutar SİPARİŞ fiyatından (TRY'ye çevrilmiş) gelir. Fatura farklı
/// tutarla gelirse fark fatura kaydında düzeltilir; mal kabul anında
/// elimizdeki tek fiyat siparişin fiyatıdır.
///
/// PROJE ETİKETİ YOK. Depoya giren mal henüz proje maliyeti değil —
/// bilanço kalemidir. Proje maliyeti, malzeme depodan projeye
/// ÇIKARKEN doğar (740) ve stok çıkışı zaten projeyi oraya yazıyor.
/// Girişte de proje yazılsaydı aynı malzeme projeye iki kez
/// bağlanır, proje maliyet raporu şişerdi.
/// </summary>
public interface IGoodsReceiptAccountingPoster
{
    Task<Guid> PostAsync(
        GoodsReceiptEntity receipt,
        IReadOnlyDictionary<Guid, decimal> costByInventoryItem,
        CancellationToken cancellationToken);
}

public sealed class GoodsReceiptAccountingPoster(
    IInventoryAccountResolver accounts,
    IAccountingVoucherService vouchers) : IGoodsReceiptAccountingPoster
{
    public async Task<Guid> PostAsync(
        GoodsReceiptEntity receipt,
        IReadOnlyDictionary<Guid, decimal> costByInventoryItem,
        CancellationToken cancellationToken)
    {
        var total = decimal.Round(costByInventoryItem.Values.Sum(), 2);

        if (total <= 0m)
        {
            throw new InvalidOperationException(
                "Mal kabul tutarı sıfır; muhasebe fişi oluşturulamaz. "
                + "Sipariş kaleminde birim fiyat girilmemiş olabilir.");
        }

        // Kartın kategorisi hangi hesaba yazılacağını belirler; aynı
        // kabulde hem sarf hem ticari mal olabilir, bu yüzden borç
        // tarafı türe göre GRUPLANIR.
        var byKind = new Dictionary<InventoryAccountingKind, decimal>();

        foreach (var (inventoryItemId, cost) in costByInventoryItem)
        {
            var kind = await accounts.ResolveKindAsync(inventoryItemId, cancellationToken);
            byKind[kind] = byKind.GetValueOrDefault(kind) + cost;
        }

        var lines = new List<AccountingVoucherLineRequest>();

        foreach (var (kind, amount) in byKind.OrderBy(x => (int)x.Key))
        {
            var rounded = decimal.Round(amount, 2);
            if (rounded <= 0m) continue;

            lines.Add(new AccountingVoucherLineRequest(
                AccountingAccountId: await accounts.ResolveStockAccountAsync(
                    receipt.CompanyId, kind, cancellationToken),
                Description: kind == InventoryAccountingKind.TradeGood
                    ? $"Mal kabul {receipt.ReceiptNumber} — ticari mal girişi"
                    : $"Mal kabul {receipt.ReceiptNumber} — sarf malzeme girişi",
                DebitAmount: rounded,
                CreditAmount: 0m,
                CurrencyCode: "TRY",
                ExchangeRate: 1m,
                CurrentAccountId: null,
                ProjectId: null,
                CostCenterCode: null,
                DocumentNumber: receipt.ReceiptNumber,
                DocumentDate: receipt.ReceiptDate,
                DueDate: null));
        }

        // Yuvarlama borç toplamını kuruş kaydırabilir; alacak satırı
        // BORÇ TOPLAMINDAN türetilir ki fiş her hâlükârda denk kalsın.
        var debitTotal = lines.Sum(x => x.DebitAmount);

        lines.Add(new AccountingVoucherLineRequest(
            AccountingAccountId: await accounts
                .ResolveGoodsReceivedNotInvoicedAccountAsync(
                    receipt.CompanyId, cancellationToken),
            Description: $"Mal kabul {receipt.ReceiptNumber} — faturası beklenen alım",
            DebitAmount: 0m,
            CreditAmount: debitTotal,
            CurrencyCode: "TRY",
            ExchangeRate: 1m,
            CurrentAccountId: null,
            ProjectId: null,
            CostCenterCode: null,
            DocumentNumber: receipt.ReceiptNumber,
            DocumentDate: receipt.ReceiptDate,
            DueDate: null));

        var created = await vouchers.CreateAsync(
            new CreateAccountingVoucherRequest(
                CompanyId: receipt.CompanyId,
                VoucherType: (int)AccountingVoucherType.Journal,
                VoucherDate: receipt.ReceiptDate,
                CurrencyCode: "TRY",
                ExchangeRate: 1m,
                Description: $"Mal kabul {receipt.ReceiptNumber}",
                ReferenceNumber: receipt.ReceiptNumber,
                SourceModule: "GoodsReceipt",
                SourceEntityId: receipt.Id,
                Lines: lines),
            cancellationToken);

        await vouchers.PostAsync(created.Id, cancellationToken);

        return created.Id;
    }
}
