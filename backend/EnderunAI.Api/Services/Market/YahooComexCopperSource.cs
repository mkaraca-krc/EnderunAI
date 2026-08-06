using EnderunAI.Api.Models.Market;

namespace EnderunAI.Api.Services.Market;

/// <summary>
/// COMEX bakır vadeli (HG=F) fiyatı, Yahoo Finance üzerinden. Ücretsiz
/// ve anahtar gerektirmez; varsayılan kaynak budur.
///
/// COMEX kotasyonu USD/lb'dir, sistem USD/ton üzerinden çalışır —
/// çevrim burada, tek yerde yapılır.
///
/// ÖNEMLİ: Bu LME değildir. Türkiye'deki kablo alımları LME'ye endeksli
/// olduğundan buradan gelen rakam yön olarak doğru, seviye olarak
/// sapmalıdır. Etiket her ekranda görünür; METAL_API_KEY tanımlanınca
/// LME kaynağı devreye girer.
/// </summary>
public sealed class YahooComexCopperSource(
    HttpClient httpClient,
    ILogger<YahooComexCopperSource> logger) : ICommodityPriceSource
{
    /// <summary>1 metrik ton = 2204,6226 lb.</summary>
    public const decimal PoundsPerMetricTon = 2204.6226m;

    public CommodityPriceSourceKind Kind => CommodityPriceSourceKind.Comex;

    public string Symbol => "HG=F";

    public string DisplayName => "COMEX bakır vadeli (LME değil)";

    public async Task<CommodityFetchResult> GetDailyPricesAsync(
        int days, CancellationToken cancellationToken = default)
    {
        var range = days switch
        {
            <= 7 => "5d",
            <= 35 => "1mo",
            <= 100 => "3mo",
            _ => "1y"
        };

        var path =
            $"v8/finance/chart/HG%3DF?range={range}&interval=1d";

        try
        {
            using var response = await httpClient.GetAsync(path, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new CommodityFetchResult(
                    [], $"COMEX bakır: HTTP {(int)response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            // Kotasyon birimi beklenmedik şekilde değişirse çevrim sessizce
            // yanlış olur; USD dışında bir birim geldiğinde veri alınmaz.
            var currency = YahooChartParser.ReadCurrency(json);
            if (!string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase))
            {
                return new CommodityFetchResult(
                    [], $"COMEX bakır: beklenmeyen kotasyon para birimi ({currency ?? "yok"}).");
            }

            var quotes = YahooChartParser.Parse(json);

            if (quotes.Count == 0)
                return new CommodityFetchResult([], "COMEX bakır: fiyat verisi okunamadı.");

            // USD/lb → USD/ton
            var converted = quotes
                .Select(x => x with
                {
                    Price = decimal.Round(x.Price * PoundsPerMetricTon, 2)
                })
                .ToList();

            return new CommodityFetchResult(converted, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "COMEX bakır fiyatı alınamadı.");

            return new CommodityFetchResult([], $"COMEX bakır: {ex.Message}");
        }
    }
}
