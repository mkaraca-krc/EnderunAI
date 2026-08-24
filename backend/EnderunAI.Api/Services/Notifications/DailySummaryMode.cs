namespace EnderunAI.Api.Services.Notifications;

/// <summary>
/// GÜNLÜK ÖZET KADEMESİ — TEK DEĞİŞKEN, ÜÇ DURUM.
///
/// `DAILY_SUMMARY_MODE` ortam değişkeninden okunuyor; değer KAYNAK
/// KODA GÖMÜLMÜYOR. Tanımsızsa <see cref="Kapali"/>: sessizce
/// e-posta göndermeye başlamak, tanımsız bir değişkenin kabul
/// edilebilir sonucu değildir.
///
/// NEDEN KADEMELİ: bu özellik açıldığı gün gerçek insanlara e-posta
/// göndermeye başlıyor. "Aç ve gör" burada kabul edilemez — yanlış
/// içerik ya da yanlış alıcı geri alınamaz.
///
/// ÖNCEKİ SÜRÜMDE DÖRT KADEME VARDI (off/dryrun/test/on). `test`
/// kademesi kaldırıldı: yalnız belirli adreslere gönderim, gönderim
/// yolunu AÇIK tutmayı gerektiriyordu ve "kapalıyken hiç çağrılmaz"
/// güvencesini zayıflatıyordu. Artık iki net durum var — gönderim
/// yolu ya tamamen kapalı ya tamamen açık — ve arada gözlem için
/// <see cref="DryRun"/>.
/// </summary>
public enum DailySummaryMode
{
    /// <summary>
    /// ÖZET ÜRETİLMEZ, E-POSTA GİTMEZ. VARSAYILAN.
    /// Ortam değişkeni tanımsız ya da tanınmayan bir değerse de bu.
    ///
    /// DİKKAT — BU BİR ANA ŞALTER DEĞİL, GÖNDERİM KAPISIDIR.
    /// `Kapali` modda da şunlar KOŞMAYA DEVAM EDER:
    ///   - <c>TaskDueNotificationScanner</c> (termin uyarıları) —
    ///     bunlar uygulama içi bildirim, e-posta değil;
    ///   - <c>ScopeDeferralWatchdog</c> (G3 erteleme nöbetçisi) —
    ///     bu bir bildirim değil, GÜVENLİK UYARISI.
    ///
    /// Bir güvenlik uyarısını e-posta tercihine bağlamak, "e-postalar
    /// rahatsız ediyor, kapat" diyen kişinin farkında olmadan
    /// nöbetçiyi de susturması demek olurdu. Bu yüzden bayrak yalnız
    /// GÖNDERİMİ kesiyor.
    ///
    /// Bu yorum bir kez yanlıştı: "Tarama HİÇ KOŞMAZ" yazıyordu ve
    /// koddaki davranışla çelişiyordu (bayrak önce ana şalter olarak
    /// tasarlanmış, sonra gönderim kapısına daraltılmış, yorum
    /// güncellenmemişti). Artık iddia yorumda değil TESTTE duruyor:
    /// <c>Kapali_TaramaVeBekciyiYineDeKosturur</c>, çağrı sayacıyla.
    /// </summary>
    Kapali = 0,

    /// <summary>
    /// Özet ÜRETİLİR, e-posta GÖNDERİLMEZ.
    ///
    /// SMTP istemcisi HİÇ ÇAĞRILMAZ — sahte bir istemciyle
    /// değiştirilmez, çağrı yoluna hiç girilmez. Fark önemli: sahte
    /// istemci "gönderim kodu çalıştı ama bir şey olmadı" demektir;
    /// burada gönderim kodu HİÇ ÇALIŞMIYOR.
    /// </summary>
    DryRun = 1,

    /// <summary>Herkese gönderir.</summary>
    Acik = 2
}
