using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Projects;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Hakedis;

/// <summary>Hakediş satırının geliri ve ona düşen maliyet.</summary>
public sealed record HakedisLineProfit(
    Guid ItemId,
    string PositionCode,
    string Description,
    string Unit,
    decimal CurrentQuantity,
    decimal UnitPrice,
    decimal CurrentAmount,
    /// <summary>Gerçekleşen birim maliyet; hesaplanamadıysa boş.</summary>
    decimal? UnitCost,
    decimal? PeriodCost,
    decimal? Profit,
    decimal? ProfitMarginPercent,
    /// <summary>Maliyetin ne kadarı ölçüme dayanıyor (0-1).</summary>
    decimal MeasuredRatio,
    /// <summary>Rakamın nereden geldiği ya da neden boş olduğu.</summary>
    string CostBasis);

public sealed record HakedisProfit(
    Guid ProgressPaymentId,
    string ProgressPaymentNumber,
    int PeriodNumber,
    int Status,
    DateTime? PeriodStartDate,
    DateTime? PeriodEndDate,
    bool IncludesExtraPayments,

    // --- Dönem geliri, üç parçaya ayrılmış ---
    /// <summary>Bu dönem yapılan imalatın bedeli — kâr hesabının tabanı.</summary>
    decimal ProductionRevenue,
    decimal PriceDifferenceAmount,
    /// <summary>
    /// İhzarat hareketi: henüz imalata dönmemiş malzeme bedeli. Kazanılmış
    /// imalat olmadığı için kâr hesabına girmez.
    /// </summary>
    decimal AdvanceMaterialMovement,
    /// <summary>Hakedişin kendi tutarı — üç parçanın toplamı.</summary>
    decimal HakedisAmount,

    // --- Dönem maliyeti: iki ayrı taban ---
    /// <summary>Dönem tarihleri arasında deftere işlenen maliyet (ölçüm).</summary>
    decimal? CostByDate,
    decimal? ProfitByDate,
    decimal? MarginByDatePercent,
    string CostByDateBasis,

    /// <summary>Bu dönem imalatına düşen maliyet (birim maliyetten dağıtım).</summary>
    decimal CostByProduction,
    decimal ProfitByProduction,
    decimal? MarginByProductionPercent,
    /// <summary>Maliyeti hesaplanamayan satırların gelir toplamı.</summary>
    decimal RevenueWithoutCost,

    // --- Kümülatif (proje başından bu hakedişe kadar) ---
    decimal CumulativeRevenue,
    decimal CumulativeCost,
    decimal CumulativeProfit,
    decimal? CumulativeMarginPercent,

    IReadOnlyList<HakedisLineProfit> Lines,
    IReadOnlyList<string> Assumptions);

