using EnderunAI.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Security;

/// <summary>
/// Bir kullanıcının şu anda sisteme erişip erişemeyeceğini belirler.
/// Öncelik sırası: Admin/Genel Müdür rolü → her zaman izinli; kullanıcı
/// bazlı kalıcı istisna (WorkHoursExempt) → her zaman izinli; aktif geçici
/// erişim (TemporaryAccessGrant) veya rol bazlı mesai penceresi → izinli,
/// pencerenin/grantın kapanma anı raporlanır (birden fazlası aynı anda
/// geçerliyse en geç kapanan esas alınır, kullanıcı erken kapanan bir
/// yoldan değil fiilen sahip olduğu en geniş erişimden çıkarılır).
/// </summary>
public interface IWorkHourAccessService
{
    Task<WorkHourEvaluation> EvaluateAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// MESAİ/1 — ÜÇ DEĞERLİ KARAR, İKİ DEĞERLİ DEĞİL.
///
/// Eskiden sonuç yalnız `IsAllowed` taşıyordu ve "kullanıcı satırı
/// okunamadı" da `false` dönüyordu — yani "mesai dışı" ile AYNI
/// cevaptı. Çağıran ikisini ayırt edemiyordu. ÖLÇÜLDÜ (2026-09-10):
/// satırı silinmiş kullanıcı "Mesai saatiniz sona erdiği için
/// oturumunuz kapatıldı" cevabı alıyor, denetime sahte bir
/// `WorkHoursSessionRejected` yazılıyordu.
///
/// OKUNAMAYAN BİR SATIR "MESAİ DIŞI" DEĞİLDİR. Kalıcı çıkışı yalnız
/// `MesaiDisi` üretebilir; `Belirlenemedi` bir KARAR değil, kararın
/// verilemediğinin beyanıdır.
/// </summary>
public enum MesaiKarari
{
    Izinli = 1,
    MesaiDisi = 2,
    Belirlenemedi = 3
}

public static class MesaiKarariMetni
{
    /// <summary>
    /// İzleyicinin okuduğu sözleşme. Ön yüz YALNIZ "mesai-disi"
    /// gördüğünde çıkış yapar; bu metin değişirse izleyici gerçek
    /// mesai dışını tanıyamaz (bkz. `mesai-izleyicisi.spec.ts`).
    /// </summary>
    public static string Metin(this MesaiKarari karar) => karar switch
    {
        MesaiKarari.Izinli => "izinli",
        MesaiKarari.MesaiDisi => "mesai-disi",
        _ => "belirlenemedi"
    };
}

public sealed record WorkHourEvaluation(
    MesaiKarari Karar,
    bool IsExempt,
    DateTime? WindowEndsAtUtc,
    string? Reason)
{
    public bool IsAllowed => Karar == MesaiKarari.Izinli;
}

public sealed class WorkHourAccessService(AppDbContext db) : IWorkHourAccessService
{
    private static readonly TimeZoneInfo TurkeyTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    private const string OutsideWindowMessage =
        "Bu saatte sisteme erişim izniniz yok. Mesai saatleri dışında " +
        "erişim için gerekçeli bir erişim talebi gönderebilirsiniz.";

    public async Task<WorkHourEvaluation> EvaluateAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.WorkHoursExempt,
                RoleNames = u.UserRoles.Select(ur => ur.Role.Name).ToArray(),
                RoleIds = u.UserRoles.Select(ur => ur.RoleId).ToArray()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
            return new WorkHourEvaluation(MesaiKarari.Belirlenemedi, false, null, "Kullanıcı satırı okunamadı.");

        if (user.RoleNames.Contains("Admin", StringComparer.OrdinalIgnoreCase) ||
            user.RoleNames.Contains("Genel Müdür", StringComparer.OrdinalIgnoreCase))
        {
            return new WorkHourEvaluation(MesaiKarari.Izinli, true, null, null);
        }

        if (user.WorkHoursExempt)
            return new WorkHourEvaluation(MesaiKarari.Izinli, true, null, null);

        var nowUtc = DateTime.UtcNow;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, TurkeyTimeZone);
        var todayLocal = DateOnly.FromDateTime(nowLocal);
        var timeOfDay = TimeOnly.FromDateTime(nowLocal);
        var dayOfWeek = (int)nowLocal.DayOfWeek;

        DateTime? latestEndUtc = null;

        var activeGrant = await db.TemporaryAccessGrants
            .AsNoTracking()
            .Where(g => g.UserId == userId && g.ExpiresAtUtc > nowUtc)
            .OrderByDescending(g => g.ExpiresAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (activeGrant is not null)
            latestEndUtc = activeGrant.ExpiresAtUtc;

        if (user.RoleIds.Length > 0)
        {
            var windows = await db.RoleWorkHourWindows
                .AsNoTracking()
                .Where(w => user.RoleIds.Contains(w.RoleId) && w.DayOfWeek == dayOfWeek)
                .ToListAsync(cancellationToken);

            foreach (var window in windows)
            {
                if (timeOfDay < window.StartTime || timeOfDay > window.EndTime)
                    continue;

                var endLocalUnspecified = DateTime.SpecifyKind(
                    todayLocal.ToDateTime(window.EndTime),
                    DateTimeKind.Unspecified);
                var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocalUnspecified, TurkeyTimeZone);

                if (latestEndUtc is null || endUtc > latestEndUtc)
                    latestEndUtc = endUtc;
            }
        }

        if (latestEndUtc is not null)
            return new WorkHourEvaluation(MesaiKarari.Izinli, false, latestEndUtc, null);

        return new WorkHourEvaluation(MesaiKarari.MesaiDisi, false, null, OutsideWindowMessage);
    }
}
