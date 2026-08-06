using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Engineering;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Poz kitabı toplu içe aktarma: incele → eşle → önizle → aktar.
///
/// Akış bilinçli olarak DURUMSUZ: dosya her adımda yeniden gönderilir.
/// Sunucuda geçici dosya tutmak, yarım kalan aktarımlarda hangi
/// dosyanın hangi eşlemeyle bekletildiğini takip etme derdini getirirdi.
/// </summary>
[ApiController]
[Authorize]
[Route("api/engineering-positions/import")]
public sealed class PositionImportController(
    IPositionImportService importService,
    IBookImportService bookImportService) : ControllerBase
{
    private const long MaxFileSize = 60L * 1024 * 1024;

    /// <summary>Hazır eşleme profilleri (ÇŞB, TEDAŞ).</summary>
    [HttpGet("profiles")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringView)]
    public IActionResult GetProfiles() => Ok(bookImportService.GetProfiles());

    /// <summary>Profil ile önizleme — hiçbir şey yazmaz.</summary>
    [HttpPost("profile/preview")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringManage)]
    [RequestSizeLimit(MaxFileSize)]
    public Task<IActionResult> ProfilePreview(
        IFormFile file,
        [FromForm] string profileKey,
        [FromForm] Guid companyId,
        [FromForm] int year,
        [FromForm] string? sourceNote,
        [FromForm] string? codePrefix,
        CancellationToken cancellationToken)
        => RunProfileAsync(
            file, profileKey, companyId, year, sourceNote, codePrefix,
            write: false, cancellationToken);

    /// <summary>Profil ile gerçek aktarım.</summary>
    [HttpPost("profile/commit")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringManage)]
    [RequestSizeLimit(MaxFileSize)]
    public Task<IActionResult> ProfileCommit(
        IFormFile file,
        [FromForm] string profileKey,
        [FromForm] Guid companyId,
        [FromForm] int year,
        [FromForm] string? sourceNote,
        [FromForm] string? codePrefix,
        CancellationToken cancellationToken)
        => RunProfileAsync(
            file, profileKey, companyId, year, sourceNote, codePrefix,
            write: true, cancellationToken);

    private async Task<IActionResult> RunProfileAsync(
        IFormFile file,
        string profileKey,
        Guid companyId,
        int year,
        string? sourceNote,
        string? codePrefix,
        bool write,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Dosya seçilmedi." });

        try
        {
            await using var stream = await CopyAsync(file, cancellationToken);

            var summary = write
                ? await bookImportService.ImportAsync(
                    profileKey, stream, companyId, year, sourceNote, codePrefix, cancellationToken)
                : await bookImportService.PreviewAsync(
                    profileKey, stream, companyId, year, sourceNote, codePrefix, cancellationToken);

            return Ok(summary);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception exception)
        {
            return BadRequest(new
            {
                message = $"Dosya işlenemedi. ({exception.GetType().Name}: {exception.Message})"
            });
        }
    }

    /// <summary>
    /// Dosyayı açar; sayfa adlarını, tahmini başlık satırını, sütun
    /// başlıklarını ve örnek satırları döner. Hiçbir şey yazmaz.
    /// </summary>
    [HttpPost("inspect")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringManage)]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<IActionResult> Inspect(
        IFormFile file,
        [FromQuery] string? sheetName,
        [FromQuery] int? headerRow,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Dosya seçilmedi." });

        try
        {
            await using var stream = await CopyAsync(file, cancellationToken);

            return Ok(PositionImportParser.Inspect(stream, sheetName, headerRow));
        }
        catch (Exception exception)
        {
            return BadRequest(new
            {
                message = "Dosya okunamadı. Excel (.xlsx) dosyası olduğundan ve " +
                          $"şifreli olmadığından emin olun. ({exception.GetType().Name})"
            });
        }
    }

    /// <summary>
    /// Eşlemeye göre satırları ayrıştırır ve ne olacağını gösterir:
    /// kaç yeni poz, kaç fiyat güncellemesi, hangi tanımlar değişecek,
    /// hangi satırlar neden atlanacak. Yazma yapmaz.
    /// </summary>
    [HttpPost("preview")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringManage)]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<IActionResult> Preview(
        IFormFile file,
        [FromForm] string mapping,
        [FromForm] string options,
        CancellationToken cancellationToken)
    {
        var request = Deserialize(mapping, options);
        if (request.Error is not null)
            return BadRequest(new { message = request.Error });

        try
        {
            await using var stream = await CopyAsync(file, cancellationToken);
            var parsed = PositionImportParser.Parse(stream, request.Mapping!);

            return Ok(await importService.PreviewAsync(
                parsed, request.Options!, cancellationToken));
        }
        catch (Exception exception)
        {
            return BadRequest(new
            {
                message = $"Dosya işlenemedi. ({exception.GetType().Name})"
            });
        }
    }

    /// <summary>
    /// Önizlemesi görülen dosyayı yazar. Hatalı satırlar atlanır, geçerli
    /// olanlar aktarılır — tek bozuk satır tüm kitabı reddettirmez.
    /// </summary>
    [HttpPost("commit")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringManage)]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<IActionResult> Commit(
        IFormFile file,
        [FromForm] string mapping,
        [FromForm] string options,
        CancellationToken cancellationToken)
    {
        var request = Deserialize(mapping, options);
        if (request.Error is not null)
            return BadRequest(new { message = request.Error });

        await using var stream = await CopyAsync(file, cancellationToken);
        var parsed = PositionImportParser.Parse(stream, request.Mapping!);

        if (parsed.Rows.All(x => !x.IsValid))
        {
            return BadRequest(new
            {
                message = "Aktarılabilecek geçerli satır yok. " +
                          "Sütun eşlemesini ve başlık satırını kontrol edin."
            });
        }

        return Ok(await importService.CommitAsync(
            parsed, request.Options!, cancellationToken));
    }

    private static async Task<Stream> CopyAsync(
        IFormFile file, CancellationToken cancellationToken)
    {
        // ClosedXML akışta ileri geri konumlanıyor; IFormFile akışı her
        // zaman aranabilir olmadığı için belleğe kopyalanıyor.
        var memory = new MemoryStream();
        await file.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;

        return memory;
    }

    private static (PositionImportMapping? Mapping, PositionImportOptions? Options, string? Error)
        Deserialize(string mappingJson, string optionsJson)
    {
        PositionImportMapping? mapping;
        ImportOptionsPayload? payload;

        try
        {
            mapping = System.Text.Json.JsonSerializer.Deserialize<PositionImportMapping>(
                mappingJson,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            payload = System.Text.Json.JsonSerializer.Deserialize<ImportOptionsPayload>(
                optionsJson,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }
        catch (System.Text.Json.JsonException)
        {
            return (null, null, "Eşleme veya seçenek bilgisi okunamadı.");
        }

        if (mapping is null || payload is null)
            return (null, null, "Eşleme ve seçenek bilgisi zorunludur.");

        if (mapping.CodeColumn <= 0 || mapping.NameColumn <= 0
            || mapping.UnitColumn <= 0 || mapping.PriceColumn <= 0)
        {
            return (null, null,
                "Poz numarası, tanım, birim ve fiyat sütunlarının hepsi eşlenmelidir.");
        }

        if (payload.Year is < 2000 or > 2100)
            return (null, null, "Fiyat yılı 2000-2100 aralığında olmalıdır.");

        if (!Enum.IsDefined(typeof(PositionPriceInstitution), payload.Institution))
            return (null, null, "Geçersiz kurum.");

        if (!Enum.IsDefined(typeof(EngineeringPositionDiscipline), payload.Discipline))
            return (null, null, "Geçersiz disiplin.");

        return (
            mapping,
            new PositionImportOptions(
                payload.CompanyId,
                payload.Year,
                (PositionPriceInstitution)payload.Institution,
                (EngineeringPositionDiscipline)payload.Discipline,
                string.IsNullOrWhiteSpace(payload.SourceNote)
                    ? null
                    : payload.SourceNote.Trim()),
            null);
    }

    private sealed record ImportOptionsPayload(
        Guid CompanyId,
        int Year,
        int Institution,
        int Discipline,
        string? SourceNote);
}
