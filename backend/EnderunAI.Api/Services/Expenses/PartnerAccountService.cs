using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Expenses;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Expenses;

/// <summary>Şahsın bakiyesi ve hareket dökümü.</summary>
public sealed record PartnerAccountBalance(
    Guid PartnerAccountId,
    string FullName,
    string? Title,
    decimal AdvanceTotal,
    decimal SettlementTotal,
    decimal RepaymentTotal,
    /// <summary>Şahsın şirkete borcu: avans − (mahsup + geri ödeme).</summary>
    decimal Balance);

/// <summary>
/// Şahıs/ortak carisi.
///
/// ÇİFT SAYIM KURALI: para şirketten AVANS anında çıkar ve kasa/banka
/// tarafında zaten kayıtlıdır. Faturasız gider o avansı MAHSUP eder —
/// gider merkezinde gider olarak sayılır ama şirket nakdini ikinci
/// kez etkilemez. Bu yüzden burada ne kasa hareketi ne muhasebe fişi
/// üretiliyor.
/// </summary>
public sealed class PartnerAccountService(AppDbContext db)
{
    /// <summary>
    /// Faturasız gider kaydının mahsubunu yazar ya da günceller.
    ///
    /// İDEMPOTENT: mahsup satırı gider kaydına bağlı
    /// (<see cref="PartnerAccountEntry.ExpenseEntryId"/>). Aynı gider
    /// ikinci kez kaydedilirse yeni satır açılmaz, mevcut satır
    /// güncellenir — ayrı bir "mahsup edildi" bayrağı tutulsaydı
    /// bayrak ile defter arasında tutarsızlık doğardı.
    /// </summary>
    public async Task SyncSettlementAsync(
        ExpenseEntry entry, CancellationToken cancellationToken)
    {
        var existing = await db.PartnerAccountEntries
            .SingleOrDefaultAsync(x => x.ExpenseEntryId == entry.Id, cancellationToken);

        // Gider artık şahıs carisinden karşılanmıyorsa mahsup kalkar.
        if (entry.PaymentMethod != ExpensePaymentMethod.PartnerAccount ||
            entry.PartnerAccountId is not Guid partnerId)
        {
            if (existing is not null)
                db.PartnerAccountEntries.Remove(existing);

            return;
        }

        if (existing is null)
        {
            db.PartnerAccountEntries.Add(new PartnerAccountEntry
            {
                PartnerAccountId = partnerId,
                Kind = PartnerAccountEntryKind.ExpenseSettlement,
                EntryDate = entry.ExpenseDate,
                Amount = entry.Amount,
                Description = entry.Description,
                ExpenseEntryId = entry.Id
            });

            return;
        }

        existing.PartnerAccountId = partnerId;
        existing.EntryDate = entry.ExpenseDate;
        existing.Amount = entry.Amount;
        existing.Description = entry.Description;
        existing.UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Gider silinince mahsup da kalkar: sahipsiz bir mahsup satırı
    /// bakiyeyi olduğundan düşük gösterirdi.
    /// </summary>
    public async Task RemoveSettlementAsync(
        Guid expenseEntryId, CancellationToken cancellationToken)
    {
        var rows = await db.PartnerAccountEntries
            .Where(x => x.ExpenseEntryId == expenseEntryId)
            .ToListAsync(cancellationToken);

        if (rows.Count > 0)
            db.PartnerAccountEntries.RemoveRange(rows);
    }

    /// <summary>Şirketteki bütün şahısların bakiyesi.</summary>
    public async Task<List<PartnerAccountBalance>> GetBalancesAsync(
        Guid companyId, CancellationToken cancellationToken)
    {
        var partners = await db.PartnerAccounts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.FullName)
            .Select(x => new { x.Id, x.FullName, x.Title })
            .ToListAsync(cancellationToken);

        if (partners.Count == 0)
            return [];

        var ids = partners.Select(x => x.Id).ToList();

        var totals = await db.PartnerAccountEntries
            .AsNoTracking()
            .Where(x => ids.Contains(x.PartnerAccountId))
            .GroupBy(x => new { x.PartnerAccountId, x.Kind })
            .Select(x => new
            {
                x.Key.PartnerAccountId,
                x.Key.Kind,
                Amount = x.Sum(row => row.Amount)
            })
            .ToListAsync(cancellationToken);

        return partners
            .Select(partner =>
            {
                decimal Total(PartnerAccountEntryKind kind) =>
                    totals
                        .Where(x => x.PartnerAccountId == partner.Id && x.Kind == kind)
                        .Sum(x => x.Amount);

                var advance = Total(PartnerAccountEntryKind.Advance);
                var settlement = Total(PartnerAccountEntryKind.ExpenseSettlement);
                var repayment = Total(PartnerAccountEntryKind.Repayment);

                return new PartnerAccountBalance(
                    partner.Id, partner.FullName, partner.Title,
                    decimal.Round(advance, 2),
                    decimal.Round(settlement, 2),
                    decimal.Round(repayment, 2),
                    decimal.Round(advance - settlement - repayment, 2));
            })
            .ToList();
    }
}
