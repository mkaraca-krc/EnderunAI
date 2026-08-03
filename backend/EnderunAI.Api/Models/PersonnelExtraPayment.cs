namespace EnderunAI.Api.Models;

/// <summary>
/// Personelin resmi bordroda görünmeyen, elden ödenen aylık tutarı.
///
/// AYRI TABLO OLMASI BİLİNÇLİ: bu tutar <see cref="HumanResources.HrSalaryDefinition"/>
/// üzerine kolon olarak eklenseydi, ücret kartını okuyan mevcut uçların
/// projeksiyonlarına farkında olmadan sızabilirdi — ücret kartlarının
/// personnel.view üzerinden sızdığı hata bir kez yaşandı. Ayrı tablo,
/// extra_payment.view izni olmayan bir kullanıcının sorgusunun bu
/// tabloya hiç uğramaması demek.
///
/// Resmi muhasebeye HİÇBİR fiş yazmaz; yalnızca gerçek maliyetin ve
/// gerçek tazminat yükümlülüğünün hesaplanabilmesi için tutulur.
/// </summary>
public sealed class PersonnelExtraPayment : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid PersonnelId { get; set; }
    public Personnel Personnel { get; set; } = null!;

    /// <summary>Aylık elden ödenen tutar.</summary>
    public decimal MonthlyAmount { get; set; }

    public DateTime EffectiveStartDate { get; set; }

    /// <summary>Boşsa hâlâ yürürlükte.</summary>
    public DateTime? EffectiveEndDate { get; set; }

    public string? Note { get; set; }

    /// <summary>Verilen tarihte bu kaydın yürürlükte olup olmadığı.</summary>
    public bool IsEffectiveOn(DateTime date) =>
        EffectiveStartDate.Date <= date.Date &&
        (EffectiveEndDate is null || EffectiveEndDate.Value.Date >= date.Date);
}
