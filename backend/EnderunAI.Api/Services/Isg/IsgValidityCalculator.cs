namespace EnderunAI.Api.Services.Isg;

/// <summary>Bir İSG kaydının geçerlilik durumu.</summary>
public enum IsgValidityStatus
{
    /// <summary>Geçerli ve bitişine uyarı eşiğinden fazla var.</summary>
    Valid = 0,

    /// <summary>Uyarı eşiği içinde — yenileme başlatılmalı.</summary>
    ExpiringSoon = 1,

    /// <summary>Süresi dolmuş.</summary>
    Expired = 2,

    /// <summary>Bitiş tarihi yok; süresiz geçerli.</summary>
    NoExpiry = 3
}

/// <summary>
/// Sağlık raporu, eğitim ve sertifikanın ortak geçerlilik hesabı.
///
/// Static ve veritabanısız: üç kayıt türü de aynı kuraldan geçsin
/// istiyoruz. Kural üç yere kopyalansaydı biri güncellenip diğerleri
/// unutulurdu ve "eğitimde 30 gün, sertifikada 45 gün" gibi sessiz bir
/// tutarsızlık doğardı.
/// </summary>
public static class IsgValidityCalculator
{
    /// <summary>
    /// Kaç gün kala uyarılacağı. Brifing ve panel aynı eşiği kullanır.
    /// </summary>
    public const int WarningDays = 30;

    /// <summary>
    /// Geçerlilik durumu. <paramref name="validUntil"/> null ise
    /// süresiz sayılır — tahmin edilen bir bitiş tarihi üretilmez.
    /// </summary>
    public static IsgValidityStatus Evaluate(DateOnly? validUntil, DateOnly today)
    {
        if (validUntil is not DateOnly expiry)
            return IsgValidityStatus.NoExpiry;

        // Bitiş günü dahil geçerlidir: 30 Haziran'da biten rapor
        // 30 Haziran'da hâlâ geçerlidir.
        if (expiry < today)
            return IsgValidityStatus.Expired;

        return expiry.DayNumber - today.DayNumber <= WarningDays
            ? IsgValidityStatus.ExpiringSoon
            : IsgValidityStatus.Valid;
    }

    /// <summary>
    /// Bitişe kalan gün. Süresi dolmuşsa negatif, bitiş yoksa null.
    /// </summary>
    public static int? DaysRemaining(DateOnly? validUntil, DateOnly today) =>
        validUntil is DateOnly expiry ? expiry.DayNumber - today.DayNumber : null;

    public static string StatusName(IsgValidityStatus status) => status switch
    {
        IsgValidityStatus.Valid => "Geçerli",
        IsgValidityStatus.ExpiringSoon => "Süresi doluyor",
        IsgValidityStatus.Expired => "Süresi doldu",
        IsgValidityStatus.NoExpiry => "Süresiz",
        _ => "—"
    };

    /// <summary>
    /// Arayüzdeki rozet rengi (erp-status sınıfı). Süresi dolan kırmızı,
    /// dolmak üzere olan sarı.
    /// </summary>
    public static string StatusColor(IsgValidityStatus status) => status switch
    {
        IsgValidityStatus.Valid => "green",
        IsgValidityStatus.ExpiringSoon => "yellow",
        IsgValidityStatus.Expired => "red",
        IsgValidityStatus.NoExpiry => "gray",
        _ => "gray"
    };
}
