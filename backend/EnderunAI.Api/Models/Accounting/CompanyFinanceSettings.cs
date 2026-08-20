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

    /// <summary>
    /// Maaş ödeme günü (ayın kaçı). Nakit akış takvimi bordro çıkışını
    /// bu güne koyar.
    /// 
    /// Bordro kaydında ileriye dönük bir ödeme tarihi yok — PaidAtUtc
    /// ödeme SONRASI damga. Bu parametre olmadan şirketin en büyük ve
    /// en düzenli aylık çıkışı takvimde hiç görünmüyordu ve tablo
    /// ciddi şekilde iyimser çıkıyordu.
    /// </summary>
    public int PayrollPaymentDay { get; set; } = 5;

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

    /// <summary>
    /// 610 Satıştan İadeler. Satış iadesinde 600 borçlandırılmaz;
    /// iadeler kendi hesabında toplanır ki brüt satış rakamı bozulmasın.
    /// Boşsa hesap planından 610.01/610 aranır.
    /// </summary>
    public Guid? SalesReturnAccountId { get; set; }
    public AccountingAccount? SalesReturnAccount { get; set; }

    /// <summary>
    /// 190 Devreden KDV. Dönem sonu KDV tahakkukunda indirilecek KDV
    /// hesaplanandan büyükse fark buraya devreder.
    /// </summary>
    public Guid? VatCarryForwardAccountId { get; set; }
    public AccountingAccount? VatCarryForwardAccount { get; set; }

    /// <summary>
    /// 360.99 Ödenecek KDV. Dönem sonunda hesaplanan KDV fazlaysa
    /// yükümlülük buraya yazılır.
    /// </summary>
    public Guid? VatPayableAccountId { get; set; }
    public AccountingAccount? VatPayableAccount { get; set; }

    /// <summary>
    /// 191.05 Sorumlu sıfatıyla beyan edilen KDV — tevkifatlı alışta
    /// bizim beyan edip indirdiğimiz kısım.
    /// </summary>
    public Guid? ReverseChargeVatInputAccountId { get; set; }
    public AccountingAccount? ReverseChargeVatInputAccount { get; set; }

    /// <summary>
    /// 360.002 Sorumlu sıfatıyla ödenecek KDV — tevkifatlı alışta
    /// tedarikçiye değil vergi dairesine ödediğimiz kısım.
    /// </summary>
    public Guid? ReverseChargeVatPayableAccountId { get; set; }
    public AccountingAccount? ReverseChargeVatPayableAccount { get; set; }

    /// <summary>Tedarikçi faturası maliyet tarafı (ör. 740 Hizmet Üretim Maliyeti).</summary>
    public Guid? ExpenseAccountId { get; set; }
    public AccountingAccount? ExpenseAccount { get; set; }

    /// <summary>
    /// ALIŞ (stok) faturasının borç tarafı — ör. 153 Ticari Mallar veya
    /// 150 İlk Madde ve Malzeme. Boşsa maliyet hesabına düşülür ki
    /// ayar yapılmamış şirkette fatura onayı kilitlenmesin.
    /// </summary>
    public Guid? InventoryAccountId { get; set; }
    public AccountingAccount? InventoryAccount { get; set; }

    /// <summary>Faturasız cari için 320 Satıcılar grup/varsayılan hesabı.</summary>
    public Guid? PayablesAccountId { get; set; }
    public AccountingAccount? PayablesAccount { get; set; }

    /// <summary>Eşleşmemiş cari için 120 Alıcılar grup/varsayılan hesabı.</summary>
    public Guid? ReceivablesAccountId { get; set; }
    public AccountingAccount? ReceivablesAccount { get; set; }

    /// <summary>
    /// SAYIM NOKSANI hesabı. Boşsa 689.02 Stok Sayım Noksanları
    /// kullanılır (S6c'de açıldı).
    ///
    /// SEÇİLEBİLİR OLMASI KULLANICI İSTEĞİ: kimi firma noksanı
    /// doğrudan gidere, kimi 157 Stok Değer Düşüklüğü Karşılığı'na
    /// yazmayı tercih ediyor. Karar mali müşavirin; sistem dayatmıyor
    /// ama boş bırakılırsa da durmuyor.
    /// </summary>
    public Guid? StockCountShortageAccountId { get; set; }
    public AccountingAccount? StockCountShortageAccount { get; set; }

    /// <summary>
    /// SAYIM FAZLASI hesabı. Boşsa 649.03 Stok Sayım Fazlaları.
    /// </summary>
    public Guid? StockCountSurplusAccountId { get; set; }
    public AccountingAccount? StockCountSurplusAccount { get; set; }

    /// <summary>Faktoring komisyon/masraf kesintileri için finansman gideri hesabı (ör. 780).</summary>
    public Guid? FactoringExpenseAccountId { get; set; }
    public AccountingAccount? FactoringExpenseAccount { get; set; }

    /// <summary>
    /// Hakediş kesintileri (teminat, stopaj, malzeme) için varsayılan
    /// hesap — ör. 126 Verilen Depozito ve Teminatlar. Kesinti satırında
    /// kendi hesabı seçilmişse o önceliklidir.
    /// </summary>
    public Guid? DeductionAccountId { get; set; }
    public AccountingAccount? DeductionAccount { get; set; }

    /// <summary>Bordro gider hesabı — ör. 770 Genel Yönetim Giderleri.</summary>
    public Guid? PayrollExpenseAccountId { get; set; }
    public AccountingAccount? PayrollExpenseAccount { get; set; }

    /// <summary>335 Personele Borçlar — tahakkuk eden net ücret.</summary>
    public Guid? PayrollPayableAccountId { get; set; }
    public AccountingAccount? PayrollPayableAccount { get; set; }

    /// <summary>360 Ödenecek Vergi ve Fonlar — gelir ve damga vergisi.</summary>
    public Guid? TaxPayableAccountId { get; set; }
    public AccountingAccount? TaxPayableAccount { get; set; }

    /// <summary>361 Ödenecek Sosyal Güvenlik Kesintileri — işçi ve işveren payı.</summary>
    public Guid? SocialSecurityPayableAccountId { get; set; }
    public AccountingAccount? SocialSecurityPayableAccount { get; set; }

    /// <summary>
    /// 195 İş Avansları — bordroda avans kesintisi varsa bu hesap kapanır.
    /// Yalnızca kesinti bulunan dönemlerde gerekir.
    /// </summary>
    public Guid? EmployeeAdvanceAccountId { get; set; }
    public AccountingAccount? EmployeeAdvanceAccount { get; set; }
}
