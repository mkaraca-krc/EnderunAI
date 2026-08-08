using EnderunAI.Api.Models;

namespace EnderunAI.Api.Services.Offers;

/// <summary>
/// Teklif fırsat hunisinin durum geçiş kuralları.
///
/// Saf ve veritabanısız: huninin doğruluğu tek bir tabloya bakarak
/// okunabilsin ve testi bir veritabanı gerektirmeden yazılabilsin diye.
///
/// KAZANILDI, KAYBEDİLDİ ve İPTAL nihaidir. Kazanılan teklif sözleşme
/// ve proje doğurur; geri alınabilseydi projesi ortada kalırdı.
/// Kaybedilen teklif de arşivin kendisidir — "geçen sefer bu işe şu
/// fiyatı vermiştik" sorusunun cevabı, sonradan oynanmamış bir kayıt
/// olmasına bağlı.
/// </summary>
public static class OfferStatusTransitions
{
    /// <summary>
    /// Hangi durumdan hangilerine geçilebilir.
    ///
    /// <see cref="OfferStatus.Rejected"/> yeni akışta kullanılmıyor:
    /// haritada ne kaynak ne hedef olarak yer alır.
    /// </summary>
    public static readonly IReadOnlyDictionary<OfferStatus, IReadOnlyList<OfferStatus>>
        Allowed = new Dictionary<OfferStatus, IReadOnlyList<OfferStatus>>
        {
            [OfferStatus.Draft] =
            [
                OfferStatus.Submitted,
                OfferStatus.Cancelled
            ],

            [OfferStatus.Submitted] =
            [
                OfferStatus.Pending,
                OfferStatus.Won,
                OfferStatus.Lost,
                OfferStatus.Cancelled
            ],

            [OfferStatus.Pending] =
            [
                OfferStatus.Won,
                OfferStatus.Lost,
                OfferStatus.Cancelled
            ],

            [OfferStatus.Won] = [],
            [OfferStatus.Lost] = [],
            [OfferStatus.Cancelled] = [],
            [OfferStatus.Rejected] = []
        };

