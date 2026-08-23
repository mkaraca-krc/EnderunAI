namespace EnderunAI.Api.Models;

/// <summary>
/// YORUM — GÖREVİN VE KAYDIN ALTINDAKİ TEK ZAMAN ÇİZELGESİ.
///
/// TEMEL FİKİR: görev, atanmış ve terminli bir yorumdur. İkisi ayrı
/// sistem değil; kaydın altında yorumlar ve görevler kronolojik, iç
/// içe duruyor.
///
/// İŞE BAĞLI: serbest sohbet yok. Her yorum ya bir KAYDA
/// (proje, çek, hakediş, teklif, satın alma, mal kabul) ya da bir
/// GÖREVE bağlı. Bağsız yorum yazılamaz — yazılabilseydi sistem
/// zamanla ikinci bir mesajlaşma uygulamasına dönerdi.
/// </summary>
public sealed class TaskComment : BaseEntity
{
    /*
     * KAPSAM İLK GÜNDEN İÇERİDE.
     *
     * Şirket kimliği sonradan eklenen bir tabloya kapsam süzgeci
     * takmak, G3 paketinin tamamının konusuydu: 480 kapsamsız okuma
     * o yüzden birikmişti. Yeni tablo kapsamlı doğuyor ve cırcır
     * çizgisine tek satır borç eklemiyor.
     */
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>
    /// Yorumun bağlandığı kayıt: varlık tipi + kayıt kimliği.
    /// Ortak bileşen bu ikiliyle her ekrana takılabiliyor.
    ///
    /// GÖREVE YORUM: `EntityType = "WorkTask"`. Ayrı bir alan
    /// açılmadı — görev de bir kayıt; iki farklı bağlama biçimi
    /// olsaydı zaman çizelgesi ikiye bölünürdü.
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }

    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Yorumda @ ile anılan kullanıcılar (virgülle ayrılmış kimlikler).
    /// Bildirim bu listeden üretiliyor.
    /// </summary>
    public string? MentionedUserIds { get; set; }

    /// <summary>
    /// DÜZENLEME PENCERESİ: ilk 15 dakika. Sonrası yeni yorum.
    ///
    /// Süresiz düzenleme, konuşmanın geçmişini değiştirilebilir kılar:
    /// birinin cevap verdiği cümle sonradan başka bir cümleye
    /// dönüşebilir. On beş dakika, yazım hatasını düzeltmeye yeter,
    /// tartışmayı yeniden yazmaya yetmez.
    /// </summary>
    public DateTime? EditedAtUtc { get; set; }
    public int EditCount { get; set; }

    /// <summary>
    /// YORUM SİLİNMEZ, GİZLENİR. Silme, cevap verilmiş bir cümleyi
    /// konuşmadan çıkarır ve kalan cevapları anlamsızlaştırır.
    /// Gizlenen yorum "silindi" olarak görünür; kim ve ne zaman
    /// gizlediği duruyor.
    /// </summary>
    public DateTime? HiddenAtUtc { get; set; }
    public Guid? HiddenByUserId { get; set; }
}
