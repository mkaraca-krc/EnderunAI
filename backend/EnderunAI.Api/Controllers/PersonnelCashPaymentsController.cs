using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>Fiili elden ödeme kaydı.</summary>
/// <param name="PersonnelId">Personel.</param>
/// <param name="Kind">Ödemenin türü.</param>
/// <param name="PaymentDate">Ödeme tarihi.</param>
/// <param name="Amount">Tutar.</param>
/// <param name="PeriodYear">Bordro dönemi yılı; dönemsiz ödemede boş.</param>
/// <param name="PeriodMonth">Bordro dönemi ayı; dönemsiz ödemede boş.</param>
/// <param name="Note">Serbest not.</param>
public sealed record CreateCashPaymentRequest(
    Guid PersonnelId,
    int Kind,
    DateTime PaymentDate,
    decimal Amount,
    int? PeriodYear,
    int? PeriodMonth,
    string? Note);

/// <summary>
/// Elden ödeme kasası: personele FİİLEN elden ödenen tutarların
/// defteri.
///
/// <c>personnel-extra-payments</c> ucu aylık ne ödeneceğinin TANIMIdır;
/// burası gerçekten ne zaman ne kadar ödendiğidir. İkisi ayrı çünkü
/// tanım olmadan da ödeme yapılabiliyor (bir kerelik prim) ve tanım
/// varken ödeme yapılmamış olabiliyor.
///
/// İZOLASYON: bu uçların hiçbiri muhasebe fişi, kasa hareketi ya da
/// proje maliyet kaydı üretmez. Tamamı <c>extra_payment.*</c>
/// izinleriyle korunur; yetkisiz kullanıcıya 403 döner ve sorgusu bu
/// tabloya hiç uğramaz.
/// </summary>
[ApiController]
[Authorize]
[Route("api/personnel-cash-payments")]
public sealed class PersonnelCashPaymentsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.ExtraPaymentView)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? personnelId,
        [FromQuery] Guid? companyId,
        [FromQuery] int? year,
        [FromQuery] int? month,
        CancellationToken cancellationToken)
    {
        var query = db.PersonnelCashPayments.AsNoTracking();

        if (personnelId is Guid id) query = query.Where(x => x.PersonnelId == id);
        if (companyId is Guid cid) query = query.Where(x => x.CompanyId == cid);
        if (year is int y) query = query.Where(x => x.PeriodYear == y);
        if (month is int m) query = query.Where(x => x.PeriodMonth == m);

        return Ok(await query
            .OrderByDescending(x => x.PaymentDate)
            .Select(x => new
            {
                x.Id,
                x.PersonnelId,
                PersonnelFullName = x.Personnel.FirstName + " " + x.Personnel.LastName,
                x.CompanyId,
                Kind = (int)x.Kind,
                KindName = KindName(x.Kind),
                x.PaymentDate,
                x.Amount,
                x.PeriodYear,
                x.PeriodMonth,
                x.Note
            })
            .ToListAsync(cancellationToken));
    }

    /// <summary>
    /// Dönem özeti: tanımlanan aylık tutar ile FİİLEN ödenen arasındaki
    /// fark. Eksik ödeme sessiz kalmasın diye ayrı raporlanır.
    /// </summary>
    [HttpGet("summary")]
    [RequirePermission(PermissionCatalog.Keys.ExtraPaymentView)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] Guid companyId,
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçilmedi." });

        if (month is < 1 or > 12)
            return BadRequest(new { message = "Ay 1 ile 12 arasında olmalıdır." });

        var periodEnd = new DateTime(
            year, month, DateTime.DaysInMonth(year, month), 0, 0, 0, DateTimeKind.Utc);

        var defined = await db.PersonnelExtraPayments
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.EffectiveStartDate <= periodEnd &&
                        (x.EffectiveEndDate == null || x.EffectiveEndDate >= periodEnd))
            .Select(x => new
            {
                x.PersonnelId,
                PersonnelFullName = x.Personnel.FirstName + " " + x.Personnel.LastName,
                x.MonthlyAmount,
                x.EffectiveStartDate
            })
            .ToListAsync(cancellationToken);

        var paid = await db.PersonnelCashPayments
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.PeriodYear == year && x.PeriodMonth == month)
            .GroupBy(x => x.PersonnelId)
            .Select(g => new { PersonnelId = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var paidMap = paid.ToDictionary(x => x.PersonnelId, x => x.Total);

        // Aynı personelin birden çok tanımı varsa en son başlayan geçerli.
        var rows = defined
            .GroupBy(x => x.PersonnelId)
            .Select(g =>
            {
                var current = g.OrderByDescending(x => x.EffectiveStartDate).First();
                var paidTotal = paidMap.GetValueOrDefault(g.Key, 0m);

                return new
                {
                    PersonnelId = g.Key,
                    current.PersonnelFullName,
                    DefinedAmount = current.MonthlyAmount,
                    PaidAmount = paidTotal,
                    Difference = decimal.Round(paidTotal - current.MonthlyAmount, 2)
                };
            })
            .OrderBy(x => x.PersonnelFullName)
            .ToList();

        return Ok(new
        {
            companyId,
            year,
            month,
            personnelCount = rows.Count,
            definedTotal = decimal.Round(rows.Sum(x => x.DefinedAmount), 2),
            paidTotal = decimal.Round(rows.Sum(x => x.PaidAmount), 2),
            unpaidCount = rows.Count(x => x.PaidAmount < x.DefinedAmount),
            rows
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.ExtraPaymentManage)]
    public async Task<IActionResult> Create(
        CreateCashPaymentRequest request, CancellationToken cancellationToken)
    {
        if (request.Amount <= 0m)
            return BadRequest(new { message = "Tutar sıfırdan büyük olmalıdır." });

        if (!Enum.IsDefined(typeof(PersonnelCashPaymentKind), request.Kind))
            return BadRequest(new { message = "Geçersiz ödeme türü." });

        if (request.PeriodMonth is < 1 or > 12)
            return BadRequest(new { message = "Ay 1 ile 12 arasında olmalıdır." });

        var companyId = await db.Personnel
            .AsNoTracking()
            .Where(x => x.Id == request.PersonnelId)
            .Select(x => (Guid?)x.CompanyId)
            .SingleOrDefaultAsync(cancellationToken);

        if (companyId is null)
            return NotFound(new { message = "Personel bulunamadı." });

        var raw = User.FindFirst("sub")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        var entry = new PersonnelCashPaymentEntry
        {
            CompanyId = companyId.Value,
            PersonnelId = request.PersonnelId,
            Kind = (PersonnelCashPaymentKind)request.Kind,
            PaymentDate = DateTime.SpecifyKind(
                request.PaymentDate.Date, DateTimeKind.Utc),
            Amount = decimal.Round(request.Amount, 2),
            PeriodYear = request.PeriodYear,
            PeriodMonth = request.PeriodMonth,
            RecordedByUserId = Guid.TryParse(raw, out var userId) ? userId : null,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()
        };

        db.PersonnelCashPayments.Add(entry);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Elden ödeme kaydedildi. Bu kayıt muhasebeye yansımaz.",
            entry.Id,
            entry.Amount,
            entry.PaymentDate
        });
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ExtraPaymentManage)]
    public async Task<IActionResult> Delete(
        Guid id, CancellationToken cancellationToken)
    {
        var entry = await db.PersonnelCashPayments
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entry is null)
            return NotFound(new { message = "Kayıt bulunamadı." });

        entry.IsDeleted = true;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Kayıt silindi." });
    }

    private static string KindName(PersonnelCashPaymentKind kind) => kind switch
    {
        PersonnelCashPaymentKind.MonthlySalary => "Aylık ücret",
        PersonnelCashPaymentKind.Advance => "Avans",
        PersonnelCashPaymentKind.Bonus => "Prim",
        PersonnelCashPaymentKind.Severance => "Ayrılış",
        _ => "Diğer"
    };
}
