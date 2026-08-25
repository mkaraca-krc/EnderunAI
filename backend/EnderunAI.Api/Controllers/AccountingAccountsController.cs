using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EnderunAI.Api.Contracts.Core;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/accounting-accounts")]
public sealed class AccountingAccountsController(
    IAccountingAccountService service,
    IHesapPlaniAktarimService aktarim)
    : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.AccountingView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? parentAccountId,
        [FromQuery] bool? isActive,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        return Ok(await service.GetAllAsync(
            companyId,
            parentAccountId,
            isActive,
            search,
            cancellationToken));
    }

    /// <summary>
    /// ARANABİLİR SEÇİCİNİN UCU — sınırlı satır + toplam eşleşme.
    ///
    /// Hesap planı canlıda 1.114 satır; tamamını her ekran açılışında
    /// indirmek yerine yazdıkça buradan aranıyor. Toplam sayı da
    /// dönüyor ki ekran "kaç kayıt daha var" derken tahmin yürütmesin.
    /// </summary>
    [HttpGet("arama")]
    [RequirePermission(PermissionCatalog.Keys.AccountingView)]
    public async Task<IActionResult> Search(
        [FromQuery] Guid? companyId,
        [FromQuery] bool? isActive,
        [FromQuery] string? search,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        // ŞEKİL AÇIKÇA YAZILIYOR: uç, kırpılmış liste sözleşmesini
        // (kayıtlar + TOPLAM + daha var mı) döndürdüğünü kendi
        // imzasında söylüyor. Yalnız `Ok(await ...)` yazılsaydı sözleşme
        // servise gömülü kalır, uca bakan kişi kırpma olduğunu
        // göremezdi — sözleşme testi de bunu yakaladı.
        PagedResult<AccountingAccountListItemResponse> sonuc =
            await service.SearchAsync(
                companyId, isActive, search, limit, cancellationToken);

        return Ok(sonuc);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.AccountingView)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.GetByIdAsync(id, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.AccountingCreate)]
    public async Task<IActionResult> Create(
        [FromBody] CreateAccountingAccountRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.CreateAsync(
                request,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new { id = result.Id },
                result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    /// <summary>
    /// HESAP PLANI AKTARIMI — EKLER YA DA ATLAR, ASLA DEĞİŞTİRMEZ.
    ///
    /// Ayrı `chart.import` anahtarı istiyor: tek hesap açmak
    /// (`accounting.create`) ile dosyadan toplu hesap üretmek aynı iş
    /// değil. Sonuç raporu üç listeyle dönüyor — eklenen sayısı,
    /// atlanan mevcut kodlar, hata veren satırlar — böylece "sessizce
    /// bir şey oldu" hâli kalmıyor.
    /// </summary>
    [HttpPost("import")]
    [RequirePermission(PermissionCatalog.Keys.ChartImport)]
    public async Task<IActionResult> Import(
        [FromForm] Guid companyId,
        [FromForm] bool preview,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Dosya seçilmedi." });

        try
        {
            await using var akis = file.OpenReadStream();
            var sonuc = await aktarim.AktarAsync(companyId, akis, preview, cancellationToken);
            return Ok(sonuc);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }


    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.AccountingEdit)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateAccountingAccountRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.UpdateAsync(
                id,
                request,
                cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission(PermissionCatalog.Keys.AccountingDelete)]
    public async Task<IActionResult> Deactivate(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            await service.DeactivateAsync(id, cancellationToken);
            return Ok(new { message = "Muhasebe hesabı pasife alındı." });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
