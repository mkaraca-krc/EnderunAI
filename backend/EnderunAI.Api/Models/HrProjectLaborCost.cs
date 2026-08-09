namespace EnderunAI.Api.Models;

public sealed class HrProjectLaborCost : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid PersonnelId { get; set; }

    public Guid? ProjectSiteId { get; set; }
    public ProjectSite? ProjectSite { get; set; }

    public DateTime WorkDate { get; set; }
    public Guid? AttendanceRecordId { get; set; }
    public string? WorkItemCode { get; set; }
    public string? WorkItemName { get; set; }

    /// <summary>Puantajdan taşınan icmal kısmı; boş olabilir.</summary>
    public Guid? ProjectHakedisSectionId { get; set; }

    /// <summary>
    /// Maliyetin gittiği icmal satırı. OPSİYONEL ve bilinçli olarak
    /// zorunlu değil: saha her sarfı poza etiketleyemez.
    ///
    /// Doluysa maliyet o poza AYNEN yazılır ("ölçülmüş"). Boşsa maliyet
    /// kısım düzeyinde kalır ve poz görünümünde kısımdaki pozlara
    /// sözleşme tutarı oranında DAĞITILIR — dağıtım bir tahmindir ve
    /// ekranda ölçülmüş rakamdan ayrı gösterilir.
    /// </summary>
    public Guid? ProjectBoqItemId { get; set; }
    public ProjectBoqItem? ProjectBoqItem { get; set; }


    public decimal NormalHours { get; set; }
    public decimal OvertimeHours { get; set; }
    public decimal SundayHours { get; set; }
    public decimal PublicHolidayHours { get; set; }

    public decimal NormalCost { get; set; }
    public decimal OvertimeCost { get; set; }
    public decimal SundayCost { get; set; }
    public decimal PublicHolidayCost { get; set; }
    /// <summary>
    /// Ek ücret kalemlerinden bu güne düşen paylar. Kişinin ek ücret
    /// tanımlarından türetilir; elle girilen satırlarda doğrudan
    /// yazılabilir.
    /// </summary>
    public decimal MealCost { get; set; }
    public decimal AccommodationCost { get; set; }
    public decimal ShuttleCost { get; set; }
    public decimal OtherCost { get; set; }
    /// <summary>
    /// Elden (nakit) ödenen kalemlerin bu güne düşen payı. Gerçek
    /// maliyettir ama ek ödeme yetkisi olmayan kullanıcıya
    /// gösterilmez: kâr toplamlarından bu tutar düşülerek sunulur.
    /// Alan adı "tazminat" çağrıştırsa da kova elden ödemenindir.
    /// </summary>
    public decimal CompensationCost { get; set; }
    public decimal TotalLaborCost { get; set; }

    /// <summary>
    /// Bu satırın HAKEDİŞ kâr hesabına giren kısmı. Puantaj ücreti her
    /// zaman girer; ek ücret kalemlerinden yalnızca "hakediş maliyetine
    /// dâhil" işaretli olanlar girer.
    ///
    /// Ayrı tutulmasının nedeni: proje maliyeti ile hakedişe yansıyan
    /// maliyet aynı şey değil. Şirketin üstlendiği ama işverene
    /// yansıtılmayan kalemler proje kârını düşürür, hakediş kârını
    /// değil.
    /// </summary>
    public decimal ProgressPaymentCost { get; set; }

    /// <summary>
    /// ProgressPaymentCost içindeki elden ödeme payı. Hakediş kârı da
    /// yetkisiz kullanıcıya maskelenebilsin diye ayrı tutulur.
    /// </summary>
    public decimal ProgressPaymentCompensationCost { get; set; }

    public string CurrencyCode { get; set; } = "TRY";
}
