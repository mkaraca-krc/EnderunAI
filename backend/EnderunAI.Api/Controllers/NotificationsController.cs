using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Notifications;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

public sealed record SnoozeNotificationRequest(DateTime Until);

/// <summary>
/// Bildirim merkezi.
///
/// AYRI İZİN ANAHTARI YOK: her bildirim kendi
/// <c>RequiredPermission</c> alanını taşıyor ve okuma anında
/// süzülüyor. Merkezin kendisine bir kapı konsaydı, izni olan bir
/// kullanıcı kendi modülünün bildirimini göremez hale gelirdi.
/// Kimlik doğrulaması yeterli; içerik zaten yetkiye göre daralıyor.
/// </summary>
[ApiController]
[Authorize]
[Route("api/bildirimler")]
public sealed class NotificationsController(
    AppDbContext db,
    NotificationStore store,
    NotificationScanner scanner,
    ICurrentUserService currentUser,
    IUserAuthorizationService authorization,
    IExtraPaymentVisibilityService extraPaymentVisibility) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid companyId,
        [FromQuery] bool includeHandled,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
            return BadRequest(new { message = "Şirket seçimi zorunludur." });

        var permissions = await ResolvePermissionsAsync(cancellationToken);

        var rows = await store.ListVisibleAsync(
            companyId, permissions, includeHandled, DateTime.UtcNow, cancellationToken);

        var canSeeCash = await extraPaymentVisibility
            .CanViewExtraPaymentAsync(cancellationToken);

        return Ok(new
        {
            unreadCount = rows.Count(x => x.Status == NotificationStatus.Open),
            items = rows.Select(x => new
            {
                id = x.Id,
                type = x.Type,
                title = x.Title,
                // TUTARLI METİN yalnız izinliye. İzin yoksa tutarsız
                // metin dönüyor; tutarı metinden ayıklamaya
                // çalışmıyoruz.
                detail = ResolveDetail(x, permissions, canSeeCash),
                severity = (int)x.Severity,
                severityName = SeverityName(x.Severity),
                targetPath = x.TargetPath,
                dueDate = x.DueDate,
                status = x.Status.ToString(),
                snoozedUntil = x.SnoozedUntil,
                firstSeenAtUtc = x.FirstSeenAtUtc
            })
        });
    }

    [HttpPost("{id:guid}/okundu")]
    public Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken) =>
        TransitionAsync(id, NotificationStatus.Read, null, cancellationToken);

    [HttpPost("{id:guid}/kapat")]
    public Task<IActionResult> Dismiss(Guid id, CancellationToken cancellationToken) =>
        TransitionAsync(id, NotificationStatus.Dismissed, null, cancellationToken);

    /// <summary>
    /// Erteleme. Geçmiş bir tarih kabul edilmez: erteleme anında
    /// dolmuş sayılır ve kullanıcı hiçbir şey olmamış gibi görürdü.
    /// </summary>
    [HttpPost("{id:guid}/ertele")]
    public async Task<IActionResult> Snooze(
        Guid id,
        [FromBody] SnoozeNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var until = DateTime.SpecifyKind(request.Until, DateTimeKind.Utc);

        if (until <= DateTime.UtcNow)
            return BadRequest(new { message = "Erteleme tarihi gelecekte olmalıdır." });

        return await TransitionAsync(
            id, NotificationStatus.Snoozed, until, cancellationToken);
    }

    /// <summary>
    /// Taramayı elle çalıştırır.
    ///
    /// NEDEN VAR: arka plan işi günde bir koşuyor. Bir kayıt
    /// düzeltildiğinde kullanıcının ertesi güne kadar beklemesi
    /// gerekmesin; test ve destek tarafı da turu tetikleyebilsin.
    /// Sistem yönetimi izniyle sınırlı — tarama bütün şirketleri
    /// gezer.
    /// </summary>
    [HttpPost("tara")]
    [RequirePermission(PermissionCatalog.Keys.SystemUsersManage)]
    public async Task<IActionResult> Scan(CancellationToken cancellationToken)
    {
        var report = await scanner.RunAsync(DateTime.UtcNow, cancellationToken);

        return Ok(new
        {
            scanTimeUtc = report.ScanTimeUtc,
            companyCount = report.CompanyCount,
            created = report.Created,
            updated = report.Updated,
            closed = report.Closed,
            sources = report.Sources.Select(x => new
            {
                source = x.Source,
                created = x.Created,
                updated = x.Updated,
                closed = x.Closed,
                error = x.Error
            })
        });
    }

    private async Task<IActionResult> TransitionAsync(
        Guid id,
        NotificationStatus status,
        DateTime? snoozedUntil,
        CancellationToken cancellationToken)
    {
        var notification = await db.Notifications
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (notification is null)
            return NotFound(new { message = "Bildirim bulunamadı." });

        // GÖREMEDİĞİNİ DEĞİŞTİREMEZ: yetkisi olmayan kullanıcı
        // listede görmediği bir bildirimi kapatamamalı.
        var permissions = await ResolvePermissionsAsync(cancellationToken);

        if (notification.RequiredPermission is string required &&
            !permissions.Contains(required))
        {
            return Forbid();
        }

        var now = DateTime.UtcNow;

        notification.Status = status;
        notification.UpdatedAtUtc = now;

        switch (status)
        {
            case NotificationStatus.Read:
                notification.ReadAtUtc ??= now;
                notification.SnoozedUntil = null;
                break;

            case NotificationStatus.Dismissed:
                notification.DismissedAtUtc = now;
                notification.SnoozedUntil = null;
                break;

            case NotificationStatus.Snoozed:
                notification.SnoozedUntil = snoozedUntil;
                break;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { id, status = notification.Status.ToString() });
    }

    private async Task<List<string>> ResolvePermissionsAsync(
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            return [];

        var snapshot = await authorization.GetAsync(userId, cancellationToken);

        return snapshot is null || !snapshot.IsActive
            ? []
            : snapshot.Permissions.ToList();
    }

    private static string? ResolveDetail(
        Notification notification,
        IReadOnlyCollection<string> permissions,
        bool canSeeCash)
    {
        if (notification.AmountDetail is null)
            return notification.Detail;

        if (notification.AmountPermission is not string required)
            return notification.AmountDetail;

        // Elden kalemler ayrıca extra_payment.view istiyor.
        var allowed = permissions.Contains(required) &&
                      (required != PermissionCatalog.Keys.ExtraPaymentView || canSeeCash);

        return allowed ? notification.AmountDetail : notification.Detail;
    }

    private static string SeverityName(NotificationSeverity severity) => severity switch
    {
        NotificationSeverity.Critical => "Kritik",
        NotificationSeverity.Warning => "Uyarı",
        _ => "Bilgi"
    };
}
