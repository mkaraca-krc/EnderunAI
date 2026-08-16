namespace EnderunAI.Api.Services.Units;

/// <summary>
/// BİRİM YAZIMI NORMALİZASYONU — TEK KAYNAK.
///
/// Aynı fiziksel birimin farklı yazımlarını tek kanonik değere indirir.
/// Karşılaştırma yapan her yer buradan geçmeli; iki ayrı yerde iki ayrı
/// sözlük tutulursa zamanla ayrışır ve aynı dosya bir ekranda bağlanıp
/// ötekinde atlanır.
///
/// NEDEN GEREKTİ: poz kütüphanesinde adet birimi "Ad" (7.429 kayıt) ve
/// "AD" (7.199 kayıt) olarak yazılı; Enderun'un stok kartlarında ise
/// "Adet". Reçete aktarımı birimi stok kartıyla karşılaştırdığı için
/// "Ad" yazan her satır uyuşmazlık sayılıp atlanıyordu — oysa ikisi
/// aynı birim.
///
/// SÖZLÜK BİLİNÇLİ OLARAK DAR: yalnızca GERÇEKTEN eşdeğer yazımlar.
/// Farklı fiziksel birimler (m, m², kg, m³) ASLA birbirine
/// normalize edilmez; "m" yazan bir satırın "Adet" kartına sessizce
/// bağlanması, reçeteye yanlış miktar yazmak demektir ve bunu sonradan
/// fark etmek çok zordur.
///
/// Yani bu fonksiyon uyuşmazlık kontrolünü GEVŞETMİYOR; yalnızca
/// eşdeğer yazımların uyuşmazlık sayılmasını engelliyor. Gerçek
/// uyuşmazlık hâlâ satırı atlar.
/// </summary>
public static class UnitNormalizer
{
    /// <summary>
    /// Yazım -> kanonik değer. Anahtarlar zaten büyük harfe çevrilmiş
    /// ve kırpılmış hâlleriyle aranıyor, bu yüzden burada yalnız
    /// GERÇEKTEN farklı yazımlar listeleniyor ("Ad" ile "ad" ayrı
    /// satır değil).
    /// </summary>
    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.Ordinal)
    {
        // Adet
        ["AD"] = "ADET",
        ["ADET"] = "ADET",
        ["ADT"] = "ADET",

        // Metre — "MT" saha yazımı, "M" kitap yazımı
        ["M"] = "M",
        ["MT"] = "M",
        ["METRE"] = "M",

        // Metrekare
        ["M2"] = "M²",
        ["M²"] = "M²",

        // Metreküp
        ["M3"] = "M³",
        ["M³"] = "M³",

        // Kilogram
        ["KG"] = "KG",
        ["KGM"] = "KG",

        // Ton
        ["TON"] = "TON",

        // Litre
        ["LT"] = "L",
        ["L"] = "L",
        ["LITRE"] = "L",

        // Saat — poz kütüphanesinde işçilik "Sa"
        ["SA"] = "SAAT",
        ["SAAT"] = "SAAT",
        ["ST"] = "SAAT",

        // Takım / paket
        ["TK"] = "TAKIM",
        ["TAKIM"] = "TAKIM",
        ["PK"] = "PAKET",
        ["PAKET"] = "PAKET"
    };

    /// <summary>
    /// Birimi kanonik yazımına çevirir.
    ///
    /// Sözlükte olmayan birim OLDUĞU GİBİ (büyük harfe çevrilmiş ve
    /// kırpılmış) döner — tanınmayan bir birimi uydurma bir karşılığa
    /// eşlemek, sessizce yanlış eşleşme üretirdi.
    ///
    /// Boş ya da null giriş boş dize döner; çağıran taraf boş birimi
    /// kendi kuralına göre değerlendirir.
    /// </summary>
    public static string Normalize(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
            return string.Empty;

        // Türkçe'ye özgü büyük harf çevrimi bilinçli olarak KULLANILMIYOR:
        // "i" harfi kültüre göre "İ" ya da "I" oluyor ve sunucunun
        // kültürü değiştiğinde aynı birim iki farklı anahtara düşerdi.
        var trimmed = unit.Trim().ToUpperInvariant();

        return Synonyms.GetValueOrDefault(trimmed, trimmed);
    }

    /// <summary>
    /// İki birim yazımı aynı fiziksel birimi mi gösteriyor.
    ///
    /// Boş birimler eşit SAYILMAZ: birimi olmayan iki kayıt aynı
    /// birimde demek değildir, bilgi eksik demektir.
    /// </summary>
    public static bool AreEquivalent(string? left, string? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);

        return a.Length > 0 && b.Length > 0 && string.Equals(a, b, StringComparison.Ordinal);
    }
}
