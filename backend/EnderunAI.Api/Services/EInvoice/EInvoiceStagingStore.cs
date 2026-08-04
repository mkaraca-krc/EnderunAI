using Microsoft.Extensions.Caching.Memory;

namespace EnderunAI.Api.Services.EInvoice;

/// <summary>Önizlenmiş ama henüz kaydedilmemiş fatura.</summary>
public sealed record StagedInvoice(
    string FileName,
    string Xml,
    ParsedInvoice Invoice,
    InvoiceDirection Direction,
    InvoiceParseSource Source,
    bool RequiresManualReview);

public interface IEInvoiceStagingStore
{
    /// <summary>Faturayı geçici olarak tutar, commit anahtarını döner.</summary>
    string Store(StagedInvoice invoice);

    /// <summary>Anahtarla geri alır; süresi dolmuşsa null.</summary>
    StagedInvoice? Take(string token);
}

/// <summary>
/// Önizleme ile kesinleştirme arasındaki köprü.
///
/// Kullanıcı dosyayı bir kez yükler, önizlemede kontrol eder, sonra
/// onaylar. Aradaki XML'i istemciye geri gönderip tekrar almak hem
/// pahalı hem de güvensiz olurdu (istemci içeriği değiştirebilirdi),
/// bu yüzden sunucuda tutulur.
///
/// Bellekte tutuluyor: veri geçici, tek oturumluk ve kayıp hâlinde
/// kullanıcı dosyayı yeniden yükleyebilir. Süre dolarsa önizleme
/// baştan yapılır.
/// </summary>
public sealed class EInvoiceStagingStore(IMemoryCache cache) : IEInvoiceStagingStore
{
    /// <summary>Ön muhasebenin kontrol etmesi için makul süre.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);

    public string Store(StagedInvoice invoice)
    {
        var token = Guid.NewGuid().ToString("N");

        cache.Set(Key(token), invoice, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = Lifetime,
            Size = 1
        });

        return token;
    }

    public StagedInvoice? Take(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        return cache.TryGetValue<StagedInvoice>(Key(token), out var staged)
            ? staged
            : null;
    }

    private static string Key(string token) => $"einvoice-staging:{token}";
}
