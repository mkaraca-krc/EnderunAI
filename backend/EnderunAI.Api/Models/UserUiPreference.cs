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
}
