namespace EnderunAI.Api.Security;

/// <summary>
/// PAROLA POLİTİKASI — TEK YER.
///
/// ── NEDEN TEK YERDE ──
///
/// Kural iki yerde yaşasaydı (kendi parolasını değiştirme ve yönetici
/// sıfırlama) biri güncellenir diğeri kalırdı. Bu kod tabanının en sık
/// hatası bu ve bugün üç kez ayrı ayrı görüldü: merkez kuralının PUT
/// kopyası, `dotnet ef` çağrısının üç ayrı ortamı, sır bekçisinin
/// taranmayan yüzeyi.
///
/// ── ASGARİ 12 KARAKTER ──
///
/// Eskiden 10'du ve canlıdaki paylaşılan parola TAM 10 karakterdi —
/// yani asgariyi birebir karşılıyordu. Bir asgari, ihlal edilmediği
/// sürece bir şey söylemez; buradaki asgari zaten karşılanıyordu ve
/// yine de zayıftı.
///
/// 12'ye çıkarıldı (Mehmet kararı, 2026-09-03). Karmaşıklık kuralı
/// EKLENMEDİ: karmaşıklık zorunluluğu insanları tahmin edilebilir
/// kalıplara (Parola1! gibi) itiyor; uzunluk daha ucuz ve daha
/// etkili.
///
/// ── MEVCUT PAROLALARI GEÇERSİZ KILMAZ ──
///
/// Politika yalnız YENİ parolalara uygulanıyor. 10 karakterlik mevcut
/// parolalar çalışmaya devam eder; onları değiştirmek ayrı bir iş
/// (sır döndürme paketi).
/// </summary>
public static class ParolaPolitikasi
{
    public const int AsgariUzunluk = 12;

    /// <summary>
    /// Parolayı doğrular. Hata varsa Türkçe mesaj, yoksa <c>null</c>.
    /// </summary>
    public static string? Dogrula(string? parola)
    {
        if (string.IsNullOrWhiteSpace(parola))
            return "Parola boş olamaz.";

        if (parola.Length < AsgariUzunluk)
            return $"Parola en az {AsgariUzunluk} karakter olmalıdır.";

        return null;
    }
}
