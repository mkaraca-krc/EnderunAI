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

    /// <summary>
    /// Bölümün sözleşme tipi. Yalnızca KARMA projede anlamlıdır; boşsa
    /// projenin tipi geçerlidir. Karma olmayan projede bu alanın
    /// doldurulması sapma yorumunu değiştirmez.
    /// </summary>
    public ProjectContractType? ContractType { get; set; }

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

/// <summary>Bir kısım şablonu — ad ve önerilen kısım listesi.</summary>
public sealed record HakedisSectionTemplateDefinition(
    string Key,
    string Name,
    string Description,
    IReadOnlyList<string> Sections);

/// <summary>
/// Sözleşme icmalinin kısım listesi için HAZIR ŞABLONLAR.
///
/// Kısımlar koda gömülü değildir: her projenin imalat kırılımı farklıdır
/// (konutta daire içi sortiler, endüstriyelde OG/trafo/busbar,
/// hastanede izole güç/UPS). Buradakiler yalnızca tek tıkla gelen
/// başlangıç önerileridir; geldikten sonra serbestçe düzenlenir,
/// silinir, yenisi eklenir. Şablon seçmeden boş başlamak da mümkündür.
/// </summary>
public static class HakedisSectionTemplate
{
    /// <summary>
    /// NATURA (konut) şablonu. Eski adıyla erişilebilir kalıyor —
    /// mevcut çağıranlar bozulmasın.
    /// </summary>
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

    public static readonly IReadOnlyList<HakedisSectionTemplateDefinition> All =
    [
        new("konut", "Konut (NATURA)",
            "Çok bloklu konut projesi elektrik imalat kırılımı.",
            Natura),

        new("endustriyel", "Endüstriyel Tesis",
            "Fabrika ve üretim tesisi; OG dağıtım ve güç altyapısı ağırlıklı.",
            [
                "OG Hücreler / Şalt",
                "Trafo ve Trafo Merkezi",
                "AG Ana Dağıtım Panoları",
                "Busbar Sistemi",
                "Kompanzasyon",
                "Jeneratör ve Otomatik Transfer",
                "Kablo Tava ve Kanal",
                "Kuvvet Tesisatı / Makine Besleme",
                "Aydınlatma ve Priz",
                "Topraklama / Paratoner",
                "Yangın Algılama ve İhbar",
                "Zayıf Akım / Otomasyon",
                "İlave İşler"
            ]),

        new("otel", "Otel",
            "Oda ve ortak mahal ayrımı olan konaklama tesisi.",
            [
                "Trafo ve Ana Dağıtım",
                "Jeneratör ve UPS",
                "Kolon Hatları",
                "Oda İçi Elektrik Tesisatı",
                "Oda Kontrol Sistemi / Kartlı Geçiş",
                "Ortak Mahaller ve Genel Aydınlatma",
                "Mutfak ve Çamaşırhane Besleme",
                "Yangın Algılama ve Seslendirme",
                "CCTV / Zayıf Akım",
                "TV ve Data Altyapısı",
                "Topraklama / Paratoner",
                "İlave İşler"
            ]),

        new("hastane", "Hastane",
            "Sağlık tesisi; kesintisiz ve izole güç gereksinimleri ayrı izlenir.",
            [
                "Trafo ve Ana Dağıtım",
                "Jeneratör ve Otomatik Transfer",
                "UPS ve Kesintisiz Güç",
                "İzole Güç Sistemi (Ameliyathane / Yoğun Bakım)",
                "Kolon Hatları",
                "Kat Panoları ve Kuvvet Tesisatı",
                "Aydınlatma ve Priz",
                "Hemşire Çağrı Sistemi",
                "Yangın Algılama ve İhbar",
                "Medikal Gaz Alarm Panelleri",
                "Zayıf Akım / Data / CCTV",
                "Topraklama / Ekipotansiyel",
                "İlave İşler"
            ]),

        new("avm", "AVM / Ticari",
            "Kiracı üniteli ticari yapı; ortak alan ve mağaza besleme ayrı.",
            [
                "Trafo ve Ana Dağıtım",
                "Jeneratör",
                "Busbar ve Kolon Hatları",
                "Mağaza Besleme Panoları",
                "Ortak Alan Aydınlatma",
                "Otopark Elektrik Tesisatı",
                "Yangın Algılama ve Seslendirme",
                "CCTV / Güvenlik",
                "Kablo Tava ve Kanal",
                "Topraklama / Paratoner",
                "İlave İşler"
            ])
    ];
}
