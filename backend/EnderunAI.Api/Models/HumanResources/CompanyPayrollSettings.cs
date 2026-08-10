namespace EnderunAI.Api.Models;

/// <summary>
/// Şirket bazlı bordro parametreleri (tek satır/şirket). Asgari ücret,
/// SGK taban/tavan, prim oranları ve vergi oranları her yıl mevzuatla
/// değiştiği için hiçbiri koda gömülmedi; hepsi Şirket Ayarları →
/// Bordro Parametreleri ekranından yönetilir.
///
/// Fail-closed: <see cref="VerifiedAtUtc"/> boşken bordro
/// kesinleştirilemez. Sistemle gelen varsayılanlar yalnızca başlangıç
/// değeridir; yürürlükteki SGK/GİB tebliğiyle karşılaştırılıp
/// onaylanmadan resmi bordro üretilmesi engellenir.
/// </summary>
public sealed class CompanyPayrollSettings : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>Parametrelerin geçerli olduğu yıl.</summary>
    public int Year { get; set; } = DateTime.UtcNow.Year;

    // --- Asgari ücret ---

    /// <summary>Aylık brüt asgari ücret.</summary>
    public decimal MinimumWageGross { get; set; }

    /// <summary>Aylık net asgari ücret (bilgi amaçlı; hesaplama brütten yürür).</summary>
    public decimal MinimumWageNet { get; set; }

    // --- SGK ---

    /// <summary>SGK primine esas kazanç alt sınırı (genelde brüt asgari ücret).</summary>
    public decimal SgkBaseFloor { get; set; }

    /// <summary>SGK primine esas kazanç üst sınırı (tavan). Aşan kısımdan prim kesilmez.</summary>
    public decimal SgkBaseCeiling { get; set; }

    /// <summary>İşçi SGK primi oranı (%). Yasal: 14.</summary>
    public decimal SgkEmployeeRate { get; set; } = 14m;

    /// <summary>İşçi işsizlik sigortası primi oranı (%). Yasal: 1.</summary>
    public decimal UnemploymentEmployeeRate { get; set; } = 1m;

    /// <summary>İşveren SGK primi oranı (%). Yasal: 20,75.</summary>
    public decimal SgkEmployerRate { get; set; } = 20.75m;

    /// <summary>İşveren işsizlik sigortası primi oranı (%). Yasal: 2.</summary>
    public decimal UnemploymentEmployerRate { get; set; } = 2m;

    /// <summary>
    /// İşveren SGK prim indiriminden yararlanılıyor mu. Açıksa işveren
    /// SGK oranından <see cref="SgkEmployerDiscountPoints"/> düşülür.
    /// </summary>
    public bool SgkEmployerDiscountEnabled { get; set; } = true;

    /// <summary>
    /// İşveren prim indirimi puanı. İmalat dışı sektörlerde (elektrik
    /// taahhüt dahil) 2 puan: %20,75 − 2 = %18,75. İmalat sektöründe 3
    /// puana çıkabildiği için sabit değil, ayarlanabilir.
    /// </summary>
    public decimal SgkEmployerDiscountPoints { get; set; } = 2m;

    // --- Vergiler ---

    /// <summary>Damga vergisi oranı (binde). Yasal: 7,59.</summary>
    public decimal StampTaxPerMille { get; set; } = 7.59m;

    /// <summary>
    /// Asgari ücret gelir vergisi istisnası uygulanıyor mu. Açıksa her
    /// çalışanın gelir vergisinden, brüt asgari ücrete karşılık gelen
    /// vergi tutarı düşülür (7349 sayılı kanun).
    /// </summary>
    public bool MinimumWageIncomeTaxExemptionEnabled { get; set; } = true;

    /// <summary>
    /// Asgari ücret damga vergisi istisnası uygulanıyor mu. Açıksa damga
    /// vergisi matrahından brüt asgari ücret düşülür.
    /// </summary>
    public bool MinimumWageStampTaxExemptionEnabled { get; set; } = true;

    // --- Yemek / yol istisna tavanları ---
    //
    // Nakdî yemek ve yol yardımı, GÜNLÜK bir tavana kadar istisnadır;
    // tavanı aşan kısım matraha girer. Tavanlar her yıl tebliğle
    // değiştiği ve SGK ile gelir vergisi için AYRI belirlendiği için
    // dördü de ayrı parametre — hiçbiri koda gömülü değil.
    //
    // Boş (null) bırakılan tavan "bu yıl için tanımlanmadı" demektir ve
    // varsayılana düşmez: o kalemde istisna UYGULANMAZ, bordro ön
    // kontrolü uyarır. Eksik parametre yüzünden sessizce eksik vergi
    // hesaplamaktansa, fazla hesaplayıp uyarmak tercih edildi.

    /// <summary>Nakdî yemek yardımının günlük SGK istisna tavanı.</summary>
    public decimal? MealSgkExemptionDailyCap { get; set; }

    /// <summary>Nakdî yemek yardımının günlük gelir vergisi istisna tavanı.</summary>
    public decimal? MealIncomeTaxExemptionDailyCap { get; set; }

    /// <summary>Nakdî yol yardımının günlük SGK istisna tavanı.</summary>
    public decimal? TravelSgkExemptionDailyCap { get; set; }

    /// <summary>Nakdî yol yardımının günlük gelir vergisi istisna tavanı.</summary>
    public decimal? TravelIncomeTaxExemptionDailyCap { get; set; }

    // --- Fazla mesai ---

    /// <summary>
    /// Bir personelin yıl içinde çalışabileceği azami fazla mesai
    /// saati (İş Kanunu m.41: 270 saat).
    ///
    /// Koda gömülü varsayılan YOK: tanımlanmamış yılda uyarı
    /// üretilmez ve bordro ön kontrolü sınırın tanımsız olduğunu
    /// söyler. Sınır ENGEL DEĞİL uyarıdır — aşımda onaylayan görür,
    /// onay yine geçer.
    /// </summary>
    public decimal? AnnualOvertimeHourLimit { get; set; }

    // --- Çalışma süresi ---

    /// <summary>
    /// Günlük normal çalışma süresi (saat). Saatlik ücret buradan
    /// türetilir: saatlik = aylık ÷ 30 ÷ bu değer.
    ///
    /// Koda gömülü değil: tek parametre. Merkez ve şantiye için AYNI
    /// değer geçerli — ikisi arasındaki fark çalışma HAFTASINDA
    /// (cumartesi çalışılıyor mu), günlük sürede değil.
    ///
    /// Varsayılan 8: şirket yevmiyeyi 8 saat üzerinden buluyor.
    /// </summary>
    public decimal DailyWorkHours { get; set; } = 8m;

    // --- Çalışma haftası ---

    /// <summary>
    /// Şirketin varsayılan çalışma haftası (gün bayrağı; bkz.
    /// <see cref="Services.Schedule.WorkWeekDays"/>). Şantiyede yaygın
    /// olan pazar hariç her gün.
    /// </summary>
    public int WorkWeek { get; set; } = (int)Services.Schedule.WorkWeekDays.MondayToSaturday;

    /// <summary>
    /// MERKEZ kadrosunun çalışma haftası. Boşsa şirket varsayılanı
    /// geçerlidir.
    ///
    /// Ayrı tutulmasının nedeni: ofis/idari kadro genelde cumartesi
    /// çalışmaz. Tek bir şirket ayarıyla yürütülürse ya sahanın
    /// cumartesisi kaybolur ya ofise olmayan bir gün yazılır; ikisi de
    /// gün ve mesai sayısını, dolayısıyla bordroyu bozar.
    /// </summary>
    public int? HeadOfficeWorkWeek { get; set; }

    // --- Kıdem tazminatı ---

    /// <summary>
    /// Kıdem tazminatı tavanı: bir hizmet yılı için ödenecek tazminat bu
    /// tutarı aşamaz. Memur maaş katsayısına bağlı olduğu için yılda iki
    /// kez (Ocak ve Temmuz) değişir — bu yüzden hangi döneme ait olduğu
    /// <see cref="SeveranceCeilingPeriodNote"/> alanında tutulur.
    /// </summary>
    public decimal SeveranceCeiling { get; set; }

    /// <summary>Tavanın geçerli olduğu dönem (ör. "01.01.2026-30.06.2026").</summary>
    public string? SeveranceCeilingPeriodNote { get; set; }

    // --- Doğrulama ---

    /// <summary>
    /// Parametrelerin yürürlükteki mevzuatla karşılaştırılıp onaylandığı an.
    /// Boşken bordro kesinleştirilemez.
    /// </summary>
    public DateTime? VerifiedAtUtc { get; set; }
    public Guid? VerifiedByUserId { get; set; }

    /// <summary>Doğrulamada kullanılan kaynak (tebliğ no, tarih vb.).</summary>
    public string? VerificationNote { get; set; }

    public ICollection<PayrollTaxBracket> TaxBrackets { get; set; }
        = new List<PayrollTaxBracket>();
}

/// <summary>
/// Gelir vergisi dilimi. Kümülatif vergi matrahı bu dilimlere göre
/// vergilendirilir; matrah yıl içinde büyüdükçe üst dilimlere geçer.
/// </summary>
public sealed class PayrollTaxBracket : BaseEntity
{
    public Guid CompanyPayrollSettingsId { get; set; }
    public CompanyPayrollSettings CompanyPayrollSettings { get; set; } = null!;

    /// <summary>Dilim sırası (1'den başlar).</summary>
    public int Order { get; set; }

    /// <summary>Dilimin başladığı kümülatif matrah (ilk dilimde 0).</summary>
    public decimal LowerBound { get; set; }

    /// <summary>Dilimin bittiği kümülatif matrah; son dilimde null (sınırsız).</summary>
    public decimal? UpperBound { get; set; }

    /// <summary>Bu dilime uygulanan vergi oranı (%).</summary>
    public decimal Rate { get; set; }
}
