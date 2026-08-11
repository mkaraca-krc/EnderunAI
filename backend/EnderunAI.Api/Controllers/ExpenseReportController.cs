using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Expenses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Gider merkezi raporu: merkez × kategori kırılımı.
///
/// Otomatik kalemler burada SALT OKUNUR gelir
/// (<c>isEditableHere = false</c>): kaynağından düzeltilir, yoksa
/// maliyet defteri ile rapor ayrışır.
/// </summary>
[ApiController]
[Authorize]
[Route("api/expenses/rapor")]
public sealed class ExpenseReportController(
    ExpenseCenterReportService reports) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.ExpenseView)]
    public async Task<IActionResult> Get(
        [FromQuery] Guid companyId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçimi zorunludur." });

        var today = DateTime.UtcNow.Date;

        // Varsayılan dönem: içinde bulunulan ay. Aralık verilmezse
        // bütün geçmişi toplamak, ekranı ilk açılışta anlamsız bir
        // kümülatif rakamla açardı.
        var start = ExpenseEntryService.AsUtcDate(
            from ?? new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc));

        var end = ExpenseEntryService.AsUtcDate(to ?? start.AddMonths(1).AddDays(-1));

        if (end < start)
            return BadRequest(new { message = "Bitiş tarihi başlangıçtan önce olamaz." });

        var report = await reports.BuildAsync(companyId, start, end, cancellationToken);

        return Ok(new
        {
            from = report.From,
            to = report.To,
            total = report.Total,
            hiddenCount = report.HiddenCount,
            hiddenNote = report.HiddenNote,
            notes = report.Notes,
            centerTotals = report.CenterTotals.Select(x => new
            {
                centerType = x.CenterType.ToString(),
                centerId = x.CenterId,
                centerName = x.CenterName,
                amount = x.Amount
            }),
            categoryTotals = report.CategoryTotals.Select(x => new
            {
                categoryCode = x.CategoryCode,
                categoryName = x.CategoryName,
                amount = x.Amount
            }),
            rows = report.Rows.Select(x => new
            {
                centerType = x.CenterType.ToString(),
                centerId = x.CenterId,
                centerName = x.CenterName,
                categoryCode = x.CategoryCode,
                categoryName = x.CategoryName,
                source = x.Source,
                amount = x.Amount,
                isEstimated = x.IsEstimated,
                isEditableHere = x.IsEditableHere
            })
        });
    }
}
