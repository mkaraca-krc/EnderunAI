using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Tax;

/// <summary>
/// Takvimdeki tek bir vergi yükümlülüğü. Tutarlar TAHMİNDİR: defterdeki
/// rakamdan hesaplanır ama beyan müşavirde kesinleşir.
/// </summary>
public sealed record TaxObligation(
    TaxObligationKind Kind,
    string KindName,
    int PeriodYear,
    int PeriodNumber,
    string PeriodLabel,
    DateTime DueDate,
    decimal EstimatedAmount,
    bool IsPaid,
    decimal? PaidAmount,
    DateTime? PaidAtUtc,
    /// <summary>Vadesi geçmiş ve hâlâ ödenmemiş.</summary>
    bool IsOverdue);

public interface ITaxObligationService
{
    /// <summary>
    /// Verilen tarih aralığına düşen vergi yükümlülükleri. Ödenmiş
    /// dönemler de döner (IsPaid=true); nakit akış onları dışarıda
    /// bırakır, takvim ekranı gösterir.
    /// </summary>
    Task<IReadOnlyList<TaxObligation>> GetObligationsAsync(
        Guid companyId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken);

    Task<TaxObligation> MarkPaidAsync(
        Guid companyId,
        TaxObligationKind kind,
        int periodYear,
        int periodNumber,
        decimal? amount,
        DateTime? paidAt,
        string? note,
        CancellationToken cancellationToken);

    Task UndoPaymentAsync(
        Guid companyId,
        TaxObligationKind kind,
        int periodYear,
        int periodNumber,
        CancellationToken cancellationToken);
}

