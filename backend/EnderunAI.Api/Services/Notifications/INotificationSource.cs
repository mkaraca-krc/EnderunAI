using EnderunAI.Api.Models.Notifications;

namespace EnderunAI.Api.Services.Notifications;

/// <summary>
/// Bir taramanın ürettiği tek aday bildirim.
///
/// Aday, kaydın kendisi DEĞİL: motor bunu tekilleştirme anahtarıyla
/// (<see cref="Type"/> + <see cref="SourceId"/> +
/// <see cref="PeriodKey"/>) mevcut kayda upsert eder.
/// </summary>
public sealed record NotificationCandidate(
    string Type,
    Guid? SourceId,
    string PeriodKey,
    string Title,
    string? Detail,
    NotificationSeverity Severity,
    string? TargetPath = null,
    DateTime? DueDate = null,
    /// <summary>Tutar içeren metin; yalnız AmountPermission ile görünür.</summary>
    string? AmountDetail = null,
    string? AmountPermission = null,
    /// <summary>
    /// Bildirimi görebilmek için gereken izin. Boşsa herkese açık.
    /// Finans bildirimi finansa, İK bildirimi İK'ya bununla gider.
    /// </summary>
    string? RequiredPermission = null);

/// <summary>Tarama bağlamı — hangi şirket, hangi gün.</summary>
public sealed record NotificationScanContext(Guid CompanyId, DateTime Today);

/// <summary>
/// Bildirim kaynağı.
///
/// DESEN BRİFİNGDEN ALINDI (<c>IHizirBriefingSource</c>): yeni bir
/// tetikleyici eklemek için tek yapılacak şey bu arayüzü uygulayan
/// bir sınıf yazıp DI'ya kaydetmek; tarama servisine dokunulmaz.
///
/// YETKİ KAYNAKTA DEĞİL BİLDİRİMDE: brifing kaynağı, kullanıcının
/// izni yoksa hiç çalıştırılmıyordu çünkü orada tarama isteği ATAN
/// kullanıcı adına yapılıyor. Burada tarama arka planda ve
/// kullanıcısız koşuyor; bu yüzden izin, üretilen bildirimin
/// <see cref="NotificationCandidate"/> üzerinden taşınıp OKUMA
/// anında süzülüyor. Sonuç aynı: kimse yetkisi dışındaki bir sayıyı
/// görmüyor.
/// </summary>
public interface INotificationSource
{
    /// <summary>Kaynağın anahtarı — günlükte ve testte bununla anılır.</summary>
    string Key { get; }

    /// <summary>
    /// Bu kaynağın ürettiği bildirim türleri.
    ///
    /// ADAYLARDAN TÜRETİLMİYOR: kaynak bu turda hiç aday üretmese
    /// bile kendi türlerini KAPATABİLMELİ. Adaylardan çıkarılsaydı,
    /// çözülen son iş (ödenen son çek) kapanmaz ve bildirim ilelebet
    /// açık kalırdı.
    /// </summary>
    IReadOnlyCollection<string> OwnedTypes { get; }

    Task<IReadOnlyList<NotificationCandidate>> BuildAsync(
        NotificationScanContext context,
        CancellationToken cancellationToken);
}
