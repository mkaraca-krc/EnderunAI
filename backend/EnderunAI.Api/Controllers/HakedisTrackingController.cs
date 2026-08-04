using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Hakedis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Hakediş takip tablosu — NATURA'daki Hak.Takip sayfasının karşılığı.
///
/// Projenin tüm hakedişlerini dönem sırasıyla tek tabloda verir: imalat,
/// ihzarat, KDV, tevkifat, stopaj, her kesintinin dönem bazında ve
/// kümülatif seyri, ödeme dağılımı ve açık bakiyeler.
/// </summary>
[ApiController]
[Authorize]
[Route("api/hakedis-tracking")]
public sealed class HakedisTrackingController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> Get(
        [FromQuery] Guid projectId,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                x.ContractAmount,
                x.CurrencyCode
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (project is null)
            return NotFound(new { message = "Proje bulunamadı." });

        // İptal edilen hakedişler takipte yer almaz; kümülatif seriyi
        // bozarlar.
        var payments = await db.ProgressPayments
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId &&
                        x.Status != ProgressPaymentStatus.Cancelled)
            .OrderBy(x => x.PeriodNumber)
            .Select(x => new
            {
                x.Id,
                x.ProgressPaymentNumber,
                x.PeriodNumber,
                x.ProgressPaymentDate,
                Status = (int)x.Status,
                x.CumulativeWorkAmount,
                x.CumulativeAdvanceMaterialAmount,
                x.PreviousAmount,
                x.CurrentAmount,
                x.CumulativeAmount,
                x.PriceDifferenceAmount,
                x.VatAmount,
                x.WithholdingAmount,
                x.IncomeTaxWithholdingAmount,
                x.TotalDeductionAmount,
                x.GrossPayableAmount,
                x.NetPayableAmount,
                Deductions = x.Deductions
                    .OrderBy(d => d.LineNumber)
                    .Select(d => new
                    {
                        d.DeductionType,
                        d.Description,
                        d.Rate,
                        d.Amount,
                        d.PreviousAmount,
                        d.CumulativeAmount
                    })
                    .ToList(),
                PaymentPlans = x.PaymentPlans
                    .OrderBy(p => p.LineNumber)
                    .Select(p => new
                    {
                        PaymentType = (int)p.PaymentType,
                        p.Amount,
                        p.DueDate
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        // Kesinti türleri sütun başlıklarını oluşturur; hangi türlerin
        // kullanıldığı projeye göre değişir.
        var deductionTypes = payments
            .SelectMany(x => x.Deductions)
            .GroupBy(x => x.DeductionType)
            .Select(g => new
            {
                deductionType = g.Key,
                name = g.First().Description,
                // Türün proje boyunca kesilmiş toplamı.
                totalAmount = g.Sum(x => x.Amount)
            })
            .OrderBy(x => x.deductionType)
            .ToList();

        var barterEntries = await db.BarterLedgerEntries
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Select(x => new { x.EntryType, x.Amount })
            .ToListAsync(cancellationToken);

        var barterDeducted = barterEntries
            .Where(x => x.EntryType == BarterEntryType.Deduction)
            .Sum(x => x.Amount);

        var barterReceived = barterEntries
            .Where(x => x.EntryType == BarterEntryType.Receipt)
            .Sum(x => x.Amount);

        var openAdvanceMaterial = await db.ProgressPaymentAdvanceMaterials
            .AsNoTracking()
            .Where(x => x.ProgressPayment.ProjectId == projectId &&
                        x.ProgressPayment.Status != ProgressPaymentStatus.Cancelled)
            .SumAsync(x => (decimal?)(x.Amount - x.OffsetAmount), cancellationToken) ?? 0m;

        var last = payments.LastOrDefault();

        return Ok(new
        {
            project,
            periods = payments,
            deductionTypes,
            totals = new
            {
                // Kümülatif değerler son dönemin satırından okunur;
                // dönem tutarlarını toplamak yuvarlama farkı biriktirir.
                cumulativeWorkAmount = last?.CumulativeWorkAmount ?? 0m,
                cumulativeTotalAmount = last?.CumulativeAmount ?? 0m,
                openAdvanceMaterialAmount = openAdvanceMaterial,
                totalVat = payments.Sum(x => x.VatAmount),
                totalWithholding = payments.Sum(x => x.WithholdingAmount),
                totalIncomeTaxWithholding = payments.Sum(x => x.IncomeTaxWithholdingAmount),
                totalDeduction = payments.Sum(x => x.TotalDeductionAmount),
                totalNetPayable = payments.Sum(x => x.NetPayableAmount),
                // Sözleşme bedeline göre gerçekleşme.
                completionRate = project.ContractAmount is > 0m
                    ? decimal.Round(
                        (last?.CumulativeAmount ?? 0m) / project.ContractAmount.Value * 100m,
                        2)
                    : 0m
            },
            barter = new
            {
                totalDeducted = barterDeducted,
                totalReceived = barterReceived,
                openBalance = HakedisCalculationService.CalculateBarterBalance(
                    barterDeducted, barterReceived)
            }
        });
    }
}
