using EnderunAI.Api.Security;
using EnderunAI.Api.Services.AI;
using EnderunAI.Api.Services.Upload;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Route("api/hakedis")]
[Authorize]
public sealed class HakedisController(
    IUploadService uploadService,
    IHakedisAnalysisService analysisService
) : ControllerBase
{
    [HttpPost("upload")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    [RequirePermission(PermissionCatalog.Keys.HakedisCreate)]
    public async Task<IActionResult> Upload(
        [FromForm] IFormFile file,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var uploaded =
                await uploadService.SaveAsync(
                    file,
                    "hakedis",
                    cancellationToken
                );

            var storedFile =
                uploadService.GetFile(
                    "hakedis",
                    uploaded.StoredName
                );

            if (storedFile is null)
            {
                return StatusCode(
                    StatusCodes
                        .Status500InternalServerError,
                    new
                    {
                        message =
                            "Dosya kaydedildi ancak analiz için bulunamadı.",
                    }
                );
            }

            var analysis =
                await analysisService.AnalyzeAsync(
                    storedFile.FullPath,
                    uploaded.OriginalName,
                    cancellationToken
                );

            return Ok(new
            {
                file = uploaded,
                analysis,
            });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new
            {
                message = exception.Message,
            });
        }
        catch (Exception exception)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    message =
                        "Hakediş analizi sırasında hata oluştu.",
                    detail = exception.Message,
                }
            );
        }
    }

    [HttpPost("files/{storedName}/analyze")]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> Analyze(
        string storedName,
        CancellationToken cancellationToken
    )
    {
        var storedFile = uploadService.GetFile(
            "hakedis",
            storedName
        );

        if (storedFile is null)
        {
            return NotFound(new
            {
                message = "Dosya bulunamadı.",
            });
        }

        try
        {
            var analysis =
                await analysisService.AnalyzeAsync(
                    storedFile.FullPath,
                    storedFile.StoredName,
                    cancellationToken
                );

            return Ok(analysis);
        }
        catch (Exception exception)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    message =
                        "Dosya analiz edilemedi.",
                    detail = exception.Message,
                }
            );
        }
    }

    [HttpGet("files")]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public IActionResult GetFiles()
    {
        return Ok(
            uploadService.GetFiles("hakedis")
        );
    }

    [HttpGet("files/{storedName}")]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public IActionResult Download(string storedName)
    {
        var file = uploadService.GetFile(
            "hakedis",
            storedName
        );

        if (file is null)
        {
            return NotFound(new
            {
                message = "Dosya bulunamadı.",
            });
        }

        var stream = new FileStream(
            file.FullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read
        );

        return File(
            stream,
            file.ContentType,
            file.StoredName,
            enableRangeProcessing: true
        );
    }

    [HttpDelete("files/{storedName}")]
    [RequirePermission(PermissionCatalog.Keys.HakedisDelete)]
    public IActionResult Delete(string storedName)
    {
        var deleted = uploadService.DeleteFile(
            "hakedis",
            storedName
        );

        if (!deleted)
        {
            return NotFound(new
            {
                message = "Dosya bulunamadı.",
            });
        }

        return Ok(new
        {
            message = "Dosya silindi.",
            storedName,
        });
    }
}