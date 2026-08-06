namespace EnderunAI.Api.Services.Market;

/// <summary>
/// Bir belgeye uygulanacak kurun çözümü.
/// <paramref name="IsManual"/> doluysa kur kullanıcıdan ya da belgenin
/// kendisinden gelmiştir; ekranda "elle girildi" olarak gösterilir ve
/// TCMB arşiviyle çakışsa bile ezilmez.
/// </summary>
public sealed record DocumentRateResolution(
    bool Success,
    decimal Rate,
    string Source,
    DateTime? EffectiveDate,
    bool IsManual,
    string? Error);

public interface IInvoiceExchangeRateResolver
{
    /// <summary>
    /// Fatura/çek gibi bir belgeye uygulanacak kuru bulur.
    ///
    /// Sıra: yerel para birimi → belge/kullanıcı kuru → TCMB arşivi.
    /// Hiçbiri yoksa <c>Success = false</c> döner ve belge kaydedilmez.
    /// Kur uydurulmaz: yanlış kurla defterlenmiş bir belge, hiç
    /// defterlenmemişten çok daha pahalıya patlar.
    /// </summary>
    Task<DocumentRateResolution> ResolveAsync(
        string? currencyCode,
        DateTime documentDate,
        decimal? explicitRate,
        CancellationToken cancellationToken = default);
}

public sealed class InvoiceExchangeRateResolver(IExchangeRateService exchangeRates)
    : IInvoiceExchangeRateResolver
{
    public async Task<DocumentRateResolution> ResolveAsync(
        string? currencyCode,
        DateTime documentDate,
        decimal? explicitRate,
        CancellationToken cancellationToken = default)
    {
        var code = string.IsNullOrWhiteSpace(currencyCode)
            ? "TRY"
            : currencyCode.Trim().ToUpperInvariant();

        if (code == "TRY")
        {
            return new DocumentRateResolution(
                true, 1m, "Yerel para birimi", documentDate.Date, false, null);
        }

        // Belgenin kendi beyan ettiği veya kullanıcının girdiği kur.
        // 1 değeri dövizli bir belgede kur girilmemiş demektir — geçmişte
        // tam da bu sabit yüzünden dövizli faturalar 1:1 defterleniyordu.
        if (explicitRate is > 0 and not 1m)
        {
            return new DocumentRateResolution(
                true, explicitRate.Value, "Belge/elle", documentDate.Date, true, null);
        }

        var lookup = await exchangeRates.GetAsync(code, documentDate, cancellationToken);

        if (lookup is null)
        {
            return new DocumentRateResolution(
                false, 0m, "TCMB", null, false,
                $"{code} için {documentDate:dd.MM.yyyy} tarihine kur bulunamadı. " +
                "Kur arşivi güncellenene kadar kuru elle girmeniz gerekiyor; " +
                "kur olmadan dövizli belge kaydedilemez.");
        }

        var source = lookup.DaysBack > 0
            ? $"TCMB {lookup.EffectiveDate:dd.MM.yyyy} (döviz alış)"
            : "TCMB (döviz alış)";

        return new DocumentRateResolution(
            true, lookup.ForexBuying, source, lookup.EffectiveDate, false, null);
    }
}
