using EnderunAI.Api.Contracts.Isg;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.Upload;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Isg;

public interface IIsgSiteDocumentService
{
    Task<IReadOnlyCollection<IsgSiteDocumentListItem>> GetAllAsync(
        Guid? companyId, Guid? projectId, Guid? projectSiteId, int? documentType,
        CancellationToken cancellationToken);

    Task<IsgSiteDocumentListItem> UploadAsync(
        Guid companyId, Guid projectId, Guid? projectSiteId,
        int documentType, string title, DateOnly issueDate, DateOnly? validUntil,
        string? notes, IFormFile file, CancellationToken cancellationToken);

    Task<IsgSiteDocumentListItem> UpdateAsync(
        Guid id, UpdateIsgSiteDocumentRequest request, CancellationToken cancellationToken);

    Task<FileDownloadResult> GetFileAsync(Guid id, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// Şantiye İSG belgeleri: risk değerlendirmesi, acil durum planı, kurul
/// tutanağı, denetim formu, KKD zimmet formu.
///
/// Dosyalar <see cref="IUploadService"/> ile saklanır (şantiye günlük
/// rapor fotoğraflarıyla aynı depo). Geçerlilik hesabı
/// <see cref="IsgValidityCalculator"/>'dan gelir — personel kayıtlarıyla
/// aynı eşik ve aynı kural.
/// </summary>
public sealed class IsgSiteDocumentService(
    AppDbContext db,
    IUploadService uploadService,
    ICurrentUserService currentUser) : IIsgSiteDocumentService
{
    private const string Category = "isg-belgeler";

    public async Task<IReadOnlyCollection<IsgSiteDocumentListItem>> GetAllAsync(
        Guid? companyId, Guid? projectId, Guid? projectSiteId, int? documentType,
        CancellationToken cancellationToken)
    {
        var query = db.IsgSiteDocuments.AsNoTracking();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);
        if (projectId.HasValue)
            query = query.Where(x => x.ProjectId == projectId.Value);
        if (projectSiteId.HasValue)
            query = query.Where(x => x.ProjectSiteId == projectSiteId.Value);
        if (documentType.HasValue)
            query = query.Where(x => (int)x.DocumentType == documentType.Value);

        var rows = await query
            .Include(x => x.Project)
            .Include(x => x.ProjectSite)
            .OrderByDescending(x => x.IssueDate)
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return rows.Select(x => Map(x, today)).ToList();
    }

    public async Task<IsgSiteDocumentListItem> UploadAsync(
        Guid companyId, Guid projectId, Guid? projectSiteId,
        int documentType, string title, DateOnly issueDate, DateOnly? validUntil,
        string? notes, IFormFile file, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(typeof(IsgSiteDocumentType), documentType))
            throw new ArgumentException("Geçersiz belge tipi.");

        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Belge başlığı zorunludur.");

        if (validUntil is DateOnly expiry && expiry < issueDate)
            throw new ArgumentException(
                "Geçerlilik bitişi düzenlenme tarihinden önce olamaz.");

        var projectExists = await db.Projects.AnyAsync(
            x => x.Id == projectId && x.CompanyId == companyId, cancellationToken);

        if (!projectExists)
            throw new ArgumentException("Proje bulunamadı.");

        if (projectSiteId is Guid site)
        {
            var siteExists = await db.ProjectSites.AnyAsync(
                x => x.Id == site && x.ProjectId == projectId, cancellationToken);

            if (!siteExists)
                throw new ArgumentException("Şantiye seçilen projeye ait değil.");
        }

        var uploaded = await uploadService.SaveAsync(file, Category, cancellationToken);

        var document = new IsgSiteDocument
        {
            CompanyId = companyId,
            ProjectId = projectId,
            ProjectSiteId = projectSiteId,
            DocumentType = (IsgSiteDocumentType)documentType,
            Title = title.Trim(),
            IssueDate = issueDate,
            ValidUntil = validUntil,
            StoredFileName = uploaded.StoredName,
            OriginalFileName = uploaded.OriginalName,
            ContentType = uploaded.ContentType,
            SizeBytes = uploaded.Size,
            UploadedByUserId = currentUser.UserId,
            Notes = Normalize(notes)
        };

        db.IsgSiteDocuments.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        return await LoadAsync(document.Id, cancellationToken);
    }

