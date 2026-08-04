namespace EnderunAI.Api.Contracts.Isg;

// --- Sağlık raporu ---

public sealed record CreateIsgHealthReportRequest(
    Guid CompanyId,
    Guid PersonnelId,
    Guid? IsgOsgbContractId,
    int ReportType,
    DateOnly ExamDate,
    DateOnly? ValidUntil,
    int Result,
    string? DoctorName,
    string? Restrictions,
    string? DoctorNotes);

public sealed record UpdateIsgHealthReportRequest(
    Guid? IsgOsgbContractId,
    int ReportType,
    DateOnly ExamDate,
    DateOnly? ValidUntil,
    int Result,
    string? DoctorName,
    string? Restrictions,
    string? DoctorNotes);

/// <summary>
/// Sağlık raporu. Tarih ve geçerlilik her İSG kullanıcısına döner;
/// <see cref="Restrictions"/>, <see cref="DoctorNotes"/> ve
/// <see cref="HasDocument"/>/<see cref="DocumentPath"/> yalnızca
/// isg.health.view izniyle dolar, aksi halde null gelir.
/// </summary>
public sealed record IsgHealthReportResponse(
    Guid Id,
    Guid PersonnelId,
    string PersonnelName,
    string? EmployeeNumber,
    int ReportType,
    string ReportTypeName,
    DateOnly ExamDate,
    DateOnly? ValidUntil,
    int Result,
    string ResultName,
    string? DoctorName,
    string ValidityStatus,
    string ValidityStatusName,
    string ValidityColor,
    int? DaysRemaining,
    string? Restrictions,
    string? DoctorNotes,
    bool? HasDocument,
    /// <summary>Tıbbi detay gizlendiyse true — arayüz bunu yazar.</summary>
    bool HealthDetailHidden);

// --- Eğitim ---

public sealed record CreateIsgTrainingRequest(
    Guid CompanyId,
    Guid PersonnelId,
    Guid? IsgOsgbContractId,
    int TrainingType,
    string Topic,
    DateOnly TrainingDate,
    decimal DurationHours,
    DateOnly? ValidUntil,
    string? TrainerName,
    string? Notes);

public sealed record UpdateIsgTrainingRequest(
    Guid? IsgOsgbContractId,
    int TrainingType,
    string Topic,
    DateOnly TrainingDate,
    decimal DurationHours,
    DateOnly? ValidUntil,
    string? TrainerName,
    string? Notes);

public sealed record IsgTrainingResponse(
    Guid Id,
    Guid PersonnelId,
    string PersonnelName,
    string? EmployeeNumber,
    int TrainingType,
    string TrainingTypeName,
    string Topic,
    DateOnly TrainingDate,
    decimal DurationHours,
    DateOnly? ValidUntil,
    string? TrainerName,
    string ValidityStatus,
    string ValidityStatusName,
    string ValidityColor,
    int? DaysRemaining,
    bool HasDocument,
    string? Notes);

// --- Sertifika ---

public sealed record CreateIsgCertificateRequest(
    Guid CompanyId,
    Guid PersonnelId,
    int CertificateType,
    string? CustomTypeName,
    string? CertificateNumber,
    string? IssuedBy,
    DateOnly IssueDate,
    DateOnly? ExpiryDate,
    string? Notes);

public sealed record UpdateIsgCertificateRequest(
    int CertificateType,
    string? CustomTypeName,
    string? CertificateNumber,
    string? IssuedBy,
    DateOnly IssueDate,
    DateOnly? ExpiryDate,
    string? Notes);

public sealed record IsgCertificateResponse(
    Guid Id,
    Guid PersonnelId,
    string PersonnelName,
    string? EmployeeNumber,
    int CertificateType,
    string CertificateTypeName,
    string? CertificateNumber,
    string? IssuedBy,
    DateOnly IssueDate,
    DateOnly? ExpiryDate,
    string ValidityStatus,
    string ValidityStatusName,
    string ValidityColor,
    int? DaysRemaining,
    bool HasDocument,
    string? Notes);

/// <summary>
/// Bir personelin tüm İSG kartı. Self-servis ekranı da bunu döner —
/// tek fark, kimin kaydı olduğunun kullanıcıdan değil oturumdan
/// belirlenmesi.
/// </summary>
public sealed record IsgPersonnelCard(
    Guid PersonnelId,
    string PersonnelName,
    string? EmployeeNumber,
    string? JobTitle,
    IReadOnlyCollection<IsgHealthReportResponse> HealthReports,
    IReadOnlyCollection<IsgTrainingResponse> Trainings,
    IReadOnlyCollection<IsgCertificateResponse> Certificates,
    /// <summary>Süresi dolmuş kayıt sayısı (üç tür toplamı).</summary>
    int ExpiredCount,
    /// <summary>30 gün içinde dolacak kayıt sayısı.</summary>
    int ExpiringSoonCount);

/// <summary>Personel listesi satırı — panelde ve liste ekranında.</summary>
public sealed record IsgPersonnelSummary(
    Guid PersonnelId,
    string PersonnelName,
    string? EmployeeNumber,
    string? JobTitle,
    bool HasValidHealthReport,
    DateOnly? HealthReportValidUntil,
    bool HasValidBasicTraining,
    int CertificateCount,
    int ExpiredCount,
    int ExpiringSoonCount,
    /// <summary>Hiç sağlık raporu veya temel eğitimi yoksa true.</summary>
    bool HasMissingRecords);
