namespace EnderunAI.Api.Models;

/// <summary>
/// Projenin imalat bölümü şablonu (NATURA'da 12 bölüm: Panolar,
/// Kuvvetli Akım Daire İçi, Ortak Mahaller, TV, Telefon, Yangın,
/// Görüntülü Konuşma, Topraklama, Kolon Kablo, Kablo Tava, Busbar,
/// İlave İşler).
///
/// Bölümler koda gömülmedi: her projenin imalat kırılımı farklı olur.
/// NATURA'nın listesi yalnızca yeni projede öneri olarak sunulan bir
/// şablondur (bkz. HakedisSectionTemplate).
/// </summary>
public sealed class ProjectHakedisSection : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    /// <summary>Gösterim sırası — icmalde bu sırayla yazılır.</summary>
    public int Order { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Bölüm kodu (ör. "1", "A"). Zorunlu değil.</summary>
    public string? Code { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Hakedişin kendi bölüm satırı. Proje şablonundan kopyalanır — hakediş
/// kesinleştikten sonra proje şablonu değişse bile geçmiş hakedişin
/// icmali oynamamalı.
/// </summary>
public sealed class ProgressPaymentSection : BaseEntity
{
    public Guid ProgressPaymentId { get; set; }
    public ProgressPayment ProgressPayment { get; set; } = null!;

    /// <summary>Kopyalandığı proje bölümü; şablon silinse de kayıt kalır.</summary>
    public Guid? ProjectHakedisSectionId { get; set; }

    public int Order { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }

    // --- İcmal (poz satırlarından hesaplanır, okunabilirlik için saklanır) ---

    public decimal MaterialAmount { get; set; }
    public decimal LaborAmount { get; set; }
    public decimal OverheadAmount { get; set; }
    public decimal CurrentAmount { get; set; }
    public decimal PreviousAmount { get; set; }
    public decimal CumulativeAmount { get; set; }
}

/// <summary>
/// NATURA hakedişindeki 12 imalat bölümü. Yeni projeye bölüm listesi
/// kurulurken başlangıç noktası olarak sunulur; zorunlu değildir.
/// </summary>
public static class HakedisSectionTemplate
{
    public static readonly IReadOnlyList<string> Natura =
    [
        "Panolar / Tablolar",
        "Kuvvetli Akım Daire İçi",
        "Ortak Mahaller",
        "TV Tesisatı",
        "Telefon Tesisatı",
        "Yangın İhbar",
        "Görüntülü Konuşma",
        "Topraklama / Paratoner",
        "Kolon Kablo",
        "Kablo Tava",
        "Busbar",
        "İlave İşler"
    ];
}
