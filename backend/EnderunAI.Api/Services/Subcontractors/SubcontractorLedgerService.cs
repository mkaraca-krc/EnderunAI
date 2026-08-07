using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Subcontractors;

/// <summary>
/// Taşeron hesabının özeti.
/// </summary>
/// <param name="InvoicedPaymentTotal">Faturalı ödemeler toplamı.</param>
/// <param name="InvoicedAdvanceTotal">Verilen resmî avans toplamı.</param>
/// <param name="CashPaymentTotal">Elden ödeme toplamı; yetki yoksa
/// null.</param>
/// <param name="CashAdvanceTotal">Elden avans toplamı; yetki yoksa
/// null.</param>
/// <param name="OffsetTotal">Hakedişlerden mahsup edilen avans
/// toplamı.</param>
/// <param name="OpenAdvance">Açık (henüz mahsup edilmemiş) avans.
/// Yetki yoksa yalnızca resmî kısmı içerir.</param>
/// <param name="CashHidden">Elden rakamlar gizlendi mi.</param>
public sealed record SubcontractorLedgerSummary(
    decimal InvoicedPaymentTotal,
    decimal InvoicedAdvanceTotal,
    decimal? CashPaymentTotal,
    decimal? CashAdvanceTotal,
    decimal OffsetTotal,
    decimal OpenAdvance,
    bool CashHidden);

