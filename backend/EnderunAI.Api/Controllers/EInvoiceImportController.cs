using EnderunAI.Api.Security;
using EnderunAI.Api.Services.EInvoice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// E-fatura (UBL-TR 2.1) içe aktarma. Yüklenen XML/ZIP okunur, yön
/// VKN'den belirlenir; gelen fatura alış, giden fatura satış tarafına
/// yönlendirilir. Önizleme kayıt yazmaz — ön muhasebe onaylamadan
/// hiçbir şey sisteme girmez.
/// </summary>
[ApiController]
[Authorize]
[Route("api/e-invoice/import")]
public sealed class EInvoiceImportController(
    IEInvoiceImportService service) : ControllerBase
{
    /// <summary>Tek dosyada izin verilen boyut (25 MB).</summary>
    private const long MaxFileSize = 25L * 1024 * 1024;

    private static readonly string[] AllowedExtensions = [".xml", ".zip"];

    [HttpPost("preview")]
    [RequirePermission(PermissionCatalog.Keys.AccountingCreate)]
    [RequestSizeLimit(120L * 1024 * 1024)]
    public async Task<IActionResult> Preview(
        [FromQuery] Guid companyId,
        [FromForm] IFormFileCollection files,
        CancellationToken cancellationToken)
    {
        if (files is null || files.Count == 0)
            return BadRequest(new { message = "Yüklenecek dosya seçilmedi." });

        var streams = new List<(string, Stream)>();

        try
        {
            foreach (var file in files)
            {
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

                if (!AllowedExtensions.Contains(extension))
                {
                    return BadRequest(new
                    {
                        message = $"'{file.FileName}' desteklenmiyor. Yalnızca XML veya ZIP yükleyin."
                    });
                }

                if (file.Length > MaxFileSize)
                {
                    return BadRequest(new
                    {
                        message = $"'{file.FileName}' 25 MB sınırını aşıyor."
                    });
                }

                // ZIP okuma akışta ileri-geri konumlanır; bellek akışına alınır.
                var buffer = new MemoryStream();
                await file.CopyToAsync(buffer, cancellationToken);
                buffer.Position = 0;

                streams.Add((file.FileName, buffer));
            }

            return Ok(await service.PreviewAsync(companyId, streams, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        finally
        {
            foreach (var (_, stream) in streams)
                await stream.DisposeAsync();
        }
    }

    [HttpPost("commit")]
    [RequirePermission(PermissionCatalog.Keys.AccountingCreate)]
    public async Task<IActionResult> Commit(
        [FromQuery] Guid companyId,
        [FromBody] ImportCommitRequest request,
        CancellationToken cancellationToken)
    {
        if (request?.Items is null || request.Items.Count == 0)
            return BadRequest(new { message = "İçe aktarılacak fatura seçilmedi." });

        try
        {
            return Ok(await service.CommitAsync(companyId, request, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
