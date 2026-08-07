using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.Isg;
using EnderunAI.Api.Services.Upload;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Taşeron evrakları — geçerlilik takipli.
///
/// Geçerlilik hesabı İSG belgeleriyle AYNI motordan
/// (<see cref="IsgValidityCalculator"/>) geçiyor: kural iki yere
/// kopyalansaydı biri güncellenip diğeri unutulur ve "İSG'de 30 gün,
/// taşeronda 45 gün" gibi sessiz bir tutarsızlık doğardı.
///
/// SGK BORCU YOKTUR yazısı özel: kanunen üç ay geçerlidir ve kullanıcı
/// bitiş tarihi girmese bile bu kural uygulanır. Aksi halde belge
/// "süresiz" görünür ve asıl işveren müteselsil sorumluluk altında
/// kalır.
/// </summary>
[ApiController]
[Authorize]
[Route("api/subcontractor-documents")]
public sealed class SubcontractorDocumentsController(
    AppDbContext db,
    IUploadService uploadService,
    ICurrentUserService currentUser) : ControllerBase
{
    private const string UploadCategory = "taseron-evrak";

    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.SubcontractorView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? subcontractorContractId,
        [FromQuery] bool onlyProblems = false,
        CancellationToken cancellationToken = default)
    {
        var query = db.SubcontractorDocuments.AsNoTracking();

        if (subcontractorContractId is Guid contractId)
            query = query.Where(x => x.SubcontractorContractId == contractId);

        var items = await query
            .OrderBy(x => x.DocumentType)
            .ThenByDescending(x => x.IssueDate)
            .Select(x => new
            {
                x.Id,
                x.SubcontractorContractId,
                ContractNumber = x.SubcontractorContract.ContractNumber,
                SubcontractorTitle = x.SubcontractorContract.CurrentAccount.Title,
                DocumentType = (int)x.DocumentType,
                DocumentTypeName = TypeName(x.DocumentType),
                x.Title,
                x.IssueDate,
                x.ValidUntil,
                x.OriginalFileName,
                x.SizeBytes,
                x.Notes
            })
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Geçerlilik hesabı bellekte: EffectiveValidUntil bir hesaplanmış
        // özellik (SGK üç ay kuralı) ve SQL'e çevrilemez.
        var withStatus = items
            .Select(x =>
            {
                var effective = EffectiveValidUntil(
                    x.ValidUntil, (SubcontractorDocumentType)x.DocumentType, x.IssueDate);
                var status = IsgValidityCalculator.Evaluate(effective, today);

                return new
                {
                    x.Id,
                    x.SubcontractorContractId,
                    x.ContractNumber,
                    x.SubcontractorTitle,
                    x.DocumentType,
                    x.DocumentTypeName,
                    x.Title,
                    x.IssueDate,
                    x.ValidUntil,
                    EffectiveValidUntil = effective,
                    // Bitiş tarihi girilmemiş ama kanunen sınırlıysa
                    // kullanıcı bunun nereden geldiğini görmeli.
                    ValidUntilIsImplied =
                        x.ValidUntil is null && effective is not null,
                    Status = (int)status,
                    StatusName = IsgValidityCalculator.StatusName(status),
                    DaysRemaining = IsgValidityCalculator.DaysRemaining(effective, today),
                    x.OriginalFileName,
                    x.SizeBytes,
                    x.Notes
                };
            })
            .Where(x => !onlyProblems ||
                        x.Status == (int)IsgValidityStatus.Expired ||
                        x.Status == (int)IsgValidityStatus.ExpiringSoon)
            .OrderBy(x => x.Status == (int)IsgValidityStatus.Expired ? 0 : 1)
            .ThenBy(x => x.DaysRemaining ?? int.MaxValue)
            .ToList();

        return Ok(withStatus);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.SubcontractorManage)]
    [RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> Create(
        [FromForm] Guid subcontractorContractId,
        [FromForm] int documentType,
        [FromForm] string title,
        [FromForm] DateOnly issueDate,
        [FromForm] DateOnly? validUntil,
        [FromForm] string? notes,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Dosya seçilmedi." });

        if (string.IsNullOrWhiteSpace(title))
            return BadRequest(new { message = "Belge başlığı zorunludur." });

        if (!Enum.IsDefined(typeof(SubcontractorDocumentType), documentType))
            return BadRequest(new { message = "Geçersiz belge türü." });

        if (validUntil is DateOnly expiry && expiry < issueDate)
        {
            return BadRequest(new
            {
                message = "Geçerlilik bitişi düzenlenme tarihinden önce olamaz."
            });
        }

        var contract = await db.SubcontractorContracts
            .AsNoTracking()
            .Where(x => x.Id == subcontractorContractId)
            .Select(x => new { x.CompanyId })
            .SingleOrDefaultAsync(cancellationToken);

        if (contract is null)
            return BadRequest(new { message = "Taşeron sözleşmesi bulunamadı." });

        var stored = await uploadService.SaveAsync(
            file, UploadCategory, cancellationToken);

        var document = new SubcontractorDocument
        {
            CompanyId = contract.CompanyId,
            SubcontractorContractId = subcontractorContractId,
            DocumentType = (SubcontractorDocumentType)documentType,
            Title = title.Trim(),
            IssueDate = issueDate,
            ValidUntil = validUntil,
            StoredFileName = stored.StoredName,
            OriginalFileName = stored.OriginalName,
            ContentType = stored.ContentType,
            SizeBytes = stored.Size,
            UploadedByUserId = currentUser.UserId,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };

        db.SubcontractorDocuments.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        var effective = document.EffectiveValidUntil;

        return Ok(new
        {
            document.Id,
            EffectiveValidUntil = effective,
            message = document.ValidUntil is null && effective is not null
                ? "Evrak yüklendi. SGK borcu yoktur yazısı kanunen üç ay " +
                  "geçerlidir; geçerlilik bitişi buna göre belirlendi."
                : "Evrak yüklendi."
        });
    }

    [HttpGet("{id:guid}/dosya")]
    [RequirePermission(PermissionCatalog.Keys.SubcontractorView)]
    public async Task<IActionResult> Download(
        Guid id, CancellationToken cancellationToken)
    {
        var document = await db.SubcontractorDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (document is null)
            return NotFound(new { message = "Evrak bulunamadı." });

        var file = uploadService.GetFile(UploadCategory, document.StoredFileName);

        if (file is null)
            return NotFound(new { message = "Dosya diskte bulunamadı." });

        return PhysicalFile(
            file.FullPath, document.ContentType, document.OriginalFileName);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.SubcontractorManage)]
    public async Task<IActionResult> Delete(
        Guid id, CancellationToken cancellationToken)
    {
        var document = await db.SubcontractorDocuments
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (document is null)
            return NotFound(new { message = "Evrak bulunamadı." });

        document.IsDeleted = true;
        document.DeletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Evrak silindi." });
    }

    // ---------- Yardımcılar ----------

    /// <summary>
    /// Uygulanacak bitiş tarihi. SGK borcu yoktur yazısında bitiş
    /// girilmemişse üç aylık kanuni süre uygulanır.
    /// </summary>
    private static DateOnly? EffectiveValidUntil(
        DateOnly? validUntil, SubcontractorDocumentType type, DateOnly issueDate) =>
        validUntil ?? (type == SubcontractorDocumentType.SocialSecurityClearance
            ? issueDate.AddMonths(SubcontractorDocument.SocialSecurityClearanceMonths)
            : null);

    private static string TypeName(SubcontractorDocumentType type) => type switch
    {
        SubcontractorDocumentType.Contract => "Sözleşme",
        SubcontractorDocumentType.SignatureCircular => "İmza sirküleri",
        SubcontractorDocumentType.TaxCertificate => "Vergi levhası",
        SubcontractorDocumentType.SocialSecurityClearance => "SGK borcu yoktur",
        SubcontractorDocumentType.TaxClearance => "Vergi borcu yoktur",
        SubcontractorDocumentType.OccupationalSafety => "İSG evrakı",
        SubcontractorDocumentType.TradeRegistry => "Ticaret sicil gazetesi",
        SubcontractorDocumentType.InsurancePolicy => "Sigorta poliçesi",
        SubcontractorDocumentType.Other => "Diğer",
        _ => "Bilinmiyor"
    };
}
