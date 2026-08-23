using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Notifications;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Notifications;

/// <summary>Bir taramanın sonucu — günlüğe ve teste okunur çıktı.</summary>
public sealed record NotificationScanResult(
    int Created,
    int Updated,
    int Closed);

/// <summary>
/// Bildirimlerin kalıcı deposu ve tekilleştirme kuralı.
///
/// UPSERT: aday, anahtarıyla eşleşen kayda yazılır. Yeni satır ancak
/// eşleşme yoksa açılır. Bu, paketin ana kuralı — her tarama yeni
/// satır açsaydı bir haftalık vade uyarısı yedi kayıt üretir,
/// "okundu" her gece kaybolur ve bildirim merkezi çöp kutusuna
/// dönerdi.
///
/// KAYNAK KALKINCA KAPANIR: taramada görülmeyen açık bildirimler
/// <see cref="NotificationStatus.Closed"/> olur. Çek ödendiğinde,
/// belge yenilendiğinde ya da talep onaylandığında kullanıcının
/// ayrıca kapatması gerekmez.
/// </summary>
public sealed class NotificationStore(AppDbContext db)
{
    /// <summary>
    /// Bir kaynağın ürettiği adayları kalıcı hale getirir ve o
    /// kaynağın artık üretmediği açık bildirimleri kapatır.
    ///
    /// Kapatma KAYNAK BAZINDA: her kaynak yalnız kendi türlerini
    /// kapatır. Tüm türler tek seferde kapatılsaydı, bir kaynak hata
    /// verip boş dönünce başka kaynakların bildirimleri de sessizce
    /// silinirdi.
    /// </summary>
    public async Task<NotificationScanResult> ApplyAsync(
        Guid companyId,
        IReadOnlyCollection<string> ownedTypes,
        IReadOnlyCollection<NotificationCandidate> candidates,
        DateTime scanTimeUtc,
        CancellationToken cancellationToken)
    {
        if (ownedTypes.Count == 0)
            return new NotificationScanResult(0, 0, 0);

        var existing = await db.Notifications
            .Where(x => x.CompanyId == companyId && ownedTypes.Contains(x.Type))
            .ToListAsync(cancellationToken);

        var byKey = existing.ToDictionary(
            x => (x.Type, x.SourceId, x.PeriodKey));

        var created = 0;
        var updated = 0;

        var seen = new HashSet<(string, Guid?, string)>();

        foreach (var candidate in candidates)
        {
            var period = string.IsNullOrWhiteSpace(candidate.PeriodKey)
                ? "-"
                : candidate.PeriodKey;

            // VADE UTC'YE ÇEKİLİYOR — TEK YERDE.
            //
            // DateOnly.ToDateTime() Kind=Unspecified üretiyor ve Npgsql
            // bunu timestamptz kolonuna yazmayı reddediyor. Dönüşüm
            // kaynakların her birine bırakılsaydı biri unutur ve o
            // kaynak sessizce hiç bildirim üretemezdi.
            var dueDate = candidate.DueDate is DateTime due
                ? DateTime.SpecifyKind(due, DateTimeKind.Utc)
                : (DateTime?)null;

            var key = (candidate.Type, candidate.SourceId, period);
            seen.Add(key);

            if (byKey.TryGetValue(key, out var row))
            {
                // METİN TAZELENİR, DURUM KORUNUR: tutar değişmiş
                // olabilir ama kullanıcının "okudum" kaydı silinmez.
                row.Title = candidate.Title;
                row.Detail = candidate.Detail;
                row.AmountDetail = candidate.AmountDetail;
                row.AmountPermission = candidate.AmountPermission;
                row.Severity = candidate.Severity;
                row.TargetPath = candidate.TargetPath;
                row.RequiredPermission = candidate.RequiredPermission;
                row.DueDate = dueDate;
                row.LastSeenAtUtc = scanTimeUtc;
                row.UpdatedAtUtc = scanTimeUtc;

                // Kapanmış bir bildirimin kaynağı geri geldiyse
                // yeniden açılır: çek iptal edilip tekrar açıldığında
                // uyarı da geri gelmeli.
                if (row.Status == NotificationStatus.Closed)
                {
                    row.Status = NotificationStatus.Open;
                    row.ClosedAtUtc = null;
                }

                // Erteleme süresi dolduysa yeniden görünür olur.
                if (row.Status == NotificationStatus.Snoozed &&
                    row.SnoozedUntil is DateTime until &&
                    until <= scanTimeUtc)
                {
                    row.Status = NotificationStatus.Open;
                    row.SnoozedUntil = null;
                }

                updated++;
                continue;
            }

            db.Notifications.Add(new Notification
            {
                CompanyId = companyId,
                Type = candidate.Type,
                SourceId = candidate.SourceId,
                PeriodKey = period,
                Title = candidate.Title,
                Detail = candidate.Detail,
                AmountDetail = candidate.AmountDetail,
                AmountPermission = candidate.AmountPermission,
                Severity = candidate.Severity,
                TargetPath = candidate.TargetPath,
                RequiredPermission = candidate.RequiredPermission,
                DueDate = dueDate,
                Status = NotificationStatus.Open,
                FirstSeenAtUtc = scanTimeUtc,
                LastSeenAtUtc = scanTimeUtc
            });

            created++;
        }

        var closed = 0;

        foreach (var row in existing)
        {
            if (seen.Contains((row.Type, row.SourceId, row.PeriodKey)))
                continue;

            if (row.Status == NotificationStatus.Closed)
                continue;

            row.Status = NotificationStatus.Closed;
            row.ClosedAtUtc = scanTimeUtc;
            row.UpdatedAtUtc = scanTimeUtc;

            closed++;
        }

        await db.SaveChangesAsync(cancellationToken);

        return new NotificationScanResult(created, updated, closed);
    }

