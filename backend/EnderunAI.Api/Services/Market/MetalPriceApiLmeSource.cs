using System.Globalization;
using System.Text.Json;
using EnderunAI.Api.Models.Market;

namespace EnderunAI.Api.Services.Market;

/// <summary>
/// LME bakır fiyatı, metalpriceapi.com üzerinden. Yalnızca
/// <c>METAL_API_KEY</c> tanımlıysa DI'ya girer; anahtar yoksa sistem
/// COMEX kaynağıyla çalışmaya devam eder.
///
/// DİKKAT — kotasyon birimi VARSAYILMAZ. Sağlayıcının metal fiyatlarını
/// hangi birimde döndürdüğü hesaba göre değişebiliyor; birim
/// <c>METAL_API_UNIT</c> ile açıkça beyan edilir (ton | lb | oz).
/// Beyan edilmemişse ton kabul edilir ve ilk çekimde gelen değer akla
/// yatkın aralıkta değilse veri REDDEDİLİR. Yanlış birimle 2.204 kat
/// sapmış bir bakır fiyatı, kâr etkisi ekranını sessizce çöpe çevirir.
/// </summary>
public sealed class MetalPriceApiLmeSource(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<MetalPriceApiLmeSource> logger) : ICommodityPriceSource
{
    private const string RateKey = "USDLME-XCU";

    /// <summary>Troy ons başına gram değil — metrik ton başına troy ons.</summary>
    private const decimal TroyOuncesPerMetricTon = 32150.7466m;

    /// <summary>
    /// Akla yatkın LME bakır aralığı (USD/ton). Tarihsel olarak
    /// 1.000–30.000 bandının dışına çıkmadı; bu aralığın dışı birim
    /// hatasıdır, fiyat hareketi değil.
    /// </summary>
    private const decimal MinPlausibleUsdPerTon = 1_000m;
    private const decimal MaxPlausibleUsdPerTon = 30_000m;

    public CommodityPriceSourceKind Kind => CommodityPriceSourceKind.Lme;

    public string Symbol => "LME-XCU";

    public string DisplayName => "LME bakır";

    public async Task<CommodityFetchResult> GetDailyPricesAsync(
        int days, CancellationToken cancellationToken = default)
    {
        var apiKey = configuration["METAL_API_KEY"];

        if (string.IsNullOrWhiteSpace(apiKey))
            return new CommodityFetchResult([], "LME: METAL_API_KEY tanımlı değil.");

        var end = DateTime.UtcNow.Date;
        var start = end.AddDays(-Math.Clamp(days, 1, 365));

        var path =
            $"v1/timeframe?api_key={Uri.EscapeDataString(apiKey)}" +
            $"&start_date={start:yyyy-MM-dd}&end_date={end:yyyy-MM-dd}" +
            $"&base=USD&currencies=LME-XCU";

        try
        {
            using var response = await httpClient.GetAsync(path, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return new CommodityFetchResult([], $"LME: HTTP {(int)response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            return ParseTimeframe(json, ResolveUnit());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "LME bakır fiyatı alınamadı.");

            return new CommodityFetchResult([], $"LME: {ex.Message}");
        }
    }

    private string ResolveUnit() =>
        (configuration["METAL_API_UNIT"] ?? "ton").Trim().ToLowerInvariant();

    /// <summary>
    /// metalpriceapi timeframe yanıtı:
    /// <c>{"success":true,"rates":{"2026-08-05":{"USDLME-XCU":9123.45}}}</c>
    ///
    /// Ayrıştırma statiktir ve ağ gerektirmez; birim doğrulaması burada
    /// yapılır, böylece anahtar geldiğinde davranış testle sabitlenebilir.
    /// </summary>
    public static CommodityFetchResult ParseTimeframe(string json, string unit)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new CommodityFetchResult([], "LME: boş yanıt.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return new CommodityFetchResult([], "LME: yanıt okunamadı.");
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.TryGetProperty("success", out var success)
                && success.ValueKind == JsonValueKind.False)
            {
                var message = root.TryGetProperty("error", out var error)
                    && error.TryGetProperty("message", out var text)
                        ? text.GetString()
                        : "bilinmeyen hata";

                return new CommodityFetchResult([], $"LME: {message}");
            }

            if (!root.TryGetProperty("rates", out var rates)
                || rates.ValueKind != JsonValueKind.Object)
            {
                return new CommodityFetchResult([], "LME: yanıtta fiyat bulunamadı.");
            }

            var quotes = new List<CommodityQuote>();

            foreach (var day in rates.EnumerateObject())
            {
                if (!DateTime.TryParseExact(
                        day.Name, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date))
                {
                    continue;
                }

                if (day.Value.ValueKind != JsonValueKind.Object
                    || !day.Value.TryGetProperty(RateKey, out var value)
                    || value.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                var raw = value.GetDecimal();
                if (raw <= 0)
                    continue;

                var usdPerTon = unit switch
                {
                    "lb" => raw * YahooComexCopperSource.PoundsPerMetricTon,
                    "oz" => raw * TroyOuncesPerMetricTon,
                    _ => raw
                };

                // Birim yanlış beyan edilmişse değer akla yatkın bandın
                // dışına düşer; sessizce kaydetmektense veriyi reddediyoruz.
                if (usdPerTon is < MinPlausibleUsdPerTon or > MaxPlausibleUsdPerTon)
                {
                    return new CommodityFetchResult(
                        [],
                        $"LME: {day.Name} için hesaplanan {usdPerTon:N0} USD/ton akla yatkın " +
                        "aralıkta değil. METAL_API_UNIT ayarı (ton | lb | oz) yanlış olabilir.");
                }

                quotes.Add(new CommodityQuote(
                    DateTime.SpecifyKind(date.Date, DateTimeKind.Utc),
                    decimal.Round(usdPerTon, 2)));
            }

            if (quotes.Count == 0)
                return new CommodityFetchResult([], "LME: kullanılabilir fiyat yok.");

            return new CommodityFetchResult(
                quotes.OrderBy(x => x.PriceDate).ToList(), null);
        }
    }
}
