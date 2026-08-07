using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Market;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Accounting;

/// <summary>
/// Bir carinin tek bir para birimindeki hareket toplamı.
/// </summary>
/// <param name="CurrencyCode">Para birimi (TRY dahil).</param>
/// <param name="TotalDebit">Borç toplamı, İŞLEM para biriminde.</param>
/// <param name="TotalCredit">Alacak toplamı, işlem para biriminde.</param>
/// <param name="Balance">Bakiye (borç − alacak), işlem para biriminde.</param>
/// <param name="TotalDebitLocal">Aynı satırların defterdeki TL karşılığı
/// (borç).</param>
/// <param name="TotalCreditLocal">Aynı satırların defterdeki TL karşılığı
/// (alacak).</param>
/// <param name="BalanceLocal">Bakiyenin DEFTER değeri: hareketler
/// işlem günündeki kurla TL'ye çevrilmiş haliyle toplanır. Bugünkü
/// kurla değerlenmiş hali değildir — aradaki fark kur farkıdır.</param>
/// <param name="MovementCount">Hareket sayısı.</param>
/// <param name="LastMovementDate">Son hareket tarihi.</param>
public sealed record CurrentAccountCurrencyBalance(
    string CurrencyCode,
    decimal TotalDebit,
    decimal TotalCredit,
    decimal Balance,
    decimal TotalDebitLocal,
    decimal TotalCreditLocal,
    decimal BalanceLocal,
    int MovementCount,
    DateTime LastMovementDate);

/// <summary>
/// Bir carinin para birimi kırılımlı bakiyesi.
/// </summary>
/// <param name="CurrentAccountId">Cari kart.</param>
/// <param name="TotalDebit">TL cinsinden toplam borç (defter değeri).</param>
/// <param name="TotalCredit">TL cinsinden toplam alacak (defter değeri).</param>
/// <param name="Balance">TL bakiye — bugüne kadarki tek rakam, aynen
/// korunuyor.</param>
/// <param name="MovementCount">Toplam hareket sayısı.</param>
/// <param name="LastMovementDate">Son hareket tarihi.</param>
/// <param name="HasForeignCurrency">TRY dışı hareketi var mı.</param>
/// <param name="CurrencyBalances">Para birimi kırılımı.</param>
public sealed record CurrentAccountBalanceBreakdown(
    Guid CurrentAccountId,
    decimal TotalDebit,
    decimal TotalCredit,
    decimal Balance,
    int MovementCount,
    DateTime LastMovementDate,
    bool HasForeignCurrency,
    IReadOnlyList<CurrentAccountCurrencyBalance> CurrencyBalances);

/// <summary>
/// Tek bir dövizin değerleme sonucu.
/// </summary>
/// <param name="CurrencyCode">Para birimi.</param>
/// <param name="Balance">Döviz bakiyesi (borç − alacak).</param>
/// <param name="BookValueLocal">Defterdeki TL karşılığı.</param>
/// <param name="RateAvailable">Değerleme kuru bulunabildi mi.</param>
/// <param name="ValuationRate">Değerleme kuru; bulunamadıysa null.</param>
/// <param name="RateSource">Kurun kaynağı (TCMB tarihi vb.).</param>
/// <param name="ValuedLocal">Değerleme kuruyla TL karşılığı.</param>
/// <param name="Difference">Değerlenmiş − defter. Pozitif: lehimize
/// (kur geliri), negatif: aleyhimize (kur gideri). İşareti carinin
/// borçlu/alacaklı olmasına göre yorumlanır.</param>
/// <param name="Message">Kur bulunamadıysa nedeni.</param>
public sealed record CurrentAccountCurrencyValuation(
    string CurrencyCode,
    decimal Balance,
    decimal BookValueLocal,
    bool RateAvailable,
    decimal? ValuationRate,
    string? RateSource,
    decimal? ValuedLocal,
    decimal? Difference,
    string? Message);

/// <summary>
/// Carinin döviz değerlemesi.
/// </summary>
/// <param name="CurrentAccountId">Cari kart.</param>
/// <param name="ValuationDate">Değerleme tarihi.</param>
/// <param name="Currencies">Döviz bazında değerleme.</param>
/// <param name="TotalDifference">Kuru bulunabilen dövizlerin fark
/// toplamı.</param>
/// <param name="HasMissingRate">Kuru bulunamayan döviz var mı — varsa
/// toplam eksiktir.</param>
public sealed record CurrentAccountValuation(
    Guid CurrentAccountId,
    DateTime ValuationDate,
    IReadOnlyList<CurrentAccountCurrencyValuation> Currencies,
    decimal TotalDifference,
    bool HasMissingRate);

