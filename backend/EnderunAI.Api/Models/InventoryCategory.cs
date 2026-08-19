namespace EnderunAI.Api.Models;

/// <summary>
/// Kategorinin tipi — kartın nasıl oluşturulacağını belirler.
/// </summary>
public enum InventoryCategoryKind
{
    /// <summary>
    /// STANDART: ad ve mükerrer engeli ÖZELLİKLERDEN türer. Kullanıcı
    /// ad yazmaz, açılır listelerden seçer. Aynı kombinasyon ikinci kez
    /// kart olamaz — bölünmüş stok böyle engellenir.
    /// </summary>
    Standard = 0,

    /// <summary>
    /// SERBEST: ad elle yazılır, fotoğraf ve proje bağı taşır, mükerrer
    /// engeli UYGULANMAZ. Dekoratif aydınlatma ve özel imalat gibi her
    /// biri tekil olan ürünler için.
    /// </summary>
    Free = 1
}

/// <summary>
/// Kategorinin MUHASEBE karşılığı — stokun hangi hesapta durduğunu ve
/// çıkışta hangi hesaba yazıldığını belirler.
///
/// Ağırlıklı olarak taahhüt işi yapıldığı için VARSAYILAN SARF'tır:
/// yeni açılan hiçbir kategori kendiliğinden "ticari mal" olmaz.
/// Satılabilir kategoriler sonradan, mali müşavir onayıyla ve ayrı
/// bir izinle işaretlenir — yanlış hesaba yazılan stok, mali tabloyu
/// sessizce bozar ve fark ancak mizanda görülür.
/// </summary>
public enum InventoryAccountingKind
{
    /// <summary>
    /// SARF / PROJE MALZEMESİ — 150 İlk Madde ve Malzeme'de durur,
    /// tüketildiğinde 740 Hizmet Üretim Maliyeti'ne yazılır.
    /// </summary>
    Consumable = 0,

    /// <summary>
    /// TİCARİ MAL — 153 Ticari Mallar'da durur, satıldığında
    /// 621 Satılan Ticari Mallar Maliyeti'ne yazılır.
    /// </summary>
    TradeGood = 1
}

/// <summary>
/// STOK KATEGORİSİ — SAP Classification benzeri özellik şablonu taşır.
///
/// SİSTEM GENELİ: kategori şirkete bağlı DEĞİL, kart bağlı. "Kablo
/// tavası" her şirkette aynı şeydir; iki ayrı sette tutmak mükerrer
/// bakım ve zamanla tutarsız özellik listeleri doğururdu.
/// </summary>
public sealed class InventoryCategory : BaseEntity
{
    /// <summary>Makine tarafı kimlik (KABLO_TAVASI). Değişmez.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Kullanıcının gördüğü ad (Kablo Tavası).</summary>
    public string Name { get; set; } = string.Empty;

    public InventoryCategoryKind Kind { get; set; } = InventoryCategoryKind.Standard;

    /// <summary>
    /// Muhasebe karşılığı. VARSAYILAN SARF — bilinçli olarak en
    /// dar/güvenli seçenek. Ticari mala geçiş ayrı bir uçtan ve ayrı
    /// bir izinle yapılır; kategori oluşturma isteği bu alanı ALMAZ.
    ///
    /// Kategori sistem geneli olduğu halde bu alanın burada durması
    /// sorun değil: 150/153 tekdüzen hesap planı kodlarıdır, her
    /// şirkette aynı anlama gelir. Kod → hesap kimliği çözümü
    /// şirket bazında yapılır.
    /// </summary>
    public InventoryAccountingKind AccountingKind { get; set; }
        = InventoryAccountingKind.Consumable;

    /// <summary>Listede görünme sırası.</summary>
    public int SortOrder { get; set; }

    public ICollection<InventoryCategoryUnit> AllowedUnits { get; set; }
        = new List<InventoryCategoryUnit>();

    public ICollection<InventoryAttribute> Attributes { get; set; }
        = new List<InventoryAttribute>();
}

/// <summary>
/// Kategorinin İZİN VERDİĞİ birimlerden biri.
///
/// NEDEN LİSTE, NEDEN TEK ALAN DEĞİL: kategorilerin çoğu tek birimli
/// ama hepsi değil. Topraklama hem metre (bakır şerit) hem adet
/// (toprak çubuğu) taşır; sarf malzemesi kg, paket ve adet olabilir.
/// Tek alan olsaydı bu kategoriler ya bölünecek ya birim serbest
/// bırakılacaktı — ikisi de kötü.
///
/// BİRİM KİLİDİ KART DÜZEYİNDE: kart açılırken bu listeden BİR birim
/// seçilir ve bir daha değişmez; hareket girişi kartın birimini
/// kullanır, seçim sunmaz.
/// </summary>
public sealed class InventoryCategoryUnit : BaseEntity
{
    public Guid InventoryCategoryId { get; set; }
    public InventoryCategory InventoryCategory { get; set; } = null!;

    public string Unit { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

/// <summary>
/// Kategoriye ait bir ÖZELLİK tanımı (Ölçü, Kaplama, Amper…).
///
/// Değerler serbest yazılmaz; <see cref="InventoryAttributeOption"/>
/// listesinden seçilir. Serbest yazım "200mm" / "200 mm" / "200MM"
/// gibi üç ayrı gerçek üretir ve mükerrer engeli çalışamaz.
/// </summary>
public sealed class InventoryAttribute : BaseEntity
{
    public Guid InventoryCategoryId { get; set; }
    public InventoryCategory InventoryCategory { get; set; } = null!;

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    /// <summary>
    /// Zorunlu özellik kart açılırken boş bırakılamaz. Ad üretiminde
    /// ve mükerrer kontrolünde hepsi kullanılır.
    /// </summary>
    public bool IsRequired { get; set; } = true;

    public ICollection<InventoryAttributeOption> Options { get; set; }
        = new List<InventoryAttributeOption>();
}

/// <summary>
/// Bir özelliğin seçilebilir değeri.
/// </summary>
public sealed class InventoryAttributeOption : BaseEntity
{
    public Guid InventoryAttributeId { get; set; }
    public InventoryAttribute InventoryAttribute { get; set; } = null!;

    /// <summary>Kimlik değeri (200). Mükerrer kontrolü bunu kullanır.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// ADA GİREN metin (200mm). Boşsa <see cref="Value"/> kullanılır.
    ///
    /// İkisi ayrı: "200" ile "200mm" aynı değerdir ama ada birimiyle
    /// girmesi gerekir; değeri "200mm" yapmak arama ve karşılaştırmayı
    /// zorlaştırırdı.
    /// </summary>
    public string? Display { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>
/// Bir stok kartının bir özelliğe verdiği değer.
/// </summary>
public sealed class InventoryItemAttributeValue : BaseEntity
{
    public Guid InventoryItemId { get; set; }
    public InventoryItem InventoryItem { get; set; } = null!;

    public Guid InventoryAttributeId { get; set; }
    public InventoryAttribute InventoryAttribute { get; set; } = null!;

    public Guid InventoryAttributeOptionId { get; set; }
    public InventoryAttributeOption InventoryAttributeOption { get; set; } = null!;
}
