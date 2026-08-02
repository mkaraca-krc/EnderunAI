using EnderunAI.Api.Contracts;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Email;
using EnderunAI.Api.Services.Upload;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Şirketin kendi kurumsal kimlik bilgileri (unvan, vergi, IBAN, logo) —
/// tekil bir "ayarlar" ekranı olarak tasarlandı. Sistemde şu an tek şirket
/// kaydı var; birden fazla olursa ilk (en eski) aktif kayıt "bizim
/// şirketimiz" kabul edilir. Genel çoklu-şirket listesi (/sirketler)
/// ayrı, bu controller'a dokunmaz.
/// </summary>
[ApiController]
[Authorize]
[Route("api/company-settings")]
public sealed class CompanySettingsController(
    AppDbContext db,
    IUploadService uploadService,
    IEmailService emailService) : ControllerBase
{
    private const string LogoCategory = "company-logo";

    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.CompanySettingsView)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var company = await GetPrimaryCompanyAsync(cancellationToken);
        if (company is null)
            return NotFound(new { message = "Şirket kaydı bulunamadı." });

        return Ok(ToResponse(company));
    }

    [HttpPut]
    [RequirePermission(PermissionCatalog.Keys.CompanySettingsEdit)]
    public async Task<IActionResult> Update(
        UpdateCompanySettingsRequest request,
        CancellationToken cancellationToken)
    {
        var company = await GetPrimaryCompanyAsync(cancellationToken, tracking: true);
        if (company is null)
            return NotFound(new { message = "Şirket kaydı bulunamadı." });

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Şirket unvanı zorunludur." });

        company.Name = request.Name.Trim();
        company.TradeName = NormalizeOptional(request.TradeName);
        company.TaxOffice = NormalizeOptional(request.TaxOffice);
        company.TaxNumber = NormalizeOptional(request.TaxNumber);
        company.MersisNumber = NormalizeOptional(request.MersisNumber);
        company.TradeRegistryNumber = NormalizeOptional(request.TradeRegistryNumber);
        company.Phone = NormalizeOptional(request.Phone);
        company.Email = NormalizeOptional(request.Email);
        company.Website = NormalizeOptional(request.Website);
        company.Address = NormalizeOptional(request.Address);
        company.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Şirket bilgileri güncellendi.",
            company = ToResponse(company)
        });
    }

    [HttpPost("logo")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [RequirePermission(PermissionCatalog.Keys.CompanySettingsEdit)]
    public async Task<IActionResult> UploadLogo(
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        var company = await GetPrimaryCompanyAsync(cancellationToken, tracking: true);
        if (company is null)
            return NotFound(new { message = "Şirket kaydı bulunamadı." });

        try
        {
            var uploaded = await uploadService.SaveAsync(file, LogoCategory, cancellationToken);

            if (!string.IsNullOrWhiteSpace(company.LogoPath))
                uploadService.DeleteFile(LogoCategory, company.LogoPath);

            company.LogoPath = uploaded.StoredName;
            company.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                message = "Logo güncellendi.",
                logoUrl = "/api/backend/company-settings/logo"
            });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpGet("logo")]
    [AllowAnonymous]
    public async Task<IActionResult> GetLogo(CancellationToken cancellationToken)
    {
        var company = await GetPrimaryCompanyAsync(cancellationToken);
        if (company is null || string.IsNullOrWhiteSpace(company.LogoPath))
            return NotFound();

        var file = uploadService.GetFile(LogoCategory, company.LogoPath);
        if (file is null)
            return NotFound();

        var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Response.Headers.CacheControl = "public, max-age=3600";
        return File(stream, file.ContentType, enableRangeProcessing: true);
    }

    [HttpPost("bank-accounts")]
    [RequirePermission(PermissionCatalog.Keys.CompanySettingsEdit)]
    public async Task<IActionResult> AddBankAccount(
        CreateCompanyBankAccountRequest request,
        CancellationToken cancellationToken)
    {
        var company = await GetPrimaryCompanyAsync(cancellationToken, tracking: true);
        if (company is null)
            return NotFound(new { message = "Şirket kaydı bulunamadı." });

        if (string.IsNullOrWhiteSpace(request.BankName) || string.IsNullOrWhiteSpace(request.Iban))
            return BadRequest(new { message = "Banka adı ve IBAN zorunludur." });

        var account = new CompanyBankAccount
        {
            CompanyId = company.Id,
            BankName = request.BankName.Trim(),
            Iban = request.Iban.Trim().Replace(" ", "").ToUpperInvariant(),
            AccountHolder = NormalizeOptional(request.AccountHolder),
            CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode)
                ? "TRY"
                : request.CurrencyCode.Trim().ToUpperInvariant()
        };

        db.CompanyBankAccounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "IBAN eklendi.",
            account.Id
        });
    }

    [HttpDelete("bank-accounts/{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.CompanySettingsEdit)]
    public async Task<IActionResult> DeleteBankAccount(
        Guid id,
        CancellationToken cancellationToken)
    {
        var account = await db.CompanyBankAccounts.SingleOrDefaultAsync(
            x => x.Id == id, cancellationToken);

        if (account is null)
            return NotFound(new { message = "IBAN kaydı bulunamadı." });

        account.IsDeleted = true;
        account.IsActive = false;
        account.DeletedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Rol bazlı mesai penceresi yönetimi — Admin ve Genel Müdür rolleri
    /// hiç satır taşımaz (WorkHourAccessService içinde her zaman
    /// istisnasız izinli), bu yüzden burada listelenmezler.
    /// </summary>
    [HttpGet("work-hour-windows")]
    [RequirePermission(PermissionCatalog.Keys.CompanySettingsView)]
    public async Task<IActionResult> GetWorkHourWindows(CancellationToken cancellationToken)
    {
        var roles = await db.Roles
            .AsNoTracking()
            .Where(role => role.Name != "Admin" && role.Name != "Genel Müdür")
            .OrderBy(role => role.Name)
            .Select(role => new { role.Id, role.Name })
            .ToListAsync(cancellationToken);

        var windowsByRoleId = (await db.RoleWorkHourWindows
                .AsNoTracking()
                .OrderBy(w => w.DayOfWeek)
                .Select(w => new WorkHourWindowDto(w.RoleId, w.DayOfWeek, w.StartTime, w.EndTime))
                .ToListAsync(cancellationToken))
            .GroupBy(w => w.RoleId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(w => new WorkHourWindowResponseItem(w.DayOfWeek, w.StartTime, w.EndTime)).ToList());

        var result = roles.Select(role => new
        {
            role.Id,
            role.Name,
            Windows = windowsByRoleId.TryGetValue(role.Id, out var windows)
                ? windows
                : new List<WorkHourWindowResponseItem>()
        });

        return Ok(result);
    }

    private sealed record WorkHourWindowDto(Guid RoleId, int DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);

    private sealed record WorkHourWindowResponseItem(int DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);

    [HttpPut("work-hour-windows/{roleId:guid}")]
    [RequirePermission(PermissionCatalog.Keys.CompanySettingsEdit)]
    public async Task<IActionResult> UpdateWorkHourWindows(
        Guid roleId,
        UpdateRoleWorkHourWindowsRequest request,
        CancellationToken cancellationToken)
    {
        var role = await db.Roles.SingleOrDefaultAsync(item => item.Id == roleId, cancellationToken);
        if (role is null)
            return NotFound(new { message = "Rol bulunamadı." });

        if (string.Equals(role.Name, "Admin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role.Name, "Genel Müdür", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "Admin ve Genel Müdür rolleri için mesai penceresi tanımlanamaz, her zaman açıktır."
            });
        }

        foreach (var window in request.Windows)
        {
            if (window.DayOfWeek is < 0 or > 6)
                return BadRequest(new { message = "Geçersiz gün değeri." });

            if (window.EndTime <= window.StartTime)
                return BadRequest(new { message = "Bitiş saati başlangıç saatinden sonra olmalıdır." });
        }

        var existing = await db.RoleWorkHourWindows
            .Where(item => item.RoleId == roleId)
            .ToListAsync(cancellationToken);
        db.RoleWorkHourWindows.RemoveRange(existing);

        foreach (var window in request.Windows)
        {
            db.RoleWorkHourWindows.Add(new RoleWorkHourWindow
            {
                RoleId = roleId,
                DayOfWeek = window.DayOfWeek,
                StartTime = window.StartTime,
                EndTime = window.EndTime
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = $"{role.Name} rolünün mesai penceresi güncellendi." });
    }

    /// <summary>
    /// Brevo API entegrasyonunu doğrulamak için tek seferlik test e-postası
    /// gönderir. BREVO_API_KEY tanımlı değilse 400 döner.
    /// </summary>
    [HttpPost("email-test")]
    [RequirePermission(PermissionCatalog.Keys.CompanySettingsEdit)]
    public async Task<IActionResult> SendTestEmail(
        SendTestEmailRequest request,
        CancellationToken cancellationToken)
    {
        if (!emailService.IsConfigured)
        {
            return BadRequest(new
            {
                message = "E-posta yapılandırılmamış (BREVO_API_KEY veya gönderen adres eksik)."
            });
        }

        if (string.IsNullOrWhiteSpace(request.ToEmail) ||
            !System.Net.Mail.MailAddress.TryCreate(request.ToEmail, out _))
        {
            return BadRequest(new { message = "Geçerli bir e-posta adresi girin." });
        }

        try
        {
            await emailService.SendAsync(
                request.ToEmail.Trim(),
                null,
                "Enderun ERP - Test E-postası",
                "<p>Bu, Enderun ERP e-posta entegrasyonunun (Brevo API) doğru " +
                "çalıştığını doğrulamak için gönderilen bir test e-postasıdır.</p>",
                cancellationToken);
        }
        catch (Exception exception)
        {
            return StatusCode(502, new
            {
                message = $"Test e-postası gönderilemedi: {exception.Message}"
            });
        }

        return Ok(new { message = $"Test e-postası {request.ToEmail.Trim()} adresine gönderildi." });
    }

    private async Task<Company?> GetPrimaryCompanyAsync(
        CancellationToken cancellationToken,
        bool tracking = false)
    {
        var query = db.Companies
            .Include(x => x.BankAccounts)
            .OrderBy(x => x.CreatedAtUtc)
            .AsQueryable();

        if (!tracking)
            query = query.AsNoTracking();

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    private static object ToResponse(Company company) => new
    {
        company.Id,
        company.Code,
        company.Name,
        company.TradeName,
        company.TaxOffice,
        company.TaxNumber,
        company.MersisNumber,
        company.TradeRegistryNumber,
        company.Phone,
        company.Email,
        company.Website,
        company.Address,
        logoUrl = string.IsNullOrWhiteSpace(company.LogoPath)
            ? null
            : "/api/backend/company-settings/logo",
        bankAccounts = company.BankAccounts
            .Where(x => !x.IsDeleted)
            .Select(x => new
            {
                x.Id,
                x.BankName,
                x.Iban,
                x.AccountHolder,
                x.CurrencyCode
            })
    };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
