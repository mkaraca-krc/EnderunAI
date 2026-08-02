using EnderunAI.Api.Contracts.OfferCosting;
using EnderunAI.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Costing;

public sealed class CostEngine(AppDbContext db) : ICostEngine
{
    public async Task<EstimatePositionCostResponse> EstimatePositionAsync(
        EstimatePositionCostRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var currency = request.Currency.Trim().ToUpperInvariant();

        var position = await db.EngineeringPositions
            .AsNoTracking()
            .Where(x =>
                x.Id == request.EngineeringPositionId &&
                x.CompanyId == request.CompanyId)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                x.Unit
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (position is null)
        {
            throw new KeyNotFoundException(
                "Mühendislik pozu bulunamadı.");
        }

        var recipe = await db.EngineeringRecipes
            .AsNoTracking()
            .Where(x => x.EngineeringPositionId == position.Id)
            .OrderByDescending(x => x.IsDefault)
            .ThenByDescending(x => x.Version)
            .Select(x => new
            {
                x.Id,
                x.Version,

                Materials = x.Materials
                    .OrderBy(y => y.MaterialName)
                    .Select(y => new
                    {
                        y.Id,
                        y.MaterialCode,
                        y.MaterialName,
                        y.Quantity,
                        y.Unit,
                        y.WastePercent
                    })
                    .ToList(),

                Labors = x.Labors
                    .Select(y => new
                    {
                        y.PersonCount,
                        y.Hours
                    })
                    .ToList(),

                Machines = x.Machines
                    .Select(y => new
                    {
                        y.Quantity,
                        y.Hours
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (recipe is null)
        {
            throw new InvalidOperationException(
                "Bu poz için mühendislik reçetesi bulunmuyor.");
        }

        var today = DateTime.UtcNow.Date;

        var priceProducts = await db.ManufacturerPriceListItems
            .AsNoTracking()
            .Where(x =>
                x.ManufacturerPriceList.CompanyId == request.CompanyId &&
                x.ManufacturerPriceList.IsActive &&
                x.ManufacturerPriceList.Currency == currency &&
                (!x.ManufacturerPriceList.ValidUntil.HasValue ||
                 x.ManufacturerPriceList.ValidUntil.Value.Date >= today))
            .Select(x => new
            {
                x.Id,
                Manufacturer =
                    x.ManufacturerPriceList.ManufacturerName,
                x.ManufacturerPriceList.ListDate,
                x.ProductCode,
                x.ProductDescription,
                x.Unit,
                x.ListPrice,
                x.Brand,
                x.Model
            })
            .ToListAsync(cancellationToken);

        var materialResults =
            new List<EstimatedMaterialCost>();

        foreach (var material in recipe.Materials)
        {
            var code = material.MaterialCode.Trim();
            var name = material.MaterialName.Trim();

            var candidates = priceProducts
                .Where(x =>
                    (!string.IsNullOrWhiteSpace(code) &&
                     x.ProductCode.Equals(
                         code,
                         StringComparison.OrdinalIgnoreCase)) ||
                    x.ProductDescription.Contains(
                        name,
                        StringComparison.OrdinalIgnoreCase) ||
                    name.Contains(
                        x.ProductDescription,
                        StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.ListPrice)
                .ThenByDescending(x => x.ListDate)
                .ToList();

            var selected = candidates.FirstOrDefault();

            var effectiveQuantity = decimal.Round(
                material.Quantity *
                (1 + material.WastePercent / 100m),
                4);

            var unitPrice = selected?.ListPrice ?? 0m;

            var totalPrice = decimal.Round(
                effectiveQuantity * unitPrice,
                2);

            materialResults.Add(
                new EstimatedMaterialCost(
                    material.Id,
                    material.MaterialCode,
                    material.MaterialName,
                    material.Quantity,
                    material.WastePercent,
                    effectiveQuantity,
                    selected is not null,
                    selected?.Id,
                    selected?.Manufacturer,
                    selected?.ProductCode,
                    selected?.Brand,
                    selected?.Model,
                    unitPrice,
                    totalPrice,
                    currency));
        }

        var materialCost = decimal.Round(
            materialResults.Sum(x => x.TotalPrice),
            2);

        var laborHours = decimal.Round(
            recipe.Labors.Sum(
                x => x.PersonCount * x.Hours),
            4);

        var machineHours = decimal.Round(
            recipe.Machines.Sum(
                x => x.Quantity * x.Hours),
            4);

        var laborCost = decimal.Round(
            laborHours * request.LaborHourRate,
            2);

        var machineCost = decimal.Round(
            machineHours * request.MachineHourRate,
            2);

        var unitCost = decimal.Round(
            materialCost + laborCost + machineCost,
            2);

        return new EstimatePositionCostResponse(
            position.Id,
            position.Code,
            position.Name,
            position.Unit,
            recipe.Id,
            recipe.Version,
            materialCost,
            laborHours,
            laborCost,
            machineHours,
            machineCost,
            unitCost,
            materialResults.Count(x => x.PriceFound),
            materialResults.Count(x => !x.PriceFound),
            materialResults);
    }

    private static void ValidateRequest(
        EstimatePositionCostRequest request)
    {
        if (request.CompanyId == Guid.Empty)
        {
            throw new ArgumentException(
                "Şirket seçimi zorunludur.");
        }

        if (request.EngineeringPositionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Mühendislik pozu zorunludur.");
        }

        if (request.LaborHourRate < 0 ||
            request.MachineHourRate < 0)
        {
            throw new ArgumentException(
                "İşçilik ve makine saat ücretleri negatif olamaz.");
        }

        if (string.IsNullOrWhiteSpace(request.Currency))
        {
            throw new ArgumentException(
                "Para birimi zorunludur.");
        }
    }
}
