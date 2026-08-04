namespace EnderunAI.Api.Models;

/// <summary>
/// Faturanın hangi okuyucudan geldiği — veritabanına yazılan hâli.
///
/// Servis katmanındaki <c>Services.EInvoice.InvoiceParseSource</c> ile
/// aynı anlamı taşır; model katmanı servise bağımlı olmasın diye ayrı
/// tutuldu. Değerler kalıcıdır, değiştirilmemeli.
/// </summary>
public enum EInvoiceParseSource
{
    /// <summary>Elle girilmiş — XML'den okunmadı.</summary>
    Manual = 0,

    /// <summary>Standart UBL-TR ayrıştırıcı.</summary>
    Standard = 1,

    /// <summary>
    /// AI yedek ayrıştırıcı. Bu faturalar her zaman elle kontrol
    /// edilmeli; otomatik onay yolu yoktur.
    /// </summary>
    Ai = 2
}
