namespace EnderunAI.Api.Services.Notifications;

/// <summary>
/// Hatırlatma pencereleri — kaç gün kala uyarılacağı.
///
/// TEK YER: bütün kaynaklar buradan okuyor. Eşikler kaynakların
/// içine dağılsaydı "çekte 7 gün, kredi taksitinde 5 gün" gibi
/// sessiz bir tutarsızlık doğar ve kimse hangisinin doğru olduğunu
/// bilemezdi.
/// </summary>
public static class NotificationWindow
{
    /// <summary>
    /// Vade kalemleri: 7 / 3 / 1 gün kala uyarı, şiddeti artarak.
    /// Tek eşik olsaydı ya çok erken uyarıp gürültü yapardı ya da
    /// çok geç uyarıp iş kaçırtırdı.
    /// </summary>
    public const int DueEarlyDays = 7;
    public const int DueSoonDays = 3;
    public const int DueImminentDays = 1;

    /// <summary>
    /// Belge geçerliliği: 30 gün.
    ///
    /// İSG'deki <c>IsgValidityCalculator.WarningDays</c> ile AYNI
    /// SAYI olmak zorunda — ikinci bir eşik açılsaydı aynı sertifika
    /// panelde "yakında bitiyor", bildirimde "hâlâ geçerli" görünür
    /// ve kullanıcı hangisine inanacağını bilemezdi.
    /// </summary>
    public const int DocumentExpiryDays = Isg.IsgValidityCalculator.WarningDays;

    /// <summary>
    /// Onay bekleyen talepler: 2 günden eskiler. Bekleyen her talep
    /// anında bildirim üretseydi, aynı gün onaylanacak işler için de
    /// gürültü çıkardı.
    /// </summary>
    public const int PendingApprovalDays = 2;

    /// <summary>
    /// Vadeye kalan güne göre şiddet. Vadesi geçmiş kalem her zaman
    /// kritik: geciken bir ödeme, yaklaşan bir ödemeden daha acildir.
    /// </summary>
    public static Models.Notifications.NotificationSeverity SeverityForDue(
        int daysRemaining) => daysRemaining switch
    {
        < 0 => Models.Notifications.NotificationSeverity.Critical,
        <= DueImminentDays => Models.Notifications.NotificationSeverity.Critical,
        <= DueSoonDays => Models.Notifications.NotificationSeverity.Warning,
        _ => Models.Notifications.NotificationSeverity.Info
    };

    /// <summary>"3 gün kaldı" / "2 gün gecikti" — insan diliyle.</summary>
    public static string DueLabel(int daysRemaining) => daysRemaining switch
    {
        < 0 => $"{Math.Abs(daysRemaining)} gün gecikti",
        0 => "bugün",
        1 => "yarın",
        _ => $"{daysRemaining} gün kaldı"
    };
}
