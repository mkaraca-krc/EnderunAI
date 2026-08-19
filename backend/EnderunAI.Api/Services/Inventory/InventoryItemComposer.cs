using EnderunAI.Api.Models;

namespace EnderunAI.Api.Services.Inventory;

/// <summary>
/// STOK KARTININ ADINI VE MÜKERRER İMZASINI ÖZELLİKLERDEN ÜRETİR.
///
/// Kullanıcı ad yazmaz. Elle yazılan ad üç ayrı gerçek doğurur —
/// "Kablo Tavası 200", "200lük kablo tavası", "KABLO TAVASI 200 MM" —
/// ve aynı malzeme üç kez açılır, stok üçe bölünür.
/// </summary>
public static class InventoryItemComposer
{
    public sealed record SelectedAttribute(
        string AttributeCode,
        int SortOrder,
        string Value,
        string Display);

    /// <summary>
    /// AD: kategori adı + özellik gösterimleri, ÖZELLİK SIRASINA göre.
    ///
    /// Sıra kategori tanımından gelir (SortOrder) — seçim sırasından
    /// değil. Aksi hâlde aynı malzeme, kullanıcının doldurma sırasına
    /// göre farklı adlar alırdı.
    ///
    /// Örnek: "Kablo Tavası 200mm 1.5mm Perfore Sıcak Daldırma Galvaniz"
    /// </summary>
    public static string BuildName(
        string categoryName,
        IEnumerable<SelectedAttribute> attributes)
    {
        var parts = attributes
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.AttributeCode, StringComparer.Ordinal)
            .Select(x => x.Display.Trim())
            .Where(x => x.Length > 0);

        return string.Join(" ", new[] { categoryName.Trim() }.Concat(parts)).Trim();
    }

    /// <summary>
    /// MÜKERRER İMZASI: kategori kodu + özellik DEĞERLERİ.
    ///
    /// Ada değil DEĞERE dayanır: gösterim metni değişirse ("200mm" →
    /// "200 mm") aynı malzeme yeniden açılabilir hâle gelmemeli.
    ///
    /// Özellikler KODA GÖRE sıralanır — seçim sırası imzayı
    /// değiştirmesin diye. Sıralama `Ordinal`: kültüre bağlı
    /// sıralama Türkçe'de "I/İ" yüzünden makineden makineye
    /// değişebilir ve aynı malzeme iki farklı imza üretirdi.
    /// </summary>
    public static string BuildSignature(
        string categoryCode,
        IEnumerable<SelectedAttribute> attributes)
    {
        var parts = attributes
            .OrderBy(x => x.AttributeCode, StringComparer.Ordinal)
            .Select(x => $"{x.AttributeCode}={x.Value.Trim()}");

        return string.Join("|", new[] { categoryCode.Trim() }.Concat(parts));
    }

    /// <summary>
    /// İmza yalnız STANDART kategoride üretilir. SERBEST tipte her
    /// ürün tekildir (dekoratif aydınlatma, özel imalat); mükerrer
    /// engeli uygulanmaz ve imza null kalır.
    /// </summary>
    public static bool RequiresSignature(InventoryCategoryKind kind) =>
        kind == InventoryCategoryKind.Standard;
}