public interface IHakedisProfitService
{
    Task<HakedisProfit?> GetAsync(
        Guid progressPaymentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Hakediş kâr marjı.
///
/// Gelir üç parçaya ayrılır: imalat, fiyat farkı ve ihzarat hareketi.
/// İhzarat kâra girmez — henüz yapılmamış bir imalatın malzeme bedeli
/// tahsil edilmiştir; onu kâr saymak, malzemeyi erken alan bir projeyi
/// kârlı gibi gösterir ve imalat yapıldığı dönemde zarar çıkarırdı.
///
/// Maliyet iki ayrı tabanda gösterilir ve TOPLANMAZ:
/// 1) Tarih bazlı — dönem içinde deftere işlenen gerçek maliyet. Ölçümdür
///    ama zamanlamaya duyarlıdır (peşin alınan malzeme, geç gelen fatura).
/// 2) İmalata düşen — satırın gerçekleşen birim maliyeti × bu dönem
///    miktarı. Gelirle aynı işi karşılaştırır ama bir DAĞITIMDIR.
/// İkisini tek rakama indirmek, hangi soruya cevap verildiğini gizlerdi.
/// </summary>
public sealed class HakedisProfitService(
    AppDbContext db,
    IBoqItemCostService costs,
    IExtraPaymentVisibilityService extraPaymentVisibility) : IHakedisProfitService
{
    public async Task<HakedisProfit?> GetAsync(
        Guid progressPaymentId,
        CancellationToken cancellationToken = default)
    {
        var header = await db.ProgressPayments
            .AsNoTracking()
            .Where(x => x.Id == progressPaymentId)
            .Select(x => new
            {
                x.Id,
                x.ProjectId,
                x.ProgressPaymentNumber,
                x.PeriodNumber,
                x.Status,
                x.PeriodStartDate,
                x.PeriodEndDate,
                x.CurrentAmount,
                x.CumulativeWorkAmount,
                x.PriceDifferenceAmount
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (header is null)
            return null;

        var items = await db.ProgressPaymentItems
            .AsNoTracking()
            .Where(x => x.ProgressPaymentId == progressPaymentId)
            .OrderBy(x => x.LineNumber)
            .Select(x => new
            {
                x.Id,
                x.ProjectBoqItemId,
                x.PositionCode,
                x.Description,
                x.Unit,
                x.CurrentQuantity,
                x.CumulativeQuantity,
                x.UnitPrice,
                x.CurrentAmount
            })
            .ToListAsync(cancellationToken);

        var snapshot = await costs.GetAsync(header.ProjectId, cancellationToken);
        var assumptions = new List<string>(snapshot.Assumptions);

        // ---- Gelirin üç parçası ----
        var productionRevenue = decimal.Round(items.Sum(x => x.CurrentAmount), 2);

        // Hakediş tutarı imalat + ihzarat hareketini birlikte taşır;
        // aradaki fark ihzarattır.
        var advanceMovement = decimal.Round(header.CurrentAmount - productionRevenue, 2);

        if (advanceMovement != 0)
        {
            assumptions.Add(
                $"Hakediş tutarının {advanceMovement:N2} TL'lik kısmı ihzarat " +
                "hareketidir; henüz yapılmamış imalatın malzeme bedeli olduğu için " +
                "kâr hesabına girmez.");
        }

        if (header.PriceDifferenceAmount != 0)
        {
            assumptions.Add(
                "Fiyat farkı aynı imalatın bedeline eklendiği için gelire dahil " +
                "edilmiştir.");
        }

        var profitRevenue = productionRevenue + header.PriceDifferenceAmount;

        // ---- Satır bazında maliyet: birim maliyetten dağıtım ----
        var lines = new List<HakedisLineProfit>(items.Count);
        var costByProduction = 0m;
        var revenueWithoutCost = 0m;

        foreach (var item in items)
        {
            BoqItemCost? cost = item.ProjectBoqItemId is Guid boqItemId
                && snapshot.ByBoqItem.TryGetValue(boqItemId, out var found)
                    ? found
                    : null;

            if (cost is null)
            {
                revenueWithoutCost += item.CurrentAmount;

                lines.Add(Bare(item.Id, item.PositionCode, item.Description, item.Unit,
                    item.CurrentQuantity, item.UnitPrice, item.CurrentAmount,
                    "Satır sözleşme icmaline bağlı değil; maliyet hesaplanamıyor."));

                continue;
            }

            // Birim maliyetin paydası KÜMÜLATİF miktardır: maliyet defteri
            // de proje başından beri birikmiştir, ikisi aynı dönemi kapsar.
            if (item.CumulativeQuantity <= 0)
            {
                revenueWithoutCost += item.CurrentAmount;

                lines.Add(Bare(item.Id, item.PositionCode, item.Description, item.Unit,
                    item.CurrentQuantity, item.UnitPrice, item.CurrentAmount,
                    "Kümülatif metraj sıfır; birim maliyet hesaplanamıyor."));

                continue;
            }

            if (cost.Total == 0)
            {
                revenueWithoutCost += item.CurrentAmount;

                lines.Add(Bare(item.Id, item.PositionCode, item.Description, item.Unit,
                    item.CurrentQuantity, item.UnitPrice, item.CurrentAmount,
                    "Bu poza henüz maliyet işlenmemiş."));

                continue;
            }

            var unitCost = decimal.Round(cost.Total / item.CumulativeQuantity, 4);
            var periodCost = decimal.Round(unitCost * item.CurrentQuantity, 2);
            var profit = decimal.Round(item.CurrentAmount - periodCost, 2);

            costByProduction += periodCost;

            lines.Add(new HakedisLineProfit(
                item.Id,
                item.PositionCode,
                item.Description,
                item.Unit,
                item.CurrentQuantity,
                item.UnitPrice,
                item.CurrentAmount,
                unitCost,
                periodCost,
                profit,
                Percent(item.CurrentAmount, profit),
                cost.MeasuredRatio,
                cost.MeasuredRatio == 1m
                    ? "Poza etiketlenmiş gerçek maliyetten."
                    : "Maliyetin bir kısmı kısımdan dağıtıldı; bu bölümü tahmindir."));
        }

        if (revenueWithoutCost != 0)
        {
            assumptions.Add(
                $"{revenueWithoutCost:N2} TL gelirin maliyeti hesaplanamadı; imalata " +
                "düşen maliyet bu kadar eksik, kâr o oranda iyimser görünüyor.");
        }

        // ---- Tarih bazlı dönem maliyeti ----
        var (costByDate, costByDateBasis) = await GetPeriodCostAsync(
            header.ProjectId, header.PeriodStartDate, header.PeriodEndDate,
            snapshot.IncludesExtraPayments, cancellationToken);

        // ---- Kümülatif ----
        var cumulativeCost = decimal.Round(
            snapshot.ByBoqItem.Values.Sum(x => x.Total) + snapshot.UnassignedCost, 2);

        var cumulativeRevenue = decimal.Round(header.CumulativeWorkAmount, 2);
        var cumulativeProfit = decimal.Round(cumulativeRevenue - cumulativeCost, 2);

        assumptions.Add(
            "Gelir KDV hariç ve kesinti öncesidir; kesintiler tahsilatı etkiler, " +
            "işin kârını değil.");

        var profitByProduction = decimal.Round(profitRevenue - costByProduction, 2);

        return new HakedisProfit(
            header.Id,
            header.ProgressPaymentNumber,
            header.PeriodNumber,
            (int)header.Status,
            header.PeriodStartDate,
            header.PeriodEndDate,
            snapshot.IncludesExtraPayments,
            productionRevenue,
            decimal.Round(header.PriceDifferenceAmount, 2),
            advanceMovement,
            decimal.Round(header.CurrentAmount, 2),
            costByDate,
            costByDate.HasValue
                ? decimal.Round(profitRevenue - costByDate.Value, 2)
                : null,
            costByDate.HasValue
                ? Percent(profitRevenue, profitRevenue - costByDate.Value)
                : null,
            costByDateBasis,
            decimal.Round(costByProduction, 2),
            profitByProduction,
            Percent(profitRevenue, profitByProduction),
            decimal.Round(revenueWithoutCost, 2),
            cumulativeRevenue,
            cumulativeCost,
            cumulativeProfit,
            Percent(cumulativeRevenue, cumulativeProfit),
            lines,
            assumptions);
    }

    /// <summary>
    /// Dönem tarihleri arasında deftere işlenen maliyet. Tarih yoksa
    /// hesaplanmaz: dönemi belirsiz bir hakedişe rastgele bir aralık
    /// uydurmak, kârı olduğundan iyi ya da kötü gösterirdi.
    /// </summary>
    private async Task<(decimal? Cost, string Basis)> GetPeriodCostAsync(
        Guid projectId,
        DateTime? start,
        DateTime? end,
        bool includesExtraPayments,
        CancellationToken cancellationToken)
    {
        if (start is null || end is null)
        {
            return (null,
                "Hakedişin dönem başlangıç/bitiş tarihi girilmemiş; tarih bazlı " +
                "maliyet hesaplanamıyor.");
        }

        var from = DateTime.SpecifyKind(start.Value.Date, DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(end.Value.Date, DateTimeKind.Utc).AddDays(1);

        var material = await db.ProjectCostTransactions
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId
                        && x.CostDate >= from && x.CostDate < to)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        var laborRows = await db.HrProjectLaborCosts
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId
                        && x.WorkDate >= from && x.WorkDate < to)
            .Select(x => new { x.TotalLaborCost, x.CompensationCost })
            .ToListAsync(cancellationToken);

        var labor = laborRows.Sum(x => includesExtraPayments
            ? x.TotalLaborCost
            : x.TotalLaborCost - x.CompensationCost);

        return (decimal.Round(material + labor, 2),
            "Dönem tarihleri arasında deftere işlenen maliyet. Peşin alınan " +
            "malzeme ya da geç gelen fatura bu rakamı dönemler arasında kaydırır.");
    }

    private static HakedisLineProfit Bare(
        Guid id, string code, string description, string unit,
        decimal quantity, decimal unitPrice, decimal amount, string reason) =>
        new(id, code, description, unit, quantity, unitPrice, amount,
            null, null, null, null, 0m, reason);

    private static decimal? Percent(decimal basis, decimal value) =>
        basis == 0 ? null : decimal.Round(value / basis * 100m, 2);
}
