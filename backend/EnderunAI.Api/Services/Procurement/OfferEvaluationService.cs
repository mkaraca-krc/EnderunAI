using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Procurement;

public sealed record OfferScoreBreakdown(
    Guid OfferId,
    string OfferNumber,
    decimal TotalCost,
    decimal PriceScore,
    decimal PaymentTermScore,
    decimal StockScore,
    decimal DeliveryScore,
    decimal FreightScore,
    decimal CheckTermScore,
    decimal SupplierScore,
    decimal TotalScore,
    IReadOnlyList<string> Warnings);

public interface IOfferEvaluationService
{
    Task<IReadOnlyList<OfferScoreBreakdown>> EvaluateAsync(Guid rfqId, CancellationToken cancellationToken = default);
}

public sealed class OfferEvaluationService(ProcurementDbContext db) : IOfferEvaluationService
{
    public async Task<IReadOnlyList<OfferScoreBreakdown>> EvaluateAsync(Guid rfqId, CancellationToken cancellationToken = default)
    {
        var offers = await db.SupplierOffers
            .AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.CheckTerms)
            .Where(x => x.RfqId == rfqId && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        if (offers.Count == 0)
            return Array.Empty<OfferScoreBreakdown>();

        var totals = offers.ToDictionary(
            x => x.Id,
            x => x.Items.Sum(i => i.OfferedQuantity * i.UnitPrice) * (1 - x.DiscountRate / 100m) + x.FreightAmount);

        var minimumTotal = totals.Values.Min();
        var maximumPaymentTerm = Math.Max(1, offers.Max(x => x.PaymentTermDays));
        var maximumDelivery = Math.Max(1, offers.Max(x => Math.Max(x.DeliveryTermDays, x.Items.DefaultIfEmpty().Max(i => i?.ItemDeliveryDays ?? 0))));

        return offers
            .Select(offer =>
            {
                var warnings = new List<string>();
                var total = totals[offer.Id];
                var requested = offer.Items.Sum(x => x.OfferedQuantity);
                var available = offer.Items.Sum(x => Math.Min(x.OfferedQuantity, x.AvailableStockQuantity));
                var stockRatio = requested <= 0 ? 0 : available / requested;

                var priceScore = minimumTotal <= 0 || total <= 0 ? 0 : Math.Min(100m, minimumTotal / total * 100m);
                var paymentScore = Math.Min(100m, (decimal)offer.PaymentTermDays / maximumPaymentTerm * 100m);
                var stockScore = Math.Min(100m, stockRatio * 100m);
                var deliveryDays = Math.Max(offer.DeliveryTermDays, offer.Items.DefaultIfEmpty().Max(i => i?.ItemDeliveryDays ?? 0));
                var deliveryScore = Math.Max(0m, 100m - (decimal)deliveryDays / maximumDelivery * 100m);
                var freightScore = offer.FreightResponsibility == FreightResponsibility.Supplier
                    ? 100m
                    : offer.FreightResponsibility == FreightResponsibility.Shared ? 60m : 25m;

                var checkScore = CalculateCheckTermScore(offer, total, warnings);
                var supplierScore = Math.Clamp(offer.SupplierPerformanceScore, 0m, 100m);

                if (stockRatio < 1m)
                    warnings.Add($"Teklif edilen miktarın yalnızca %{stockRatio * 100m:0.#} kadarı mevcut stokta.");
                if (deliveryDays > 30)
                    warnings.Add($"Teslim süresi {deliveryDays} gün.");
                if (!string.Equals(offer.CurrencyCode, "TRY", StringComparison.OrdinalIgnoreCase))
                    warnings.Add($"{offer.CurrencyCode} kur riski bulunuyor.");
                if (!offer.AllowsPartialShipment && stockRatio < 1m)
                    warnings.Add("Kısmi sevkiyat kabul edilmiyor ve stok yetersiz.");

                var totalScore =
                    priceScore * 0.30m +
                    paymentScore * 0.15m +
                    stockScore * 0.15m +
                    deliveryScore * 0.10m +
                    freightScore * 0.10m +
                    checkScore * 0.10m +
                    supplierScore * 0.10m;

                return new OfferScoreBreakdown(
                    offer.Id,
                    offer.OfferNumber,
                    total,
                    decimal.Round(priceScore, 2),
                    decimal.Round(paymentScore, 2),
                    decimal.Round(stockScore, 2),
                    decimal.Round(deliveryScore, 2),
                    decimal.Round(freightScore, 2),
                    decimal.Round(checkScore, 2),
                    decimal.Round(supplierScore, 2),
                    decimal.Round(totalScore, 2),
                    warnings);
            })
            .OrderByDescending(x => x.TotalScore)
            .ThenBy(x => x.TotalCost)
            .ToList();
    }

    private static decimal CalculateCheckTermScore(SupplierOffer offer, decimal total, ICollection<string> warnings)
    {
        if (offer.CheckTerms.Count == 0)
            return offer.PaymentTermDays > 0 ? Math.Min(100m, offer.PaymentTermDays / 1.2m) : 30m;

        var checkTotal = offer.CheckTerms.Sum(x => x.Amount);
        if (total > 0 && Math.Abs(checkTotal - total) > 1m)
            warnings.Add("Çek toplamı ile teklif toplamı uyuşmuyor.");

        var weightedDays = checkTotal <= 0
            ? 0m
            : offer.CheckTerms.Sum(x => x.Amount * Math.Max(0, (x.DueDateUtc.Date - offer.OfferDateUtc.Date).Days)) / checkTotal;

        if (weightedDays < 30)
            warnings.Add("Ağırlıklı çek vadesi 30 günden kısa.");

        return Math.Clamp(weightedDays / 1.2m, 0m, 100m);
    }
}
