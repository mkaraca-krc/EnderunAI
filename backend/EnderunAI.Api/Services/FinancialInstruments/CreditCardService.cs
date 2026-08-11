using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Expenses;
using EnderunAI.Api.Models.FinancialInstruments;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.FinancialInstruments;

/// <summary>Bir kartın tek ekstre dönemi.</summary>
public sealed record CreditCardStatement(
    Guid CreditCardId,
    string CardName,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    DateTime DueDate,
    decimal Amount,
    int ItemCount);

/// <summary>
/// Kredi kartı: harcamalardan ekstre dönemi üretir ve nakit akışa
/// TEK çıkış verir.
///
/// ÇİFT SAYIM KURALI — bu paketin en kritik yeri:
/// - Harcama tarihi = GİDER (gider merkezinde sayılır, tahakkuk).
/// - Ekstre son ödeme tarihi = NAKİT ÇIKIŞI (burada sayılır).
/// Aynı harcama her iki yerde de "para çıktı" diye sayılsaydı gider
/// bir kez, nakit iki kez düşerdi. Bu yüzden gider kaydı
/// projeksiyonda kart harcamalarını ELEMEK zorunda
/// (CashFlowProjectionService.GetExpenseEntryMovementsAsync).
///
/// ŞAHIS KARTI HİÇ ÇIKIŞ ÜRETMEZ: ekstreyi şahıs ödüyor, şirketin
/// nakdi çıkmıyor. Harcama şahsın carisine yazılıyor (şirket ona
/// borçlanıyor) ve nakit akışta görünmüyor.
///
/// AYRI EKSTRE TABLOSU YOK: dönem, kartın kesim/son ödeme gününden
/// ve harcamaların tarihinden TÜRETİLİYOR. Ayrı tablo tutulsaydı
/// harcama düzeltildiğinde ekstre eskir ve iki kaynak ayrışırdı.
/// </summary>
public sealed class CreditCardService(AppDbContext db) : IFinancialInstrumentSource
{
    public const string StatementKind = "CreditCardStatement";

    public async Task<List<InstrumentCashLine>> GetCashLinesAsync(
        Guid companyId, DateTime from, DateTime to,
        CancellationToken cancellationToken)
    {
        var statements = await GetStatementsAsync(
            companyId,
            // Ekstre, harcamadan sonraki ay ödendiği için geriye doğru
            // bir tampon veriyoruz: bu ay ödenecek ekstrenin
            // harcamaları geçen ay yapılmış olabilir.
            from.AddMonths(-2),
            to,
            includePersonal: false,
            cancellationToken);

        return statements
            .Where(x => x.DueDate >= from.Date && x.DueDate <= to.Date && x.Amount > 0m)
            .Select(x => new InstrumentCashLine(
                x.PeriodEnd,
                x.DueDate,
                StatementKind,
                "Kredi kartı ekstresi",
                $"{x.CardName} — {x.PeriodStart:dd.MM} - {x.PeriodEnd:dd.MM} dönemi " +
                $"({x.ItemCount} harcama)",
                x.Amount,
                false,
                // Ekstre tutarı harcamalardan çıkıyor, tarihi kartın
                // son ödeme gününden: ikisi de belli.
                CashFlowCertainty.Confirmed))
            .ToList();
    }

    /// <summary>
    /// Kart harcamalarını ekstre dönemlerine böler.
    ///
    /// Dönem: kesim gününden bir sonraki kesim gününe kadar. Son
    /// ödeme günü, kesimden SONRAKİ ilk "son ödeme günü"dür — kesim
    /// 25, ödeme 10 ise ödeme ertesi ayın 10'udur.
    /// </summary>
    public async Task<List<CreditCardStatement>> GetStatementsAsync(
        Guid companyId, DateTime from, DateTime to,
        bool includePersonal, CancellationToken cancellationToken)
    {
        var cards = await db.CreditCards
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Where(x => includePersonal || x.Ownership == CreditCardOwnership.Company)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.StatementDay,
                x.DueDay,
                x.Ownership
            })
            .ToListAsync(cancellationToken);

        if (cards.Count == 0)
            return [];

        var cardIds = cards.Select(x => x.Id).ToList();

        var expenses = await db.ExpenseEntries
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.CreditCardId != null &&
                        cardIds.Contains(x.CreditCardId!.Value) &&
                        x.ExpenseDate >= from.Date && x.ExpenseDate <= to.Date &&
                        x.Amount > 0m)
            .Select(x => new
            {
                CardId = x.CreditCardId!.Value,
                x.ExpenseDate,
                x.Amount
            })
            .ToListAsync(cancellationToken);

        if (expenses.Count == 0)
            return [];

        var result = new List<CreditCardStatement>();

        foreach (var card in cards)
        {
            var cardExpenses = expenses.Where(x => x.CardId == card.Id).ToList();

            if (cardExpenses.Count == 0)
                continue;

            foreach (var group in cardExpenses
                         .GroupBy(x => CutDateFor(x.ExpenseDate, card.StatementDay)))
            {
                var cut = group.Key;
                var periodStart = PreviousCut(cut, card.StatementDay).AddDays(1);

                result.Add(new CreditCardStatement(
                    card.Id,
                    card.Name,
                    periodStart,
                    cut,
                    DueDateFor(cut, card.DueDay),
                    decimal.Round(group.Sum(x => x.Amount), 2),
                    group.Count()));
            }
        }

        return result
            .OrderBy(x => x.DueDate)
            .ThenBy(x => x.CardName)
            .ToList();
    }

    /// <summary>Harcamanın düştüğü kesim günü (harcama tarihinde ya da sonrasında).</summary>
    private static DateTime CutDateFor(DateTime expenseDate, int statementDay)
    {
        var day = Math.Clamp(statementDay, 1, 31);

        var thisMonthCut = SafeDate(expenseDate.Year, expenseDate.Month, day);

        // Kesim gününde yapılan harcama o ekstreye girer.
        return expenseDate.Date <= thisMonthCut
            ? thisMonthCut
            : SafeDate(
                expenseDate.AddMonths(1).Year,
                expenseDate.AddMonths(1).Month,
                day);
    }

    private static DateTime PreviousCut(DateTime cut, int statementDay)
    {
        var previous = cut.AddMonths(-1);

        return SafeDate(previous.Year, previous.Month, Math.Clamp(statementDay, 1, 31));
    }

    /// <summary>
    /// Kesimden sonraki ilk son ödeme günü. Ödeme günü kesim gününden
    /// büyükse aynı ay, değilse ertesi ay.
    /// </summary>
    private static DateTime DueDateFor(DateTime cut, int dueDay)
    {
        var day = Math.Clamp(dueDay, 1, 31);

        var sameMonth = SafeDate(cut.Year, cut.Month, day);

        if (sameMonth > cut)
            return sameMonth;

        var next = cut.AddMonths(1);

        return SafeDate(next.Year, next.Month, day);
    }

    /// <summary>Ayda olmayan gün (31 Şubat) ayın son gününe çekilir.</summary>
    private static DateTime SafeDate(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)),
            0, 0, 0, DateTimeKind.Utc);
}
