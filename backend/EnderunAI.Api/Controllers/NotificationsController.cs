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

        /*
         * ZİLDE TEK SAYAÇ — İKİ MODEL BİRLİKTE OKUNUYOR.
         *
         * ŞİRKET SATIRLARI (`TargetUserId` boş): mevcut dört tarama
         * kaynağı. Görünürlük izne göre, okunma damgası satırın
         * kendisinde — bir çek vadesi herkesi ilgilendirdiği için o
         * tasarım doğru.
         *
         * KİŞİSEL SATIRLAR (`TargetUserId` dolu): M1 olayları.
         * Görünürlük kişiye, okunma durumu `NotificationRecipient`
         * üzerinden. Şirket satırında tek `ReadAtUtc` olduğu için
         * "bana atandı" bildirimini bir kişi okuyunca herkes için
         * okunmuş sayılırdı.
         *
         * KULLANICI İKİ AYRI SAYI GÖRMEMELİ: iki liste ve iki sayaç,
         * hangisine bakacağını bilemez hale getirir.
         */
        var kisiselOkunmamis = await KisiselOkunmamisSayisiAsync(
            companyId, cancellationToken);

        var kisiselSatirlar = await KisiselSatirlariGetirAsync(
            companyId, includeHandled, cancellationToken);

        return Ok(new
        {
            unreadCount =
                rows.Count(x => x.Status == NotificationStatus.Open) +
                kisiselOkunmamis,

            personalItems = kisiselSatirlar,
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

    /// <summary>
    /// Kullanıcının OKUNMAMIŞ kişisel bildirim sayısı.
    ///
    /// Okuma durumu ALICI TABLOSUNDA: şirket satırındaki tek
    /// `ReadAtUtc` kişiye özel olamazdı — bir kişi okuyunca herkes
    /// için okunmuş sayılırdı.
    /// </summary>
    private async Task<int> KisiselOkunmamisSayisiAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            return 0;

        return await db.NotificationRecipients
            .AsNoTracking()
            .CountAsync(
                x => x.UserId == userId &&
                     x.ReadAtUtc == null &&
                     x.DismissedAtUtc == null &&
                     x.Notification.CompanyId == companyId &&
                     x.Notification.Status != NotificationStatus.Closed,
                cancellationToken);
    }

    private async Task<List<object>> KisiselSatirlariGetirAsync(
        Guid companyId,
        bool includeHandled,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            return [];

        var query = db.NotificationRecipients
            .AsNoTracking()
            .Where(x =>
                x.UserId == userId &&
                x.Notification.CompanyId == companyId);

        if (!includeHandled)
        {
            query = query.Where(x =>
                x.ReadAtUtc == null &&
                x.DismissedAtUtc == null &&
                x.Notification.Status != NotificationStatus.Closed);
        }

        var satirlar = await query
            .OrderByDescending(x => x.Notification.FirstSeenAtUtc)
            .Take(100)
            .Select(x => new
            {
                x.Id,
                NotificationId = x.NotificationId,
                x.Notification.Type,
                x.Notification.Title,
                x.Notification.Detail,
                x.Notification.TargetPath,
                Severity = (int)x.Notification.Severity,
                x.Notification.SourceId,
                OccurredAtUtc = x.Notification.FirstSeenAtUtc,
                IsRead = x.ReadAtUtc != null
            })
            .ToListAsync(cancellationToken);

        return satirlar.Cast<object>().ToList();
    }

    /// <summary>Kişisel bildirimi okundu işaretler.</summary>
    [HttpPost("kisisel/{recipientId:guid}/okundu")]
    public async Task<IActionResult> MarkPersonalRead(
        Guid recipientId,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            return Unauthorized();

        // KENDİ BİLDİRİMİ: başkasının satırını okundu işaretleyemez.
        var alici = await db.NotificationRecipients
            .SingleOrDefaultAsync(
                x => x.Id == recipientId && x.UserId == userId, cancellationToken);

        if (alici is null)
            return NotFound(new { message = "Bildirim bulunamadı." });

        alici.ReadAtUtc ??= DateTime.UtcNow;
        alici.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { alici.Id, isRead = true });
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
