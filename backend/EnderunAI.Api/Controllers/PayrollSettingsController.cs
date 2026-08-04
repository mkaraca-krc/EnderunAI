using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EnderunAI.Api.Contracts.HumanResources;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Bordro parametreleri: asgari ücret, SGK taban/tavan, prim oranları,
/// gelir vergisi dilimleri ve damga vergisi. Değerler her yıl mevzuatla
/// değiştiği için koda gömülmez; buradan yönetilir.
///
/// Parametreler doğrulanmadan (VerifiedAtUtc boşken) bordro
/// kesinleştirilemez — yanlış parametreyle üretilmiş resmi bordro,
/// eksik prim/vergi beyanı anlamına geldiği için akış fail-closed.
/// </summary>
[ApiController]
[Authorize]
[Route("api/payroll-settings")]
public sealed class PayrollSettingsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.SalaryView)]
    public async Task<IActionResult> Get(
        [FromQuery] Guid companyId,
        [FromQuery] int? year,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçimi zorunludur." });

        var settings = await LoadAsync(companyId, year, cancellationToken);

        return settings is null
            ? NotFound(new
            {
                message = $"{year ?? DateTime.UtcNow.Year} yılı için bordro " +
                    "parametresi tanımlı değil."
            })
            : Ok(ToResponse(settings));
    }

    [HttpPut]
    [RequirePermission(PermissionCatalog.Keys.SalaryManage)]
    public async Task<IActionResult> Update(
        [FromQuery] Guid companyId,
        [FromQuery] int? year,
        UpdatePayrollSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var targetYear = year ?? DateTime.UtcNow.Year;

        // Dilimler ayrı yönetildiği için ana kayıt navigasyon YÜKLENMEDEN
        // çekilir; aksi halde koleksiyonun toptan değiştirilmesi EF'in
        // silme/ekleme sırasını çözememesine yol açıyor.
        var settings = await db.CompanyPayrollSettings
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.Year == targetYear,
                cancellationToken);

        if (settings is null)
            return NotFound(new { message = "Bordro parametresi bulunamadı." });

        var validationError = Validate(request);
        if (validationError is not null)
            return BadRequest(new { message = validationError });

        settings.MinimumWageGross = request.MinimumWageGross;
        settings.MinimumWageNet = request.MinimumWageNet;
        settings.SgkBaseFloor = request.SgkBaseFloor;
        settings.SgkBaseCeiling = request.SgkBaseCeiling;
        settings.SgkEmployeeRate = request.SgkEmployeeRate;
        settings.UnemploymentEmployeeRate = request.UnemploymentEmployeeRate;
        settings.SgkEmployerRate = request.SgkEmployerRate;
        settings.UnemploymentEmployerRate = request.UnemploymentEmployerRate;
        settings.SgkEmployerDiscountEnabled = request.SgkEmployerDiscountEnabled;
        settings.SgkEmployerDiscountPoints = request.SgkEmployerDiscountPoints;
        settings.StampTaxPerMille = request.StampTaxPerMille;
        settings.MinimumWageIncomeTaxExemptionEnabled =
            request.MinimumWageIncomeTaxExemptionEnabled;
        settings.MinimumWageStampTaxExemptionEnabled =
            request.MinimumWageStampTaxExemptionEnabled;
        // Sıfır/negatif saat saatlik ücreti bozar; yasal varsayılana düşülür.
        settings.DailyWorkHours =
            request.DailyWorkHours > 0m ? request.DailyWorkHours : 7.5m;
        settings.SeveranceCeiling = request.SeveranceCeiling;
        settings.SeveranceCeilingPeriodNote =
            string.IsNullOrWhiteSpace(request.SeveranceCeilingPeriodNote)
                ? null
                : request.SeveranceCeilingPeriodNote.Trim();
        settings.UpdatedAtUtc = DateTime.UtcNow;
        settings.UpdatedByUserId = CurrentUserId();

        // Parametre değişince doğrulama düşer: değişen değerlerin yeniden
        // kontrol edilmesi gerekir.
        settings.VerifiedAtUtc = null;
        settings.VerifiedByUserId = null;
        settings.VerificationNote = null;

        // Dilimler (SettingsId, Order) üzerinde benzersiz. Eski satırlar
        // önce silinip kaydedilir, yeniler sonra eklenir — tek SaveChanges
        // olsaydı EF insert'leri delete'lerden önce gönderip indeksi
        // ihlal ederdi.
        var existingBrackets = await db.PayrollTaxBrackets
            .Where(x => x.CompanyPayrollSettingsId == settings.Id)
            .ToListAsync(cancellationToken);

        db.PayrollTaxBrackets.RemoveRange(existingBrackets);
        await db.SaveChangesAsync(cancellationToken);

        db.PayrollTaxBrackets.AddRange(request.TaxBrackets
            .OrderBy(x => x.Order)
            .Select(x => new PayrollTaxBracket
            {
                CompanyPayrollSettingsId = settings.Id,
                Order = x.Order,
                LowerBound = x.LowerBound,
                UpperBound = x.UpperBound,
                Rate = x.Rate
            }));

        await db.SaveChangesAsync(cancellationToken);

        var reloaded = await LoadAsync(companyId, targetYear, cancellationToken);
        return Ok(ToResponse(reloaded!));
    }

    /// <summary>
    /// Parametrelerin yürürlükteki mevzuatla karşılaştırıldığını onaylar.
    /// Bordronun kesinleştirilebilmesi için gereken adım.
    /// </summary>
    [HttpPost("verify")]
    [RequirePermission(PermissionCatalog.Keys.AttendancePayrollApprove)]
    public async Task<IActionResult> Verify(
        [FromQuery] Guid companyId,
        [FromQuery] int? year,
        VerifyPayrollSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await LoadAsync(companyId, year, cancellationToken);
        if (settings is null)
            return NotFound(new { message = "Bordro parametresi bulunamadı." });

        if (settings.TaxBrackets.Count == 0)
        {
            return Conflict(new
            {
                message = "Gelir vergisi dilimleri tanımlanmadan doğrulama yapılamaz."
            });
        }

        if (settings.MinimumWageGross <= 0m || settings.SgkBaseCeiling <= 0m)
        {
            return Conflict(new
            {
                message = "Asgari ücret ve SGK tavanı sıfırdan büyük olmalıdır."
            });
        }

        settings.VerifiedAtUtc = DateTime.UtcNow;
        settings.VerifiedByUserId = CurrentUserId();
        settings.VerificationNote = string.IsNullOrWhiteSpace(request.VerificationNote)
            ? null
            : request.VerificationNote.Trim();

        await db.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(settings));
    }

    private async Task<CompanyPayrollSettings?> LoadAsync(
        Guid companyId, int? year, CancellationToken cancellationToken)
    {
        var targetYear = year ?? DateTime.UtcNow.Year;

        return await db.CompanyPayrollSettings
            .Include(x => x.TaxBrackets)
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.Year == targetYear,
                cancellationToken);
    }

    /// <summary>
    /// Dilimlerin kesintisiz ve artan sırada olmasını doğrular — boşluk
    /// ya da çakışma, kümülatif matrahın yanlış vergilendirilmesi demek.
    /// </summary>
    private static string? Validate(UpdatePayrollSettingsRequest request)
    {
        if (request.MinimumWageGross <= 0m)
            return "Brüt asgari ücret sıfırdan büyük olmalıdır.";

        if (request.SgkBaseCeiling <= request.SgkBaseFloor)
            return "SGK tavanı tabandan büyük olmalıdır.";

        foreach (var (name, rate) in new (string, decimal)[]
        {
            ("İşçi SGK primi", request.SgkEmployeeRate),
            ("İşçi işsizlik primi", request.UnemploymentEmployeeRate),
            ("İşveren SGK primi", request.SgkEmployerRate),
            ("İşveren işsizlik primi", request.UnemploymentEmployerRate),
            ("Damga vergisi", request.StampTaxPerMille)
        })
        {
            if (rate < 0m || rate > 100m)
                return $"{name} oranı 0 ile 100 arasında olmalıdır.";
        }

        var brackets = request.TaxBrackets.OrderBy(x => x.Order).ToList();
        if (brackets.Count == 0)
            return "En az bir gelir vergisi dilimi tanımlanmalıdır.";

        if (brackets[0].LowerBound != 0m)
            return "İlk gelir vergisi diliminin alt sınırı 0 olmalıdır.";

        for (var i = 0; i < brackets.Count; i++)
        {
            var bracket = brackets[i];

            if (bracket.Rate < 0m || bracket.Rate > 100m)
                return $"{bracket.Order}. dilimin oranı 0 ile 100 arasında olmalıdır.";

            var isLast = i == brackets.Count - 1;

            if (isLast)
            {
                if (bracket.UpperBound is not null)
                    return "Son gelir vergisi diliminin üst sınırı boş olmalıdır.";
                continue;
            }

            if (bracket.UpperBound is null)
                return $"{bracket.Order}. dilimin üst sınırı zorunludur.";

            if (bracket.UpperBound <= bracket.LowerBound)
                return $"{bracket.Order}. dilimin üst sınırı alt sınırından büyük olmalıdır.";

            if (brackets[i + 1].LowerBound != bracket.UpperBound)
            {
                return $"{bracket.Order}. dilim ile {brackets[i + 1].Order}. dilim " +
                    "arasında boşluk veya çakışma var.";
            }
        }

        return null;
    }

    private static PayrollSettingsResponse ToResponse(CompanyPayrollSettings settings) =>
        new(
            settings.Id,
            settings.CompanyId,
            settings.Year,
            settings.MinimumWageGross,
            settings.MinimumWageNet,
            settings.SgkBaseFloor,
            settings.SgkBaseCeiling,
            settings.SgkEmployeeRate,
            settings.UnemploymentEmployeeRate,
            settings.SgkEmployerRate,
            settings.UnemploymentEmployerRate,
            settings.SgkEmployerDiscountEnabled,
            settings.SgkEmployerDiscountPoints,
            settings.StampTaxPerMille,
            settings.MinimumWageIncomeTaxExemptionEnabled,
            settings.MinimumWageStampTaxExemptionEnabled,
            settings.SeveranceCeiling,
            settings.SeveranceCeilingPeriodNote,
            settings.VerifiedAtUtc,
            settings.VerificationNote,
            settings.VerifiedAtUtc is not null,
            settings.TaxBrackets
                .OrderBy(x => x.Order)
                .Select(x => new PayrollTaxBracketResponse(
                    x.Id, x.Order, x.LowerBound, x.UpperBound, x.Rate))
                .ToList(),
            settings.DailyWorkHours);

    private Guid? CurrentUserId()
    {
        var value =
            User.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
            User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out var id) ? id : null;
    }
}
