using EnderunAI.Api.Search;

namespace EnderunAI.Api.Services.Messaging;

/// <summary>
/// MESAJ ARAMA KURALI — SAF, TEK YERDE.
///
/// EN AZ ÜÇ HARF. Ölçüldü (2026-08-25, 200.000 ve 500.000 satır,
/// PG 16.15): iki harflik sorguda trigram indeksi devre dışı kalıyor
/// ve sorgu sıra taramasına düşüyor — 200 bin satırda 86 ms, iki
/// milyonda saniyenin altında kalmaz. İkinci bir indeks (tsvector)
/// açmak yazmayı %25 ağırlaştırırdı; mesaj sistemin en çok yazılan
/// tablosu. Sorguyu engellemek doğru: iki harflik arama zaten
/// kullanışlı sonuç vermiyor.
///
/// KARAR NEDEN SAF FONKSİYON: aynı kural hem sunucuda hem ekranda
/// geçerli. İki yere gömülseydi eşzamanlılık paketinde yaşadığımızın
/// aynısı olurdu — iki bariyer birbirini örter, hiçbiri tek başına
/// sondalanamaz ve yeşil hiçbir şey söylemez (Kural 25).
///
/// HARF SAYISI **KATLANMIŞ** METİN ÜZERİNDEN: kullanıcı "İŞÇ" yazdığında
/// üç harf saymalıyız. Katlama sonrası uzunluk değişmiyor ama boşluk
/// kırpma ve büyük/küçük aynı yerden geçsin diye kural tek kapıdan
/// yürüyor.
/// </summary>
public static class MesajAramaKurali
{
    /// <summary>Aranan metinde olması gereken en az harf sayısı.</summary>
    public const int EnAzHarf = 3;

    public const string Uyari =
        "Arama için en az 3 harf yazın. Daha kısa aramalar tüm mesajları "
        + "taramak zorunda kalır ve kullanışlı sonuç vermez.";

    /// <summary>
    /// Sorgu aranabilir mi. Boş ya da üç harften kısa ise HAYIR —
    /// "boş sorgu her kaydı geçirir" kuralı burada GEÇERSİZ: mesaj
    /// aramada boş sorgu, tüm mesajları dökmek demek olurdu.
    /// </summary>
    public static bool Gecerli(string? sorgu) => Normalize(sorgu).Length >= EnAzHarf;

    /// <summary>
    /// Aramaya girecek hâli: kırpılmış ve katlanmış. Veritabanındaki
    /// `SearchFold` kolonu da aynı katlamadan geçiyor; ikisi
    /// ayrışırsa aynı arama bir yerde bulur, diğerinde bulamaz.
    /// </summary>
    public static string Normalize(string? sorgu) =>
        TurkishSearch.Fold((sorgu ?? string.Empty).Trim());
}
