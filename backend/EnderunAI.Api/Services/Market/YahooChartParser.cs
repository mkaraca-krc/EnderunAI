using System.Text.Json;

namespace EnderunAI.Api.Services.Market;

/// <summary>Kaynaktan gelen tek günlük kapanış.</summary>
public sealed record CommodityQuote(DateTime PriceDate, decimal Price);

/// <summary>
/// Yahoo Finance chart yanıtını ayrıştırır. Saf ve statik: ağ yok,
/// sabit JSON ile test edilebilir.
///
/// İki noktaya dikkat edildi:
/// 1. Günlük barların zaman damgası borsanın yerel gün başlangıcıdır
///    (COMEX için 04:00 UTC), dolayısıyla UTC tarihi doğrudan işlem
///    gününe karşılık gelir.
/// 2. Kapanış dizisinde <c>null</c> olabilir (tatil, veri boşluğu).
///    Böyle günler atlanır — sıfır fiyat arşive girerse trend ve alım
///    fırsatı uyarısı çöker.
/// </summary>
public static class YahooChartParser
{
    public static IReadOnlyList<CommodityQuote> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return [];
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("chart", out var chart))
                return [];

            if (!chart.TryGetProperty("result", out var results)
                || results.ValueKind != JsonValueKind.Array
                || results.GetArrayLength() == 0)
            {
                return [];
            }

            var result = results[0];

            if (!result.TryGetProperty("timestamp", out var timestamps)
                || timestamps.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            if (!result.TryGetProperty("indicators", out var indicators)
                || !indicators.TryGetProperty("quote", out var quotes)
                || quotes.ValueKind != JsonValueKind.Array
                || quotes.GetArrayLength() == 0
                || !quotes[0].TryGetProperty("close", out var closes)
                || closes.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var count = Math.Min(timestamps.GetArrayLength(), closes.GetArrayLength());
            var items = new List<CommodityQuote>(count);

            for (var i = 0; i < count; i++)
            {
                if (closes[i].ValueKind != JsonValueKind.Number)
                    continue;

                if (!timestamps[i].TryGetInt64(out var epochSeconds))
                    continue;

                var close = closes[i].GetDecimal();
                if (close <= 0)
                    continue;

                var date = DateTimeOffset.FromUnixTimeSeconds(epochSeconds).UtcDateTime.Date;

                items.Add(new CommodityQuote(
                    DateTime.SpecifyKind(date, DateTimeKind.Utc), close));
            }

            return items;
        }
    }

    /// <summary>Yanıtın kotasyon para birimi; beklenmeyen bir birim sessizce kullanılmamalı.</summary>
    public static string? ReadCurrency(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);

            return document.RootElement
                .GetProperty("chart")
                .GetProperty("result")[0]
                .GetProperty("meta")
                .GetProperty("currency")
                .GetString();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException
                                       or InvalidOperationException
                                       or IndexOutOfRangeException)
        {
            return null;
        }
    }
}
