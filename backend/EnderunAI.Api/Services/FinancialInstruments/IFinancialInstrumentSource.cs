using EnderunAI.Api.Contracts.Accounting;

namespace EnderunAI.Api.Services.FinancialInstruments;

/// <summary>
/// Bir finansal aracın nakit akışa verdiği tek satır.
///
/// İKİ TARİH: <see cref="TransactionDate"/> işlemin yapıldığı gün
/// (kartla harcama, kredi çekilişi, çek keşidesi),
/// <see cref="CashDate"/> paranın gerçekten hesaptan çıktığı/girdiği
/// gün (ekstre son ödeme, taksit vadesi, çek vadesi).
///
/// İkisinin ayrılması bu paketin çekirdeği: kartla bugün yapılan
/// harcama bugün nakit çıkışı DEĞİLDİR. Tek tarih tutulsaydı ya gider
/// bir ay geç görünürdü ya da nakit bir ay erken.
/// </summary>
public sealed record InstrumentCashLine(
    DateTime TransactionDate,
    DateTime CashDate,
    string Kind,
    string KindName,
    string Title,
    decimal Amount,
    bool IsInflow,
    CashFlowCertainty Certainty,
    Guid? ProjectId = null,
    string? ProjectCode = null,
    string? Reference = null);

/// <summary>
/// Nakit akışa beslenen finansal araç.
///
/// TEK SÖZLEŞME: kredi, kredi kartı ve barter aynı arayüzü uyguluyor;
/// projeksiyon hepsini AYNI okuyor ve her yeni araç için kendi içine
/// bir dal eklemek zorunda kalmıyor. Her araç kendi kuralını (hangi
/// kalem sayılır, hangisi sayılmaz) kendi servisinde tutuyor.
///
/// İPTAL/ERTELENEN SAYILMAZ: her kaynak, iptal edilmiş ya da
/// ertelenmiş kalemi kendisi eler. Çekteki iptal dersinin aynısı —
/// kapatılan bir kaydın mali etkisi de kalkmalı.
/// </summary>
public interface IFinancialInstrumentSource
{
    Task<List<InstrumentCashLine>> GetCashLinesAsync(
        Guid companyId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken);
}