    public async Task<IsgSiteDocumentListItem> UpdateAsync(
        Guid id, UpdateIsgSiteDocumentRequest request, CancellationToken cancellationToken)
    {
        var document = await db.IsgSiteDocuments
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Belge bulunamadı.");

        if (!Enum.IsDefined(typeof(IsgSiteDocumentType), request.DocumentType))
            throw new ArgumentException("Geçersiz belge tipi.");

        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ArgumentException("Belge başlığı zorunludur.");

        if (request.ValidUntil is DateOnly expiry && expiry < request.IssueDate)
            throw new ArgumentException(
                "Geçerlilik bitişi düzenlenme tarihinden önce olamaz.");

        if (request.ProjectSiteId is Guid site)
        {
            var siteExists = await db.ProjectSites.AnyAsync(
                x => x.Id == site && x.ProjectId == document.ProjectId, cancellationToken);

            if (!siteExists)
                throw new ArgumentException("Şantiye belgenin projesine ait değil.");
        }

        document.DocumentType = (IsgSiteDocumentType)request.DocumentType;
        document.Title = request.Title.Trim();
        document.IssueDate = request.IssueDate;
        document.ValidUntil = request.ValidUntil;
        document.ProjectSiteId = request.ProjectSiteId;
        document.Notes = Normalize(request.Notes);
        document.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return await LoadAsync(document.Id, cancellationToken);
    }

    public async Task<FileDownloadResult> GetFileAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var document = await db.IsgSiteDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Belge bulunamadı.");

        return uploadService.GetFile(Category, document.StoredFileName)
            ?? throw new KeyNotFoundException("Belge dosyası bulunamadı.");
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await db.IsgSiteDocuments
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Belge bulunamadı.");

        // Kayıt soft-delete; dosya diskte kalır. Denetimde silinmiş bir
        // belgenin aslına ulaşmak gerekebilir.
        document.IsDeleted = true;
        document.DeletedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<IsgSiteDocumentListItem> LoadAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var document = await db.IsgSiteDocuments
            .AsNoTracking()
            .Include(x => x.Project)
            .Include(x => x.ProjectSite)
            .SingleAsync(x => x.Id == id, cancellationToken);

        return Map(document, DateOnly.FromDateTime(DateTime.UtcNow));
    }

    private static IsgSiteDocumentListItem Map(IsgSiteDocument x, DateOnly today)
    {
        var status = IsgValidityCalculator.Evaluate(x.ValidUntil, today);

        return new IsgSiteDocumentListItem(
            x.Id, x.ProjectId, x.Project.Code, x.Project.Name,
            x.ProjectSiteId, x.ProjectSite?.Name,
            (int)x.DocumentType, DocumentTypeName(x.DocumentType),
            x.Title, x.IssueDate, x.ValidUntil,
            status.ToString(),
            IsgValidityCalculator.StatusName(status),
            IsgValidityCalculator.StatusColor(status),
            IsgValidityCalculator.DaysRemaining(x.ValidUntil, today),
            x.OriginalFileName, x.SizeBytes, x.Notes, x.CreatedAtUtc);
    }

    private static string DocumentTypeName(IsgSiteDocumentType type) => type switch
    {
        IsgSiteDocumentType.RiskAssessment => "Risk değerlendirmesi",
        IsgSiteDocumentType.EmergencyPlan => "Acil durum planı",
        IsgSiteDocumentType.CommitteeMinutes => "İSG kurul tutanağı",
        IsgSiteDocumentType.SiteAudit => "Saha denetim formu",
        IsgSiteDocumentType.PpeHandover => "KKD zimmet formu",
        _ => "Diğer"
    };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
