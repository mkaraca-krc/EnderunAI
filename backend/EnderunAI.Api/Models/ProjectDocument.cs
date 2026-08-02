namespace EnderunAI.Api.Models;

/// <summary>
/// Proje Dosya Merkezi'ndeki tek bir dosya sürümü. Aynı (ProjectId,
/// ProjectSiteId, Folder, FileName) eşleşmesiyle tekrar yükleme, eski
/// satırı silmez — yeni bir sürüm satırı ekler ve IsCurrentVersion
/// bayrağını kaydırır. Fiziksel dosyalar
/// /var/www/enderun-data/project-files/{ProjectId}/{Folder}/{StoredFileName}
/// altında, her sürüm kendi dosyasında, hiç üzerine yazılmadan tutulur.
/// </summary>
public sealed class ProjectDocument : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    /// <summary>Opsiyonel — belirli bir şantiyeye özel dosyalar için (ör. Saha Fotoğrafları).</summary>
    public Guid? ProjectSiteId { get; set; }
    public ProjectSite? ProjectSite { get; set; }

    /// <summary>
    /// Serbest metin — frontend'de sabit bir öneri listesi (Keşif,
    /// Çizimler, Şartnameler, Sözleşme, Saha Fotoğrafları, Hakediş
    /// Ekleri, Diğer) sunulur ama backend herhangi bir klasör adını kabul
    /// eder.
    /// </summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>Kullanıcının yüklediği orijinal dosya adı (indirmede geri verilir).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Disk üzerindeki benzersiz dosya adı.</summary>
    public string StoredFileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;

    public Guid UploadedByUserId { get; set; }
    public AppUser UploadedByUser { get; set; } = null!;

    public string? Description { get; set; }

    public int VersionNumber { get; set; } = 1;
    public bool IsCurrentVersion { get; set; } = true;
}
