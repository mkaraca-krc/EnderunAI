namespace EnderunAI.Api.Services.Notifications;

/// <summary>
/// GÜNLÜK ÖZET KADEMESİ — TEK DEĞİŞKEN, DÖRT DURUM.
///
/// `DAILY_SUMMARY_MODE` ortam değişkeninden okunuyor.
///
/// NEDEN KADEMELİ: bu özellik canlıya çıktığı gün gerçek insanlara
/// e-posta göndermeye başlıyor. "Deploy et ve gör" burada kabul
/// edilemez — yanlış içerik ya da yanlış alıcı, geri alınamaz.
/// Kademeler, her adımı ayrı ayrı doğrulamayı mümkün kılıyor.
/// </summary>
public enum DailySummaryMode
{
    /// <summary>
    /// Tarama HİÇ KOŞMAZ. VARSAYILAN — deploy bu durumda çıkıyor.
    /// Değişken tanımsızsa da bu geçerli: sessizce e-posta göndermeye
    /// başlamak, tanımsız bir değişkenin kabul edilebilir sonucu
    /// değil.
    /// </summary>
    Off = 0,

    /// <summary>
    /// Tarama koşar, E-POSTA GÖNDERMEZ. Kime ne gideceğini sunucu
    /// günlüğüne yazar: kaç kişi, her birine kaç görev ve kaç
    /// bildirim.
    ///
    /// E-POSTA ADRESİ KAYDA YAZILMAZ — kullanıcı adı yeterli.
    /// </summary>
    DryRun = 1,

    /// <summary>
    /// Yalnız `DAILY_SUMMARY_TEST_RECIPIENTS` listesindeki adreslere
    /// gönderir. Gerçek e-postanın biçimini görmek için.
    /// </summary>
    Test = 2,

    /// <summary>Herkese gönderir.</summary>
    On = 3
}
