using System.Security.Cryptography;
using System.Text;

namespace EnderunAI.Api.Services.Portal;

/// <summary>
/// PORTAL TOKENININ ÖZETİ — TEK NOKTA.
///
/// Token tabloda tutulmuyor; yalnız SHA-256 özeti saklanıyor ve arama
/// onunla yapılıyor. Bir sırrı saklamanın en güvenli yolu onu hiç
/// saklamamaktır.
///
/// NEDEN TUZ (SALT) YOK — PAROLADAN FARKI BURADA:
/// Parola özetlerinde tuz ve yavaş algoritma (bcrypt/argon2) şart,
/// çünkü parolalar insan seçimidir: kısa, tekrar eden, sözlükten
/// tahmin edilebilir. Portal tokenı ise 256 bit kriptografik
/// rastgelelik — sözlüğü yok, gökkuşağı tablosu kurulamaz, kaba
/// kuvvet hesaplanamaz. Burada yavaş algoritma yalnızca her portal
/// isteğini yavaşlatırdı, güvenlik eklemezdi.
///
/// Aramanın hızlı olması da gerekiyor: özet üzerinde benzersiz indeks
/// var ve her istek bir kez bakıyor.
/// </summary>
public static class PortalTokenHasher
{
    /// <summary>
    /// Tokenın özeti — küçük harf hex, 64 karakter.
    /// Biçim sabit tutuluyor: bir gün değişirse mevcut bütün
    /// bağlantılar eşleşmeyi bırakır.
    /// </summary>
    public static string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Tanıtıcı önek — ilk 8 karakter. SIR DEĞİLDİR.
    /// </summary>
    public static string Prefix(string token) =>
        string.IsNullOrEmpty(token)
            ? string.Empty
            : token[..Math.Min(8, token.Length)];
}
