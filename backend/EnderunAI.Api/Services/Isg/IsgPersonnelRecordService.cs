using EnderunAI.Api.Contracts.Isg;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Isg;

public interface IIsgPersonnelRecordService
{
    Task<IReadOnlyCollection<IsgPersonnelSummary>> GetPersonnelSummaryAsync(
        Guid? companyId, string? search, CancellationToken cancellationToken);

    Task<IsgPersonnelCard> GetCardAsync(Guid personnelId, CancellationToken cancellationToken);

    /// <summary>
    /// Oturumdaki kullanıcının kendi kartı. Personel bağı yoksa
    /// KeyNotFoundException — "en yakın personel" tahmin edilmez.
    /// </summary>
    Task<IsgPersonnelCard> GetOwnCardAsync(CancellationToken cancellationToken);

    Task<IsgHealthReportResponse> CreateHealthReportAsync(
        CreateIsgHealthReportRequest request, CancellationToken cancellationToken);
    Task<IsgHealthReportResponse> UpdateHealthReportAsync(
        Guid id, UpdateIsgHealthReportRequest request, CancellationToken cancellationToken);
    Task DeleteHealthReportAsync(Guid id, CancellationToken cancellationToken);

    Task<IsgTrainingResponse> CreateTrainingAsync(
        CreateIsgTrainingRequest request, CancellationToken cancellationToken);
    Task<IsgTrainingResponse> UpdateTrainingAsync(
        Guid id, UpdateIsgTrainingRequest request, CancellationToken cancellationToken);
    Task DeleteTrainingAsync(Guid id, CancellationToken cancellationToken);

    Task<IsgCertificateResponse> CreateCertificateAsync(
        CreateIsgCertificateRequest request, CancellationToken cancellationToken);
    Task<IsgCertificateResponse> UpdateCertificateAsync(
        Guid id, UpdateIsgCertificateRequest request, CancellationToken cancellationToken);
    Task DeleteCertificateAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// Personel bazlı İSG kayıtları: sağlık raporu, eğitim, sertifika.
///
/// Sağlık raporunun tıbbi detayı bu servisten geçerken maskelenir —
/// controller'a maskesiz veri hiç çıkmaz.
/// </summary>
public sealed class IsgPersonnelRecordService(
    AppDbContext db,
    IIsgHealthVisibilityService healthVisibility,
    ICurrentUserService currentUser) : IIsgPersonnelRecordService
{
    public async Task<IReadOnlyCollection<IsgPersonnelSummary>> GetPersonnelSummaryAsync(
        Guid? companyId, string? search, CancellationToken cancellationToken)
    {
        var query = db.Personnel.AsNoTracking()
            .Where(x => x.Status != PersonnelStatus.Terminated);

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                EF.Functions.ILike(x.FirstName + " " + x.LastName, $"%{term}%") ||
                EF.Functions.ILike(x.EmployeeNumber, $"%{term}%"));
        }

        var personnel = await query
            .OrderBy(x => x.FirstName).ThenBy(x => x.LastName)
            .Select(x => new
            {
                x.Id, x.FirstName, x.LastName, x.EmployeeNumber, x.JobTitle
            })
            .ToListAsync(cancellationToken);

        var ids = personnel.Select(x => x.Id).ToList();
        var today = Today();

        var healthReports = await db.IsgHealthReports.AsNoTracking()
            .Where(x => ids.Contains(x.PersonnelId))
            .Select(x => new { x.PersonnelId, x.ValidUntil })
            .ToListAsync(cancellationToken);

        var trainings = await db.IsgTrainings.AsNoTracking()
            .Where(x => ids.Contains(x.PersonnelId))
            .Select(x => new { x.PersonnelId, x.TrainingType, x.ValidUntil })
            .ToListAsync(cancellationToken);

        var certificates = await db.IsgCertificates.AsNoTracking()
            .Where(x => ids.Contains(x.PersonnelId))
            .Select(x => new { x.PersonnelId, x.ExpiryDate })
            .ToListAsync(cancellationToken);

