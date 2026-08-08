namespace EnderunAI.Api.Models.HumanResources;

/// <summary>
/// Özlük dosyasındaki belge türü.
///
/// Sağlık raporu, eğitim ve sertifika BURADA YOK: onlar İSG modülünde
/// (IsgHealthReport, IsgTraining, IsgCertificate) kendi geçerlilik
/// takipleriyle duruyor. İkinci bir yere kopyalamak, aynı belgenin iki
/// farklı geçerlilik tarihi taşıması demekti.
/// </summary>
public enum PersonnelDocumentType
{
    EmploymentContract = 0,
    IdentityCopy = 1,
    Diploma = 2,
    DriverLicense = 3,
    CriminalRecord = 4,
    ResidenceCertificate = 5,
    MilitaryStatus = 6,
    Photograph = 7,
    BankAccount = 8,
    SgkEntryNotice = 9,
    SgkExitNotice = 10,
    Other = 99
}

/// <summary>
/// Personel özlük belgesi.
///
/// MEVCUT TABLOYA BAĞLANDI: canlıda <c>hr_personnel_documents</c> tablosu
/// zaten vardı — modeli, ucu ve ekranı olmayan, terk edilmiş bir
/// tasarımdan kalma (menüden kaldırdığımız Yetkinlikler/Performans/
/// Disiplin ekranlarıyla aynı aileden). Tablo boştu ve alanları
/// yazacağımdan daha zengindi. Yeni bir tablo açmak, personel belgesi
/// için iki ayrı kaynak yaratırdı.
///
/// Dosyanın kendisi <see cref="Services.Upload.IUploadService"/> ile
/// saklanıyor — şantiye fotoğrafları ve İSG belgeleriyle aynı depo.
///
/// GİZLİLİK: bu kayıtlar kimlik fotokopisi ve adli sicil gibi belgeler
/// taşıyor. personnel.view izni sahada da var (Şantiye Şefi, Formen);
/// bu yüzden özlük belgeleri kendi dar anahtarıyla korunuyor
/// (personnel_document.*), tıpkı elden ödemenin extra_payment.* ile
/// korunması gibi.
/// </summary>
public sealed class PersonnelDocument : BaseEntity
{
    public Guid CompanyId { get; set; }

    public Guid PersonnelId { get; set; }
    public Personnel Personnel { get; set; } = null!;

    public PersonnelDocumentType DocumentType { get; set; }

    /// <summary>Belge başlığı.</summary>
    public string DocumentName { get; set; } = string.Empty;

    /// <summary>Belge numarası (ehliyet no, sicil no gibi).</summary>
    public string? DocumentNumber { get; set; }

    public DateTime? IssueDate { get; set; }

    /// <summary>
    /// Geçerlilik bitişi. Boşsa süresiz — diploma gibi belgeler için.
    /// Doluysa yaklaşan bitiş uyarı üretir (İSG ile aynı eşikler).
    /// </summary>
    public DateTime? ExpiryDate { get; set; }

    public string? IssuingInstitution { get; set; }

    /// <summary>Depodaki dosya adı.</summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Özlük dosyasında bulunması zorunlu belgelerden mi. Eksik zorunlu
    /// belge, veri eksiği raporunda ayrıca sayılabilsin diye.
    /// </summary>
    public bool IsMandatory { get; set; }

    /// <summary>Aslı görülüp doğrulandı mı.</summary>
    public bool IsVerified { get; set; }

    public Guid? VerifiedByUserId { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }

    public string? Notes { get; set; }

    // --- Dosya künyesi ---
    // Mevcut tabloda yalnızca FilePath vardı; indirirken kullanıcıya
    // özgün adı ve doğru içerik tipini vermek için üç kolon eklendi.

    public string? OriginalName { get; set; }
    public string? ContentType { get; set; }
    public long FileSize { get; set; }
}
