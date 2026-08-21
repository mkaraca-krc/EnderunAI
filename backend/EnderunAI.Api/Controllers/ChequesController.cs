using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Çift yönlü çek defteri: alınan çekler (işverenden) ve verilen
/// çekler (tedarikçiye). Her durum geçişi hareket geçmişine yazılır ve
/// muhasebe etkisi olan geçişler dengeli fiş üretir.
/// </summary>
[ApiController]
[Authorize]
[Route("api/cheques")]
public sealed class ChequesController(
    IChequeService service,
    ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] int? direction,
        [FromQuery] int? status,
        [FromQuery] Guid? currentAccountId,
        [FromQuery] Guid? projectId,
        /// <summary>Merkez (ya da proje dışı masraf merkezi) süzgeci.</summary>
        [FromQuery] string? costCenterCode,
        [FromQuery] string? search,
        /// <summary>İptal edilen çekler varsayılan olarak gelmez.</summary>
        [FromQuery] bool includeVoided,
        CancellationToken cancellationToken)
    {
        return Ok(await service.GetAllAsync(
            companyId, direction, status, currentAccountId, projectId,
            costCenterCode, search,
            includeVoided,
            cancellationToken));
    }

    [HttpGet("summary")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] Guid? companyId, CancellationToken cancellationToken)
    {
        return Ok(await service.GetSummaryAsync(companyId, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.FinanceView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
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
    [RequirePermission(PermissionCatalog.Keys.FinanceCreate)]
    public async Task<IActionResult> Create(
        CreateChequeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.CreateAsync(request, cancellationToken));
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

    /*
     * ÇEK DÜZENLEME AYRI YETKİDE (F-çek/2).
     *
     * `finance.edit` yetmiyor: düzenleme geçmişe dönük bir düzeltme ve
     * tutar/vade değişirse muhasebe fişi yeniden kesiliyor. Bu, sıradan
     * bir finans kaydı güncellemesinden farklı bir güven seviyesi
     * istiyor — varsayılan olarak yalnız GM ve Finans Sorumlusu'nda
     * açık.
     */
    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.ChequeEdit)]
    public async Task<IActionResult> Update(
        Guid id, UpdateChequeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.UpdateAsync(
                id, request, currentUser.UserId, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
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
    /// Son durum değişikliğini geri alır — yanlış işaretlenen "Ödendi"
    /// için.
    ///
    /// YETKİ FinanceApprove: geri alma banka bakiyesini ve muhasebe
    /// defterini değiştiriyor; durum değiştirmeye yeten yetki (edit)
    /// bunun için yeterli değil.
    /// </summary>
    [HttpPost("{id:guid}/durum-geri-al")]
    [RequirePermission(PermissionCatalog.Keys.FinanceApprove)]
    public async Task<IActionResult> ReverseStatus(
        Guid id, ChequeReversalRequest request, CancellationToken cancellationToken)
    {
        try
        {
            /*
             * KAPANMIŞ DURUMDAN GERİ ALMA AYRI YETKİDE — iptaldeki
             * ayrımın aynısı. Tahsil edilmiş bir çeki geri almak da
             * gerçekleşmiş bir para hareketini storno ediyor; aynı mali
             * etkinin daha düşük bir yetkiyle üretilebilmesi bir açıktı.
             */
            return Ok(await service.ReverseLastMovementAsync(
                id, request, currentUser.UserId,
                currentUser.HasPermission(PermissionCatalog.Keys.ChequeVoidClosed),
                cancellationToken));
        }
        catch (UnauthorizedAccessException exception)
        {
            // Yetki eksikliği 403 — "beklenmeyen hata" değil.
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
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
    /// Çeki iptale çeker ve ürettiği bütün mali etkileri geri alır.
    /// Silme değil: mali kayıt olduğu için geçmiş defterde kalıyor.
    /// </summary>
    [HttpPost("{id:guid}/iptal")]
    [RequirePermission(PermissionCatalog.Keys.FinanceApprove)]
    public async Task<IActionResult> Void(
        Guid id, ChequeReversalRequest request, CancellationToken cancellationToken)
    {
        try
        {
            /*
             * KAPANMIŞ ÇEK İPTALİ AYRI YETKİDE.
             *
             * Uç `finance.approve` ile açık kalıyor — portföydeki çeki
             * iptal etmek sıradan bir finans işlemi. Ama tahsil edilmiş
             * ya da ödenmiş bir çeki iptal etmek gerçekleşmiş bir para
             * hareketini geri alır ve numarayı yeniden kullanıma açar;
             * onun kapısı `cheque.void-closed`.
             *
             * KARAR SERVİSTE VERİLİYOR, burada değil: hangi durumun
             * "kapanmış" sayıldığını tek yer bilmeli.
             */
            return Ok(await service.VoidAsync(
                id, request, currentUser.UserId,
                currentUser.HasPermission(PermissionCatalog.Keys.ChequeVoidClosed),
                cancellationToken));
        }
        catch (UnauthorizedAccessException exception)
        {
            /*
             * YETKİ EKSİKLİĞİ 403, 500 DEĞİL.
             *
             * Kapanmış çek iptali ayrı yetki istiyor ve servis bunu
             * `UnauthorizedAccessException` ile söylüyor. Haritalanmasaydı
             * kullanıcı "beklenmeyen hata" görürdü — oysa olan şey
             * sıradan ve anlatılabilir: bu işlem için yetkisi yok.
             */
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
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
    /// Çekin proje/masraf merkezi dağılımını baştan yazar. Boş liste
    /// dağılımı kaldırır ve çek tek parça işlenmeye döner.
    /// </summary>
    [HttpPut("{id:guid}/allocations")]
    [RequirePermission(PermissionCatalog.Keys.FinanceEdit)]
    public async Task<IActionResult> ReplaceAllocations(
        Guid id, ChequeAllocationsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.ReplaceAllocationsAsync(id, request, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
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
    /// Çek erteleme/değişim. Eski çek "Ertelendi" olur, yerine aynı
    /// tutarda yeni vadeli çek açılır ve zincire bağlanır.
    /// </summary>
    [HttpPost("{id:guid}/replace")]
    [RequirePermission(PermissionCatalog.Keys.FinanceEdit)]
    public async Task<IActionResult> Replace(
        Guid id, ReplaceChequeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.ReplaceAsync(id, request, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
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

    [HttpPost("{id:guid}/status")]
    [RequirePermission(PermissionCatalog.Keys.FinanceEdit)]
    public async Task<IActionResult> ChangeStatus(
        Guid id, ChequeStatusChangeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.ChangeStatusAsync(id, request, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
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
}
