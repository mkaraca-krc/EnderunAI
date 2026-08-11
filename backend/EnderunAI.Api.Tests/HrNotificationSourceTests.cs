using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Models.Notifications;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Isg;
using EnderunAI.Api.Services.Notifications;
using EnderunAI.Api.Services.Notifications.Sources;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// İK ve yönetim tetikleyicileri: belge geçerliliği, onay bekleyen
/// talepler, görevlendirme onayı ve doğrulanmamış yıl parametreleri.
///
/// EŞİK ORTAKLIĞI en kritik nokta: belge eşiği İSG panelininkiyle
/// AYNI sayı olmak zorunda. İkinci bir eşik açılsaydı aynı sertifika
/// panelde "yakında bitiyor", bildirimde "hâlâ geçerli" görünür ve
/// kullanıcı hangisine inanacağını bilemezdi.
/// </summary>
[Collection("Integration")]
public sealed class HrNotificationSourceTests(DatabaseFixture fixture)
{
    private static readonly DateTime Today = DateTime.UtcNow.Date;

    private async Task<(Guid CompanyId, Guid PersonnelId)> CreateContextAsync(
        string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, suffix);

        return (project.CompanyId, personnel.Id);
    }

    private async Task<IReadOnlyList<NotificationCandidate>> BuildAsync<TSource>(
        Guid companyId)
        where TSource : INotificationSource
    {
        using var scope = fixture.Factory.Services.CreateScope();

        var source = scope.ServiceProvider
            .GetRequiredService<IEnumerable<INotificationSource>>()
            .OfType<TSource>()
            .Single();

        return await source.BuildAsync(
            new NotificationScanContext(companyId, Today), CancellationToken.None);
    }

    // ---------------- Belge geçerliliği ----------------

    /// <summary>
    /// EŞİK İSG İLE AYNI: eşiğin bir gün içindeki sertifika aday
    /// üretiyor, bir gün dışındaki üretmiyor. Sayı burada elle
    /// yazılmıyor, IsgValidityCalculator'dan okunuyor — kopyalansaydı
    /// biri değişince diğeri sessizce eskirdi.
    /// </summary>
    [Fact]
    public async Task DocumentExpiry_UsesTheSameThresholdAsTheIsgPanel()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, personnelId) = await CreateContextAsync(suffix);

        var inside = DateOnly.FromDateTime(
            Today.AddDays(IsgValidityCalculator.WarningDays - 1));

        var outside = DateOnly.FromDateTime(
            Today.AddDays(IsgValidityCalculator.WarningDays + 5));

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.IsgCertificates.AddRange(
                new IsgCertificate
                {
                    CompanyId = companyId,
                    PersonnelId = personnelId,
                    CertificateType = IsgCertificateType.Other,
                    CustomTypeName = "Yüksekte çalışma",
                    IssueDate = DateOnly.FromDateTime(Today.AddYears(-1)),
                    ExpiryDate = inside
                },
                new IsgCertificate
                {
                    CompanyId = companyId,
                    PersonnelId = personnelId,
                    CertificateType = IsgCertificateType.Other,
                    CustomTypeName = "İlk yardım",
                    IssueDate = DateOnly.FromDateTime(Today.AddYears(-1)),
                    ExpiryDate = outside
                });

            await db.SaveChangesAsync();
        }

        var candidates = await BuildAsync<DocumentExpiryNotificationSource>(companyId);

        var certificates = candidates
            .Where(x => x.Type == DocumentExpiryNotificationSource.CertificateTypeKey)
            .ToList();

        Assert.Single(certificates);
        Assert.Contains("doluyor", certificates[0].Title);
    }

    /// <summary>
    /// SÜRESİ DOLMUŞ BELGE KRİTİK: geçerliliğini yitirmiş bir sağlık
    /// raporuyla çalışan personel yasal risktir; yaklaşan bir
    /// bitişten daha acildir.
    /// </summary>
    [Fact]
    public async Task ExpiredHealthReport_IsCritical()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, personnelId) = await CreateContextAsync(suffix);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.IsgHealthReports.Add(new IsgHealthReport
            {
                CompanyId = companyId,
                PersonnelId = personnelId,
                ReportType = IsgHealthReportType.Periodic,
                ExamDate = DateOnly.FromDateTime(Today.AddYears(-2)),
                ValidUntil = DateOnly.FromDateTime(Today.AddDays(-10)),
                Result = IsgHealthResult.Fit
            });

            await db.SaveChangesAsync();
        }

        var candidate = (await BuildAsync<DocumentExpiryNotificationSource>(companyId))
            .Single(x => x.Type == DocumentExpiryNotificationSource.HealthReportTypeKey);

        Assert.Equal(NotificationSeverity.Critical, candidate.Severity);
        Assert.Contains("önce doldu", candidate.Title);
        Assert.Equal(PermissionCatalog.Keys.IsgView, candidate.RequiredPermission);
    }

    /// <summary>
    /// Özlük belgesi de aynı kaynaktan geliyor ve personel iznine
    /// bağlı — modelin yorumu bunu vaat ediyordu, üretmiyordu.
    /// </summary>
    [Fact]
    public async Task PersonnelDocumentExpiry_ProducesACandidate()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, personnelId) = await CreateContextAsync(suffix);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.PersonnelDocuments.Add(new PersonnelDocument
            {
                CompanyId = companyId,
                PersonnelId = personnelId,
                DocumentType = PersonnelDocumentType.DriverLicense,
                DocumentName = "Ehliyet",
                ExpiryDate = Today.AddDays(10)
            });

            await db.SaveChangesAsync();
        }

        var candidate = (await BuildAsync<DocumentExpiryNotificationSource>(companyId))
            .Single(x => x.Type ==
                DocumentExpiryNotificationSource.PersonnelDocumentTypeKey);

        Assert.Equal(
            PermissionCatalog.Keys.PersonnelView, candidate.RequiredPermission);

        // Belgede tutar yok: maskelenecek bir şey de yok.
        Assert.Null(candidate.AmountDetail);
    }

    // ---------------- Onay bekleyenler ----------------

    /// <summary>
    /// EŞİK 2 GÜN: bugün açılan talep bildirim üretmiyor, üç gün
    /// önceki üretiyor. Her bekleyen talep anında bildirim üretseydi
    /// aynı gün onaylanacak işler için de gürültü çıkardı.
    /// </summary>
    [Fact]
    public async Task PendingRequests_OnlyNotifyAfterTheWaitingThreshold()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, personnelId) = await CreateContextAsync(suffix);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

            hrDb.LeaveRequests.AddRange(
                new HrLeaveRequest
                {
                    CompanyId = companyId,
                    PersonnelId = personnelId,
                    LeaveType = HrLeaveType.Annual,
                    StartDate = Today.AddDays(10),
                    EndDate = Today.AddDays(12),
                    TotalDays = 3,
                    Reason = "Bugün açıldı",
                    Status = HrApprovalStatus.Pending,
                    CreatedAtUtc = DateTime.UtcNow
                },
                new HrLeaveRequest
                {
                    CompanyId = companyId,
                    PersonnelId = personnelId,
                    LeaveType = HrLeaveType.Annual,
                    StartDate = Today.AddDays(20),
                    EndDate = Today.AddDays(22),
                    TotalDays = 3,
                    Reason = "Üç gündür bekliyor",
                    Status = HrApprovalStatus.Pending,
                    CreatedAtUtc = DateTime.UtcNow.AddDays(-3)
                });

            await hrDb.SaveChangesAsync();
        }

        var candidates = await BuildAsync<PendingApprovalNotificationSource>(companyId);

        var leaves = candidates
            .Where(x => x.Type == PendingApprovalNotificationSource.LeaveTypeKey)
            .ToList();

        Assert.Single(leaves);
        Assert.Contains("3 gündür", leaves[0].Title);
    }

    /// <summary>
    /// ONAYLANAN TALEP ADAY ÜRETMEZ; tarama görmeyince bildirim
    /// kendiliğinden kapanır.
    /// </summary>
    [Fact]
    public async Task ApprovedRequest_ProducesNoCandidate()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, personnelId) = await CreateContextAsync(suffix);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

            hrDb.AdvanceRequests.Add(new HrAdvanceRequest
            {
                CompanyId = companyId,
                PersonnelId = personnelId,
                RequestDate = Today.AddDays(-5),
                RequestedAmount = 10_000m,
                ApprovedAmount = 10_000m,
                Status = HrApprovalStatus.Approved,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-5)
            });

            await hrDb.SaveChangesAsync();
        }

        var candidates = await BuildAsync<PendingApprovalNotificationSource>(companyId);

        Assert.DoesNotContain(candidates,
            x => x.Type == PendingApprovalNotificationSource.AdvanceTypeKey);
    }

    /// <summary>
    /// Bir haftayı aşan bekleme KRİTİK: unutulmuş demektir.
    /// </summary>
    [Fact]
    public async Task RequestWaitingOverAWeek_BecomesCritical()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, personnelId) = await CreateContextAsync(suffix);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

            hrDb.OvertimeRequests.Add(new HrOvertimeRequest
            {
                CompanyId = companyId,
                PersonnelId = personnelId,
                WorkDate = Today.AddDays(-10),
                RequestedHours = 4,
                Reason = "Uzun süredir bekliyor",
                Status = HrApprovalStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-9)
            });

            await hrDb.SaveChangesAsync();
        }

        var candidate = (await BuildAsync<PendingApprovalNotificationSource>(companyId))
            .Single(x => x.Type == PendingApprovalNotificationSource.OvertimeTypeKey);

        Assert.Equal(NotificationSeverity.Critical, candidate.Severity);
    }

    // ---------------- Yönetim ----------------

    /// <summary>
    /// DOĞRULANMAMIŞ YIL PARAMETRESİ: kaynak kimliği YOK, tekilleştirme
    /// dönem anahtarıyla yürüyor — yıl başına tek bildirim. Bordro
    /// parametresi kritik çünkü doğrulanmadan bordro hiç üretilemiyor.
    /// </summary>
    [Fact]
    public async Task UnverifiedYearSettings_ProduceOneNotificationPerYear()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, _) = await CreateContextAsync(suffix);

        var candidates = await BuildAsync<ManagementNotificationSource>(companyId);

        var payroll = candidates.Single(x =>
            x.Type == ManagementNotificationSource.PayrollSettingsTypeKey);

        Assert.Null(payroll.SourceId);
        Assert.Equal(Today.Year.ToString(), payroll.PeriodKey);
        Assert.Equal(NotificationSeverity.Critical, payroll.Severity);

        var calendar = candidates.Single(x =>
            x.Type == ManagementNotificationSource.HolidayCalendarTypeKey);

        Assert.Equal(NotificationSeverity.Warning, calendar.Severity);
    }

    /// <summary>
    /// Parametre DOĞRULANINCA aday üretilmiyor; bildirim kapanır.
    /// </summary>
    [Fact]
    public async Task VerifiedPayrollSettings_ProduceNoCandidate()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, _) = await CreateContextAsync(suffix);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var settings = await db.CompanyPayrollSettings
                .SingleOrDefaultAsync(x => x.CompanyId == companyId &&
                                           x.Year == Today.Year);

            if (settings is null)
            {
                settings = new CompanyPayrollSettings
                {
                    CompanyId = companyId,
                    Year = Today.Year
                };

                db.CompanyPayrollSettings.Add(settings);
            }

            settings.VerifiedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync();
        }

        var candidates = await BuildAsync<ManagementNotificationSource>(companyId);

        Assert.DoesNotContain(candidates,
            x => x.Type == ManagementNotificationSource.PayrollSettingsTypeKey);
    }

    /// <summary>
    /// Onay bekleyen görevlendirme aday üretiyor; onaylanan üretmiyor.
    /// </summary>
    [Fact]
    public async Task RequestedDuty_NotifiesAndApprovedDutyDoesNot()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, personnelId) = await CreateContextAsync(suffix);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var projectId = await db.Projects
                .Where(x => x.CompanyId == companyId)
                .Select(x => x.Id)
                .FirstAsync();

            var pending = new PersonnelDuty
            {
                PersonnelId = personnelId,
                TargetProjectId = projectId,
                DutyType = PersonnelDutyType.Work,
                Status = PersonnelDutyStatus.Requested,
                StartDate = Today.AddDays(5),
                EndDate = Today.AddDays(7)
            };

            db.PersonnelDuties.AddRange(
                pending,
                new PersonnelDuty
                {
                    PersonnelId = personnelId,
                    TargetProjectId = projectId,
                    DutyType = PersonnelDutyType.Work,
                    Status = PersonnelDutyStatus.Approved,
                    StartDate = Today.AddDays(5),
                    EndDate = Today.AddDays(7)
                });

            await db.SaveChangesAsync();

            // KAYIT HAM SQL İLE ESKİTİLİYOR: AuditSaveChangesInterceptor
            // CreatedAtUtc'yi ekleme anında EZİYOR ve sonradan
            // değiştirilmesini de engelliyor. EF üzerinden eskitmek
            // mümkün değil; bekleme eşiğini sınamanın tek yolu bu.
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE personnel_duties SET \"CreatedAtUtc\" = {0} WHERE \"Id\" = {1}",
                DateTime.UtcNow.AddDays(-4), pending.Id);
        }

        var candidates = await BuildAsync<ManagementNotificationSource>(companyId);

        var duties = candidates
            .Where(x => x.Type == ManagementNotificationSource.DutyApprovalTypeKey)
            .ToList();

        Assert.Single(duties);
        Assert.Contains("4 gündür", duties[0].Title);
    }
}
