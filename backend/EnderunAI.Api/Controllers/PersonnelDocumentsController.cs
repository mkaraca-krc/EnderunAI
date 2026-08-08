using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Isg;
using EnderunAI.Api.Services.Upload;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Personel özlük belgeleri.
///
/// Dosyalar <see cref="IUploadService"/> ile saklanıyor — şantiye
/// fotoğrafları ve İSG belgeleriyle aynı depo; ikinci bir yükleme
/// mekanizması kurulmadı. Geçerlilik durumu da
/// <see cref="IsgValidityCalculator"/>'dan geliyor: aynı eşikler, aynı
/// renkler.
///
/// GİZLİLİK: kimlik fotokopisi ve adli sicil gibi belgeler taşıdığı
/// için personnel_document.* dar anahtarıyla korunuyor.
/// personnel.view sahada da var (Şantiye Şefi, Formen) ve bu belgeler
/// oradan görünmemeli.
/// </summary>
[ApiController]
[Authorize]
[Route("api/hr/personel-belgeleri")]
public sealed class PersonnelDocumentsController(
    AppDbContext db,
    IUploadService uploadService) : ControllerBase
{
    private const string Category = "ozluk-belgeleri";

    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.PersonnelDocumentView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? personnelId,
        [FromQuery] int? documentType,
        [FromQuery] bool? expiringOnly,
        CancellationToken cancellationToken)
    {
        var query = db.PersonnelDocuments.AsNoTracking();

        if (companyId is Guid cid) query = query.Where(x => x.CompanyId == cid);
        if (personnelId is Guid pid) query = query.Where(x => x.PersonnelId == pid);

        if (documentType is int type)
        {
            if (!Enum.IsDefined(typeof(PersonnelDocumentType), type))
                return BadRequest(new { message = "Geçersiz belge türü." });

            query = query.Where(x => (int)x.DocumentType == type);
        }

        var rows = await query
            .OrderByDescending(x => x.IssueDate)
            .Select(x => new
            {
                x.Id,
                x.PersonnelId,
                PersonnelName = x.Personnel.FirstName + " " + x.Personnel.LastName,
                x.Personnel.EmployeeNumber,
                DocumentType = (int)x.DocumentType,
                Title = x.DocumentName,
                x.DocumentNumber,
                x.IssueDate,
                x.ExpiryDate,
                x.IssuingInstitution,
                x.IsMandatory,
                x.IsVerified,
                x.OriginalName,
                x.ContentType,
                x.FileSize,
                x.Notes,
                x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var items = rows.Select(x =>
        {
            var expiry = x.ExpiryDate is DateTime e
                ? DateOnly.FromDateTime(e)
                : (DateOnly?)null;

            var status = IsgValidityCalculator.Evaluate(expiry, today);

            return new
            {
                x.Id,
                x.PersonnelId,
                x.PersonnelName,
                x.EmployeeNumber,
                x.DocumentType,
                DocumentTypeName = TypeName((PersonnelDocumentType)x.DocumentType),
                x.Title,
                x.DocumentNumber,
                x.IssueDate,
                x.ExpiryDate,
                x.IssuingInstitution,
                x.IsMandatory,
                x.IsVerified,
                Status = (int)status,
                StatusName = IsgValidityCalculator.StatusName(status),
                StatusColor = IsgValidityCalculator.StatusColor(status),
                DaysRemaining = IsgValidityCalculator.DaysRemaining(expiry, today),
                x.OriginalName,
                x.ContentType,
                x.FileSize,
                x.Notes,
                x.CreatedAtUtc
            };
        });

        if (expiringOnly == true)
        {
            // Süresiz belgeler (NoExpiry) uyarı listesine girmez.
            items = items.Where(x =>
                x.Status == (int)IsgValidityStatus.ExpiringSoon ||
                x.Status == (int)IsgValidityStatus.Expired);
        }

        var list = items.ToList();

        return Ok(new
        {
            count = list.Count,
            expiringCount = list.Count(x =>
                x.Status == (int)IsgValidityStatus.ExpiringSoon),
            expiredCount = list.Count(x =>
                x.Status == (int)IsgValidityStatus.Expired),
            items = list
        });
    }

    /// <summary>Belgeyi indirir.</summary>
    [HttpGet("{id:guid}/indir")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelDocumentView)]
    public async Task<IActionResult> Download(
        Guid id, CancellationToken cancellationToken)
    {
        var document = await db.PersonnelDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (document is null)
            return NotFound(new { message = "Belge bulunamadı." });

        var file = document.FilePath is null
            ? null
            : uploadService.GetFile(Category, document.FilePath);

        if (file is null)
        {
            return NotFound(new
            {
                message = "Belgenin dosyası depoda bulunamadı."
            });
        }

        return PhysicalFile(
            file.FullPath,
            document.ContentType ?? file.ContentType,
            document.OriginalName ?? document.DocumentName);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.PersonnelDocumentManage)]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        [FromForm] Guid personnelId,
        [FromForm] int documentType,
        [FromForm] string title,
        [FromForm] DateTime? issueDate,
        [FromForm] DateTime? expiryDate,
        [FromForm] string? documentNumber,
        [FromForm] string? issuingInstitution,
        [FromForm] bool isMandatory,
        [FromForm] string? notes,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(typeof(PersonnelDocumentType), documentType))
            return BadRequest(new { message = "Geçersiz belge türü." });

        if (string.IsNullOrWhiteSpace(title))
            return BadRequest(new { message = "Belge başlığı zorunludur." });

        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Dosya seçilmedi." });

        if (issueDate is DateTime issued && expiryDate is DateTime expires &&
            expires < issued)
        {
            return BadRequest(new
            {
                message = "Geçerlilik bitişi düzenlenme tarihinden önce olamaz."
            });
        }

        var personnel = await db.Personnel
            .AsNoTracking()
            .Where(x => x.Id == personnelId)
            .Select(x => new { x.Id, x.CompanyId })
            .SingleOrDefaultAsync(cancellationToken);

        if (personnel is null)
            return BadRequest(new { message = "Personel bulunamadı." });

        var saved = await uploadService.SaveAsync(file, Category, cancellationToken);

        var document = new PersonnelDocument
        {
            CompanyId = personnel.CompanyId,
            PersonnelId = personnelId,
            DocumentType = (PersonnelDocumentType)documentType,
            DocumentName = title.Trim(),
            DocumentNumber = documentNumber?.Trim(),
            IssueDate = ToUtc(issueDate),
            ExpiryDate = ToUtc(expiryDate),
            IssuingInstitution = issuingInstitution?.Trim(),
            IsMandatory = isMandatory,
            FilePath = saved.StoredName,
            OriginalName = saved.OriginalName,
            ContentType = saved.ContentType,
            FileSize = saved.Size,
            Notes = notes?.Trim()
        };

        db.PersonnelDocuments.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { id = document.Id, message = "Belge yüklendi." });
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelDocumentManage)]
    public async Task<IActionResult> Delete(
        Guid id, CancellationToken cancellationToken)
    {
        var document = await db.PersonnelDocuments
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (document is null)
            return NotFound(new { message = "Belge bulunamadı." });

        // Kayıt yumuşak siliniyor; dosya da depodan kaldırılıyor.
        // Özlük belgesi kişisel veridir, silme talebinde dosyanın
        // diskte kalması doğru olmaz.
        if (document.FilePath is not null)
            uploadService.DeleteFile(Category, document.FilePath);

        db.PersonnelDocuments.Remove(document);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Belge silindi." });
    }

    /// <summary>Belge türlerinin listesi — ekran seçim kutusu için.</summary>
    [HttpGet("turler")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelDocumentView)]
    public IActionResult Types() =>
        Ok(Enum.GetValues<PersonnelDocumentType>()
            .Select(x => new { value = (int)x, name = TypeName(x) }));

    private static DateTime? ToUtc(DateTime? value) =>
        value is DateTime date
            ? DateTime.SpecifyKind(date.Date, DateTimeKind.Utc)
            : null;

    private static string TypeName(PersonnelDocumentType type) => type switch
    {
        PersonnelDocumentType.EmploymentContract => "İş sözleşmesi",
        PersonnelDocumentType.IdentityCopy => "Kimlik fotokopisi",
        PersonnelDocumentType.Diploma => "Diploma / öğrenim belgesi",
        PersonnelDocumentType.DriverLicense => "Ehliyet",
        PersonnelDocumentType.CriminalRecord => "Adli sicil kaydı",
        PersonnelDocumentType.ResidenceCertificate => "İkametgâh belgesi",
        PersonnelDocumentType.MilitaryStatus => "Askerlik durum belgesi",
        PersonnelDocumentType.Photograph => "Fotoğraf",
        PersonnelDocumentType.BankAccount => "Banka hesap bilgisi",
        PersonnelDocumentType.SgkEntryNotice => "SGK işe giriş bildirgesi",
        PersonnelDocumentType.SgkExitNotice => "SGK işten çıkış bildirgesi",
        _ => "Diğer"
    };
}
