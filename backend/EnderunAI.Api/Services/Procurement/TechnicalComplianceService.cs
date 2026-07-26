using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Procurement;

public sealed record TechnicalComplianceSummary(
    Guid SupplierOfferId,
    decimal Score,
    TechnicalComplianceStatus Status,
    int CompliantCount,
    int ConditionalCount,
    int NonCompliantCount,
    IReadOnlyList<string> Warnings);

public interface ITechnicalComplianceService
{
    Task<TechnicalComplianceSummary> EvaluateOfferAsync(Guid supplierOfferId, CancellationToken cancellationToken = default);
}

public sealed class TechnicalComplianceService(
    ProcurementDbContext procurementDb,
    ProcurementTechnicalDbContext technicalDb) : ITechnicalComplianceService
{
    public async Task<TechnicalComplianceSummary> EvaluateOfferAsync(Guid supplierOfferId, CancellationToken cancellationToken = default)
    {
        var offer = await procurementDb.SupplierOffers
            .AsNoTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == supplierOfferId, cancellationToken)
            ?? throw new InvalidOperationException("Tedarikçi teklifi bulunamadı.");

        var specification = await technicalDb.Specifications
            .AsNoTracking()
            .Include(x => x.Criteria)
            .Where(x => x.IsActive && x.RfqId == offer.RfqId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("RFQ için aktif teknik şartname bulunamadı.");

        var itemIds = offer.Items.Select(x => x.Id).ToList();
        var responses = await technicalDb.Responses
            .Where(x => x.SupplierOfferId == supplierOfferId && itemIds.Contains(x.SupplierOfferItemId))
            .ToListAsync(cancellationToken);

        var warnings = new List<string>();
        decimal earned = 0m;
        decimal totalWeight = 0m;
        var compliant = 0;
        var conditional = 0;
        var nonCompliant = 0;

        foreach (var item in offer.Items)
        {
            var criteria = specification.Criteria
                .Where(x => !x.RfqItemId.HasValue || x.RfqItemId == item.RfqItemId)
                .ToList();

            foreach (var criterion in criteria)
            {
                totalWeight += Math.Max(criterion.Weight, 0m);
                var response = responses.FirstOrDefault(x => x.SupplierOfferItemId == item.Id && x.TechnicalCriterionId == criterion.Id);
                if (response is null)
                {
                    response = new SupplierOfferTechnicalResponse
                    {
                        SupplierOfferId = supplierOfferId,
                        SupplierOfferItemId = item.Id,
                        TechnicalCriterionId = criterion.Id
                    };
                    technicalDb.Responses.Add(response);
                    responses.Add(response);
                }

                var status = EvaluateCriterion(criterion, response, out var note);
                response.Status = status;
                response.Score = status switch
                {
                    TechnicalComplianceStatus.Compliant => 100m,
                    TechnicalComplianceStatus.ConditionallyCompliant => 50m,
                    _ => 0m
                };
                response.EvaluationNote = note;
                response.EvaluatedAtUtc = DateTime.UtcNow;

                earned += Math.Max(criterion.Weight, 0m) * response.Score / 100m;

                if (status == TechnicalComplianceStatus.Compliant) compliant++;
                else if (status == TechnicalComplianceStatus.ConditionallyCompliant) conditional++;
                else
                {
                    nonCompliant++;
                    if (criterion.IsMandatory)
                        warnings.Add($"Zorunlu kriter karşılanmadı: {criterion.Code} - {criterion.Name}");
                }
            }
        }

        await technicalDb.SaveChangesAsync(cancellationToken);

        var score = totalWeight <= 0 ? 0m : decimal.Round(earned / totalWeight * 100m, 2);
        var mandatoryFailure = warnings.Count > 0;
        var overall = mandatoryFailure
            ? TechnicalComplianceStatus.NonCompliant
            : conditional > 0 || nonCompliant > 0
                ? TechnicalComplianceStatus.ConditionallyCompliant
                : TechnicalComplianceStatus.Compliant;

        return new TechnicalComplianceSummary(supplierOfferId, score, overall, compliant, conditional, nonCompliant, warnings);
    }

    private static TechnicalComplianceStatus EvaluateCriterion(
        TechnicalCriterion criterion,
        SupplierOfferTechnicalResponse response,
        out string note)
    {
        note = string.Empty;

        if (criterion.Type == TechnicalCriterionType.Certificate)
        {
            if (response.IsProvided == true)
            {
                note = "Sertifika/belge sağlandı.";
                return TechnicalComplianceStatus.Compliant;
            }
            note = criterion.IsMandatory ? "Zorunlu sertifika/belge sağlanmadı." : "Belge eksik; koşullu değerlendirme gerekir.";
            return criterion.IsMandatory ? TechnicalComplianceStatus.NonCompliant : TechnicalComplianceStatus.ConditionallyCompliant;
        }

        if (criterion.Type is TechnicalCriterionType.NumericMinimum or TechnicalCriterionType.NumericMaximum)
        {
            if (!response.OfferedNumericValue.HasValue || !criterion.NumericValue.HasValue)
            {
                note = "Sayısal değer eksik.";
                return criterion.IsMandatory ? TechnicalComplianceStatus.NonCompliant : TechnicalComplianceStatus.ConditionallyCompliant;
            }

            var ok = criterion.Type == TechnicalCriterionType.NumericMinimum
                ? response.OfferedNumericValue.Value >= criterion.NumericValue.Value
                : response.OfferedNumericValue.Value <= criterion.NumericValue.Value;
            note = ok ? "Sayısal kriter karşılandı." : "Sayısal kriter karşılanmadı.";
            return ok ? TechnicalComplianceStatus.Compliant : TechnicalComplianceStatus.NonCompliant;
        }

        var offered = response.OfferedValue?.Trim();
        var expected = criterion.ExpectedValue?.Trim();
        if (string.IsNullOrWhiteSpace(offered))
        {
            note = "Teklif değeri girilmedi.";
            return criterion.IsMandatory ? TechnicalComplianceStatus.NonCompliant : TechnicalComplianceStatus.ConditionallyCompliant;
        }

        if (string.IsNullOrWhiteSpace(expected))
        {
            note = "Manuel teknik değerlendirme gerekiyor.";
            return TechnicalComplianceStatus.ConditionallyCompliant;
        }

        var allowed = expected.Split(new[] { ',', ';', '|' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var matches = criterion.Type == TechnicalCriterionType.Text
            ? allowed.Any(x => offered.Contains(x, StringComparison.OrdinalIgnoreCase))
            : allowed.Any(x => string.Equals(x, offered, StringComparison.OrdinalIgnoreCase));

        note = matches ? "Teknik kriter karşılandı." : "Teklif değeri şartnameyle uyuşmuyor.";
        return matches ? TechnicalComplianceStatus.Compliant : TechnicalComplianceStatus.NonCompliant;
    }
}
