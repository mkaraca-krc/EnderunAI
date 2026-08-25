namespace EnderunAI.Api.Models.Messaging;

/// <summary>Konuşma türü.</summary>
public enum ConversationType
{
    /// <summary>İki kişi arasında.</summary>
    Direct = 0,

    /// <summary>Departman ya da konu kanalı.</summary>
    Channel = 1
}

/// <summary>
/// KONUŞMA.
///
/// ERİŞİM POLİTİKASI KODA GÖMÜLÜ: bir konuşmayı YALNIZ ÜYELERİ
/// okuyabilir. Genel Müdür ve Admin dahil kimsenin istisnası yok —
/// global veri kapsamı bu kapıyı AÇMAZ. Kapsam süzgeci "hangi
/// şirketin verisi" sorusunu cevaplar; üyelik "bu konuşma senin mi"
/// sorusunu. İkisi ayrı sorulardır ve mesajlaşmada ikincisi
/// belirleyicidir.
///
/// İstisna akışı (gerekçe + iki bağımsız yetkili onayı + süreli +
/// denetim kaydı, konuşmanın tarafı onaylayamaz) bu fazda
/// YAZILMADI; model onu sonradan taşıyabilecek şekilde kuruldu.
/// </summary>
public sealed class Conversation : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public ConversationType Type { get; set; }

    /// <summary>Kanal adı. Birebir konuşmada boş — başlık taraflardır.</summary>
    public string? Title { get; set; }

    /// <summary>Departman kanalıysa hangi departman.</summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>
    /// Son mesaj zamanı — konuşma listesini sıralamak için.
    ///
    /// Mesaj tablosuna `MAX(CreatedAtUtc)` sorgusu atmak, liste
    /// açılışında konuşma başına bir sorgu demekti. Bu alan mesaj
    /// yazılırken güncelleniyor.
    /// </summary>
    public DateTime? LastMessageAtUtc { get; set; }

    /// <summary>
    /// ARŞİVLENMİŞ KONUŞMA — SİLİNMİŞ DEĞİL.
    ///
    /// Mesaj silme mekanizması kurulmadı (karar). Arşiv yalnızca
    /// "listede görünmesin" demek; içerik ve erişim aynen duruyor.
    /// </summary>
    public bool IsArchived { get; set; }

    public ICollection<ConversationMember> Members { get; set; } = new List<ConversationMember>();
}

/// <summary>
/// KONUŞMA ÜYELİĞİ — ERİŞİMİN TEK KAYNAĞI.
///
/// `LeftAtUtc` dolu olan üye ARTIK OKUYAMAZ. Satır silinmiyor:
/// kimin ne zaman ayrıldığı, geçmişte kimin okuduğunu açıklayan tek
/// kayıt. Silinseydi "bu mesajı o zaman kim görüyordu" sorusu
/// cevapsız kalırdı.
/// </summary>
public sealed class ConversationMember : BaseEntity
{
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;

    public Guid UserId { get; set; }

    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Ayrıldıysa dolu. Dolu olan üye okuyamaz.</summary>
    public DateTime? LeftAtUtc { get; set; }

    /// <summary>
    /// Bu üyenin okuduğu son mesajın zamanı. Okunmamış sayısı
    /// buradan hesaplanıyor; mesaj başına "okundu" satırı tutmak
    /// mesaj sayısı × üye sayısı kadar satır üretirdi.
    /// </summary>
    public DateTime? LastReadAtUtc { get; set; }
}

/// <summary>
/// MESAJ — EKLENİR, SİLİNMEZ.
///
/// Silme mekanizması kurulmadı (karar). `HiddenAtUtc` yalnızca
/// yazanın kendi mesajını gizlemesi; gövde saklanır ve kimin
/// gizlediği durur — yorum bileşenindeki kararın aynısı: cevap
/// verilmiş bir cümleyi konuşmadan çıkarmak kalan cevapları
/// anlamsızlaştırır.
/// </summary>
public sealed class Message : BaseEntity
{
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;

    /// <summary>
    /// Şirket, konuşmadan KOPYALANIYOR.
    ///
    /// Kapsam süzgeci mesaj sorgusunda konuşmaya JOIN atmak zorunda
    /// kalmasın diye. Konuşmanın şirketi değişmiyor, yani kopya
    /// bayatlamaz.
    /// </summary>
    public Guid CompanyId { get; set; }

    public Guid SenderUserId { get; set; }

    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// ARAMA İÇİN KATLANMIŞ GÖVDE — VERİTABANI ÜRETİR.
    ///
    /// `enderun_fold` ile: `translate(lower(...), 'ışğüöçâîû',
    /// 'isguocaiu')`. Arayüzdeki `lib/search/fold.ts` ile BİREBİR
    /// aynı kural; ayrışırlarsa aynı arama bir yerde bulur diğerinde
    /// bulamaz.
    ///
    /// Uygulama katmanı şifrelemesi YOK (karar) — arama korunuyor.
    /// Şifrelenseydi bu kolon ve üstündeki trigram indeksi anlamsız
    /// olurdu.
    /// </summary>
    public string? SearchFold { get; private set; }

    public DateTime? EditedAtUtc { get; set; }
    public int EditCount { get; set; }

    public DateTime? HiddenAtUtc { get; set; }
    public Guid? HiddenByUserId { get; set; }
}

/// <summary>
/// DEPARTMAN DEĞİŞİKLİK TARİHÇESİ.
///
/// NEDEN ŞART: "ayrıldığı tarihe kadarki geçmişi görür" kuralı,
/// kişinin O TARİHTE hangi departmanda olduğunu bilmeden
/// hesaplanamaz. `personnel.DepartmentId` yalnız BUGÜNÜ söyler;
/// dünkü soruyu cevaplayamaz.
///
/// Satır SİLİNMEZ ve GÜNCELLENMEZ — yalnız eklenir.
/// </summary>
public sealed class PersonnelDepartmentHistory : BaseEntity
{
    public Guid PersonnelId { get; set; }
    public Personnel Personnel { get; set; } = null!;

    public Guid CompanyId { get; set; }

    /// <summary>Yeni departman. Boş = departmandan çıkarıldı.</summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>Önceki departman. İlk atamada boş.</summary>
    public Guid? PreviousDepartmentId { get; set; }

    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;

    public string? Reason { get; set; }
}
