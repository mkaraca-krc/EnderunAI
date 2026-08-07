namespace EnderunAI.Api.Services.Accounting;

/// <summary>
/// Bir kur farkının tutarı ve yönü.
/// </summary>
/// <param name="Amount">Farkın büyüklüğü (TL, her zaman pozitif).</param>
/// <param name="IsGain">Kambiyo kârı mı (646) yoksa zararı mı (656).</param>
/// <param name="CarryingRate">Bakiyenin defterdeki taşıma kuru.</param>
/// <param name="SettlementRate">Kapanış/değerleme kuru.</param>
public sealed record ExchangeDifference(
    decimal Amount,
    bool IsGain,
    decimal CarryingRate,
    decimal SettlementRate);

/// <summary>
/// Kur farkı hesabı — saf, veritabanısız.
///
/// İki farklı fark vardır ve karıştırılmamalıdır:
/// - GERÇEKLEŞMİŞ fark: para hareket ettiğinde doğar (dövizli borcu
///   öderken kur değişmiştir). Kesin ve nihaidir.
/// - DEĞERLEME farkı: para hareket etmeden, dönem sonunda bakiyenin
///   o günkü kurla karşılığına çekilmesinden doğar. Sonraki dönemde
///   kur geri dönerse tersine döner.
///
/// İşaret kuralı ikisinde de aynıdır ve tek cümleyle özetlenir:
/// TL karşılığı ARTAN bir varlık (alacak) kârdır, TL karşılığı ARTAN
/// bir borç zarardır. Bakiyenin işareti bu ayrımı zaten taşıdığı için
/// tek formül her iki durumu da doğru çözer.
/// </summary>
public static class ExchangeDifferenceCalculator
{
    /// <summary>
    /// Bir döviz bakiyesinin taşıma kuru: defter değeri ÷ döviz
    /// bakiyesi. Bakiye sıfırsa taşıma kuru tanımsızdır (null) —
    /// uydurulmaz.
    /// </summary>
    /// <param name="balance">Döviz bakiyesi (borç − alacak).</param>
    /// <param name="bookValueLocal">Defterdeki TL karşılığı.</param>
    public static decimal? CarryingRate(decimal balance, decimal bookValueLocal)
    {
        if (balance == 0m)
            return null;

        var rate = bookValueLocal / balance;

        // Negatif taşıma kuru, defter değeriyle döviz bakiyesinin ters
        // işaretli olması demektir; bu bir veri tutarsızlığıdır ve
        // üstüne kur farkı hesaplamak yanlış rakam üretir.
        return rate > 0m ? decimal.Round(rate, 6) : null;
    }

    /// <summary>
    /// Döviz bakiyesinin değerleme/kapanış farkı.
    ///
    /// <paramref name="balance"/> İŞARETLİDİR: pozitif alacak (bize
    /// borçlu), negatif borç (biz borçluyuz). İşaret sayesinde alacak
    /// ve borç için ayrı formül gerekmez.
    /// </summary>
    /// <param name="balance">Döviz bakiyesi (işaretli).</param>
    /// <param name="bookValueLocal">Defterdeki TL karşılığı (işaretli).</param>
    /// <param name="rate">Değerleme/kapanış kuru.</param>
    /// <returns>Fark; sıfırsa veya hesaplanamıyorsa null.</returns>
    public static ExchangeDifference? Calculate(
        decimal balance, decimal bookValueLocal, decimal rate)
    {
        if (rate <= 0m || balance == 0m)
            return null;

        var valued = decimal.Round(balance * rate, 2);
        var difference = decimal.Round(valued - decimal.Round(bookValueLocal, 2), 2);

        if (difference == 0m)
            return null;

        var carrying = CarryingRate(balance, bookValueLocal);

        return new ExchangeDifference(
            Amount: Math.Abs(difference),
            // Fark pozitifse net varlık değeri arttı: alacakta kâr,
            // borçta (bakiye negatif olduğu için) fark zaten negatif
            // çıkar ve zarar olarak işaretlenir.
            IsGain: difference > 0m,
            CarryingRate: carrying ?? 0m,
            SettlementRate: rate);
    }

    /// <summary>
    /// Kapanan (ödenen/tahsil edilen) bir tutarın GERÇEKLEŞMİŞ farkı.
    ///
    /// Kapanan kısım her zaman pozitif bir büyüklük olarak verilir;
    /// yönü <paramref name="isReceivable"/> belirler.
    /// </summary>
    /// <param name="settledAmount">Kapanan döviz tutarı (pozitif).</param>
    /// <param name="carryingRate">Bakiyenin defterdeki taşıma kuru.</param>
    /// <param name="settlementRate">Ödeme/tahsilat günü kuru.</param>
    /// <param name="isReceivable">Alacak mı kapanıyor (tahsilat) yoksa
    /// borç mu (ödeme).</param>
    public static ExchangeDifference? CalculateRealized(
        decimal settledAmount,
        decimal carryingRate,
        decimal settlementRate,
        bool isReceivable)
    {
        if (settledAmount <= 0m || carryingRate <= 0m || settlementRate <= 0m)
            return null;

        // Alacağı pozitif, borcu negatif bakiye olarak modelleyip aynı
        // formülü kullanıyoruz; böylece işaret kuralı tek yerde durur.
        var signedBalance = isReceivable ? settledAmount : -settledAmount;
        var bookValue = decimal.Round(signedBalance * carryingRate, 2);

        return Calculate(signedBalance, bookValue, settlementRate);
    }
}
