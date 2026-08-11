using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Models.Notifications;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Isg;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Notifications.Sources;

/// <summary>
/// GEÇERLİLİĞİ BİTEN BELGELER — özlük belgesi, sağlık raporu,
/// eğitim ve sertifika.
///
/// DÖRT KAYIT TÜRÜ TEK KAYNAKTA: hepsi aynı soruyu soruyor ("bu
/// belge ne zaman bitiyor") ve aynı eşiği kullanıyor. Dört ayrı
/// kaynak yazılsaydı eşiklerden biri güncellenip diğerleri unutulur,
/// aynı ekipte "sertifikada 30 gün, raporda 45 gün" gibi bir
/// tutarsızlık doğardı.
///
/// EŞİK <see cref="IsgValidityCalculator.WarningDays"/>'ten geliyor;
/// İSG paneliyle bildirim aynı günü söylemek zorunda.
/// </summary>
public sealed class DocumentExpiryNotificationSource(AppDbContext db)
    : INotificationSource
{
    public const string PersonnelDocumentTypeKey = "document.personnel.expiring";
    public const string HealthReportTypeKey = "isg.health.expiring";
    public const string TrainingTypeKey = "isg.training.expiring";
    public const string CertificateTypeKey = "isg.certificate.expiring";

    public string Key => "belge_gecerliligi";

    public IReadOnlyCollection<string> OwnedTypes =>
    [
        PersonnelDocumentTypeKey, HealthReportTypeKey,
        TrainingTypeKey, CertificateTypeKey
    ];

    public async Task<IReadOnlyList<NotificationCandidate>> BuildAsync(
        NotificationScanContext context, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(context.Today);
        var limit = today.AddDays(NotificationWindow.DocumentExpiryDays);
        var limitDate = context.Today.AddDays(NotificationWindow.DocumentExpiryDays);

        var items = new List<NotificationCandidate>();

        // --- Özlük belgeleri ---
        //
        // Modelin yorumu "yaklaşan bitiş uyarı üretir" diyordu ama
        // üretmiyordu; sözü tutan yer burası.
        var documents = await db.PersonnelDocuments
            .AsNoTracking()
            .Where(x => x.CompanyId == context.CompanyId &&
                        x.ExpiryDate != null && x.ExpiryDate <= limitDate)
            .Select(x => new
            {
                x.Id,
                x.DocumentName,
                x.ExpiryDate,
                PersonnelName = x.Personnel.FirstName + " " + x.Personnel.LastName
            })
            .ToListAsync(cancellationToken);

        foreach (var row in documents)
        {
            var expiry = row.ExpiryDate!.Value;
            var days = (expiry.Date - context.Today).Days;

            items.Add(Build(
                PersonnelDocumentTypeKey, row.Id, expiry, days,
                $"Özlük belgesi {ExpiryLabel(days)}",
                $"{row.PersonnelName} · {row.DocumentName}",
                "/insan-kaynaklari/personeller",
                PermissionCatalog.Keys.PersonnelView));
        }

        // --- İSG: sağlık raporu ---
        var health = await db.IsgHealthReports
            .AsNoTracking()
            .Where(x => x.CompanyId == context.CompanyId &&
                        x.ValidUntil != null && x.ValidUntil <= limit)
            .Select(x => new
            {
                x.Id,
                x.ValidUntil,
                PersonnelName = x.Personnel.FirstName + " " + x.Personnel.LastName
            })
            .ToListAsync(cancellationToken);

        foreach (var row in health)
        {
            var expiry = row.ValidUntil!.Value;
            var days = expiry.DayNumber - today.DayNumber;

            items.Add(Build(
                HealthReportTypeKey, row.Id,
                DateTime.SpecifyKind(expiry.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc), days,
                $"Sağlık raporu {ExpiryLabel(days)}",
                row.PersonnelName,
                "/isg",
                PermissionCatalog.Keys.IsgView));
        }

        // --- İSG: eğitim ---
        var trainings = await db.IsgTrainings
            .AsNoTracking()
            .Where(x => x.CompanyId == context.CompanyId &&
                        x.ValidUntil != null && x.ValidUntil <= limit)
            .Select(x => new
            {
                x.Id,
                x.ValidUntil,
                PersonnelName = x.Personnel.FirstName + " " + x.Personnel.LastName
            })
            .ToListAsync(cancellationToken);

        foreach (var row in trainings)
        {
            var expiry = row.ValidUntil!.Value;
            var days = expiry.DayNumber - today.DayNumber;

            items.Add(Build(
                TrainingTypeKey, row.Id,
                DateTime.SpecifyKind(expiry.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc), days,
                $"İSG eğitimi {ExpiryLabel(days)}",
                row.PersonnelName,
                "/isg",
                PermissionCatalog.Keys.IsgView));
        }

        // --- İSG: sertifika ---
        var certificates = await db.IsgCertificates
            .AsNoTracking()
            .Where(x => x.CompanyId == context.CompanyId &&
                        x.ExpiryDate != null && x.ExpiryDate <= limit)
            .Select(x => new
            {
                x.Id,
                x.ExpiryDate,
                PersonnelName = x.Personnel.FirstName + " " + x.Personnel.LastName
            })
            .ToListAsync(cancellationToken);

        foreach (var row in certificates)
        {
            var expiry = row.ExpiryDate!.Value;
            var days = expiry.DayNumber - today.DayNumber;

            items.Add(Build(
                CertificateTypeKey, row.Id,
                DateTime.SpecifyKind(expiry.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc), days,
                $"Sertifika {ExpiryLabel(days)}",
                row.PersonnelName,
                "/isg",
                PermissionCatalog.Keys.IsgView));
        }

        return items;
    }

    /// <summary>
    /// Süresi DOLMUŞ belge kritik: geçerliliğini yitirmiş bir sağlık
    /// raporuyla çalışan personel yasal risktir, yaklaşan bir bitişten
    /// daha acildir.
    /// </summary>
    private static NotificationCandidate Build(
        string type, Guid sourceId, DateTime expiry, int days,
        string title, string detail, string path, string permission) =>
        new(
            type,
            sourceId,
            expiry.ToString("yyyy-MM-dd"),
            title,
            detail,
            days < 0
                ? NotificationSeverity.Critical
                : NotificationSeverity.Warning,
            path,
            expiry,
            null,
            null,
            permission);

    private static string ExpiryLabel(int days) => days switch
    {
        < 0 => $"{Math.Abs(days)} gün önce doldu",
        0 => "bugün doluyor",
        _ => $"{days} gün sonra doluyor"
    };
}

