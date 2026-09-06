namespace EnderunAI.Api.Models;

/// <summary>
/// Kullanıcının arayüz tercihleri — menü daraltılmış mı, hangi sayfalar
/// favori.
///
/// NEDEN SUNUCUDA: tercih tarayıcıda tutulsaydı kullanıcı ofisteki
/// bilgisayarından şantiyedeki tablete geçtiğinde favorilerini
/// kaybederdi; tarayıcı verisi temizlendiğinde de silinirdi. Kullanıcıya
/// ait bir ayar, kullanıcıyla birlikte gezmeli.
///
/// GÜVENLİK SINIRI DEĞİLDİR: favori bir yol, o sayfaya erişim hakkı
/// VERMEZ. Menü favorileri de aynı yol→izin haritasından geçiriyor,
/// uçlar da kendi yetkisini kendi kontrol ediyor. Burada tutulan şey
/// yalnızca "kullanıcı bunu kısayolda görmek istiyor" bilgisi.
/// </summary>
public sealed class UserUiPreference : BaseEntity
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    /// <summary>Yan menü daraltılmış mı.</summary>
    public bool SidebarCollapsed { get; set; }

    /// <summary>
    /// GÜNLÜK E-POSTA ÖZETİ İSTİYOR MU. Varsayılan: EVET.
    ///
    /// Kapatan kişiye e-posta gitmiyor; ZİL VE UYGULAMA İÇİ BİLDİRİM
    /// ETKİLENMİYOR — yalnız e-posta.
    ///
    /// NEDEN GEREKLİ: bu seçenek olmasaydı e-postayı istemeyen kişi
    /// onu filtreye atardı; sonra gerçekten önemli bir e-posta da
    /// aynı filtreye düşerdi. Kapatma seçeneği, sessizce yok sayılan
    /// bir kanaldan iyidir.
    /// </summary>
    public bool DailySummaryEmailEnabled { get; set; } = true;

    /// <summary>
    /// Favori sayfa yolları, kullanıcının verdiği sırayla. Yol metni
    /// olarak saklanır: menü yeniden düzenlendiğinde kimlik kaymasın
    /// diye kayıt kimliğine bağlanmıyor. Karşılığı olmayan bir yol
    /// arayüzde sessizce elenir.
    /// </summary>
    public List<string> FavoritePaths { get; set; } = [];

    /// <summary>
    /// MESAJ PANELİ AÇIK MI (M3/2c-1).
    ///
    /// NEDEN SUNUCUDA: bu dosyanın başındaki gerekçenin aynısı —
    /// kullanıcı ofisteki bilgisayarından şantiyedeki tablete geçtiğinde
    /// paneli yeniden açmak zorunda kalmasın. `localStorage` bunu
    /// veremezdi ve aynı sorunun ikinci çözümünü doğururdu.
    ///
    /// YAZMA SIKLIĞI — YALNIZ KAPANIŞTA. Panel açılıp kapandıkça
    /// yazılsaydı, "açtım hemen kapattım" iki yazma üretirdi. Açılış
    /// zaten bir sonraki kapanışta kaydediliyor.
    /// </summary>
    public bool MessagePanelOpen { get; set; }

    /// <summary>
    /// SON AÇIK KONUŞMA (M3/2c-1).
    ///
    /// YABANCI ANAHTAR DEĞİL — BİLEREK. Konuşma silinirse ya da
    /// kullanıcı ondan çıkarılırsa bu değer boşta kalır; arayüz
    /// karşılığı olmayan bir kimliği SESSİZCE eler. `FavoritePaths`
    /// için verilen kararın aynısı: *"karşılığı olmayan bir yol
    /// arayüzde sessizce elenir."*
    ///
    /// Yabancı anahtar konsaydı, bir konuşmanın silinmesi tercih
    /// satırını da düşürür ya da silmeyi engellerdi — ikisi de bir
    /// kısayol tercihinin hak etmediği ağırlıkta.
    ///
    /// YAZMA SIKLIĞI — 1 SANİYE GECİKMELİ. Hızlı ardışık geçişlerde
    /// yalnız sonuncusu anlamlıdır; her seçimde yazmak günde yüzlerce
    /// gereksiz güncelleme üretirdi.
    /// </summary>
    public Guid? LastConversationId { get; set; }
}
