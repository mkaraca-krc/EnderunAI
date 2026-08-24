namespace EnderunAI.Api.Services.Notifications;

/*
 * GÜNLÜK TURUN MODDAN BAĞIMSIZ ADIMLARI.
 *
 * Bu iki arayüz yalnızca test edilebilirlik için var ve bilerek
 * dar tutuldu: `DailySummaryBackgroundService`'in bu adımları
 * `DAILY_SUMMARY_MODE` ne olursa olsun çağırdığı, çağrı sayacı
 * tutan sahtelerle kanıtlanabilsin diye (DURUM.md §5 kural 23).
 *
 * NEDEN ETKİYE BAKAN TEST YETMEDİ: `ExecuteAsync` turu bir
 * `catch (Exception)` içinde koşturuyor ("bir günün hatası ertesi
 * günü kaybettirmesin"). Yutulan bir hata, etkiye bakan testi de
 * sessizce yanıltırdı. Sayaç yutulamaz.
 *
 * Somut sınıflar `sealed` KALDI — sırf test için mühür açmak
 * yerine dar arayüz eklendi.
 */

/// <summary>Termin uyarısı taraması — uygulama içi bildirim üretir.</summary>
public interface ITaskDueNotificationScanner
{
    Task<int> ScanAsync(CancellationToken cancellationToken);
}

/// <summary>G3 kapsam erteleme nöbetçisi — güvenlik uyarısı yazar.</summary>
public interface IScopeDeferralWatchdog
{
    Task CheckAsync(CancellationToken cancellationToken);
}

/// <summary>
/// ÖZET/GÖNDERİM YOLU — moddan ETKİLENEN tek adım.
///
/// Bu arayüz de sayaç içindir. Önce "test kapsayıcısına
/// `DailySummaryService`'i hiç kaydetmem, girilirse
/// `GetRequiredService` fırlar" diye dolaylı bir kanıt kullanılmıştı.
/// O kanıt GEÇERSİZ: üretim zincirinde
/// `DailySummaryBackgroundService.ExecuteAsync` (satır 38) turu
/// `catch (Exception)` ile sarıyor, yani fırlatmaya dayanan kanıt
/// yalnızca testin o sarmalayıcıyı atlaması sayesinde çalışıyordu —
/// biri testi `ExecuteAsync` üzerinden koşturmaya çevirse kanıt
/// sessizce buharlaşırdı.
///
/// Artık tek kanıt ÇAĞRI SAYACI. (DURUM.md §5 kural 23)
/// </summary>
public interface IDailySummaryRunner
{
    Task<int> RunAsync(DailySummaryMode mode, CancellationToken cancellationToken);
}
