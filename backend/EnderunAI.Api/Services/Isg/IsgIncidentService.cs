using EnderunAI.Api.Contracts.Isg;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Isg;

public interface IIsgIncidentService
{
    Task<IReadOnlyCollection<IsgIncidentListItem>> GetAllAsync(
        Guid? companyId, Guid? projectId, int? status, int? incidentType,
        CancellationToken cancellationToken);

    Task<IsgIncidentDetail> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IsgIncidentDetail> CreateAsync(
        CreateIsgIncidentRequest request, CancellationToken cancellationToken);

    Task<IsgIncidentDetail> UpdateAsync(
        Guid id, UpdateIsgIncidentRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// Kaza ve ramak kala kayıt defteri.
///
/// SGK bildirimi: iş kazası üç iş günü içinde bildirilir. Ramak kala
/// ve meslek hastalığı bu kurala girmez; bildirim gecikmesi yalnızca
/// gerçek kazalar için hesaplanır — aksi halde her ramak kala kaydı
/// sahte bir "bildirilmedi" uyarısı üretirdi.
/// </summary>
public sealed class IsgIncidentService(
    AppDbContext db,
    ICurrentUserService currentUser) : IIsgIncidentService
{
    /// <summary>Kazanın SGK'ya bildirilmesi için yasal süre (iş günü).</summary>
    private const int SgkNotificationDays = 3;

    public async Task<IReadOnlyCollection<IsgIncidentListItem>> GetAllAsync(
        Guid? companyId, Guid? projectId, int? status, int? incidentType,
        CancellationToken cancellationToken)
    {
        var query = db.IsgIncidents.AsNoTracking();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);
        if (projectId.HasValue)
            query = query.Where(x => x.ProjectId == projectId.Value);
        if (status.HasValue)
            query = query.Where(x => (int)x.Status == status.Value);
        if (incidentType.HasValue)
            query = query.Where(x => (int)x.IncidentType == incidentType.Value);

        var rows = await query
            .Include(x => x.Project)
            .Include(x => x.ProjectSite)
            .Include(x => x.Personnel)
            .OrderByDescending(x => x.IncidentDateTime)
            .ToListAsync(cancellationToken);

        return rows.Select(x => new IsgIncidentListItem(
            x.Id, x.IncidentDateTime,
            (int)x.IncidentType, IncidentTypeName(x.IncidentType),
            (int)x.Severity, SeverityName(x.Severity), SeverityColor(x.Severity),
            x.ProjectId, x.Project?.Code,
            x.ProjectSiteId, x.ProjectSite?.Name,
            x.PersonnelId, PersonnelName(x.Personnel),
            x.LostWorkDays, x.SgkNotified, IsNotificationOverdue(x),
            (int)x.Status, StatusName(x.Status))).ToList();
    }

    public async Task<IsgIncidentDetail> GetByIdAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var incident = await db.IsgIncidents
            .AsNoTracking()
            .Include(x => x.Project)
            .Include(x => x.ProjectSite)
            .Include(x => x.Personnel)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Kaza kaydı bulunamadı.");

        return MapDetail(incident);
    }

    public async Task<IsgIncidentDetail> CreateAsync(
        CreateIsgIncidentRequest request, CancellationToken cancellationToken)
    {
        Validate(request.IncidentType, request.Severity, request.Description,
            request.LostWorkDays, request.SgkNotified, request.SgkNotificationDate);

        await ValidateReferencesAsync(
            request.CompanyId, request.ProjectId, request.ProjectSiteId,
            request.PersonnelId, cancellationToken);

        var incident = new IsgIncident
        {
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            ProjectSiteId = request.ProjectSiteId,
            PersonnelId = request.PersonnelId,
            IncidentDateTime = AsUtc(request.IncidentDateTime),
            IncidentType = (IsgIncidentType)request.IncidentType,
            Severity = (IsgIncidentSeverity)request.Severity,
            Description = request.Description.Trim(),
            RootCause = Normalize(request.RootCause),
            ActionTaken = Normalize(request.ActionTaken),
            LostWorkDays = request.LostWorkDays,
            SgkNotified = request.SgkNotified,
            SgkNotificationDate = request.SgkNotificationDate.HasValue
                ? AsUtc(request.SgkNotificationDate.Value)
                : null,
            SgkNotificationNumber = Normalize(request.SgkNotificationNumber),
            ReportedByUserId = currentUser.UserId,
            Status = IsgIncidentStatus.Open
        };

        db.IsgIncidents.Add(incident);
        await db.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(incident.Id, cancellationToken);
    }

