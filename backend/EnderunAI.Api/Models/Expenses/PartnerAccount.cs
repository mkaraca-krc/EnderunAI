namespace EnderunAI.Api.Models.Expenses;

/// <summary>
/// ŞAHIS / ORTAK CARİSİ — şirketten şahsa çıkan para ve o paranın
/// faturasız giderlerle mahsubu.
///
/// NEDEN AYRI TABLO, NEDEN <see cref="CurrentAccount"/> DEĞİL:
/// mevcut cari modülü <c>current-accounts.view</c> ile okunuyor ve
/// muhasebe fişi üretiyor. Bu defter ise elden kalemler taşıyor;
/// oraya konsaydı tutarlar resmî cari ekranından sızardı ve
/// faturasız giderler resmî deftere girerdi.
///
/// İZOLASYON — <see cref="PersonnelCashPaymentEntry"/> ile aynı
/// kurallar:
/// - Muhasebe fişi YAZILMAZ.
/// - <c>CashTransaction</c> ÜRETİLMEZ: paranın şirketten çıkışı
///   kasa/banka modülünde zaten kayıtlı. İkisi birden yazsaydı aynı
///   para iki kez çıkardı.
/// - Sorgu yalnız <c>extra_payment.view</c> doğrulandıktan sonra
///   atılır.
/// </summary>
public sealed class PartnerAccount : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public string FullName { get; set; } = string.Empty;

    /// <summary>Ortak, yönetici, şirket sahibi gibi serbest etiket.</summary>
    public string? Title { get; set; }

    public string? Notes { get; set; }
}

/// <summary>Şahıs carisindeki hareketin yönü ve kaynağı.</summary>
public enum PartnerAccountEntryKind
{
    /// <summary>
    /// Şirketten şahsa çıkan para. Şahsın borcunu ARTIRIR.
    /// Paranın kendisi kasa/banka tarafında; burada yalnızca
    /// kimin ne kadar aldığı izleniyor.
    /// </summary>
    Advance = 0,

    /// <summary>
    /// Faturasız giderin mahsubu. Şahsın borcunu AZALTIR ve
    /// giderin kendisi gider merkezinde kategorize edilir.
    /// Şirket nakdini TEKRAR etkilemez: para zaten avansta çıktı.
    /// </summary>
    ExpenseSettlement = 1,

    /// <summary>Şahsın parayı geri ödemesi. Borcu azaltır.</summary>
    Repayment = 2
}

/// <summary>
/// Şahıs carisinin tek hareketi. Bakiye = avanslar − (mahsuplar +
/// geri ödemeler); yani "şahsın şirkete borcu".
/// </summary>
public sealed class PartnerAccountEntry : BaseEntity
{
    public Guid PartnerAccountId { get; set; }
    public PartnerAccount PartnerAccount { get; set; } = null!;

    public PartnerAccountEntryKind Kind { get; set; }

    public DateTime EntryDate { get; set; }

    public decimal Amount { get; set; }

    /// <summary>
    /// ZORUNLU: bu defter resmî belgeye dayanmıyor. Açıklaması
    /// olmayan bir hareket, aylar sonra kimsenin ne olduğunu
    /// söyleyemediği bir bakiye bırakır.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Mahsubu doğuran gider kaydı. Gider silinince mahsup da
    /// kalkar; bağ tek yönlü tutuluyor ki bakiye ile gider defteri
    /// arasında sahipsiz bir satır kalmasın.
    /// </summary>
    public Guid? ExpenseEntryId { get; set; }
    public ExpenseEntry? ExpenseEntry { get; set; }
}
