using EnderunAI.Api.Contracts.Accounting;

namespace EnderunAI.Api.Services.FinancialInstruments;

/// <summary>
/// Finansal araçların özeti.
///
/// <c>BarterReceivable</c> NAKİT DEĞİLDİR ve nakit toplamlarından
/// ayrı duruyor: barter alacağı mal/hizmetle kapanır, kasaya para
/// girmez. Nakit toplamına eklenseydi likidite olduğundan iyi
/// görünürdü.
/// </summary>
public sealed record FinancialInstrumentSummary(
    DateTime From,
    DateTime To,
    /// <summary>Vadesi aralığa düşen kredi taksitlerinin toplamı.</summary>
    decimal LoanInstallmentOutflow,
    int LoanInstallmentCount,
    /// <summary>Aralıkta çekilecek/çekilmiş kredi tutarı.</summary>
    decimal LoanDrawdownInflow,
    /// <summary>Son ödeme günü aralığa düşen kart ekstrelerinin toplamı.</summary>
    decimal CardStatementOutflow,
    int CardStatementCount,
    /// <summary>NAKİT DIŞI: barter alacağı.</summary>
    decimal BarterReceivable,
    int BarterCount,
    /// <summary>Kredi taksiti + kart ekstresi. Barter DAHİL DEĞİL.</summary>
    decimal TotalCashOutflow,
    /// <summary>En yakın nakit çıkışının tarihi; yoksa null.</summary>
    DateTime? NextOutflowDate,
    decimal NextOutflowAmount,
    string? NextOutflowTitle);

/// <summary>
/// Finansal araç özeti.
///
/// OKUR, YENİDEN HESAPLAMAZ: üç aracın da nakit akışa verdiği
/// satırları (<see cref="IFinancialInstrumentSource.GetCashLinesAsync"/>)
/// olduğu gibi alıp topluyor. Taksit planı, ekstre dönemi ve barter
/// mahsubu kurallarının hiçbiri burada tekrarlanmıyor; her araç kendi
/// kuralını kendi servisinde tutuyor ve iptal/ertelenen kalemi kendisi
/// eliyor. Burada ikinci bir sorgu yazılsaydı, aynı taksit nakit akış
/// takviminde bir tutarla, özette başka bir tutarla görünebilirdi.
/// </summary>
public sealed class FinancialInstrumentSummaryService(
    BankLoanService loans,
    CreditCardService cards,
    BarterInstrumentService barter)
{
    public async Task<FinancialInstrumentSummary> GetAsync(
        Guid companyId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        var lines = new List<InstrumentCashLine>();

        lines.AddRange(await loans.GetCashLinesAsync(companyId, from, to, cancellationToken));
        lines.AddRange(await cards.GetCashLinesAsync(companyId, from, to, cancellationToken));
        lines.AddRange(await barter.GetCashLinesAsync(companyId, from, to, cancellationToken));

        var installments = lines
            .Where(x => x.Kind == BankLoanService.InstallmentKind)
            .ToList();

        var drawdowns = lines
            .Where(x => x.Kind == BankLoanService.DrawdownKind)
            .ToList();

        var statements = lines
            .Where(x => x.Kind == CreditCardService.StatementKind)
            .ToList();

        var barterLines = lines
            .Where(x => x.Kind == BarterInstrumentService.ReceivableKind)
            .ToList();

        // Nakit çıkışı: yalnızca gerçekten kasadan çıkan kalemler.
        // Barter NAKİT DIŞI olduğu için burada yok.
        var outflows = installments.Concat(statements).ToList();

        var next = outflows
            .Where(x => !x.IsInflow)
            .OrderBy(x => x.CashDate)
            .FirstOrDefault();

        return new FinancialInstrumentSummary(
            from,
            to,
            installments.Sum(x => x.Amount),
            installments.Count,
            drawdowns.Sum(x => x.Amount),
            statements.Sum(x => x.Amount),
            statements.Count,
            barterLines.Sum(x => x.Amount),
            barterLines.Count,
            outflows.Where(x => !x.IsInflow).Sum(x => x.Amount),
            next?.CashDate,
            next?.Amount ?? 0m,
            next?.Title);
    }
}