/// <summary>
/// Vergi takvimi: hangi yükümlülük ne zaman, ne kadar.
///
/// Tutarlar tek kaynaktan (<see cref="ITaxLedgerService"/>) gelir;
/// takvim kendi başına hesap yapmaz. İki ayrı hesap olsaydı ekrandaki
/// rakam nakit akıştakinden farklı çıkabilirdi.
/// </summary>
public sealed class TaxObligationService(
    AppDbContext db,
    ITaxLedgerService taxLedger) : ITaxObligationService
{
    public async Task<IReadOnlyList<TaxObligation>> GetObligationsAsync(
        Guid companyId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken)
    {
        var from = fromDate.Date;
        var to = toDate.Date;

        // Aralığa düşen ödeme tarihleri en fazla bir önceki yılın
        // dönemlerinden gelebilir (aralık dönemi ocakta ödenir).
        var years = new[] { from.Year - 1, from.Year, to.Year }.Distinct().ToList();

        var payments = await db.TaxPayments
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new
            {
                x.Kind, x.PeriodYear, x.PeriodNumber, x.Amount, x.PaidAtUtc
            })
            .ToListAsync(cancellationToken);

        var result = new List<TaxObligation>();
        var today = DateTime.UtcNow.Date;

        foreach (var year in years)
        {
            var overview = await taxLedger.GetOverviewAsync(companyId, year, cancellationToken);

            foreach (var period in overview.Vat)
            {
                if (period.PayableVat <= 0m)
                    continue;

                Add(TaxObligationKind.Vat, period.Year, period.Month,
                    $"{period.Month:00}/{period.Year}",
                    TaxCalendar.MonthlyDueDate(period.Year, period.Month),
                    period.PayableVat);
            }

            foreach (var period in overview.Payroll)
            {
                var due = TaxCalendar.MonthlyDueDate(period.Year, period.Month);

                if (period.SgkTotal > 0m)
                {
                    Add(TaxObligationKind.SocialSecurity, period.Year, period.Month,
                        $"{period.Month:00}/{period.Year}", due, period.SgkTotal);
                }

                var withholding = decimal.Round(
                    period.IncomeTaxWithholding + period.StampTax, 2);

                if (withholding > 0m)
                {
                    Add(TaxObligationKind.Withholding, period.Year, period.Month,
                        $"{period.Month:00}/{period.Year}", due, withholding);
                }
            }

            foreach (var period in overview.AdvanceTax)
            {
                if (period.EstimatedTax <= 0m)
                    continue;

                Add(TaxObligationKind.AdvanceTax, period.Year, period.Quarter,
                    $"{period.Quarter}. dönem {period.Year}",
                    period.DueDate, period.EstimatedTax);
            }
        }

        void Add(
            TaxObligationKind kind, int periodYear, int periodNumber,
            string label, DateTime dueDate, decimal amount)
        {
            if (dueDate.Date < from || dueDate.Date > to)
                return;

            var payment = payments.SingleOrDefault(x =>
                x.Kind == kind && x.PeriodYear == periodYear &&
                x.PeriodNumber == periodNumber);

            result.Add(new TaxObligation(
                kind,
                TaxCalendar.KindName(kind),
                periodYear,
                periodNumber,
                label,
                dueDate,
                amount,
                payment is not null,
                payment?.Amount,
                payment?.PaidAtUtc,
                payment is null && dueDate.Date < today));
        }

        return result
            .OrderBy(x => x.DueDate)
            .ThenBy(x => x.Kind)
            .ToList();
    }

    public async Task<TaxObligation> MarkPaidAsync(
        Guid companyId,
        TaxObligationKind kind,
        int periodYear,
        int periodNumber,
        decimal? amount,
        DateTime? paidAt,
        string? note,
        CancellationToken cancellationToken)
    {
        ValidatePeriod(kind, periodNumber);

        if (!await db.Companies.AnyAsync(x => x.Id == companyId, cancellationToken))
            throw new KeyNotFoundException("Şirket bulunamadı.");

        var existing = await db.TaxPayments.SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.Kind == kind &&
                 x.PeriodYear == periodYear && x.PeriodNumber == periodNumber,
            cancellationToken);

        if (existing is not null)
        {
            throw new InvalidOperationException(
                $"{TaxCalendar.KindName(kind)} {periodNumber}/{periodYear} dönemi " +
                "zaten ödendi işaretlenmiş.");
        }

        var estimated = await GetEstimatedAmountAsync(
            companyId, kind, periodYear, periodNumber, cancellationToken);

        var paidAmount = amount ?? estimated;

        if (paidAmount <= 0m)
        {
            throw new ArgumentException(
                "Ödenen tutar sıfırdan büyük olmalıdır; dönemde tahmini " +
                "yükümlülük yoksa tutarı elle girin.");
        }

        var payment = new TaxPayment
        {
            CompanyId = companyId,
            Kind = kind,
            PeriodYear = periodYear,
            PeriodNumber = periodNumber,
            Amount = decimal.Round(paidAmount, 2),
            PaidAtUtc = DateTime.SpecifyKind(
                (paidAt ?? DateTime.UtcNow).Date, DateTimeKind.Utc),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        };

        db.TaxPayments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);

        var dueDate = kind == TaxObligationKind.AdvanceTax
            ? TaxCalendar.AdvanceTaxDueDate(periodYear, periodNumber)
            : TaxCalendar.MonthlyDueDate(periodYear, periodNumber);

        return new TaxObligation(
            kind,
            TaxCalendar.KindName(kind),
            periodYear,
            periodNumber,
            kind == TaxObligationKind.AdvanceTax
                ? $"{periodNumber}. dönem {periodYear}"
                : $"{periodNumber:00}/{periodYear}",
            dueDate,
            estimated,
            true,
            payment.Amount,
            payment.PaidAtUtc,
            false);
    }

    public async Task UndoPaymentAsync(
        Guid companyId,
        TaxObligationKind kind,
        int periodYear,
        int periodNumber,
        CancellationToken cancellationToken)
    {
        var payment = await db.TaxPayments.SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.Kind == kind &&
                 x.PeriodYear == periodYear && x.PeriodNumber == periodNumber,
            cancellationToken)
            ?? throw new KeyNotFoundException("Ödeme kaydı bulunamadı.");

        // Soft delete: yanlışlıkla işaretlenen ödemenin izi kalsın.
        payment.IsDeleted = true;
        payment.DeletedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void ValidatePeriod(TaxObligationKind kind, int periodNumber)
    {
        var valid = kind == TaxObligationKind.AdvanceTax
            ? periodNumber is >= 1 and <= 4
            : periodNumber is >= 1 and <= 12;

        if (!valid)
        {
            throw new ArgumentException(
                kind == TaxObligationKind.AdvanceTax
                    ? "Geçici vergi dönemi 1 ile 4 arasında olmalıdır."
                    : "Ay 1 ile 12 arasında olmalıdır.");
        }
    }

    private async Task<decimal> GetEstimatedAmountAsync(
        Guid companyId,
        TaxObligationKind kind,
        int periodYear,
        int periodNumber,
        CancellationToken cancellationToken)
    {
        var overview = await taxLedger.GetOverviewAsync(
            companyId, periodYear, cancellationToken);

        return kind switch
        {
            TaxObligationKind.Vat => overview.Vat
                .Where(x => x.Month == periodNumber)
                .Select(x => x.PayableVat)
                .FirstOrDefault(),

            TaxObligationKind.SocialSecurity => overview.Payroll
                .Where(x => x.Month == periodNumber)
                .Select(x => x.SgkTotal)
                .FirstOrDefault(),

            TaxObligationKind.Withholding => overview.Payroll
                .Where(x => x.Month == periodNumber)
                .Select(x => decimal.Round(x.IncomeTaxWithholding + x.StampTax, 2))
                .FirstOrDefault(),

            TaxObligationKind.AdvanceTax => overview.AdvanceTax
                .Where(x => x.Quarter == periodNumber)
                .Select(x => x.EstimatedTax)
                .FirstOrDefault(),

            _ => 0m
        };
    }
}
