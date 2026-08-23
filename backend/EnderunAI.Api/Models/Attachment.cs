namespace EnderunAI.Api.Models;

/// <summary>
/// ORTAK EK DOSYA — VARLIK TİPİ + KAYIT KİMLİĞİ.
///
/// NEDEN ORTAK: sistemde ortak DEPOLAMA vardı (`IUploadService`,
/// 10 kategori) ama ortak EKLENTİ yoktu. Her modül dosya-kayıt bağını
/// kendi tablosunda tutuyordu (ProjectDocument, PersonnelDocument,
/// DutySurveyPhoto…) — hakediş ise hiç tutmuyordu: diskte kaydı
/// olmayan gevşek dosyalar, portal denetiminde bulunan açık tam
/// olarak buydu.
///
/// BU PAKETTE YALNIZ yorum ve görev bunu kullanıyor. Diğer modüllerin
/// buraya taşınması AYRI İŞ — ama yol açıldı.
///
/// SAHA İÇİN ASIL KULLANIM: fotoğraf. Şantiyeden çekilen bir kare,
/// üç paragraf açıklamadan daha çok şey anlatıyor.
/// </summary>
public sealed class Attachment : BaseEntity
{
    /// KAPSAM İLK GÜNDEN İÇERİDE — bkz. TaskComment.
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }

    /// <summary>
    /// `IUploadService` kategorisi ve diskteki adı. Dosyanın kendisi
    /// yine orada duruyor; bu tablo yalnız BAĞI kuruyor.
    /// </summary>
    public string Category { get; set; } = string.Empty;
    public string StoredName { get; set; } = string.Empty;

    public string OriginalName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    public Guid? UploadedByUserId { get; set; }
}