        return personnel.Select(person =>
        {
            var personHealth = healthReports.Where(x => x.PersonnelId == person.Id).ToList();
            var personTraining = trainings.Where(x => x.PersonnelId == person.Id).ToList();
            var personCerts = certificates.Where(x => x.PersonnelId == person.Id).ToList();

            var validities = personHealth.Select(x => x.ValidUntil)
                .Concat(personTraining.Select(x => x.ValidUntil))
                .Concat(personCerts.Select(x => x.ExpiryDate))
                .Select(x => IsgValidityCalculator.Evaluate(x, today))
                .ToList();

            // En geç biten geçerli rapor: birden çok rapor varsa en
            // yenisi geçerliyse personel geçerli sayılır.
            var latestHealth = personHealth
                .Select(x => x.ValidUntil)
                .OrderByDescending(x => x ?? DateOnly.MaxValue)
                .FirstOrDefault();

            var hasValidHealth = personHealth.Count > 0 &&
                IsgValidityCalculator.Evaluate(latestHealth, today)
                    is not IsgValidityStatus.Expired;

            var hasValidBasicTraining = personTraining
                .Where(x => x.TrainingType is IsgTrainingType.Basic or IsgTrainingType.Refresher)
                .Any(x => IsgValidityCalculator.Evaluate(x.ValidUntil, today)
                    is not IsgValidityStatus.Expired);

            return new IsgPersonnelSummary(
                person.Id,
                $"{person.FirstName} {person.LastName}".Trim(),
                person.EmployeeNumber,
                person.JobTitle,
                hasValidHealth,
                latestHealth,
                hasValidBasicTraining,
                personCerts.Count,
                validities.Count(x => x == IsgValidityStatus.Expired),
                validities.Count(x => x == IsgValidityStatus.ExpiringSoon),
                !hasValidHealth || !hasValidBasicTraining);
        }).ToList();
    }

    public async Task<IsgPersonnelCard> GetCardAsync(
        Guid personnelId, CancellationToken cancellationToken)
    {
        var canViewHealthDetail = await healthVisibility
            .CanViewHealthDetailAsync(cancellationToken);

        return await BuildCardAsync(personnelId, canViewHealthDetail, cancellationToken);
    }

    public async Task<IsgPersonnelCard> GetOwnCardAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            throw new KeyNotFoundException("Oturum bulunamadı.");

        var personnelId = await db.Users
            .Where(x => x.Id == userId)
            .Select(x => x.PersonnelId)
            .SingleOrDefaultAsync(cancellationToken);

        if (personnelId is not Guid id)
        {
            throw new KeyNotFoundException(
                "Kullanıcınız bir personel kartına bağlı değil. " +
                "İnsan Kaynakları'ndan eşleştirme yapılmasını isteyin.");
        }

        // Kişi kendi raporunun tıbbi detayını görebilir; kısıtlaması
        // kendisini ilgilendirir. Başkasının kaydına bu uçtan
        // erişilemez — personel kimliği istekten değil oturumdan gelir.
        return await BuildCardAsync(id, canViewHealthDetail: true, cancellationToken);
    }

    private async Task<IsgPersonnelCard> BuildCardAsync(
        Guid personnelId, bool canViewHealthDetail, CancellationToken cancellationToken)
    {
        var person = await db.Personnel.AsNoTracking()
            .Where(x => x.Id == personnelId)
            .Select(x => new
            {
                x.Id, x.FirstName, x.LastName, x.EmployeeNumber, x.JobTitle
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Personel bulunamadı.");

        var name = $"{person.FirstName} {person.LastName}".Trim();
        var today = Today();

        var healthReports = await db.IsgHealthReports.AsNoTracking()
            .Where(x => x.PersonnelId == personnelId)
            .OrderByDescending(x => x.ExamDate)
            .ToListAsync(cancellationToken);

        var trainings = await db.IsgTrainings.AsNoTracking()
            .Where(x => x.PersonnelId == personnelId)
            .OrderByDescending(x => x.TrainingDate)
            .ToListAsync(cancellationToken);

        var certificates = await db.IsgCertificates.AsNoTracking()
            .Where(x => x.PersonnelId == personnelId)
            .OrderByDescending(x => x.IssueDate)
            .ToListAsync(cancellationToken);

        var healthResponses = healthReports
            .Select(x => MapHealth(x, name, person.EmployeeNumber, canViewHealthDetail, today))
            .ToList();
        var trainingResponses = trainings
            .Select(x => MapTraining(x, name, person.EmployeeNumber, today))
            .ToList();
        var certificateResponses = certificates
            .Select(x => MapCertificate(x, name, person.EmployeeNumber, today))
            .ToList();

        var statuses = healthReports.Select(x => x.ValidUntil)
            .Concat(trainings.Select(x => x.ValidUntil))
            .Concat(certificates.Select(x => x.ExpiryDate))
            .Select(x => IsgValidityCalculator.Evaluate(x, today))
            .ToList();

        return new IsgPersonnelCard(
            person.Id, name, person.EmployeeNumber, person.JobTitle,
            healthResponses, trainingResponses, certificateResponses,
            statuses.Count(x => x == IsgValidityStatus.Expired),
            statuses.Count(x => x == IsgValidityStatus.ExpiringSoon));
    }

    // --- Sağlık raporu ---

    public async Task<IsgHealthReportResponse> CreateHealthReportAsync(
        CreateIsgHealthReportRequest request, CancellationToken cancellationToken)
    {
        await ValidatePersonnelAsync(request.CompanyId, request.PersonnelId, cancellationToken);

        if (!Enum.IsDefined(typeof(IsgHealthReportType), request.ReportType))
            throw new ArgumentException("Geçersiz rapor tipi.");

        if (!Enum.IsDefined(typeof(IsgHealthResult), request.Result))
            throw new ArgumentException("Geçersiz muayene sonucu.");

        ValidateDateRange(request.ExamDate, request.ValidUntil, "Muayene");

        var report = new IsgHealthReport
        {
            CompanyId = request.CompanyId,
            PersonnelId = request.PersonnelId,
            IsgOsgbContractId = request.IsgOsgbContractId,
            ReportType = (IsgHealthReportType)request.ReportType,
            ExamDate = request.ExamDate,
            ValidUntil = request.ValidUntil,
            Result = (IsgHealthResult)request.Result,
            DoctorName = Normalize(request.DoctorName),
            Restrictions = Normalize(request.Restrictions),
            DoctorNotes = Normalize(request.DoctorNotes)
        };

        db.IsgHealthReports.Add(report);
        await db.SaveChangesAsync(cancellationToken);

        return await LoadHealthResponseAsync(report.Id, cancellationToken);
    }

    public async Task<IsgHealthReportResponse> UpdateHealthReportAsync(
        Guid id, UpdateIsgHealthReportRequest request, CancellationToken cancellationToken)
    {
        var report = await db.IsgHealthReports
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Sağlık raporu bulunamadı.");

        // Tıbbi alanı yalnızca o veriyi görebilen değiştirebilir; aksi
        // halde göremediği bir notu silmiş olurdu.
        if (!await healthVisibility.CanViewHealthDetailAsync(cancellationToken))
            throw new UnauthorizedAccessException(
                "Sağlık raporu detayını düzenlemek için yetkiniz yok.");

        ValidateDateRange(request.ExamDate, request.ValidUntil, "Muayene");

        report.IsgOsgbContractId = request.IsgOsgbContractId;
        report.ReportType = (IsgHealthReportType)request.ReportType;
        report.ExamDate = request.ExamDate;
        report.ValidUntil = request.ValidUntil;
        report.Result = (IsgHealthResult)request.Result;
        report.DoctorName = Normalize(request.DoctorName);
        report.Restrictions = Normalize(request.Restrictions);
        report.DoctorNotes = Normalize(request.DoctorNotes);
        report.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return await LoadHealthResponseAsync(report.Id, cancellationToken);
    }

    public async Task DeleteHealthReportAsync(Guid id, CancellationToken cancellationToken)
    {
        var report = await db.IsgHealthReports
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Sağlık raporu bulunamadı.");

        report.IsDeleted = true;
        report.DeletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    // --- Eğitim ---

    public async Task<IsgTrainingResponse> CreateTrainingAsync(
        CreateIsgTrainingRequest request, CancellationToken cancellationToken)
    {
        await ValidatePersonnelAsync(request.CompanyId, request.PersonnelId, cancellationToken);

        if (!Enum.IsDefined(typeof(IsgTrainingType), request.TrainingType))
            throw new ArgumentException("Geçersiz eğitim tipi.");

        if (string.IsNullOrWhiteSpace(request.Topic))
            throw new ArgumentException("Eğitim konusu zorunludur.");

        if (request.DurationHours < 0m)
            throw new ArgumentException("Eğitim süresi negatif olamaz.");

        ValidateDateRange(request.TrainingDate, request.ValidUntil, "Eğitim");

        var training = new IsgTraining
        {
            CompanyId = request.CompanyId,
            PersonnelId = request.PersonnelId,
            IsgOsgbContractId = request.IsgOsgbContractId,
            TrainingType = (IsgTrainingType)request.TrainingType,
            Topic = request.Topic.Trim(),
            TrainingDate = request.TrainingDate,
            DurationHours = request.DurationHours,
            ValidUntil = request.ValidUntil,
            TrainerName = Normalize(request.TrainerName),
            Notes = Normalize(request.Notes)
        };

        db.IsgTrainings.Add(training);
        await db.SaveChangesAsync(cancellationToken);

        return await LoadTrainingResponseAsync(training.Id, cancellationToken);
    }

    public async Task<IsgTrainingResponse> UpdateTrainingAsync(
        Guid id, UpdateIsgTrainingRequest request, CancellationToken cancellationToken)
    {
        var training = await db.IsgTrainings
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Eğitim kaydı bulunamadı.");

        if (string.IsNullOrWhiteSpace(request.Topic))
            throw new ArgumentException("Eğitim konusu zorunludur.");

        ValidateDateRange(request.TrainingDate, request.ValidUntil, "Eğitim");

        training.IsgOsgbContractId = request.IsgOsgbContractId;
        training.TrainingType = (IsgTrainingType)request.TrainingType;
        training.Topic = request.Topic.Trim();
        training.TrainingDate = request.TrainingDate;
        training.DurationHours = request.DurationHours;
        training.ValidUntil = request.ValidUntil;
        training.TrainerName = Normalize(request.TrainerName);
        training.Notes = Normalize(request.Notes);
        training.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return await LoadTrainingResponseAsync(training.Id, cancellationToken);
    }

    public async Task DeleteTrainingAsync(Guid id, CancellationToken cancellationToken)
    {
        var training = await db.IsgTrainings
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Eğitim kaydı bulunamadı.");

        training.IsDeleted = true;
        training.DeletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    // --- Sertifika ---

    public async Task<IsgCertificateResponse> CreateCertificateAsync(
        CreateIsgCertificateRequest request, CancellationToken cancellationToken)
    {
        await ValidatePersonnelAsync(request.CompanyId, request.PersonnelId, cancellationToken);

        if (!Enum.IsDefined(typeof(IsgCertificateType), request.CertificateType))
            throw new ArgumentException("Geçersiz belge tipi.");

        if ((IsgCertificateType)request.CertificateType == IsgCertificateType.Other &&
            string.IsNullOrWhiteSpace(request.CustomTypeName))
        {
            throw new ArgumentException("Diğer belge tipinde belge adı zorunludur.");
        }

        ValidateDateRange(request.IssueDate, request.ExpiryDate, "Belge");

        var certificate = new IsgCertificate
        {
            CompanyId = request.CompanyId,
            PersonnelId = request.PersonnelId,
            CertificateType = (IsgCertificateType)request.CertificateType,
            CustomTypeName = Normalize(request.CustomTypeName),
            CertificateNumber = Normalize(request.CertificateNumber),
            IssuedBy = Normalize(request.IssuedBy),
            IssueDate = request.IssueDate,
            ExpiryDate = request.ExpiryDate,
            Notes = Normalize(request.Notes)
        };

        db.IsgCertificates.Add(certificate);
        await db.SaveChangesAsync(cancellationToken);

        return await LoadCertificateResponseAsync(certificate.Id, cancellationToken);
    }

    public async Task<IsgCertificateResponse> UpdateCertificateAsync(
        Guid id, UpdateIsgCertificateRequest request, CancellationToken cancellationToken)
    {
        var certificate = await db.IsgCertificates
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Belge bulunamadı.");

        ValidateDateRange(request.IssueDate, request.ExpiryDate, "Belge");

        certificate.CertificateType = (IsgCertificateType)request.CertificateType;
        certificate.CustomTypeName = Normalize(request.CustomTypeName);
        certificate.CertificateNumber = Normalize(request.CertificateNumber);
        certificate.IssuedBy = Normalize(request.IssuedBy);
        certificate.IssueDate = request.IssueDate;
        certificate.ExpiryDate = request.ExpiryDate;
        certificate.Notes = Normalize(request.Notes);
        certificate.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return await LoadCertificateResponseAsync(certificate.Id, cancellationToken);
    }

    public async Task DeleteCertificateAsync(Guid id, CancellationToken cancellationToken)
    {
        var certificate = await db.IsgCertificates
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Belge bulunamadı.");

        certificate.IsDeleted = true;
        certificate.DeletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    // --- Yükleme ve eşleme ---

    private async Task<IsgHealthReportResponse> LoadHealthResponseAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var record = await db.IsgHealthReports.AsNoTracking()
            .Include(x => x.Personnel)
            .SingleAsync(x => x.Id == id, cancellationToken);

        var canViewDetail = await healthVisibility.CanViewHealthDetailAsync(cancellationToken);

        return MapHealth(
            record,
            $"{record.Personnel.FirstName} {record.Personnel.LastName}".Trim(),
            record.Personnel.EmployeeNumber,
            canViewDetail,
            Today());
    }

    private async Task<IsgTrainingResponse> LoadTrainingResponseAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var record = await db.IsgTrainings.AsNoTracking()
            .Include(x => x.Personnel)
            .SingleAsync(x => x.Id == id, cancellationToken);

        return MapTraining(
            record,
            $"{record.Personnel.FirstName} {record.Personnel.LastName}".Trim(),
            record.Personnel.EmployeeNumber,
            Today());
    }

    private async Task<IsgCertificateResponse> LoadCertificateResponseAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var record = await db.IsgCertificates.AsNoTracking()
            .Include(x => x.Personnel)
            .SingleAsync(x => x.Id == id, cancellationToken);

        return MapCertificate(
            record,
            $"{record.Personnel.FirstName} {record.Personnel.LastName}".Trim(),
            record.Personnel.EmployeeNumber,
            Today());
    }

    private static IsgHealthReportResponse MapHealth(
        IsgHealthReport record, string personnelName, string? employeeNumber,
        bool canViewDetail, DateOnly today)
    {
        var status = IsgValidityCalculator.Evaluate(record.ValidUntil, today);

        return new IsgHealthReportResponse(
            record.Id,
            record.PersonnelId,
            personnelName,
            employeeNumber,
            (int)record.ReportType,
            HealthReportTypeName(record.ReportType),
            record.ExamDate,
            record.ValidUntil,
            (int)record.Result,
            HealthResultName(record.Result),
            record.DoctorName,
            status.ToString(),
            IsgValidityCalculator.StatusName(status),
            IsgValidityCalculator.StatusColor(status),
            IsgValidityCalculator.DaysRemaining(record.ValidUntil, today),
            // Tıbbi alanlar: yetki yoksa hiç dönmez.
            canViewDetail ? record.Restrictions : null,
            canViewDetail ? record.DoctorNotes : null,
            canViewDetail ? !string.IsNullOrWhiteSpace(record.DocumentPath) : null,
            !canViewDetail);
    }

    private static IsgTrainingResponse MapTraining(
        IsgTraining record, string personnelName, string? employeeNumber, DateOnly today)
    {
        var status = IsgValidityCalculator.Evaluate(record.ValidUntil, today);

        return new IsgTrainingResponse(
            record.Id, record.PersonnelId, personnelName, employeeNumber,
            (int)record.TrainingType, TrainingTypeName(record.TrainingType),
            record.Topic, record.TrainingDate, record.DurationHours, record.ValidUntil,
            record.TrainerName,
            status.ToString(),
            IsgValidityCalculator.StatusName(status),
            IsgValidityCalculator.StatusColor(status),
            IsgValidityCalculator.DaysRemaining(record.ValidUntil, today),
            !string.IsNullOrWhiteSpace(record.DocumentPath),
            record.Notes);
    }

    private static IsgCertificateResponse MapCertificate(
        IsgCertificate record, string personnelName, string? employeeNumber, DateOnly today)
    {
        var status = IsgValidityCalculator.Evaluate(record.ExpiryDate, today);

        return new IsgCertificateResponse(
            record.Id, record.PersonnelId, personnelName, employeeNumber,
            (int)record.CertificateType,
            record.CertificateType == IsgCertificateType.Other
                ? record.CustomTypeName ?? "Diğer"
                : CertificateTypeName(record.CertificateType),
            record.CertificateNumber, record.IssuedBy, record.IssueDate, record.ExpiryDate,
            status.ToString(),
            IsgValidityCalculator.StatusName(status),
            IsgValidityCalculator.StatusColor(status),
            IsgValidityCalculator.DaysRemaining(record.ExpiryDate, today),
            !string.IsNullOrWhiteSpace(record.DocumentPath),
            record.Notes);
    }

    private async Task ValidatePersonnelAsync(
        Guid companyId, Guid personnelId, CancellationToken cancellationToken)
    {
        var exists = await db.Personnel.AnyAsync(
            x => x.Id == personnelId && x.CompanyId == companyId, cancellationToken);

        if (!exists)
            throw new ArgumentException("Personel bulunamadı.");
    }

    private static void ValidateDateRange(DateOnly start, DateOnly? end, string label)
    {
        if (end is DateOnly expiry && expiry < start)
            throw new ArgumentException($"{label} geçerlilik bitişi başlangıçtan önce olamaz.");
    }

    private static string HealthReportTypeName(IsgHealthReportType type) => type switch
    {
        IsgHealthReportType.PreEmployment => "İşe giriş",
        IsgHealthReportType.Periodic => "Periyodik",
        IsgHealthReportType.ReturnToWork => "İşe dönüş",
        IsgHealthReportType.Special => "Özel durum",
        _ => "—"
    };

    private static string HealthResultName(IsgHealthResult result) => result switch
    {
        IsgHealthResult.Fit => "Çalışabilir",
        IsgHealthResult.FitWithRestrictions => "Şartlı çalışabilir",
        IsgHealthResult.Unfit => "Çalışamaz",
        _ => "—"
    };

    private static string TrainingTypeName(IsgTrainingType type) => type switch
    {
        IsgTrainingType.Basic => "Temel İSG",
        IsgTrainingType.OnTheJob => "İşbaşı",
        IsgTrainingType.Refresher => "Yenileme",
        IsgTrainingType.Special => "Özel konu",
        _ => "—"
    };

    private static string CertificateTypeName(IsgCertificateType type) => type switch
    {
        IsgCertificateType.WorkingAtHeight => "Yüksekte çalışma",
        IsgCertificateType.ElectricalAuthorization => "Elektrik yetki belgesi",
        IsgCertificateType.FirstAid => "İlk yardımcı sertifikası",
        IsgCertificateType.FireSafety => "Yangın güvenliği",
        IsgCertificateType.MachineOperator => "İş makinesi operatörü",
        _ => "Diğer"
    };

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
