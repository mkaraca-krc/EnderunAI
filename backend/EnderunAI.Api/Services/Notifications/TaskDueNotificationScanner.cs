using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Notifications;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Notifications;

/// <summary>
/// TERMİN UYARILARI — ZAMANA BAĞLI İKİ TETİKLEYİCİ.
///
/// "Termine bir gün kaldı" ve "termin geçti" bir EYLEMİN sonucu
/// değil; hiçbir şey olmadan gün geçmesiyle doğuyorlar. Bu yüzden
/// tarayıcı gerekiyor — diğer dört tetikleyici olay anında yazılıyor.
///
/// TEK SEFER ÇALIŞIR: mükerrer engeli veritabanında,
/// (CompanyId, Type, SourceId, PeriodKey) benzersiz kısıtında.
/// PeriodKey olarak TERMİN TARİHİ kullanılıyor:
///   - Tarayıcı günde beş kez koşsa da tek bildirim üretilir.
///   - Servis yeniden başlasa da aynı.
///   - Termin DEĞİŞİRSE PeriodKey değişir ve yeni uyarı yazılabilir;
///     eski uyarı kendiliğinden geçersizleşir.
///
/// Mükerrer bildirim, insanların zili tamamen kapatmasına yol açar —
/// yani bildirim sisteminin kendisini işlevsiz kılar.
/// </summary>
public sealed class TaskDueNotificationScanner(
    AppDbContext db,
    ITaskNotificationWriter writer) : ITaskDueNotificationScanner
{
    /*
     * KAPANMIŞ GÖREVLER UYARI ÜRETMEZ.
     *
     * `Approved` ve `Cancelled` kapanmış sayılıyor. `Completed`
     * AÇIK sayılıyor: yapan bitirdi ama gönderen onaylamadı, yani iş
     * hâlâ birinin önünde — terminini geçirmesi anlamlı bir uyarı.
     */
    private static readonly WorkTaskStatus[] KapanmisDurumlar =
    [
        WorkTaskStatus.Approved,
        WorkTaskStatus.Cancelled
    ];

    public async Task<int> ScanAsync(CancellationToken cancellationToken)
    {
        var simdi = DateTime.UtcNow;
        var yarin = simdi.AddDays(1);

        var adaylar = await db.WorkTasks
            .AsNoTracking()
            .Where(x =>
                x.DueDate != null &&
                x.AssignedToUserId != null &&
                !KapanmisDurumlar.Contains(x.Status) &&
                x.DueDate < yarin)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                x.TaskNumber,
                x.Title,
                x.DueDate,
                x.AssignedToUserId
            })
            .ToListAsync(cancellationToken);

        var yazilan = 0;

        foreach (var gorev in adaylar)
        {
            var termin = gorev.DueDate!.Value;

            // PERİYOT ANAHTARI TERMİN TARİHİ — mükerrer engelinin
            // ve "termin değişince yeniden uyar" davranışının temeli.
            var periyot = termin.ToString("yyyy-MM-dd");

            var gecti = termin < simdi;

            await writer.WriteAsync(
                gorev.CompanyId,
                gorev.AssignedToUserId!.Value,
                gecti ? TaskNotificationTypes.Overdue : TaskNotificationTypes.DueSoon,
                gorev.Id,
                periyot,
                gecti
                    ? $"Görev termini geçti: {gorev.TaskNumber}"
                    : $"Görev termini yarın: {gorev.TaskNumber}",
                gorev.Title,
                $"/gorevler/{gorev.Id}",
                gecti ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                cancellationToken);

            yazilan++;
        }

        return yazilan;
    }
}
