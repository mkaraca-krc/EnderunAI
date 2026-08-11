namespace EnderunAI.Api.Services.Expenses;

/// <summary>
/// Kurulumla gelen gider kategorileri ve otomatik kaynakların hangi
/// koda düştüğü. TEK KAYNAK: hem seed hem otomatik toplama aynı
/// listeden okur. İki yere kopyalansaydı, satın alma "malzeme" yazıp
/// rapor "Malzeme" arar ve kalem kategorisiz düşerdi.
/// </summary>
public static class ExpenseCategoryCatalog
{
    public sealed record Definition(
        string Code,
        string Name,
        int SortOrder,
        bool AutomaticOnly = false);

    // Elle girilebilen kategoriler — otomatik akmayan kalemler için.
    public const string Rent = "kira";
    public const string Supplies = "sarf";
    public const string Utilities = "faturalar";
    public const string Stationery = "kirtasiye";
    public const string Vehicle = "arac-yakit";
    public const string Maintenance = "bakim";
    public const string Meals = "yemek";
    public const string Accommodation = "konaklama";
    public const string Allowance = "harcirah";
    public const string Other = "diger";

    // Yalnız otomatik kaynaklardan dolanlar.
    public const string Material = "malzeme";
    public const string Labor = "iscilik";
    public const string Subcontractor = "taseron";
    public const string Travel = "yol";

    /// <summary>Kredi faizi, kart komisyonu — finansman gideri.</summary>
    public const string Financing = "finansman";

    /// <summary>
    /// Başlangıç seti. Sıra ekranda göründüğü sıradır; otomatik
    /// kategoriler sona alınmıştır çünkü elle giriş listesinde hiç
    /// görünmezler.
    /// </summary>
    public static readonly IReadOnlyList<Definition> Defaults =
    [
        new(Rent, "Kira", 10),
        new(Supplies, "Sarf (çay-şeker, temizlik)", 20),
        new(Utilities, "Faturalar (elektrik, su, doğalgaz, internet)", 30),
        new(Stationery, "Kırtasiye", 40),
        new(Vehicle, "Araç / Yakıt", 50),
        new(Maintenance, "Bakım", 60),
        new(Meals, "Yemek", 70),
        new(Accommodation, "Konaklama", 80),
        new(Allowance, "Harcırah", 90),
        new(Other, "Diğer", 100),

        new(Material, "Malzeme", 110, AutomaticOnly: true),
        new(Labor, "İşçilik", 120, AutomaticOnly: true),
        new(Subcontractor, "Taşeron", 130, AutomaticOnly: true),
        new(Travel, "Yol", 140, AutomaticOnly: true),
        new(Financing, "Finansman gideri (faiz/komisyon)", 150, AutomaticOnly: true)
    ];
}