/// <summary>
/// Cari bakiyelerinin para birimi kırılımı ve döviz değerlemesi.
///
/// TEK GERÇEK KAYNAK muhasebe defteridir: ayrı bir cari hareket tablosu
/// tutulmaz, her şey kesinleşmiş (Posted) fiş satırlarından okunur.
/// Fiş satırı hem işlem tutarını (<c>DebitAmount</c>) hem işlem
/// günündeki kurla TL karşılığını (<c>DebitAmountLocal</c>) taşıdığı
/// için döviz kırılımı için yeni tablo/migration GEREKMEZ.
///
/// İki rakam bilinçli olarak ayrı tutulur:
/// - DEFTER değeri: hareketler kendi gününün kuruyla TL'ye çevrilmiş
///   toplamı. Muhasebe bakiyesi budur ve değişmez.
/// - DEĞERLEME değeri: aynı döviz bakiyesinin bugünkü kurla karşılığı.
/// Aradaki fark gerçekleşmemiş kur farkıdır; burada yalnızca
/// RAPORLANIR, fiş kesilmez.
/// </summary>
public sealed class CurrentAccountCurrencyService(
    AppDbContext db,
    IInvoiceExchangeRateResolver rateResolver)
{
    private const string LocalCurrency = "TRY";

    /// <summary>
    /// Cari bakiyeleri, para birimi kırılımıyla.
    /// </summary>
    /// <param name="companyId">Şirket filtresi; null ise tümü.</param>
    /// <param name="currentAccountId">Tek cari filtresi; null ise tümü.</param>
    public async Task<List<CurrentAccountBalanceBreakdown>> GetBalancesAsync(
        Guid? companyId,
        Guid? currentAccountId,
        CancellationToken cancellationToken)
    {
        var query = PostedLines().Where(x => x.CurrentAccountId != null);

        if (companyId.HasValue)
            query = query.Where(x => x.AccountingVoucher.CompanyId == companyId.Value);

        if (currentAccountId.HasValue)
            query = query.Where(x => x.CurrentAccountId == currentAccountId.Value);

        // Tek sorgu: cari × para birimi. TL toplamları bellekte bu
        // kırılımdan türetilir — ikinci bir tur atmaya gerek yok.
        var rows = await query
            .GroupBy(x => new { AccountId = x.CurrentAccountId!.Value, x.CurrencyCode })
            .Select(g => new
            {
                g.Key.AccountId,
                g.Key.CurrencyCode,
                TotalDebit = g.Sum(x => x.DebitAmount),
                TotalCredit = g.Sum(x => x.CreditAmount),
                TotalDebitLocal = g.Sum(x => x.DebitAmountLocal),
                TotalCreditLocal = g.Sum(x => x.CreditAmountLocal),
                MovementCount = g.Count(),
                LastMovementDate = g.Max(x => x.AccountingVoucher.VoucherDate)
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.AccountId)
            .Select(group =>
            {
                var currencies = group
                    // Kod normalizasyonu bellekte yapılıyor ve gruplama
                    // tekrarlanıyor: defterde "usd"/"USD" gibi farklı
                    // yazımlar varsa tek satırda birleşsinler, ekranda
                    // aynı döviz iki kez görünmesin.
                    .GroupBy(x => Normalize(x.CurrencyCode))
                    .Select(byCode => new CurrentAccountCurrencyBalance(
                        CurrencyCode: byCode.Key,
                        TotalDebit: decimal.Round(byCode.Sum(x => x.TotalDebit), 2),
                        TotalCredit: decimal.Round(byCode.Sum(x => x.TotalCredit), 2),
                        Balance: decimal.Round(
                            byCode.Sum(x => x.TotalDebit - x.TotalCredit), 2),
                        TotalDebitLocal: decimal.Round(
                            byCode.Sum(x => x.TotalDebitLocal), 2),
                        TotalCreditLocal: decimal.Round(
                            byCode.Sum(x => x.TotalCreditLocal), 2),
                        BalanceLocal: decimal.Round(
                            byCode.Sum(x => x.TotalDebitLocal - x.TotalCreditLocal), 2),
                        MovementCount: byCode.Sum(x => x.MovementCount),
                        LastMovementDate: byCode.Max(x => x.LastMovementDate)))
                    // TL önce, sonra alfabetik: ekranda yerel para birimi
                    // her zaman ilk sırada okunur.
                    .OrderBy(x => x.CurrencyCode == LocalCurrency ? 0 : 1)
                    .ThenBy(x => x.CurrencyCode, StringComparer.Ordinal)
                    .ToList();

                var totalDebitLocal = currencies.Sum(x => x.TotalDebitLocal);
                var totalCreditLocal = currencies.Sum(x => x.TotalCreditLocal);

                return new CurrentAccountBalanceBreakdown(
                    CurrentAccountId: group.Key,
                    TotalDebit: decimal.Round(totalDebitLocal, 2),
                    TotalCredit: decimal.Round(totalCreditLocal, 2),
                    Balance: decimal.Round(totalDebitLocal - totalCreditLocal, 2),
                    MovementCount: currencies.Sum(x => x.MovementCount),
                    LastMovementDate: currencies.Max(x => x.LastMovementDate),
                    HasForeignCurrency: currencies.Any(x =>
                        x.CurrencyCode != LocalCurrency),
                    CurrencyBalances: currencies);
            })
            .ToList();
    }

    /// <summary>
    /// Carinin döviz bakiyelerini verilen tarihin kuruyla değerler.
    ///
    /// Kur bulunamayan döviz için TUTAR UYDURULMAZ: o satır
    /// <c>RateAvailable = false</c> döner, toplam farka girmez ve
    /// çağıran <c>HasMissingRate</c> ile toplamın eksik olduğunu bilir.
    /// </summary>
    public async Task<CurrentAccountValuation?> ValuateAsync(
        Guid currentAccountId,
        DateTime valuationDate,
        CancellationToken cancellationToken)
    {
        var exists = await db.CurrentAccounts
            .AsNoTracking()
            .AnyAsync(x => x.Id == currentAccountId, cancellationToken);

        if (!exists)
            return null;

        var breakdown = await GetBalancesAsync(
            companyId: null, currentAccountId, cancellationToken);

        var date = DateTime.SpecifyKind(valuationDate.Date, DateTimeKind.Utc);

        var foreign = breakdown
            .SelectMany(x => x.CurrencyBalances)
            .Where(x => x.CurrencyCode != LocalCurrency)
            .ToList();

        var results = new List<CurrentAccountCurrencyValuation>(foreign.Count);

        foreach (var currency in foreign)
        {
            var resolution = await rateResolver.ResolveAsync(
                currency.CurrencyCode, date, explicitRate: null, cancellationToken);

            if (!resolution.Success)
            {
                results.Add(new CurrentAccountCurrencyValuation(
                    currency.CurrencyCode,
                    currency.Balance,
                    currency.BalanceLocal,
                    RateAvailable: false,
                    ValuationRate: null,
                    RateSource: null,
                    ValuedLocal: null,
                    Difference: null,
                    Message: resolution.Error));

                continue;
            }

            var valued = decimal.Round(currency.Balance * resolution.Rate, 2);

            results.Add(new CurrentAccountCurrencyValuation(
                currency.CurrencyCode,
                currency.Balance,
                currency.BalanceLocal,
                RateAvailable: true,
                ValuationRate: resolution.Rate,
                RateSource: resolution.Source,
                ValuedLocal: valued,
                Difference: decimal.Round(valued - currency.BalanceLocal, 2),
                Message: null));
        }

        return new CurrentAccountValuation(
            CurrentAccountId: currentAccountId,
            ValuationDate: date,
            Currencies: results,
            TotalDifference: decimal.Round(
                results.Where(x => x.Difference.HasValue).Sum(x => x.Difference!.Value), 2),
            HasMissingRate: results.Any(x => !x.RateAvailable));
    }

    private static string Normalize(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? LocalCurrency
            : code.Trim().ToUpperInvariant();

    private IQueryable<AccountingVoucherLine> PostedLines() =>
        db.AccountingVoucherLines
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                !x.AccountingVoucher.IsDeleted &&
                x.AccountingVoucher.Status == AccountingVoucherStatus.Posted);
}
