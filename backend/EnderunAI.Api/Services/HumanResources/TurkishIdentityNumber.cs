namespace EnderunAI.Api.Services.HumanResources;

/// <summary>
/// T.C. kimlik numarası doğrulaması.
///
/// Saf ve veritabanısız.
///
/// Bugüne kadar yalnızca TEKİLLİK aranıyordu; 11 hane ve sağlama
/// kontrolü yoktu. Yanlış yazılmış bir kimlik numarası sisteme
/// girebiliyor, sorun ancak SGK bildirimi reddedildiğinde —
/// yani aylar sonra, bordro döneminde — ortaya çıkıyordu.
///
/// Kural (NVİ): 11 hane, ilk hane sıfır olamaz,
///   10. hane = ((1,3,5,7,9. hanelerin toplamı × 7) −
///               (2,4,6,8. hanelerin toplamı)) mod 10
///   11. hane = ilk 10 hanenin toplamı mod 10
/// </summary>
public static class TurkishIdentityNumber
{
    public const int Length = 11;

    /// <summary>
    /// Numara geçerli mi. BOŞ değer burada geçerli SAYILMAZ; alanın
    /// zorunlu olup olmadığına çağıran karar verir — kimlik numarası
    /// girilmemiş personel kaydı kabul ediliyor, yanlış girilmiş
    /// olan kabul edilmiyor.
    /// </summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();

        if (text.Length != Length)
            return false;

        Span<int> digits = stackalloc int[Length];

        for (var index = 0; index < Length; index++)
        {
            var character = text[index];

            if (character is < '0' or > '9')
                return false;

            digits[index] = character - '0';
        }

        if (digits[0] == 0)
            return false;

        var odd = digits[0] + digits[2] + digits[4] + digits[6] + digits[8];
        var even = digits[1] + digits[3] + digits[5] + digits[7];

        // Fark negatif olabileceği için mod'u pozitife çekiyoruz:
        // C#'ta (-3 % 10) == -3, bu da geçerli numaraları reddederdi.
        var tenth = ((odd * 7 - even) % 10 + 10) % 10;

        if (tenth != digits[9])
            return false;

        var sum = 0;

        for (var index = 0; index < 10; index++)
            sum += digits[index];

        return sum % 10 == digits[10];
    }

    /// <summary>
    /// Girilen değer kabul edilebilir mi: boş bırakılabilir, ama
    /// girildiyse geçerli olmalı.
    /// </summary>
    public static bool IsBlankOrValid(string? value) =>
        string.IsNullOrWhiteSpace(value) || IsValid(value);

    /// <summary>Reddedilen değer için kullanıcıya gösterilecek gerekçe.</summary>
    public static string? Describe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim();

        if (text.Length != Length || text.Any(x => x is < '0' or > '9'))
            return "T.C. kimlik numarası 11 haneli ve yalnızca rakamlardan oluşmalıdır.";

        if (text[0] == '0')
            return "T.C. kimlik numarası sıfırla başlayamaz.";

        return IsValid(text)
            ? null
            : "T.C. kimlik numarası doğrulama algoritmasına uymuyor; " +
              "hane sırasını kontrol edin.";
    }
}
