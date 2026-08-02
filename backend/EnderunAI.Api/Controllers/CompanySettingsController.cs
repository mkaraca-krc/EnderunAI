using EnderunAI.Api.Contracts;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
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
    IUploadService uploadService) : ControllerBase
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
