using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Notifications;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Notifications;

/// <summary>
/// GÖREV BİLDİRİMLERİ — KİŞİSEL VE OLAY ANINDA.
///
/// MEVCUT MOTORDAN FARKI: `NotificationScanner` ŞİRKET satırı üretir
/// ve günde bir kez tarar; bir çek vadesi herkesi ilgilendirdiği için
/// o tasarım doğru. Ama "görev sana atandı" BİR KİŞİYE aittir ve
/// 24 saat sonra düşemez.
///
/// Bu yüzden kişisel satır: `Notification.TargetUserId` dolu ve okuma
/// durumu `NotificationRecipient` üzerinden izleniyor. Şirket
/// satırında tek `ReadAtUtc` var; bir kişi okuyunca herkes için
/// okunmuş sayılırdı.
///
/// KURAL (DURUM.md): bundan sonra eklenecek HER yeni bildirim kişisel
/// modelde doğar. Şirket satırı yalnız mevcut dört tarama kaynağı
/// için, geçici.
/// </summary>
public interface ITaskNotificationWriter
{
    /// <summary>
    /// Kişisel bildirim yazar. ASIL İŞLEMİ ÇÖKERTMEZ: hata olursa
    /// yutulmaz ama fırlatılmaz da — kayda düşer.
    /// </summary>
    Task WriteAsync(
        Guid companyId,
        Guid targetUserId,
        string type,
        Guid sourceId,
        string periodKey,
        string title,
        string? detail,
        string? targetPath,
        NotificationSeverity severity,
        CancellationToken cancellationToken);
}

public sealed class TaskNotificationWriter(
    AppDbContext db,
    ILogger<TaskNotificationWriter> logger) : ITaskNotificationWriter
{
    public async Task WriteAsync(
        Guid companyId,
        Guid targetUserId,
        string type,
        Guid sourceId,
        string periodKey,
        string title,
        string? detail,
        string? targetPath,
        NotificationSeverity severity,
        CancellationToken cancellationToken)
    {
        try
        {
            /*
             * MÜKERRER ENGELİ VERİTABANINDA.
             *
             * (CompanyId, Type, SourceId, PeriodKey) benzersiz.
             * Zaman tabanlı uyarılarda PeriodKey = TERMİN TARİHİ:
             *   - Tarayıcı günde beş kez koşsa da tek bildirim.
             *   - Termin değişirse PeriodKey değişir ve YENİ uyarı
             *     yazılabilir; eski uyarı geçersizleşmiş olur.
             *
             * Uygulama tarafında "var mı" diye bakmak yetmezdi: iki
             * eşzamanlı tarama ikisi de "yok" görür ve ikisi de yazar.
             */
            var mevcut = await db.Notifications
                .FirstOrDefaultAsync(
                    x => x.CompanyId == companyId &&
                         x.Type == type &&
                         x.SourceId == sourceId &&
                         x.PeriodKey == periodKey,
                    cancellationToken);

            if (mevcut is not null)
            {
                // Zaten var: son görülme tazeleniyor, YENİSİ AÇILMIYOR.
                mevcut.LastSeenAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            var bildirim = new Notification
            {
                CompanyId = companyId,
                TargetUserId = targetUserId,
                Type = type,
                SourceId = sourceId,
                PeriodKey = periodKey,
                Title = title,
                Detail = detail,
                TargetPath = targetPath,
                Severity = severity,
                Status = NotificationStatus.Open
            };

            db.Notifications.Add(bildirim);

            db.NotificationRecipients.Add(new NotificationRecipient
            {
                Notification = bildirim,
                UserId = targetUserId
            });

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            /*
             * YARIŞ: iki eşzamanlı yazma. Benzersiz kısıt ikincisini
             * reddetmiş olabilir — o zaman kayıt ZATEN var ve
             * yapılacak bir şey yok; bu bir hata değil, kısıtın işini
             * yapması.
             *
             * `catch ... when (await ...)` yazılamıyor (C# filtre
             * ifadesinde await yasak), bu yüzden ayrım gövdede.
             */
            if (await ZatenVarAsync(companyId, type, sourceId, periodKey, cancellationToken))
                return;

            logger.LogError(
                exception,
                "Bildirim yazılamadı (veritabanı). Type={Type} SourceId={SourceId}",
                type, sourceId);

            await HataKaydiYazAsync(
                companyId, targetUserId, type, sourceId, exception, cancellationToken);
        }
        catch (Exception exception)
        {
            /*
             * BİLDİRİM ASIL İŞLEMİ ÇÖKERTMEZ AMA SESSİZ DE KALMAZ.
             *
             * İki yanlış yol vardı:
             *   - Hata fırlatmak: bildirim yüzünden görev atanamazdı.
             *   - Sessizce yutmak: görev atanır, kimse haber almaz ve
             *     KİMSE FARK ETMEZ. Daha kötüsü bu.
             *
             * Üçüncü yol: hata yutulmuyor, KAYDA düşüyor. Sunucu
             * günlüğüne ve denetim kaydına. Bildirim tekrar
             * denenebilir — kayıt yazılmadığı için sonraki tarama
             * aynı bildirimi yeniden üretir.
             */
            logger.LogError(
                exception,
                "Bildirim yazılamadı. Type={Type} SourceId={SourceId} " +
                "TargetUserId={TargetUserId}",
                type, sourceId, targetUserId);

            await HataKaydiYazAsync(
                companyId, targetUserId, type, sourceId, exception, cancellationToken);
        }
    }

    private async Task<bool> ZatenVarAsync(
        Guid companyId, string type, Guid sourceId, string periodKey,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();

        return await db.Notifications
            .AsNoTracking()
            .AnyAsync(
                x => x.CompanyId == companyId &&
                     x.Type == type &&
                     x.SourceId == sourceId &&
                     x.PeriodKey == periodKey,
                cancellationToken);
    }

    /// <summary>
    /// Bildirim yazımının başarısızlığı da bir olaydır: görülmeli.
    /// Yalnız sunucu günlüğüne yazılsaydı, kimse bakmadığı sürece
    /// "bildirim gelmiyor" şikâyetinin sebebi bulunamazdı.
    /// </summary>
    private async Task HataKaydiYazAsync(
        Guid companyId,
        Guid targetUserId,
        string type,
        Guid sourceId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            // Değişiklik izleyici bozulmuş olabilir: temiz bir sayfa.
            db.ChangeTracker.Clear();

            db.SecurityAuditEvents.Add(new SecurityAuditEvent
            {
                Action = "NotificationWriteFailed",
                EntityType = "Notification",
                EntityId = sourceId,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    summary = "Bildirim yazılamadı; asıl işlem etkilenmedi.",
                    type,
                    companyId,
                    targetUserId,
                    hata = exception.GetType().Name,
                    mesaj = exception.Message
                }),
                OccurredAtUtc = DateTime.UtcNow
            });

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ikincil)
        {
            // Hata kaydı da yazılamıyorsa yapılabilecek son şey
            // günlüğe yazmak. Burada fırlatmak, asıl işlemi
            // çökertmemek kuralını çiğnerdi.
            logger.LogError(
                ikincil, "Bildirim hata kaydı da yazılamadı. Type={Type}", type);
        }
    }
}
