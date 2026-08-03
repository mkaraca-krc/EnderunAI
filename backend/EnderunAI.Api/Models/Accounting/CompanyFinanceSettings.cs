namespace EnderunAI.Api.Models;

/// <summary>
/// Şirket bazlı finans/muhasebe entegrasyon ayarları (tek satır/şirket).
/// Şirket Ayarları ekranından yönetilir. Varsayılan hesaplar, otomatik
/// fiş üretiminde (tedarikçi faturası, hakediş, faktoring) kullanılır;
/// boş bırakılan hesap gerektiğinde açık bir hata mesajıyla işlemi
/// durdurur (sessizce yanlış hesaba yazmak yerine).
/// </summary>
public sealed class CompanyFinanceSettings : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>
    /// Bu tutarın (TRY) ÜZERİNDEKİ tedarikçi faturaları yalnızca
    /// Admin/Genel Müdür tarafından onaylanabilir.
    /// </summary>
    public decimal GmApprovalThresholdTry { get; set; } = 100_000m;

    /// <summary>
    /// 3 yönlü kontrol (sipariş = mal kabul = fatura) tolerans yüzdesi.
    /// Fark bu yüzdeyi aşarsa fatura "tolerans dışı" işaretlenir ve GM
    /// onayı gerektirir.
    /// </summary>
    public decimal ThreeWayTolerancePercent { get; set; } = 1m;

    /// <summary>Sipariş oluştururken uygulanan varsayılan KDV oranı (%).</summary>
    public decimal DefaultVatRate { get; set; } = 20m;

    /// <summary>191 İndirilecek KDV tarafında kullanılacak hesap.</summary>
    public Guid? VatInAccountId { get; set; }
    public AccountingAccount? VatInAccount { get; set; }

    /// <summary>391 Hesaplanan KDV tarafında kullanılacak hesap.</summary>
    public Guid? VatOutAccountId { get; set; }
    public AccountingAccount? VatOutAccount { get; set; }

    /// <summary>600 Yurtiçi Satışlar tarafında kullanılacak gelir hesabı.</summary>
    public Guid? SalesAccountId { get; set; }
    public AccountingAccount? SalesAccount { get; set; }

    /// <summary>Tedarikçi faturası maliyet tarafı (ör. 740 Hizmet Üretim Maliyeti).</summary>
    public Guid? ExpenseAccountId { get; set; }
    public AccountingAccount? ExpenseAccount { get; set; }

    /// <summary>Faturasız cari için 320 Satıcılar grup/varsayılan hesabı.</summary>
    public Guid? PayablesAccountId { get; set; }
    public AccountingAccount? PayablesAccount { get; set; }

    /// <summary>Eşleşmemiş cari için 120 Alıcılar grup/varsayılan hesabı.</summary>
    public Guid? ReceivablesAccountId { get; set; }
    public AccountingAccount? ReceivablesAccount { get; set; }

    /// <summary>Faktoring komisyon/masraf kesintileri için finansman gideri hesabı (ör. 780).</summary>
    public Guid? FactoringExpenseAccountId { get; set; }
    public AccountingAccount? FactoringExpenseAccount { get; set; }
}