/// <summary>
/// ONAY BEKLEYEN TALEPLER — izin, avans, fazla mesai.
///
/// EŞİK 2 GÜN: bekleyen her talep anında bildirim üretseydi aynı gün
/// onaylanacak işler için de gürültü çıkardı. İki günü aşan bir
/// bekleme ise gerçekten unutulmuş demektir.
///
/// TALEPLER HrDbContext'te; bu kaynak iki bağlamı birlikte kullanan
/// tek yer, o yüzden ayrı tutuluyor.
/// </summary>
public sealed class PendingApprovalNotificationSource(HrDbContext hrDb)
    : INotificationSource
{
    public const string LeaveTypeKey = "hr.leave.pending";
    public const string AdvanceTypeKey = "hr.advance.pending";
    public const string OvertimeTypeKey = "hr.overtime.pending";

    public string Key => "onay_bekleyen";

    public IReadOnlyCollection<string> OwnedTypes =>
        [LeaveTypeKey, AdvanceTypeKey, OvertimeTypeKey];

    public async Task<IReadOnlyList<NotificationCandidate>> BuildAsync(
        NotificationScanContext context, CancellationToken cancellationToken)
    {
        var threshold = context.Today
            .AddDays(-NotificationWindow.PendingApprovalDays);

        var items = new List<NotificationCandidate>();

        var leaves = await hrDb.LeaveRequests
            .AsNoTracking()
            .Where(x => x.CompanyId == context.CompanyId &&
                        x.Status == HrApprovalStatus.Pending &&
                        x.CreatedAtUtc <= threshold)
            .Select(x => new { x.Id, x.StartDate, x.EndDate, x.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        foreach (var row in leaves)
        {
            items.Add(Pending(
                LeaveTypeKey, row.Id, row.CreatedAtUtc, context.Today,
                "İzin talebi onay bekliyor",
                $"{row.StartDate:dd.MM.yyyy}–{row.EndDate:dd.MM.yyyy}",
                "/insan-kaynaklari/izinler"));
        }

        var advances = await hrDb.AdvanceRequests
            .AsNoTracking()
            .Where(x => x.CompanyId == context.CompanyId &&
                        x.Status == HrApprovalStatus.Pending &&
                        x.CreatedAtUtc <= threshold)
            .Select(x => new { x.Id, x.RequestDate, x.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        foreach (var row in advances)
        {
            items.Add(Pending(
                AdvanceTypeKey, row.Id, row.CreatedAtUtc, context.Today,
                "Avans talebi onay bekliyor",
                $"Talep {row.RequestDate:dd.MM.yyyy}",
                "/insan-kaynaklari/avanslar"));
        }

        var overtime = await hrDb.OvertimeRequests
            .AsNoTracking()
            .Where(x => x.CompanyId == context.CompanyId &&
                        x.Status == HrApprovalStatus.Pending &&
                        x.CreatedAtUtc <= threshold)
            .Select(x => new { x.Id, x.WorkDate, x.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        foreach (var row in overtime)
        {
            items.Add(Pending(
                OvertimeTypeKey, row.Id, row.CreatedAtUtc, context.Today,
                "Fazla mesai talebi onay bekliyor",
                $"Çalışma günü {row.WorkDate:dd.MM.yyyy}",
                "/insan-kaynaklari/fazla-mesai"));
        }

        return items;
    }

    /// <summary>
    /// Bekleme uzadıkça şiddet artıyor: bir haftayı aşan bir onay
    /// unutulmuş demektir.
    /// </summary>
    private static NotificationCandidate Pending(
        string type, Guid sourceId, DateTime createdAtUtc, DateTime today,
        string title, string detail, string path)
    {
        var waiting = (today - createdAtUtc.Date).Days;

        return new NotificationCandidate(
            type,
            sourceId,
            createdAtUtc.ToString("yyyy-MM-dd"),
            $"{title} ({waiting} gündür)",
            detail,
            waiting >= 7
                ? NotificationSeverity.Critical
                : NotificationSeverity.Warning,
            path,
            null,
            null,
            null,
            PermissionCatalog.Keys.PersonnelView);
    }
}

/// <summary>
/// GÖREVLENDİRME onayı ve YIL PARAMETRELERİ.
///
/// Parametre bildirimlerinin KAYNAK KİMLİĞİ YOK: "2026 bordro
/// parametreleri doğrulanmadı" bir kayda değil bir eksikliğe işaret
/// ediyor. Tekilleştirme dönem anahtarıyla yürüyor — yıl başına tek
/// bildirim.
/// </summary>
public sealed class ManagementNotificationSource(AppDbContext db)
    : INotificationSource
{
    public const string DutyApprovalTypeKey = "duty.approval.pending";
    public const string PayrollSettingsTypeKey = "settings.payroll.unverified";
    public const string HolidayCalendarTypeKey = "settings.holidays.unverified";

    public string Key => "yonetim";

    public IReadOnlyCollection<string> OwnedTypes =>
        [DutyApprovalTypeKey, PayrollSettingsTypeKey, HolidayCalendarTypeKey];

    public async Task<IReadOnlyList<NotificationCandidate>> BuildAsync(
        NotificationScanContext context, CancellationToken cancellationToken)
    {
        var items = new List<NotificationCandidate>();

        var threshold = context.Today
            .AddDays(-NotificationWindow.PendingApprovalDays);

        var duties = await db.PersonnelDuties
            .AsNoTracking()
            .Where(x => x.Personnel.CompanyId == context.CompanyId &&
                        x.Status == PersonnelDutyStatus.Requested &&
                        x.CreatedAtUtc <= threshold)
            .Select(x => new
            {
                x.Id,
                x.StartDate,
                x.CreatedAtUtc,
                PersonnelName = x.Personnel.FirstName + " " + x.Personnel.LastName
            })
            .ToListAsync(cancellationToken);

        foreach (var row in duties)
        {
            var waiting = (context.Today - row.CreatedAtUtc.Date).Days;

            items.Add(new NotificationCandidate(
                DutyApprovalTypeKey,
                row.Id,
                row.CreatedAtUtc.ToString("yyyy-MM-dd"),
                $"Görevlendirme onay bekliyor ({waiting} gündür)",
                $"{row.PersonnelName} · başlangıç {row.StartDate:dd.MM.yyyy}",
                waiting >= 7
                    ? NotificationSeverity.Critical
                    : NotificationSeverity.Warning,
                "/insan-kaynaklari/gorevlendirmeler",
                row.StartDate,
                null,
                null,
                PermissionCatalog.Keys.PersonnelView));
        }

        // --- Yıl parametreleri ---
        //
        // İÇİNDE BULUNULAN YIL: gelecek yılın parametresi henüz
        // beklenmiyor, geçmiş yılınki de artık iş üretmiyor.
        var year = context.Today.Year;

        var payrollVerified = await db.CompanyPayrollSettings
            .AsNoTracking()
            .AnyAsync(x => x.CompanyId == context.CompanyId &&
                           x.Year == year && x.VerifiedAtUtc != null,
                cancellationToken);

        if (!payrollVerified)
        {
            items.Add(new NotificationCandidate(
                PayrollSettingsTypeKey,
                null,
                year.ToString(),
                $"{year} bordro parametreleri doğrulanmadı",
                "Asgari ücret, SGK taban/tavan ve vergi dilimleri " +
                "onaylanmadan bordro üretilemez.",
                NotificationSeverity.Critical,
                "/sistem-yonetimi/sirket-ayarlari",
                null,
                null,
                null,
                PermissionCatalog.Keys.PayrollView));
        }

        var calendarVerified = await db.CompanyHolidayCalendars
            .AsNoTracking()
            .AnyAsync(x => x.CompanyId == context.CompanyId &&
                           x.Year == year && x.VerifiedAtUtc != null,
                cancellationToken);

        if (!calendarVerified)
        {
            items.Add(new NotificationCandidate(
                HolidayCalendarTypeKey,
                null,
                year.ToString(),
                $"{year} resmî tatil takvimi doğrulanmadı",
                "Puantaj cetveli takvimden dolduğu için tatil günleri " +
                "otomatik işaretlenemiyor.",
                NotificationSeverity.Warning,
                "/insan-kaynaklari/tatil-takvimi",
                null,
                null,
                null,
                PermissionCatalog.Keys.AttendanceView));
        }

        return items;
    }
}
