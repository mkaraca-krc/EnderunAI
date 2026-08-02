namespace EnderunAI.Api.Services.Procurement;

internal sealed record SupplierHistoryMetrics(
    int InvitationCount,
    int ResponseCount,
    int DeliveryMeasuredOrderCount,
    int OnTimeDeliveryOrderCount,
    int ReceiptLineCount,
    int ExceptionLineCount)
{
    public decimal ResponseRate => ProcurementDecisionScoring.Rate(
        ResponseCount,
        InvitationCount,
        50m);

    public decimal OnTimeDeliveryRate => ProcurementDecisionScoring.Rate(
        OnTimeDeliveryOrderCount,
        DeliveryMeasuredOrderCount,
        50m);

    public decimal QualityRate => ReceiptLineCount == 0
        ? 50m
        : ProcurementDecisionScoring.Rate(
            ReceiptLineCount - ExceptionLineCount,
            ReceiptLineCount,
            50m);

    public decimal HistoryScore => ProcurementDecisionScoring.RoundScore(
        ResponseRate * 0.20m +
        OnTimeDeliveryRate * 0.40m +
        QualityRate * 0.40m);

    public string Confidence => ProcurementDecisionScoring.Confidence(
        InvitationCount +
        DeliveryMeasuredOrderCount +
        ReceiptLineCount);
}

internal static class ProcurementDecisionScoring
{
    public const string ComparisonCurrency = "TRY";

    public static decimal Normalize(decimal amount, decimal exchangeRate) =>
        decimal.Round(
            amount * (exchangeRate > 0m ? exchangeRate : 1m),
            2,
            MidpointRounding.AwayFromZero);

    public static decimal PriceScore(
        decimal normalizedAmount,
        decimal lowestNormalizedAmount)
    {
        if (normalizedAmount <= 0m || lowestNormalizedAmount <= 0m)
            return 0m;

        return RoundScore(
            Math.Min(100m, lowestNormalizedAmount / normalizedAmount * 100m));
    }

    public static decimal DeliveryTermScore(
        int? deliveryDays,
        int? shortestDeliveryDays)
    {
        if (!deliveryDays.HasValue)
            return 50m;

        if (!shortestDeliveryDays.HasValue)
            return 70m;

        var difference = Math.Max(
            0,
            deliveryDays.Value - shortestDeliveryDays.Value);
        return RoundScore(Math.Max(40m, 100m - difference * 2m));
    }

    public static decimal DecisionScore(
        decimal priceScore,
        decimal deliveryTermScore,
        decimal historyScore) =>
        RoundScore(
            priceScore * 0.45m +
            deliveryTermScore * 0.15m +
            historyScore * 0.40m);

    public static decimal SupplierPerformanceScore(
        decimal priceScore,
        decimal historyScore) =>
        RoundScore(priceScore * 0.35m + historyScore * 0.65m);

    public static decimal Rate(
        int numerator,
        int denominator,
        decimal fallback)
    {
        if (denominator <= 0)
            return fallback;

        return RoundScore(
            Math.Min(100m, Math.Max(0m, numerator * 100m / denominator)));
    }

    public static decimal RoundScore(decimal value) =>
        decimal.Round(
            Math.Min(100m, Math.Max(0m, value)),
            2,
            MidpointRounding.AwayFromZero);

    public static string Confidence(int observationCount) =>
        observationCount >= 20
            ? "Yüksek"
            : observationCount >= 5
                ? "Orta"
                : "Düşük";
}
