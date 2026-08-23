namespace EnderunAI.Api.Models.Notifications;

/// <summary>
/// BİLDİRİMİN KİŞİSEL OKUMA DURUMU.
///
/// NEDEN AYRI TABLO: `Notification` satırı ŞİRKETE ait ve tek bir
/// `ReadAtUtc` taşıyor — modelin kendi yorumu bunu açıkça söylüyor
/// ("Kullanıcı başına satır üretilseydi tek bir çek vadesi için
/// onlarca kopya doğardı"). O tasarım tarama kaynakları için doğru:
/// bir çek vadesi herkesi ilgilendirir.
///
/// AMA M1 OLAYLARI KİŞİSEL: "görev sana atandı" bildirimi bir kişiye
/// aittir ve okunma durumu o kişiye özeldir. Şirket satırında tek
/// `ReadAtUtc` olsaydı bir kişi okuyunca herkes için okunmuş sayılırdı.
///
/// İKİLİ MODEL GEÇİCİ: şirket satırı YALNIZ mevcut dört tarama
/// kaynağı için duruyor. Bundan sonra eklenecek her yeni bildirim
/// kişisel modelde doğar (DURUM.md'ye yazıldı) — yoksa altı ay sonra
/// hangisinin doğru olduğu bilinmez.
/// </summary>
public sealed class NotificationRecipient : BaseEntity
{
    public Guid NotificationId { get; set; }
    public Notification Notification { get; set; } = null!;

    public Guid UserId { get; set; }

    public DateTime? ReadAtUtc { get; set; }
    public DateTime? DismissedAtUtc { get; set; }
}
