namespace EnderunAI.Api.Contracts.Personnel;

public sealed record CreatePersonnelRequest(
    Guid CompanyId,
    Guid? BranchId,
    string EmployeeNumber,
    string FirstName,
    string LastName,
    string? IdentityNumber,
    DateTime? BirthDate,
    string? Phone,
    string? Email,
    string? Address,
    string? JobTitle,
    string? Profession,
    string? SgkRegistrationNumber,
    DateTime? EmploymentStartDate,
    decimal? MonthlySalary,
    // Kırmızı (işe alınamaz) engelini geçmek için gerekçe. Yalnız
    // GM ve Admin kullanabilir; boş bırakılırsa engel uygulanır.
    string? RehireOverrideReason = null);

public sealed record UpdatePersonnelRequest(
    Guid? BranchId,
    string FirstName,
    string LastName,
    string? IdentityNumber,
    DateTime? BirthDate,
    string? Phone,
    string? Email,
    string? Address,
    string? JobTitle,
    string? Profession,
    string? SgkRegistrationNumber,
    DateTime? EmploymentStartDate,
    DateTime? EmploymentEndDate,
    decimal? MonthlySalary,
    int Status,
    bool IsActive,
    // Fazla mesai muvafakati: yıllık yazılı onayın hangi yıla ait
    // olduğu ve alındığı tarih. Belgenin kendisi özlük arşivinde.
    int? OvertimeConsentYear = null,
    DateTime? OvertimeConsentDate = null,
    // Kırmızı (işe alınamaz) engelini geçmek için gerekçe. Yalnız
    // GM ve Admin kullanabilir; boş bırakılırsa engel uygulanır.
    string? RehireOverrideReason = null);

public sealed record AssignPersonnelRequest(
    Guid ProjectId,
    DateTime StartDate,
    DateTime? EndDate,
    string? Role,
    string? Notes,
    bool IsPrimaryAssignment);

/// <summary>
/// Personel kartından görev yeri belirleme.
///
/// Şantiye seçildiğinde <see cref="ProjectSiteId"/> zorunludur ve
/// mevcut aktif atama varsa kapatılıp yenisi açılır — böylece "bir
/// personelin tek aktif şantiye ataması olur" kuralı korunur.
/// </summary>
public sealed record SetWorkLocationRequest(
    /// <summary>0 = Atanmadı, 1 = Merkez, 2 = Şantiye.</summary>
    int WorkLocationType,
    Guid? ProjectSiteId,
    Guid? BranchId,
    DateTime? StartDate,
    string? Role,
    string? Notes);

/// <summary>
/// Eksik alan tamamlama isteği. Gönderilmeyen (null/boş) alan
/// DEĞİŞTİRİLMEZ — bu uç alan doldurmak için var, boşaltmak için tam
/// güncelleme kullanılır.
/// </summary>
public sealed record CompletePersonnelDataRequest(
    string? IdentityNumber,
    string? SgkRegistrationNumber,
    string? Phone,
    string? JobTitle,
    DateTime? BirthDate,
    DateTime? EmploymentStartDate,
    Guid? BranchId);

/// <summary>
/// Personelin departman ataması.
///
/// ── `DepartmentId` NEDEN NULLABLE VE NEDEN "GÖNDERİLMEDİ" DEĞİL ──
///
/// Burada `null` "değiştirme" demek DEĞİL, "departmandan çıkar"
/// demektir. `CompletePersonnelDataRequest`'in kuralının tersi ve
/// bilinçli: o uç alan DOLDURMAK için var, bu uç bir alanı YÖNETMEK
/// için. Departmandan çıkarmanın başka bir yolu olmasaydı, yanlış
/// atanan bir personel düzeltilemezdi.
///
/// ── `RecordVersion` ZORUNLU ──
///
/// Toplu atama ekranı 79 satırı aynı anda gösteriyor; iki kişinin aynı
/// listeyi açıp aynı satırı değiştirmesi olağan. Sürüm damgası
/// `KayitSurumu` ile karşılaştırılıyor (bu depoda RowVersion'ın
/// karşılığı; `xmin` denenip gerekçesiyle reddedildi).
/// </summary>
public sealed record SetPersonnelDepartmentRequest(
    Guid? DepartmentId,
    DateTime? RecordVersion,
    string? Reason);
