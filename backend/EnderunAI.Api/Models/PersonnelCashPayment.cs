namespace EnderunAI.Api.Models;

/// <summary>
/// Elden ödemenin türü.
/// </summary>
public enum PersonnelCashPaymentKind
{
    /// <summary>Aylık elden ücret ödemesi.</summary>
    MonthlySalary = 0,

    /// <summary>Elden verilen avans.</summary>
    Advance = 1,

    /// <summary>Prim, ikramiye vb.</summary>
    Bonus = 2,

    /// <summary>Ayrılışta ödenen elden fark.</summary>
    Severance = 3,

    Other = 99
}

/// <summary>
/// Personele FİİLEN elden ödenen tutarın kaydı.
///
/// <see cref="PersonnelExtraPayment"/> ile karıştırılmamalı: o, aylık
/// ne ödeneceğinin TANIMIdır; bu ise gerçekten ne zaman ne kadar
/// ödendiğinin DEFTERİdir. Tanım olmadan da ödeme yapılabilir (bir
/// kerelik prim gibi), bu yüzden ikisi arasında zorunlu bağ yoktur.
///
/// İZOLASYON — bu tablonun kuralları:
/// - Muhasebe fişi YAZILMAZ. Resmî deftere girmeyen bir ödeme.
/// - <c>CashTransaction</c> üretilmez: kasa/banka bakiyesi resmî
///   defterle mutabık kalmalı; bu ödeme oradan çıkmadı.
/// - <c>ProjectCostTransaction</c> yazılmaz: o defter projects.view
///   ile okunuyor ve elden tutar oradan sızardı. Proje maliyetine
///   katkısı okuma anında, yetki doğrulanarak eklenir
///   (ProjectCostAnalysisService).
/// - Sorgu YALNIZCA <c>extra_payment.view</c> doğrulandıktan sonra
///   atılır; yetkisiz kullanıcının sorgusu bu tabloya hiç uğramaz.
/// </summary>
public sealed class PersonnelCashPaymentEntry : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid PersonnelId { get; set; }
    public Personnel Personnel { get; set; } = null!;

    public PersonnelCashPaymentKind Kind { get; set; } =
        PersonnelCashPaymentKind.MonthlySalary;

    public DateTime PaymentDate { get; set; }

    public decimal Amount { get; set; }

    /// <summary>
    /// Hangi bordro dönemine ait olduğu. Aylık ücret ödemesinde
    /// doldurulur; prim gibi dönemsiz ödemelerde boş kalır.
    /// </summary>
    public int? PeriodYear { get; set; }
    public int? PeriodMonth { get; set; }

    /// <summary>Ödemeyi yapan/kaydeden.</summary>
    public Guid? RecordedByUserId { get; set; }

    public string? Note { get; set; }
}
