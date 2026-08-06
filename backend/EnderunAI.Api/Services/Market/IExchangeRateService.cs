using EnderunAI.Api.Models.Market;

namespace EnderunAI.Api.Services.Market;

/// <summary>
/// Bir tarihe karşılık bulunan kur. <paramref name="EffectiveDate"/>
/// istenen tarihten eski olabilir: TCMB hafta sonu ve tatilde bülten
/// yayımlamaz, o günler için en yakın önceki bülten kullanılır.
/// Hangi bültenin kullanıldığı gizlenmez, çağıran tarafa söylenir.
/// </summary>
public sealed record ExchangeRateLookup(
    string CurrencyCode,
    DateTime RequestedDate,
    DateTime EffectiveDate,
    decimal ForexBuying,
    decimal ForexSelling,
    string Source)
{
    /// <summary>İstenen tarihle kullanılan bülten arasındaki gün farkı.</summary>
    public int DaysBack => (RequestedDate.Date - EffectiveDate.Date).Days;
}

/// <summary>Arşivin tazeliği — kartlardaki "güncellenemedi" uyarısı buradan.</summary>
public sealed record ExchangeRateFreshness(
    DateTime? LatestRateDate,
    int? DaysSinceLatest,
    bool IsStale,
    string? Warning);

public sealed record ExchangeRateRefreshResult(
    int FetchedDays,
    int AlreadyPresentDays,
    int UnavailableDays,
    IReadOnlyList<string> Errors,
    string Message);

public interface IExchangeRateService
{
    /// <summary>
    /// Verilen tarihe uygulanacak kuru döner. Arşivde o para birimine
    /// ait hiç kayıt yoksa <c>null</c> döner — kur uydurulmaz, çağıran
    /// dövizli işlemi reddetmek zorundadır.
    /// TRY için her zaman 1 döner.
    /// </summary>
    Task<ExchangeRateLookup?> GetAsync(
        string currencyCode, DateTime date, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExchangeRate>> GetRangeAsync(
        string currencyCode,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);

    Task<ExchangeRateFreshness> GetFreshnessAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verilen aralıktaki eksik günleri TCMB'den çeker. Zaten arşivde
    /// olan günlere dokunmaz; bülten yayımlanmamış günleri sessizce atlar.
    /// </summary>
    Task<ExchangeRateRefreshResult> RefreshAsync(
        DateTime from, DateTime to, CancellationToken cancellationToken = default);
}
