using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models.FinancialInstruments;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.FinancialInstruments;

/// <summary>
/// Banka kredisinin nakit akışa verdiği satırlar.
///
/// ÇEKİLİŞ = GİRİŞ, TAKSİT = ÇIKIŞ. İkisi de aynı araçtan doğuyor ama
/// farklı tarihlerde: krediyi almak bugün nakit yaratır, taksitler
/// aylarca çıkış üretir. Tek satır olsaydı kredinin likiditeye
/// etkisi görünmezdi.
///
/// SAYILMAYANLAR:
/// - İptal kredi: ne çekiliş ne taksit.
/// - Çekilmiş kredi (IsDrawn): para hesaba girdi, açılış bakiyesinin
///   içinde; ayrıca giriş yazılsaydı iki kez girmiş görünürdü.
/// - Ödenmiş taksit: parası çıktı, bakiyenin içinde.
/// </summary>
public sealed class BankLoanService(AppDbContext db) : IFinancialInstrumentSource
{
    public const string DrawdownKind = "LoanDrawdown";
    public const string InstallmentKind = "LoanInstallment";

    public async Task<List<InstrumentCashLine>> GetCashLinesAsync(
        Guid companyId, DateTime from, DateTime to,
        CancellationToken cancellationToken)
    {
        var loans = await db.BankLoans
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.Status != BankLoanStatus.Cancelled)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.DrawdownDate,
                x.PrincipalAmount,
                x.IsDrawn,
                x.Status,
                x.ProjectId,
                ProjectCode = x.Project != null ? x.Project.Code : null
            })
            .ToListAsync(cancellationToken);

        if (loans.Count == 0)
            return [];

        var lines = new List<InstrumentCashLine>();

        foreach (var loan in loans)
        {
            // Henüz çekilmemiş kredinin çekilişi gelecekte bir giriş.
            if (!loan.IsDrawn &&
                loan.PrincipalAmount > 0m &&
                loan.DrawdownDate.Date >= from.Date &&
                loan.DrawdownDate.Date <= to.Date)
            {
                lines.Add(new InstrumentCashLine(
                    loan.DrawdownDate.Date,
                    loan.DrawdownDate.Date,
                    DrawdownKind,
                    "Kredi çekilişi",
                    $"{loan.Name} — kredi kullandırımı",
                    loan.PrincipalAmount,
                    true,
                    // Sözleşmesi olan bir kullandırımın tarihi bellidir.
                    loan.Status == BankLoanStatus.Planned
                        ? CashFlowCertainty.Estimated
                        : CashFlowCertainty.Confirmed,
                    loan.ProjectId,
                    loan.ProjectCode));
            }
        }

        var loanIds = loans.Select(x => x.Id).ToList();

        var installments = await db.BankLoanInstallments
            .AsNoTracking()
            .Where(x => loanIds.Contains(x.BankLoanId) &&
                        !x.IsPaid &&
                        x.DueDate >= from.Date && x.DueDate <= to.Date)
            .Select(x => new
            {
                x.BankLoanId,
                x.Number,
                x.DueDate,
                x.PrincipalAmount,
                x.InterestAmount
            })
            .ToListAsync(cancellationToken);

        var loanById = loans.ToDictionary(x => x.Id);

        foreach (var installment in installments)
        {
            var loan = loanById[installment.BankLoanId];

            var total = decimal.Round(
                installment.PrincipalAmount + installment.InterestAmount, 2);

            if (total <= 0m)
                continue;

            lines.Add(new InstrumentCashLine(
                installment.DueDate.Date,
                installment.DueDate.Date,
                InstallmentKind,
                "Kredi taksiti",
                $"{loan.Name} — {installment.Number}. taksit",
                total,
                false,
                // Taksit vadesi sözleşmede yazılı.
                CashFlowCertainty.Confirmed,
                loan.ProjectId,
                loan.ProjectCode));
        }

        return lines;
    }

    /// <summary>
    /// Krediye taksit planı üretir. Var olan plan SİLİNİP yeniden
    /// yazılır; ödenmiş taksit varsa plan yeniden üretilmez — ödenmiş
    /// bir taksitin tutarını değiştirmek geçmişi değiştirmek olurdu.
    /// </summary>
    public async Task<string?> RebuildScheduleAsync(
        Guid loanId, CancellationToken cancellationToken)
    {
        var loan = await db.BankLoans
            .Include(x => x.Installments)
            .SingleOrDefaultAsync(x => x.Id == loanId, cancellationToken);

        if (loan is null)
            return "Kredi bulunamadı.";

        if (loan.Installments.Any(x => x.IsPaid))
            return "Ödenmiş taksit var; plan yeniden üretilemez. " +
                   "Taksitler tek tek düzeltilebilir.";

        var lines = LoanScheduleCalculator.Build(
            loan.PrincipalAmount,
            loan.MonthlyInterestRate,
            loan.InstallmentCount,
            loan.FirstInstallmentDate);

        if (lines.Count == 0)
            return "Taksit planı üretilemedi: anapara ve taksit sayısı gerekli.";

        // Ayrı bir sorguyla yükleniyor: takip edilen koleksiyonu
        // döngü içinde değiştirmek EF'te fixup sırasında eşzamanlılık
        // hatası doğuruyor (keşif raporunda aynı tuzağa düşülmüştü).
        var existing = await db.BankLoanInstallments
            .Where(x => x.BankLoanId == loanId)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
            db.BankLoanInstallments.RemoveRange(existing);

        foreach (var line in lines)
        {
            db.BankLoanInstallments.Add(new BankLoanInstallment
            {
                BankLoanId = loanId,
                Number = line.Number,
                DueDate = DateTime.SpecifyKind(line.DueDate.Date, DateTimeKind.Utc),
                PrincipalAmount = line.PrincipalAmount,
                InterestAmount = line.InterestAmount
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return null;
    }

    /// <summary>
    /// Dönemin FAİZ gideri — gider merkezine finansman gideri olarak
    /// akar. Anapara geri ödemesi gider DEĞİLDİR: borcun kapanmasıdır.
    /// </summary>
    public async Task<decimal> GetInterestExpenseAsync(
        Guid companyId, DateTime from, DateTime to,
        CancellationToken cancellationToken)
    {
        return await db.BankLoanInstallments
            .AsNoTracking()
            .Where(x => x.BankLoan.CompanyId == companyId &&
                        x.BankLoan.Status != BankLoanStatus.Cancelled &&
                        x.DueDate >= from.Date && x.DueDate <= to.Date)
            .SumAsync(x => (decimal?)x.InterestAmount, cancellationToken) ?? 0m;
    }
}
