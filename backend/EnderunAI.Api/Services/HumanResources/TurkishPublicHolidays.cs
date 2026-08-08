namespace EnderunAI.Api.Services.HumanResources;

/// <summary>Kayan dini bayramlar.</summary>
public enum ReligiousHolidayKind
{
    /// <summary>Ramazan Bayramı: arife yarım gün + 3 tam gün.</summary>
    Ramazan = 0,

    /// <summary>Kurban Bayramı: arife yarım gün + 4 tam gün.</summary>
    Kurban = 1
}

/// <param name="IsHalfDay">Arife günleri yarım gündür; puantajda tam
/// gün sayılmaz.</param>
public sealed record PublicHolidayDay(DateOnly Date, string Name, bool IsHalfDay);

/// <summary>
/// Türkiye resmî tatilleri.
///
/// Saf ve veritabanısız.
///
/// SABİT tatiller her yıl aynı güne düşer ve hesaplanabilir.
///
/// DİNİ bayramlar kayar ve resmî ilana bağlıdır; TARİHLERİ KODA
/// GÖMÜLMEDİ. Uydurulmuş bir bayram tarihi puantajı ve dolayısıyla
/// bordroyu sessizce yanlış üretirdi. Bunun yerine bayramın YAPISI
/// biliniyor (arife yarım gün + Ramazan'da 3, Kurban'da 4 tam gün);
/// kullanıcı yalnızca bayramın ilk gününü giriyor, günler buradan
/// türetiliyor.
/// </summary>
public static class TurkishPublicHolidays
{
    /// <summary>
    /// Yıla göre sabit resmî tatiller.
    ///
    /// 28 Ekim öğleden sonra başlar; yarım gün olarak işaretleniyor.
    /// </summary>
    public static IReadOnlyList<PublicHolidayDay> Fixed(int year)
    {
        if (year is < 2000 or > 2100)
            throw new ArgumentOutOfRangeException(nameof(year), "Geçersiz yıl.");

        return
        [
            new(new DateOnly(year, 1, 1), "Yılbaşı", false),
            new(new DateOnly(year, 4, 23),
                "Ulusal Egemenlik ve Çocuk Bayramı", false),
            new(new DateOnly(year, 5, 1), "Emek ve Dayanışma Günü", false),
            new(new DateOnly(year, 5, 19),
                "Atatürk'ü Anma, Gençlik ve Spor Bayramı", false),
            new(new DateOnly(year, 7, 15),
                "Demokrasi ve Millî Birlik Günü", false),
            new(new DateOnly(year, 8, 30), "Zafer Bayramı", false),
            new(new DateOnly(year, 10, 28), "Cumhuriyet Bayramı arifesi", true),
            new(new DateOnly(year, 10, 29), "Cumhuriyet Bayramı", false)
        ];
    }

    /// <summary>
    /// Dini bayramın günleri, bayramın BİRİNCİ gününden türetilir.
    /// Arife bir önceki gündür ve yarım gündür.
    /// </summary>
    /// <param name="firstDay">Bayramın 1. günü — resmî ilandan alınır.</param>
    public static IReadOnlyList<PublicHolidayDay> Religious(
        ReligiousHolidayKind kind, DateOnly firstDay)
    {
        var name = kind == ReligiousHolidayKind.Ramazan
            ? "Ramazan Bayramı"
            : "Kurban Bayramı";

        var fullDays = kind == ReligiousHolidayKind.Ramazan ? 3 : 4;

        var days = new List<PublicHolidayDay>(fullDays + 1)
        {
            new(firstDay.AddDays(-1), $"{name} arifesi", true)
        };

        for (var index = 0; index < fullDays; index++)
        {
            days.Add(new PublicHolidayDay(
                firstDay.AddDays(index), $"{name} {index + 1}. gün", false));
        }

        return days;
    }

    public static string KindName(ReligiousHolidayKind kind) =>
        kind == ReligiousHolidayKind.Ramazan ? "Ramazan Bayramı" : "Kurban Bayramı";
}
