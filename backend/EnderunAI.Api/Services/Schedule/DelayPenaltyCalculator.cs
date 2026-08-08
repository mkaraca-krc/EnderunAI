namespace EnderunAI.Api.Services.Schedule;

/// <summary>
/// Sözleşmedeki gecikme cezasının biçimi.
/// </summary>
public enum DelayPenaltyKind
{
    /// <summary>Sözleşmede gecikme cezası yok — hiç hesaplanmaz.</summary>
    None = 0,

    /// <summary>
    /// Sözleşme bedelinin gecikilen her gün için belli bir oranı.
    /// Türkiye'de en yaygın biçim ("her gecikme günü için binde 1").
    /// </summary>
    RateOfContractPerDay = 1,

    /// <summary>Gecikilen her gün için sabit tutar.</summary>
    FixedAmountPerDay = 2
}

/// <summary>
/// Ceza hesabının girdisi.
/// </summary>
/// <param name="Value">
/// <see cref="DelayPenaltyKind.RateOfContractPerDay"/> için YÜZDE olarak
/// günlük oran — binde 1 için 0,1 yazılır. Yüzde seçildi çünkü bu kod
/// tabanındaki bütün oranlar (teminat, stopaj, KDV) yüzde tutuluyor;
/// tek bir alanın binde olması sessiz on kat hatası üretirdi.
/// <see cref="DelayPenaltyKind.FixedAmountPerDay"/> için günlük tutar.
/// </param>
/// <param name="CapAmount">Ceza tavanı (tutar). Sözleşmede "bedelin
/// %10'unu geçemez" gibi bir hüküm varsa buraya hesaplanmış tutar
/// konur. Boşsa tavan yok.</param>
/// <param name="DelayDays">Gecikilen gün. Cezalar TAKVİM günü üzerinden
/// yürür: sözleşme "gecikilen her gün için" der, pazarı istisna
/// tutmaz.</param>
public sealed record DelayPenaltyInput(
    DelayPenaltyKind Kind,
    decimal Value,
    decimal? CapAmount,
    decimal ContractAmount,
    int DelayDays);

/// <param name="Amount">Tavan uygulandıktan sonraki tahmini ceza.</param>
/// <param name="RawAmount">Tavan uygulanmadan önceki tutar.</param>
/// <param name="Note">Hesaplanamadıysa nedeni; hesaplandıysa null.</param>
public sealed record DelayPenaltyResult(
    bool Applicable,
    decimal DailyAmount,
    decimal RawAmount,
    decimal Amount,
    bool CapApplied,
    string? Note);

/// <summary>
/// Gecikme cezası. Saf ve veritabanısız.
///
/// Sonuç her zaman TAHMİNİdir: gerçek ceza işverenin ihtar ve kesinti
/// pratiğine bağlıdır, mücbir sebep ve süre uzatımı hesaba girmez.
/// Ekranın bunu "tahmini" diye yazması bilinçli — kesinmiş gibi
/// gösterilen bir rakam nakit planını yanlış kurar.
/// </summary>
public static class DelayPenaltyCalculator
{
    public static DelayPenaltyResult Calculate(DelayPenaltyInput input)
    {
        if (input.Kind == DelayPenaltyKind.None)
        {
            return Empty("Sözleşmede gecikme cezası tanımlı değil.");
        }

        if (input.Value <= 0m)
        {
            return Empty("Gecikme cezası oranı/tutarı girilmemiş.");
        }

        if (input.Kind == DelayPenaltyKind.RateOfContractPerDay &&
            input.ContractAmount <= 0m)
        {
            return Empty(
                "Sözleşme bedeli girilmeden oransal gecikme cezası " +
                "hesaplanamaz.");
        }

        var daily = input.Kind == DelayPenaltyKind.RateOfContractPerDay
            ? decimal.Round(input.ContractAmount * input.Value / 100m, 2)
            : decimal.Round(input.Value, 2);

        // Gecikme yoksa ceza yok; ama günlük tutar yine döner, çünkü
        // ekran "günü şu kadara mal oluyor" diye gösteriyor.
        if (input.DelayDays <= 0)
        {
            return new DelayPenaltyResult(
                Applicable: false,
                DailyAmount: daily,
                RawAmount: 0m,
                Amount: 0m,
                CapApplied: false,
                Note: "Gecikme yok.");
        }

        var raw = decimal.Round(daily * input.DelayDays, 2);
        var capped = input.CapAmount is decimal cap && cap >= 0m && raw > cap;

        return new DelayPenaltyResult(
            Applicable: true,
            DailyAmount: daily,
            RawAmount: raw,
            Amount: capped ? decimal.Round(input.CapAmount!.Value, 2) : raw,
            CapApplied: capped,
            Note: null);
    }

    /// <summary>Tavanı sözleşme bedelinin yüzdesinden tutara çevirir.</summary>
    public static decimal? CapFromRate(decimal contractAmount, decimal? capRate) =>
        capRate is decimal rate && rate > 0m && contractAmount > 0m
            ? decimal.Round(contractAmount * rate / 100m, 2)
            : null;

    private static DelayPenaltyResult Empty(string note) =>
        new(false, 0m, 0m, 0m, false, note);
}
