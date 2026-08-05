namespace EnderunAI.Api.Services.Tax;

/// <summary>Takvimdeki bir vergi yükümlülüğünün türü.</summary>
public enum TaxObligationKind
{
    /// <summary>KDV (391 − 191 netleştirmesi).</summary>
    Vat = 0,

    /// <summary>SGK primi (işçi + işveren).</summary>
    SocialSecurity = 1,

    /// <summary>Muhtasar: gelir vergisi stopajı + damga.</summary>
    Withholding = 2,

    /// <summary>Üç aylık geçici vergi.</summary>
    AdvanceTax = 3
}

/// <summary>
/// Vergi ödeme tarihleri.
///
/// Tarihler TEK YERDE: nakit akış, takvim ekranı ve Hızır hatırlatması
/// aynı fonksiyonu çağırır. Üç yere kopyalansaydı mevzuat değiştiğinde
/// biri güncellenip diğerleri unutulur ve nakit akış yanlış güne
/// çıkardı.
///
/// Gün seçimleri kullanıcının belirlediği şekildedir (aylık ödemeler
/// ayın 26'sı, geçici vergi dönem sonrası 2. ayın 17'si). Resmî takvim
/// değişirse yalnızca burası güncellenir.
/// </summary>
public static class TaxCalendar
{
    /// <summary>Aylık beyan/ödemelerin günü.</summary>
    public const int MonthlyPaymentDay = 26;

    /// <summary>Geçici vergi ödemesinin günü.</summary>
    public const int AdvanceTaxPaymentDay = 17;

    /// <summary>
    /// Bir dönemin aylık ödeme tarihi: dönemi izleyen ayın 26'sı.
    /// (03/2026 dönemi → 26.04.2026)
    /// </summary>
    public static DateTime MonthlyDueDate(int year, int month)
    {
        var next = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);

        return new DateTime(
            next.Year, next.Month,
            Math.Min(MonthlyPaymentDay, DateTime.DaysInMonth(next.Year, next.Month)),
            0, 0, 0, DateTimeKind.Utc);
    }

    /// <summary>
    /// Geçici vergi ödeme tarihi: dönemi izleyen ikinci ayın 17'si.
    /// 1. dönem → 17 Mayıs, 2. dönem → 17 Ağustos, 3. dönem → 17 Kasım,
    /// 4. dönem → ertesi yıl 17 Şubat.
    /// </summary>
    public static DateTime AdvanceTaxDueDate(int year, int quarter)
    {
        var periodEndMonth = quarter * 3;
        var due = new DateTime(year, periodEndMonth, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddMonths(2);

        return new DateTime(
            due.Year, due.Month, AdvanceTaxPaymentDay, 0, 0, 0, DateTimeKind.Utc);
    }

    public static string KindName(TaxObligationKind kind) => kind switch
    {
        TaxObligationKind.Vat => "KDV",
        TaxObligationKind.SocialSecurity => "SGK primi",
        TaxObligationKind.Withholding => "Muhtasar (stopaj + damga)",
        TaxObligationKind.AdvanceTax => "Geçici vergi",
        _ => kind.ToString()
    };
}