    /// <summary>
    /// Kullanıcının görebileceği açık bildirimler.
    ///
    /// İZİN SÜZGECİ OKUMA ANINDA: bildirim şirkete ait, kim görebilir
    /// izinle belirlenir. Tarama arka planda kullanıcısız koştuğu için
    /// süzme burada yapılmak zorunda.
    /// </summary>
    public async Task<List<Notification>> ListVisibleAsync(
        Guid companyId,
        IReadOnlyCollection<string> permissions,
        bool includeHandled,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var query = db.Notifications
            .AsNoTracking()
            /*
             * YALNIZ ŞİRKET SATIRLARI — KİŞİSEL OLANLAR HARİÇ.
             *
             * Kişisel bildirimler (`TargetUserId` dolu) aynı tabloda
             * duruyor ama başka bir kapıdan okunuyor: görünürlükleri
             * izne değil KİŞİYE bağlı, okunma durumları
             * `NotificationRecipient` üzerinden.
             *
             * Bu süzgeç olmasaydı kişisel satır İKİ KEZ sayılırdı —
             * hem burada hem kişisel sayaçta. Zil sayacı testi tam
             * olarak bunu yakaladı (beklenen 2, gelen 3).
             */
            .Where(x => x.CompanyId == companyId && x.TargetUserId == null);

        if (!includeHandled)
        {
            query = query.Where(x =>
                x.Status == NotificationStatus.Open ||
                x.Status == NotificationStatus.Read ||
                (x.Status == NotificationStatus.Snoozed &&
                 x.SnoozedUntil != null && x.SnoozedUntil <= nowUtc));
        }

        var rows = await query
            .OrderByDescending(x => x.Severity)
            .ThenBy(x => x.DueDate ?? DateTime.MaxValue)
            .ThenByDescending(x => x.FirstSeenAtUtc)
            .ToListAsync(cancellationToken);

        return rows
            .Where(x => x.RequiredPermission is null ||
                        permissions.Contains(x.RequiredPermission))
            .ToList();
    }
}