    public static bool CanTransition(OfferStatus from, OfferStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>Durumun artık değiştirilemeyeceği son duraklar.</summary>
    public static bool IsFinal(OfferStatus status) =>
        status is OfferStatus.Won or OfferStatus.Lost or OfferStatus.Cancelled;

    /// <summary>
    /// Geçişin neden reddedildiğini Türkçe anlatır; null ise geçiş
    /// serbesttir.
    ///
    /// <paramref name="hasCounterparty"/>: teklifin karşı tarafı
    /// (işveren / ana yüklenici) seçilmiş mi. Kime verildiği bilinmeyen
    /// bir teklif takip listesinde ve kazanma oranında sayılamaz, bu
    /// yüzden "Verildi" adımında zorunlu olur.
    /// </summary>
    public static string? Validate(
        OfferStatus from,
        OfferStatus to,
        bool hasCounterparty,
        OfferLostReason lostReason,
        int itemCount)
    {
        if (from == to)
            return "Teklif zaten bu durumda.";

        if (!Enum.IsDefined(typeof(OfferStatus), to))
            return "Geçersiz teklif durumu.";

        if (to == OfferStatus.Rejected)
        {
            return "Reddedildi durumu kullanımdan kalktı; " +
                   "Kaybedildi seçip nedenini girin.";
        }

        if (IsFinal(from))
        {
            return $"{Label(from)} durumundaki teklif değiştirilemez; " +
                   "gerekiyorsa yeni bir teklif açın.";
        }

        if (!CanTransition(from, to))
            return $"{Label(from)} durumundan {Label(to)} durumuna geçilemez.";

        if (to == OfferStatus.Submitted && itemCount == 0)
            return "Kalemi olmayan teklif verilemez.";

        if (to is OfferStatus.Submitted or OfferStatus.Won && !hasCounterparty)
        {
            return "Teklifin kime verildiği (işveren / ana yüklenici) " +
                   "seçilmeden bu adıma geçilemez.";
        }

        if (to == OfferStatus.Lost && lostReason == OfferLostReason.None)
        {
            return "Kayıp nedeni zorunludur; nedeni yazılmayan kayıp " +
                   "ileride sayılamaz.";
        }

        if (to != OfferStatus.Lost && lostReason != OfferLostReason.None)
            return "Kayıp nedeni yalnız Kaybedildi durumunda girilebilir.";

        return null;
    }

    public static string Label(OfferStatus status) => status switch
    {
        OfferStatus.Draft => "Hazırlanıyor",
        OfferStatus.Submitted => "Verildi",
        OfferStatus.Pending => "Beklemede",
        OfferStatus.Rejected => "Reddedildi",
        OfferStatus.Won => "Kazanıldı",
        OfferStatus.Lost => "Kaybedildi",
        _ => "İptal"
    };

    public static string LostReasonLabel(OfferLostReason reason) => reason switch
    {
        OfferLostReason.PriceTooHigh => "Fiyat yüksek",
        OfferLostReason.InsufficientReference => "Referans yetersiz",
        OfferLostReason.CompetitorWon => "Başka firmaya verildi",
        OfferLostReason.WorkCancelled => "İş iptal edildi",
        OfferLostReason.Other => "Diğer",
        _ => "—"
    };

    public static string RoleLabel(OfferCounterpartyRole role) => role switch
    {
        OfferCounterpartyRole.Employer => "İşveren",
        OfferCounterpartyRole.MainContractor => "Ana yüklenici",
        _ => "Belirtilmedi"
    };

    public static string KindLabel(OfferKind kind) => kind switch
    {
        OfferKind.UnitPrice => "Birim fiyatlı",
        OfferKind.LumpSum => "Anahtar teslim götürü",
        _ => "Belirtilmedi"
    };
}

/// <summary>
/// Kazanma oranı özeti.
///
/// İki oran birden verilir çünkü ikisi farklı soruyu cevaplar:
/// ADET oranı "kaç işe girdik kaçını aldık", TUTAR oranı "teklif
/// ettiğimiz paranın ne kadarı işe döndü". Küçük işleri kazanıp büyük
/// işleri kaybeden bir dönemde adet oranı iyi görünür, tutar oranı
/// gerçeği söyler.
/// </summary>
/// <param name="TotalCount">Değerlendirmeye giren teklif sayısı.</param>
/// <param name="WonCount">Kazanılan.</param>
/// <param name="LostCount">Kaybedilen.</param>
/// <param name="OpenCount">Hâlâ açık (hazırlanıyor/verildi/beklemede).</param>
/// <param name="CancelledCount">İptal.</param>
/// <param name="WonAmount">Kazanılan tutar.</param>
/// <param name="LostAmount">Kaybedilen tutar.</param>
/// <param name="OpenAmount">Açık tekliflerin tutarı (huninin değeri).</param>
/// <param name="CountWinRate">Adet bazlı kazanma oranı (%).</param>
/// <param name="AmountWinRate">Tutar bazlı kazanma oranı (%).</param>
public sealed record OfferWinRateSummary(
    int TotalCount,
    int WonCount,
    int LostCount,
    int OpenCount,
    int CancelledCount,
    decimal WonAmount,
    decimal LostAmount,
    decimal OpenAmount,
    decimal CountWinRate,
    decimal AmountWinRate);

/// <summary>
/// Kazanma oranı hesabı — saf, veritabanısız.
/// </summary>
public static class OfferWinRateCalculator
{
    /// <summary>
    /// Oranın paydası KAZANILAN + KAYBEDİLEN'dir.
    ///
    /// Sonucu belli olmamış teklifi paydaya koymak oranı yapay olarak
    /// düşürür (henüz kaybetmedik), iptalleri koymak ise bizim
    /// performansımız olmayan bir şeyi bize yazar. İkisi de sayılır ve
    /// ayrıca raporlanır ama orana girmez.
    /// </summary>
    public static OfferWinRateSummary Calculate(
        IEnumerable<(OfferStatus Status, decimal Amount)> offers)
    {
        int won = 0, lost = 0, open = 0, cancelled = 0;
        decimal wonAmount = 0m, lostAmount = 0m, openAmount = 0m;
        var total = 0;

        foreach (var (status, amount) in offers)
        {
            total++;

            switch (status)
            {
                case OfferStatus.Won:
                    won++;
                    wonAmount += amount;
                    break;

                case OfferStatus.Lost:
                    lost++;
                    lostAmount += amount;
                    break;

                case OfferStatus.Cancelled:
                case OfferStatus.Rejected:
                    cancelled++;
                    break;

                default:
                    open++;
                    openAmount += amount;
                    break;
            }
        }

        var decided = won + lost;
        var decidedAmount = wonAmount + lostAmount;

        var countRate = decided == 0
            ? 0m
            : decimal.Round(won * 100m / decided, 2);

        var amountRate = decidedAmount == 0m
            ? 0m
            : decimal.Round(wonAmount * 100m / decidedAmount, 2);

        return new OfferWinRateSummary(
            total, won, lost, open, cancelled,
            decimal.Round(wonAmount, 2),
            decimal.Round(lostAmount, 2),
            decimal.Round(openAmount, 2),
            countRate,
            amountRate);
    }
}
