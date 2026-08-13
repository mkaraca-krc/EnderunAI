using System.Text.Json;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Engineering;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Reçete toplu içe aktarma: incele → eşle → önizle → aktar.
///
/// Poz kitabı aktarımıyla aynı desen ve aynı gerekçe: akış DURUMSUZ,
/// dosya her adımda yeniden gönderilir. Sunucuda geçici dosya tutmak,
/// yarım kalan aktarımlarda hangi dosyanın hangi eşlemeyle beklediğini
/// takip etme derdini getirirdi.
///
/// İNCELEME UCU YOK: sayfa/başlık/örnek satır incelemesi zaten
/// <c>POST /api/position-import/inspect</c> içinde ve dosya biçimi
/// aynı. İkinci bir uç açmak aynı işi iki yerde tutardı.
/// </summary>
[ApiController]
[Route("api/recipe-import")]
[Authorize]
public sealed class RecipeImportController(
    IRecipeImportService importService) : ControllerBase
{
    private const long MaxFileSize = 20L * 1024 * 1024;

    /// <summary>Ne olacağını gösterir; hiçbir şey yazmaz.</summary>
    [HttpPost("preview")]
    [RequirePermission(PermissionCatalog.Keys.EngineeringManage)]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<IActionResult> Preview(
        IFormFile file,
        [FromForm] string mapping,
        [FromForm] string options,
        CancellationToken cancellationToken)
    {
        var (parsedMapping, parsedOptions, error) = Deserialize(mapping, options);

        if (error is not null)
            return BadRequest(new { message = error });

        var (parsed, readError) = await ReadAsync(file, parsedMapping!, cancellationToken);

        if (readError is not null)
            return BadRequest(new { message = readError });

        return Ok(await importService.PreviewAsync(
            parsed!, parsedOptions!, cancellationToken));
    }

    /// <summary>
    /// Önizlemesi görülen dosyayı yazar. Hatalı satırlar atlanır,
    /// geçerli olanlar aktarılır — tek bozuk satır tüm dosyayı
    /// reddettirmez; atlananlar sonuçta sayıyla bildirilir.
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
        var (parsedMapping, parsedOptions, error) = Deserialize(mapping, options);

        if (error is not null)
            return BadRequest(new { message = error });

        var (parsed, readError) = await ReadAsync(file, parsedMapping!, cancellationToken);

        if (readError is not null)
            return BadRequest(new { message = readError });

        return Ok(await importService.CommitAsync(
            parsed!, parsedOptions!, cancellationToken));
    }

    private static async Task<(RecipeImportParseResult? Parsed, string? Error)> ReadAsync(
        IFormFile file,
        RecipeImportMapping mapping,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return (null, "Dosya seçilmedi.");

        try
        {
            // ClosedXML akışta ileri geri konumlanıyor; IFormFile akışı
            // her zaman aranabilir olmadığı için belleğe kopyalanıyor.
            await using var memory = new MemoryStream();
            await file.CopyToAsync(memory, cancellationToken);
            memory.Position = 0;

            return (RecipeImportParser.Parse(memory, mapping), null);
        }
        catch (Exception exception)
        {
            return (null,
                "Dosya okunamadı. Excel (.xlsx) olduğundan ve şifreli " +
                $"olmadığından emin olun. ({exception.GetType().Name})");
        }
    }

    private static (RecipeImportMapping? Mapping, RecipeImportOptions? Options, string? Error)
        Deserialize(string mappingJson, string optionsJson)
    {
        RecipeImportMapping? mapping;
        OptionsPayload? payload;

        try
        {
            var serializerOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            mapping = JsonSerializer.Deserialize<RecipeImportMapping>(
                mappingJson, serializerOptions);

            payload = JsonSerializer.Deserialize<OptionsPayload>(
                optionsJson, serializerOptions);
        }
        catch (JsonException)
        {
            return (null, null, "Eşleme veya seçenek bilgisi okunamadı.");
        }

        if (mapping is null || payload is null)
            return (null, null, "Eşleme ve seçenek bilgisi zorunludur.");

        if (mapping.PositionCodeColumn <= 0 ||
            mapping.MaterialNameColumn <= 0 ||
            mapping.QuantityColumn <= 0 ||
            mapping.UnitColumn <= 0)
        {
            return (null, null,
                "Poz kodu, malzeme adı, miktar ve birim sütunlarının hepsi eşlenmelidir.");
        }

        if (payload.CompanyId == Guid.Empty)
            return (null, null, "Şirket seçilmelidir.");

        // Kart açılacaksa kod sütunu zorunlu: koda göre benzersizlik
        // kuralı var ve kodu olmayan kart sonradan kimse tarafından
        // bulunamaz.
        if (payload.CreateMissingInventoryItems && mapping.MaterialCodeColumn is null or <= 0)
        {
            return (null, null,
                "Stok kartı açma seçiliyken malzeme kodu sütunu da eşlenmelidir.");
        }

        return (
            mapping,
            new RecipeImportOptions(
                payload.CompanyId,
                payload.CreateMissingInventoryItems),
            null);
    }

    private sealed record OptionsPayload(
        Guid CompanyId,
        bool CreateMissingInventoryItems);
}
