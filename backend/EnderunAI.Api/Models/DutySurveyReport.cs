namespace EnderunAI.Api.Models;

/// <summary>
/// Keşif saha raporu: keşfe giden personelin iş hakkında yazdığı
/// kayıt.
///
/// BAĞIMSIZ ARŞİV: rapor işin kazanılıp kaybedilmesinden bağımsız
/// yaşar. Teklif kaybedilse de rapor kalır — bir sonraki benzer işte
/// okunacak tek şey odur. Bu yüzden kaybetme akışı raporu silmez,
/// arşivlemez, gizlemez.
///
/// ÖLÇÜMLER AYRI SATIRLARDA: metnin içine gömülmüş bir ölçü listesi
/// sonradan poza çevrilemez. Yapısal tutulduğu için keşif/BOQ
/// tarafına bağlanabilir; bu blokta o bağ KURULMUYOR, sadece
/// kurulabilir bırakılıyor.
/// </summary>
public sealed class DutySurveyReport : BaseEntity
{
    /// <summary>Rapor bir keşif görevine bağlıdır; görev başına tek rapor.</summary>
    public Guid DutyId { get; set; }
    public PersonnelDuty Duty { get; set; } = null!;

    /// <summary>
    /// Keşfedilen proje. Görevden de okunabilirdi ama rapor projeye
    /// göre sorgulanıyor (bir projenin keşif dosyası); ayrıca görev
    /// hedefi düzeltilirse raporun hangi işe yazıldığı kaymaz.
    /// </summary>
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public DateTime ReportDate { get; set; }

    /// <summary>Genel değerlendirme; raporun gövdesi.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Saha durumu: mevcut yapı, zemin, altyapı, çevre.</summary>
    public string? SiteConditions { get; set; }

    /// <summary>Ulaşım, araç girişi, iskele/vinç imkânı, depolama.</summary>
    public string? AccessNotes { get; set; }

    /// <summary>Riskler ve teklifi etkileyecek belirsizlikler.</summary>
    public string? Risks { get; set; }

    /// <summary>
    /// Keşfi yapanın teklif önerisi. KARAR DEĞİL — kazan/kaybet
    /// kararı proje tarafında ve ayrı yetkiyle verilir; bu alan
    /// sahanın görüşünü taşır.
    /// </summary>
    public bool? RecommendBid { get; set; }

    public ICollection<DutySurveyMeasurement> Measurements { get; set; }
        = new List<DutySurveyMeasurement>();

    public ICollection<DutySurveyPhoto> Photos { get; set; }
        = new List<DutySurveyPhoto>();
}

/// <summary>
/// Keşifte alınan tek ölçüm. Miktar OPSİYONEL: sahada her zaman
/// ölçülebilen bir sayı çıkmaz, "kaba tahmin" de rapora girebilmeli.
/// </summary>
public sealed class DutySurveyMeasurement : BaseEntity
{
    public Guid SurveyReportId { get; set; }
    public DutySurveyReport SurveyReport { get; set; } = null!;

    /// <summary>
    /// Listedeki sırası. Keşif ölçümleri sahada bir mantıkla yazılır
    /// (dıştan içe, kattan kata); sıra kaybolursa rapor okunmaz hale
    /// gelir. Veritabanı satır sırası garanti değildir, bu yüzden
    /// açıkça taşınıyor.
    /// </summary>
    public int SortOrder { get; set; }

    public string Description { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// Keşif fotoğrafı.
///
/// İşveren görünürlüğü BİLİNÇLİ OLARAK YOK: keşif raporu teklif
/// öncesi iç değerlendirmedir, işveren portalına açılacak bir belge
/// değil. Günlük saha raporundaki IsVisibleToEmployer bayrağının
/// buraya kopyalanması, iç notların portala sızma yolunu açardı.
/// </summary>
public sealed class DutySurveyPhoto : BaseEntity
{
    public Guid SurveyReportId { get; set; }
    public DutySurveyReport SurveyReport { get; set; } = null!;

    public string StoredFileName { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string? Caption { get; set; }
}
