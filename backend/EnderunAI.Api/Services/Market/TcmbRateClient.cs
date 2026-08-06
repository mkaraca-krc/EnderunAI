namespace EnderunAI.Api.Services.Market;

public interface ITcmbRateClient
{
    /// <summary>
    /// Verilen günün bültenini indirir. Bülten yoksa (hafta sonu, tatil)
    /// veya TCMB erişilemiyorsa null döner; ayrım <paramref name="error"/>
    /// üzerinden yapılır: bülten yoksa error null, erişim sorunu varsa dolu.
    /// </summary>
    Task<(TcmbBulletin? Bulletin, string? Error)> GetBulletinAsync(
        DateTime date, CancellationToken cancellationToken = default);
}

/// <summary>
/// TCMB kur bülteni indirici. Ağ katmanı ayrıştırmadan bilinçli olarak
/// ayrıldı: <see cref="TcmbRateParser"/> saf kalsın ve ağsız test
/// edilebilsin diye.
///
/// Dış kaynak çökerse istisna fırlatılmaz — çağıran döngü bir günü
/// atlayıp devam etmeli, gecelik iş yarım bir hatayla durmamalı.
/// </summary>
public sealed class TcmbRateClient(
    HttpClient httpClient,
    ILogger<TcmbRateClient> logger) : ITcmbRateClient
{
    public async Task<(TcmbBulletin? Bulletin, string? Error)> GetBulletinAsync(
        DateTime date, CancellationToken cancellationToken = default)
    {
        var path = TcmbRateParser.BuildHistoricalPath(date);

        try
        {
            using var response = await httpClient.GetAsync(path, cancellationToken);

            // 404: o gün bülten yayımlanmamış (hafta sonu/tatil). Hata değil.
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (null, null);

            if (!response.IsSuccessStatusCode)
                return (null, $"TCMB {date:dd.MM.yyyy}: HTTP {(int)response.StatusCode}");

            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            var bulletin = TcmbRateParser.Parse(xml);

            if (bulletin is null)
                return (null, $"TCMB {date:dd.MM.yyyy}: bülten okunamadı.");

            // TCMB bazen istenen günün yerine en son bülteni döndürür;
            // yanlış tarihe yazmamak için bültenin kendi tarihine güvenilir.
            return (bulletin, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(
                ex, "TCMB kur bülteni alınamadı: {Date:dd.MM.yyyy}", date);

            return (null, $"TCMB {date:dd.MM.yyyy}: {ex.Message}");
        }
    }
}
