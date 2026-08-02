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

public sealed record WorkHourEvaluation(
    bool IsAllowed,
    bool IsExempt,
    DateTime? WindowEndsAtUtc,
    string? Reason);

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
            return new WorkHourEvaluation(false, false, null, "Kullanıcı bulunamadı.");

        if (user.RoleNames.Contains("Admin", StringComparer.OrdinalIgnoreCase) ||
            user.RoleNames.Contains("Genel Müdür", StringComparer.OrdinalIgnoreCase))
        {
            return new WorkHourEvaluation(true, true, null, null);
        }

        if (user.WorkHoursExempt)
            return new WorkHourEvaluation(true, true, null, null);

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
            return new WorkHourEvaluation(true, false, latestEndUtc, null);

        return new WorkHourEvaluation(false, false, null, OutsideWindowMessage);
    }
}