/// <summary>
/// Taşeron ödemeleri, avansları ve mahsup takibi.
///
/// ELDEN İZOLASYONU: elden tutarlar ayrı tabloda durur ve bu servis
/// onları YALNIZCA çağıran <c>extra_payment.view</c> iznini
/// doğruladıysa sorgular. Yetkisiz kullanıcının sorgusu elden tablosuna
/// hiç uğramaz — maskeleme arayüzde değil, sorgu seviyesinde.
/// </summary>
public sealed class SubcontractorLedgerService(AppDbContext db)
{
    /// <summary>
    /// Sözleşmenin ödeme/avans özeti.
    /// </summary>
    /// <param name="canViewCash">Elden tutarları görme yetkisi. False
    /// ise elden tablolarına HİÇ sorgu atılmaz.</param>
    public async Task<SubcontractorLedgerSummary> GetSummaryAsync(
        Guid contractId, bool canViewCash, CancellationToken cancellationToken)
    {
        var official = await db.SubcontractorLedgerEntries
            .AsNoTracking()
            .Where(x => x.SubcontractorContractId == contractId)
            .GroupBy(x => x.Kind)
            .Select(g => new { Kind = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var invoicedPayment = official
            .Where(x => x.Kind == SubcontractorLedgerKind.Payment)
            .Sum(x => x.Total);
        var invoicedAdvance = official
            .Where(x => x.Kind == SubcontractorLedgerKind.Advance)
            .Sum(x => x.Total);

        decimal? cashPayment = null;
        decimal? cashAdvance = null;

        if (canViewCash)
        {
            var cash = await db.SubcontractorCashLedgerEntries
                .AsNoTracking()
                .Where(x => x.SubcontractorContractId == contractId)
                .GroupBy(x => x.Kind)
                .Select(g => new { Kind = g.Key, Total = g.Sum(x => x.Amount) })
                .ToListAsync(cancellationToken);

            cashPayment = cash
                .Where(x => x.Kind == SubcontractorLedgerKind.Payment)
                .Sum(x => x.Total);
            cashAdvance = cash
                .Where(x => x.Kind == SubcontractorLedgerKind.Advance)
                .Sum(x => x.Total);
        }

        var offsetTotal = await GetOffsetTotalAsync(contractId, cancellationToken);

        // Açık avans = verilen − mahsup edilen. Yetki yoksa elden kısmı
        // toplama girmez; kullanıcı gördüğü rakamın eksik olduğunu
        // CashHidden ile bilir.
        var openAdvance = invoicedAdvance + (cashAdvance ?? 0m) - offsetTotal;

        return new SubcontractorLedgerSummary(
            InvoicedPaymentTotal: decimal.Round(invoicedPayment, 2),
            InvoicedAdvanceTotal: decimal.Round(invoicedAdvance, 2),
            CashPaymentTotal: cashPayment.HasValue
                ? decimal.Round(cashPayment.Value, 2)
                : null,
            CashAdvanceTotal: cashAdvance.HasValue
                ? decimal.Round(cashAdvance.Value, 2)
                : null,
            OffsetTotal: decimal.Round(offsetTotal, 2),
            OpenAdvance: decimal.Round(Math.Max(0m, openAdvance), 2),
            CashHidden: !canViewCash);
    }

    /// <summary>
    /// Hakedişlerden bugüne kadar mahsup edilmiş avans toplamı. İptal
    /// edilen hakedişler sayılmaz.
    /// </summary>
    public async Task<decimal> GetOffsetTotalAsync(
        Guid contractId, CancellationToken cancellationToken)
    {
        const int offsetType = (int)HakedisDeductionType.AdvanceOffset;

        return await db.SubcontractorProgressPayments
            .AsNoTracking()
            .Where(x => x.SubcontractorContractId == contractId &&
                        x.Status != SubcontractorProgressPaymentStatus.Cancelled)
            .SelectMany(x => x.Deductions)
            .Where(x => x.DeductionType == offsetType)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
    }

    /// <summary>
    /// Bu hakedişte mahsup edilmesi ÖNERİLEN avans.
    ///
    /// Öneri, açık avansın tamamı ile hakedişin bu dönem tutarının
    /// KÜÇÜĞÜ kadardır: dönem tutarını aşan bir mahsup neti eksiye
    /// çekerdi ve bu, taşerondan para istemek demektir — mutabakat
    /// konusu, otomatik yapılamaz.
    ///
    /// Açık avans yoksa öneri de yok (null).
    /// </summary>
    public async Task<(decimal Amount, string Basis)?> SuggestAdvanceOffsetAsync(
        Guid contractId,
        decimal currentPeriodAmount,
        bool canViewCash,
        CancellationToken cancellationToken)
    {
        var summary = await GetSummaryAsync(contractId, canViewCash, cancellationToken);

        if (summary.OpenAdvance <= 0m || currentPeriodAmount <= 0m)
            return null;

        var amount = Math.Min(summary.OpenAdvance, decimal.Round(currentPeriodAmount, 2));

        var basis = summary.CashHidden
            ? $"Açık resmî avans {TurkishAmountFormat.Amount(summary.OpenAdvance)} " +
              "(elden avanslar bu rakama dahil değil)"
            : $"Açık avans {TurkishAmountFormat.Amount(summary.OpenAdvance)}";

        return (amount,
            amount < summary.OpenAdvance
                ? $"{basis}; bu dönem tutarı kadarı mahsup ediliyor"
                : basis);
    }

    /// <summary>
    /// Açık avans kalan işi aşıyor mu. Aşıyorsa taşerona kalan işinden
    /// fazlasını ödemişiz demektir ve bu tahsil riski taşır.
    /// </summary>
    /// <returns>Uyarı metni; risk yoksa null.</returns>
    public static string? BuildOverAdvanceWarning(
        decimal openAdvance, decimal contractAmount, decimal cumulativeWorkAmount)
    {
        var remainingWork = contractAmount - cumulativeWorkAmount;

        if (openAdvance <= 0m || openAdvance <= remainingWork)
            return null;

        return
            $"Açık avans ({TurkishAmountFormat.Amount(openAdvance)}) kalan işten " +
            $"({TurkishAmountFormat.Amount(remainingWork)}) " +
            "fazla. Bu tutar hakedişlerden mahsup edilemeyebilir.";
    }

    /// <summary>
    /// Faturalı ödemeyi proje maliyetine yazar.
    ///
    /// Elden ödeme buraya YAZILMAZ: proje maliyeti defteri
    /// <c>projects.view</c> ile okunuyor ve elden tutar oradan sızardı.
    /// Elden kısım maliyet ekranında okuma anında, yetki kontrolüyle
    /// eklenir.
    /// </summary>
    public void WriteProjectCost(
        SubcontractorLedgerEntry entry,
        SubcontractorContract contract,
        Guid? sectionId,
        string subcontractorTitle)
    {
        // Avans maliyet değildir: iş yapılmadan verilen para, hakediş
        // mahsup edilince zaten maliyetleşir. Avansı da yazmak aynı
        // işçiliği iki kez saymak olurdu.
        if (entry.Kind != SubcontractorLedgerKind.Payment)
            return;

        db.ProjectCostTransactions.Add(new ProjectCostTransaction
        {
            ProjectId = contract.ProjectId,
            ProjectSiteId = contract.ProjectSiteId,
            ProjectHakedisSectionId = sectionId,
            CostType = ProjectCostType.Subcontractor,
            CostClass = ProjectCostClass.SubcontractorLabor,
            CostDate = entry.EntryDate,
            Amount = entry.Amount,
            Description =
                $"Taşeron ödemesi {contract.ContractNumber} — {subcontractorTitle}",
            ReferenceType = "SubcontractorLedgerEntry",
            ReferenceId = entry.Id
        });
    }
}