    public async Task<IsgIncidentDetail> UpdateAsync(
        Guid id, UpdateIsgIncidentRequest request, CancellationToken cancellationToken)
    {
        var incident = await db.IsgIncidents
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Kaza kaydı bulunamadı.");

        Validate(request.IncidentType, request.Severity, request.Description,
            request.LostWorkDays, request.SgkNotified, request.SgkNotificationDate);

        if (!Enum.IsDefined(typeof(IsgIncidentStatus), request.Status))
            throw new ArgumentException("Geçersiz kayıt durumu.");

        await ValidateReferencesAsync(
            incident.CompanyId, request.ProjectId, request.ProjectSiteId,
            request.PersonnelId, cancellationToken);

        var newStatus = (IsgIncidentStatus)request.Status;

        // Kapatma gerekçesiz olmasın: neden kapandığı denetimde sorulur.
        if (newStatus == IsgIncidentStatus.Closed &&
            string.IsNullOrWhiteSpace(request.ClosureNote) &&
            string.IsNullOrWhiteSpace(incident.ClosureNote))
        {
            throw new ArgumentException(
                "Kaydı kapatmak için kapanış açıklaması girilmelidir.");
        }

        incident.ProjectId = request.ProjectId;
        incident.ProjectSiteId = request.ProjectSiteId;
        incident.PersonnelId = request.PersonnelId;
        incident.IncidentDateTime = AsUtc(request.IncidentDateTime);
        incident.IncidentType = (IsgIncidentType)request.IncidentType;
        incident.Severity = (IsgIncidentSeverity)request.Severity;
        incident.Description = request.Description.Trim();
        incident.RootCause = Normalize(request.RootCause);
        incident.ActionTaken = Normalize(request.ActionTaken);
        incident.LostWorkDays = request.LostWorkDays;
        incident.SgkNotified = request.SgkNotified;
        incident.SgkNotificationDate = request.SgkNotificationDate.HasValue
            ? AsUtc(request.SgkNotificationDate.Value)
            : null;
        incident.SgkNotificationNumber = Normalize(request.SgkNotificationNumber);
        incident.ClosureNote = Normalize(request.ClosureNote) ?? incident.ClosureNote;
        incident.UpdatedAtUtc = DateTime.UtcNow;

        if (newStatus == IsgIncidentStatus.Closed &&
            incident.Status != IsgIncidentStatus.Closed)
        {
            incident.ClosedAtUtc = DateTime.UtcNow;
        }
        else if (newStatus != IsgIncidentStatus.Closed)
        {
            incident.ClosedAtUtc = null;
        }

        incident.Status = newStatus;

        await db.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(incident.Id, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var incident = await db.IsgIncidents
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Kaza kaydı bulunamadı.");

        incident.IsDeleted = true;
        incident.DeletedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// SGK bildirimi gecikmiş mi. Yalnızca gerçek iş kazaları için
    /// hesaplanır; ramak kala ve meslek hastalığı bu bildirim kuralına
    /// girmez.
    /// </summary>
    public static bool IsNotificationOverdue(IsgIncident incident)
    {
        if (incident.IncidentType != IsgIncidentType.Accident)
            return false;

        if (incident.SgkNotified)
            return false;

        return incident.IncidentDateTime.Date.AddDays(SgkNotificationDays)
            < DateTime.UtcNow.Date;
    }

    private static void Validate(
        int incidentType, int severity, string description,
        int lostWorkDays, bool sgkNotified, DateTime? sgkNotificationDate)
    {
        if (!Enum.IsDefined(typeof(IsgIncidentType), incidentType))
            throw new ArgumentException("Geçersiz olay tipi.");

        if (!Enum.IsDefined(typeof(IsgIncidentSeverity), severity))
            throw new ArgumentException("Geçersiz ağırlık derecesi.");

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Olay açıklaması zorunludur.");

        if (lostWorkDays < 0)
            throw new ArgumentException("İş günü kaybı negatif olamaz.");

        // Bildirildi işaretlenip tarih girilmemesi, denetimde
        // ispatlanamayan bir beyan olurdu.
        if (sgkNotified && sgkNotificationDate is null)
            throw new ArgumentException(
                "SGK bildirimi yapıldıysa bildirim tarihi girilmelidir.");
    }

    private async Task ValidateReferencesAsync(
        Guid companyId, Guid? projectId, Guid? projectSiteId, Guid? personnelId,
        CancellationToken cancellationToken)
    {
        if (projectId is Guid project)
        {
            var exists = await db.Projects.AnyAsync(
                x => x.Id == project && x.CompanyId == companyId, cancellationToken);

            if (!exists)
                throw new ArgumentException("Proje bulunamadı.");
        }

        if (projectSiteId is Guid site)
        {
            var exists = await db.ProjectSites.AnyAsync(
                x => x.Id == site &&
                     (projectId == null || x.ProjectId == projectId),
                cancellationToken);

            if (!exists)
                throw new ArgumentException(
                    "Şantiye bulunamadı veya seçilen projeye ait değil.");
        }

        if (personnelId is Guid personnel)
        {
            var exists = await db.Personnel.AnyAsync(
                x => x.Id == personnel && x.CompanyId == companyId, cancellationToken);

            if (!exists)
                throw new ArgumentException("Personel bulunamadı.");
        }
    }

    private static IsgIncidentDetail MapDetail(IsgIncident x) =>
        new(
            x.Id, x.CompanyId, x.IncidentDateTime,
            (int)x.IncidentType, IncidentTypeName(x.IncidentType),
            (int)x.Severity, SeverityName(x.Severity), SeverityColor(x.Severity),
            x.ProjectId, x.Project?.Code, x.Project?.Name,
            x.ProjectSiteId, x.ProjectSite?.Name,
            x.PersonnelId, PersonnelName(x.Personnel),
            x.Description, x.RootCause, x.ActionTaken, x.LostWorkDays,
            x.SgkNotified, x.SgkNotificationDate, x.SgkNotificationNumber,
            IsNotificationOverdue(x),
            (int)x.Status, StatusName(x.Status),
            x.ClosedAtUtc, x.ClosureNote, x.CreatedAtUtc);

    private static string? PersonnelName(Personnel? personnel) =>
        personnel is null ? null : $"{personnel.FirstName} {personnel.LastName}".Trim();

    private static string IncidentTypeName(IsgIncidentType type) => type switch
    {
        IsgIncidentType.Accident => "İş kazası",
        IsgIncidentType.NearMiss => "Ramak kala",
        IsgIncidentType.OccupationalIllness => "Meslek hastalığı",
        _ => "—"
    };

    private static string SeverityName(IsgIncidentSeverity severity) => severity switch
    {
        IsgIncidentSeverity.NoInjury => "Zarar yok",
        IsgIncidentSeverity.FirstAid => "İlk yardım",
        IsgIncidentSeverity.MedicalTreatment => "Tıbbi tedavi",
        IsgIncidentSeverity.LostWorkday => "İş günü kaybı",
        IsgIncidentSeverity.PermanentDisability => "Sürekli iş göremezlik",
        IsgIncidentSeverity.Fatality => "Ölümlü",
        _ => "—"
    };

    /// <summary>erp-status sınıfı; ağırlaştıkça kırmızıya gider.</summary>
    private static string SeverityColor(IsgIncidentSeverity severity) => severity switch
    {
        IsgIncidentSeverity.NoInjury => "gray",
        IsgIncidentSeverity.FirstAid => "blue",
        IsgIncidentSeverity.MedicalTreatment => "yellow",
        _ => "red"
    };

    private static string StatusName(IsgIncidentStatus status) => status switch
    {
        IsgIncidentStatus.Open => "Açık",
        IsgIncidentStatus.UnderInvestigation => "İnceleniyor",
        IsgIncidentStatus.Closed => "Kapalı",
        _ => "—"
    };

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
