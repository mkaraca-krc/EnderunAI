using System.Text;

namespace EnderunAI.Api.Services.Engineering;

/// <summary>
/// Arama terimini Türkçeye duyarlı biçimde sadeleştirir.
///
/// VERİTABANIYLA AYNI KURAL: engineering_positions tablosundaki
/// üretilmiş "SearchNormalized" sütunu tam olarak bu eşlemeyi
/// uyguluyor (AddPositionTrigramSearch migration'ı). İkisi ayrışırsa
/// arama sessizce boş dönmeye başlar — bu yüzden harf listesi iki
/// yerde de birebir aynı sırada.
///
/// İ/ı/I/i hepsi "i"ye iniyor: Türkçe klavyede en sık yapılan arama
/// hatası bu. Aksanlı harfler de ASCII karşılığına düşüyor ki
/// "sarj"/"şarj" ya da "olcu"/"ölçü" aynı sonucu versin.
/// </summary>
public static class TurkishSearch
{
    private const string From = "ÇĞİIÖŞÜçğıöşüÂÎÛâîû";
    private const string To = "CGIIOSUcgiosuAIUaiu";

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);

        foreach (var character in value.Trim())
        {
            var index = From.IndexOf(character);

            builder.Append(index >= 0 ? To[index] : character);
        }

        // Küçültme, harfler ASCII'ye indikten SONRA: kültüre bağlı
        // ToLower Türkçe "I" harfini "ı" yapıp eşleşmeyi bozardı.
        return builder.ToString().ToLowerInvariant();
    }
}
