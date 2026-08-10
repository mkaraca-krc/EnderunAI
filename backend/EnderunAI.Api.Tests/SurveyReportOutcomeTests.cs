using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.HumanResources;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Keşif saha raporu ve kazan/kaybet gider ayrımı.
///
/// KAZANILDI → keşif masrafı olduğu yerde kalır; aynı proje aktife
/// alındığı için "gerçek projeye bağlanma" kendiliğinden olur.
/// KAYBEDİLDİ → masraf silinmez, "proje adı — Proje Keşfi" gideri
/// olarak okunur; saha raporu arşivde kalır.
///
/// Her iki yolda da TOPLAM TUTAR DEĞİŞMEZ: kararın kendisi para
/// yaratmaz, yok etmez.
/// </summary>
[Collection("Integration")]
public sealed class SurveyReportOutcomeTests(DatabaseFixture fixture)
{
    private static readonly DateTime Start =
        new(2026, 7, 6, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime End =
        new(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc);

    private const decimal DailyAllowance = 900m;
    private const decimal Travel = 1_400m;
    private const decimal Accommodation = 2_100m;

    // 3 gün × 900 = 2.700 · toplam 6.200
    private const decimal TotalExpense = 6_200m;

    private sealed record Context(
        Guid CompanyId, Guid PersonnelId, Guid ProjectId, string ProjectName);

    /// <summary>Keşif statüsünde bir proje ve ona açılmış onaylı keşif görevi.</summary>
    private async Task<(Context Context, Guid DutyId)> CreateSurveyAsync(
        bool withEmployer = true)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        project.Status = ProjectStatus.Kesif;

        // Kazanma işveren kartı arıyor; kartsız hâl ayrıca sınanıyor.
        if (!withEmployer)
            project.EmployerCurrentAccountId = null;

        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, suffix);

        var duty = new PersonnelDuty
        {
            CompanyId = project.CompanyId,
            PersonnelId = personnel.Id,
            DutyType = PersonnelDutyType.Survey,
            TargetProjectId = project.Id,
            StartDate = Start,
            EndDate = End,
            IsOutOfCity = true,
            DailyAllowance = DailyAllowance,
            TravelCost = Travel,
            AccommodationCost = Accommodation,
            Purpose = "Keşif ölçüm ve fizibilite",
            Status = PersonnelDutyStatus.Approved,
            ApprovedAtUtc = DateTime.UtcNow
        };

        db.PersonnelDuties.Add(duty);
        await db.SaveChangesAsync();

        // Masraf defterini onaylı görevin üzerinden kur.
        var posting = scope.ServiceProvider
            .GetRequiredService<DutyExpensePostingService>();

        await posting.PostAsync(duty, CancellationToken.None);
        await db.SaveChangesAsync();

        return (new Context(project.CompanyId, personnel.Id, project.Id, project.Name),
            duty.Id);
    }

    private async Task<HttpClient> ClientWithAsync(string[] permissionKeys)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "TestKesif!2026";
        string username;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();

            var role = new AppRole { Name = $"TestKesif-{suffix}" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();

            var permissions = await db.Permissions
                .Where(x => permissionKeys.Contains(x.Key))
                .ToListAsync();

            foreach (var permission in permissions)
            {
                db.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.Id,
                    PermissionId = permission.Id
                });
            }

            username = $"kesif-{suffix}";
            var hash = passwords.Hash(password);

            var user = new AppUser
            {
                Username = username,
                FullName = "Keşif Test Kullanıcısı",
                PasswordHash = hash.Hash,
                PasswordSalt = hash.Salt,
                IsActive = true,
                WorkHoursExempt = true
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });

            db.UserDataScopes.Add(new UserDataScope
            {
                UserId = user.Id,
                ScopeType = DataScopeType.All
            });

            await db.SaveChangesAsync();
        }

        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, username, password);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private static readonly string[] TechnicalPermissions =
        [PermissionCatalog.Keys.ProjectsView, PermissionCatalog.Keys.ProjectsEdit,
         PermissionCatalog.Keys.PersonnelView];

    private async Task<List<ProjectCostTransaction>> LoadCostsAsync(Guid projectId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.ProjectCostTransactions.AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .ToListAsync();
    }

    private static object ReportBody(string summary = "Zemin sağlam, ulaşım kolay.") => new
    {
        summary,
        siteConditions = "Mevcut yapı ayakta, yıkım gerekmiyor.",
        accessNotes = "Tır girişi var, vinç kurulabilir.",
        risks = "Elektrik altyapısı belirsiz.",
        recommendBid = true,
        measurements = new[]
        {
            new { description = "Cephe alanı", quantity = 480.5m, unit = "m2", note = (string?)null },
            new { description = "Kolon adedi", quantity = 24m, unit = "adet", note = "kesitler ölçülmedi" }
        }
    };

    private static Task<HttpResponseMessage> SaveReportAsync(
        HttpClient client, Guid dutyId, object? body = null) =>
        client.PutAsJsonAsync(
            $"/api/hr/gorevlendirmeler/{dutyId}/saha-raporu", body ?? ReportBody());

    private static Task<HttpResponseMessage> SetOutcomeAsync(
        HttpClient client, Guid projectId, int outcome, string? note) =>
        client.PostAsJsonAsync(
            $"/api/projects/{projectId}/kesif-sonucu", new { outcome, note });

    // ---------------- Saha raporu ----------------

    /// <summary>
    /// Rapor metin + ölçümle kaydediliyor; ölçümler ayrı satırlarda
    /// durduğu için sonradan poza çevrilebilir.
    /// </summary>
    [Fact]
    public async Task SurveyReport_IsSavedWithStructuredMeasurements()
    {
        var (context, dutyId) = await CreateSurveyAsync();
        var client = await ClientWithAsync(TechnicalPermissions);

        Assert.Equal(HttpStatusCode.OK,
            (await SaveReportAsync(client, dutyId)).StatusCode);

        var payload = JsonDocument.Parse(await (await client.GetAsync(
            $"/api/hr/gorevlendirmeler/{dutyId}/saha-raporu"))
            .Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(context.ProjectId, payload.GetProperty("projectId").GetGuid());
        Assert.True(payload.GetProperty("recommendBid").GetBoolean());

        var measurements = payload.GetProperty("measurements");

        Assert.Equal(2, measurements.GetArrayLength());
        Assert.Equal(480.5m, measurements[0].GetProperty("quantity").GetDecimal());
        Assert.Equal("m2", measurements[0].GetProperty("unit").GetString());
    }

    /// <summary>
    /// Görev başına TEK rapor: ikinci kayıt aynı raporu günceller,
    /// "hangisi geçerli" sorusu doğmaz.
    /// </summary>
    [Fact]
    public async Task SecondSave_UpdatesTheSameReport()
    {
        var (context, dutyId) = await CreateSurveyAsync();
        var client = await ClientWithAsync(TechnicalPermissions);

        await SaveReportAsync(client, dutyId);
        await SaveReportAsync(client, dutyId, new
        {
            summary = "Düzeltme: zemin etüdü gerekiyor.",
            measurements = new[]
            {
                new { description = "Cephe alanı", quantity = 500m, unit = "m2" }
            }
        });

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var reports = await db.DutySurveyReports.AsNoTracking()
            .Where(x => x.ProjectId == context.ProjectId)
            .ToListAsync();

        var single = Assert.Single(reports);

        Assert.StartsWith("Düzeltme", single.Summary);

        // Eski ölçümler kaldı mı: liste bütün olarak yenileniyor.
        Assert.Equal(1, await db.DutySurveyMeasurements
            .CountAsync(x => x.SurveyReportId == single.Id));
    }

    /// <summary>Onaysız görev yapılmamıştır; raporu da olamaz.</summary>
    [Fact]
    public async Task UnapprovedDuty_CannotHaveAReport()
    {
        var (_, dutyId) = await CreateSurveyAsync();

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var duty = await db.PersonnelDuties.SingleAsync(x => x.Id == dutyId);
            duty.Status = PersonnelDutyStatus.Requested;
            await db.SaveChangesAsync();
        }

        var client = await ClientWithAsync(TechnicalPermissions);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await SaveReportAsync(client, dutyId)).StatusCode);
    }

    /// <summary>Saha raporu keşif görevine yazılır; çalışma görevine değil.</summary>
    [Fact]
    public async Task WorkDuty_CannotHaveASurveyReport()
    {
        var (_, dutyId) = await CreateSurveyAsync();

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var duty = await db.PersonnelDuties.SingleAsync(x => x.Id == dutyId);
            duty.DutyType = PersonnelDutyType.Work;
            await db.SaveChangesAsync();
        }

        var client = await ClientWithAsync(TechnicalPermissions);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await SaveReportAsync(client, dutyId)).StatusCode);
    }

    // ---------------- Kazanıldı ----------------

    /// <summary>
    /// KAZANILDI: proje aktife alınır ve keşif masrafı olduğu yerde
    /// kalır. Aynı proje olduğu için "gerçek projeye bağlanma" ayrı
    /// bir taşıma gerektirmiyor — taşınsaydı aynı harcama iki
    /// defterde görünebilirdi.
    /// </summary>
    [Fact]
    public async Task Won_ActivatesProjectAndKeepsSurveyCostInPlace()
    {
        var (context, _) = await CreateSurveyAsync();

        var before = await LoadCostsAsync(context.ProjectId);

        Assert.Equal(3, before.Count);
        Assert.Equal(TotalExpense, before.Sum(x => x.Amount));

        var client = await ClientWithAsync(TechnicalPermissions);

        Assert.Equal(HttpStatusCode.OK,
            (await SetOutcomeAsync(client, context.ProjectId, 1, "İhale alındı"))
            .StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await db.Projects.AsNoTracking()
            .SingleAsync(x => x.Id == context.ProjectId);

        Assert.Equal(ProjectStatus.Active, project.Status);
        Assert.Equal(ProjectSurveyOutcome.Won, project.SurveyOutcome);
        Assert.NotNull(project.SurveyOutcomeAtUtc);

        var after = await LoadCostsAsync(context.ProjectId);

        // Satırlar aynı, tutar aynı, proje aynı: yaratılmadı, taşınmadı.
        Assert.Equal(3, after.Count);
        Assert.Equal(TotalExpense, after.Sum(x => x.Amount));
        Assert.Equal(
            before.Select(x => x.Id).OrderBy(x => x),
            after.Select(x => x.Id).OrderBy(x => x));
    }

    /// <summary>
    /// İşveren cari kartı yoksa iş kazanıldı olarak işaretlenemiyor;
    /// proje keşifte kalıyor. Kural proje ekranıyla ORTAK — keşiften
    /// gelen proje ekranın izin vermeyeceği bir durumda aktife
    /// düşmemeli.
    /// </summary>
    [Fact]
    public async Task Won_RequiresAnEmployerAccount()
    {
        var (context, _) = await CreateSurveyAsync(withEmployer: false);
        var client = await ClientWithAsync(TechnicalPermissions);

        var response = await SetOutcomeAsync(
            client, context.ProjectId, 1, "İhale alındı");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await db.Projects.AsNoTracking()
            .SingleAsync(x => x.Id == context.ProjectId);

        Assert.Equal(ProjectStatus.Kesif, project.Status);
        Assert.Equal(ProjectSurveyOutcome.Pending, project.SurveyOutcome);
    }

    // ---------------- Kaybedildi ----------------

    /// <summary>
    /// KAYBEDİLDİ: masraf SİLİNMEZ — gerçek para harcandı. Satırlar
    /// yerinde kalır, tutar değişmez, ama artık "proje adı — Proje
    /// Keşfi" gideri olarak okunur.
    /// </summary>
    [Fact]
    public async Task Lost_KeepsTheMoneyAndRelabelsItAsSurveyExpense()
    {
        var (context, _) = await CreateSurveyAsync();
        var client = await ClientWithAsync(TechnicalPermissions);

        Assert.Equal(HttpStatusCode.OK, (await SetOutcomeAsync(
            client, context.ProjectId, 2, "Fiyat tutmadı")).StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await db.Projects.AsNoTracking()
            .SingleAsync(x => x.Id == context.ProjectId);

        Assert.Equal(ProjectStatus.Cancelled, project.Status);
        Assert.Equal(ProjectSurveyOutcome.Lost, project.SurveyOutcome);
        Assert.Equal("Fiyat tutmadı", project.SurveyOutcomeNote);

        var costs = await LoadCostsAsync(context.ProjectId);

        // Para duruyor: kaybetmek harcamayı geri getirmez.
        Assert.Equal(3, costs.Count);
        Assert.Equal(TotalExpense, costs.Sum(x => x.Amount));

        // Ama artık kazanılmış bir işin maliyeti gibi okunmuyor.
        Assert.All(costs, x =>
            Assert.Contains($"{context.ProjectName} — Proje Keşfi", x.Description));

        // Kategori kırılımı kaybedildiğinde de duruyor.
        Assert.Contains(costs, x =>
            x.ReferenceType == DutyExpensePostingService.TravelReference);
        Assert.Contains(costs, x =>
            x.ReferenceType == DutyExpensePostingService.AccommodationReference);
        Assert.Contains(costs, x =>
            x.ReferenceType == DutyExpensePostingService.AllowanceReference);
    }

    /// <summary>
    /// RAPOR ARŞİVDE KALIR: iş kaybedilse de rapor okunabiliyor ve
    /// projenin keşif dosyasında görünüyor. Bir sonraki benzer
    /// teklifte okunacak tek kayıt odur.
    /// </summary>
    [Fact]
    public async Task Lost_LeavesTheSurveyReportInTheArchive()
    {
        var (context, dutyId) = await CreateSurveyAsync();
        var client = await ClientWithAsync(TechnicalPermissions);

        await SaveReportAsync(client, dutyId);

        await SetOutcomeAsync(client, context.ProjectId, 2, "İşveren vazgeçti");

        var report = await client.GetAsync(
            $"/api/hr/gorevlendirmeler/{dutyId}/saha-raporu");

        Assert.Equal(HttpStatusCode.OK, report.StatusCode);

        var dossier = JsonDocument.Parse(await (await client.GetAsync(
            $"/api/projects/{context.ProjectId}/kesif-dosyasi"))
            .Content.ReadAsStringAsync()).RootElement;

        Assert.Equal("Kaybedildi",
            dossier.GetProperty("surveyOutcomeName").GetString());
        Assert.Equal(1, dossier.GetProperty("reports").GetArrayLength());
        Assert.Equal(2, dossier.GetProperty("reports")[0]
            .GetProperty("measurementCount").GetInt32());
    }

    /// <summary>Gerekçesiz kaybetme, sonraki teklife hiçbir şey bırakmaz.</summary>
    [Fact]
    public async Task Lost_RequiresAReason()
    {
        var (context, _) = await CreateSurveyAsync();
        var client = await ClientWithAsync(TechnicalPermissions);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await SetOutcomeAsync(client, context.ProjectId, 2, "   ")).StatusCode);
    }

    // ---------------- Sonuç kapısı ----------------

    /// <summary>Sonuç bir kez girilir; ikincisi reddedilir.</summary>
    [Fact]
    public async Task Outcome_IsDecidedOnlyOnce()
    {
        var (context, _) = await CreateSurveyAsync();
        var client = await ClientWithAsync(TechnicalPermissions);

        Assert.Equal(HttpStatusCode.OK, (await SetOutcomeAsync(
            client, context.ProjectId, 2, "Fiyat tutmadı")).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await SetOutcomeAsync(
            client, context.ProjectId, 1, "Yanlış girdim")).StatusCode);
    }

    /// <summary>Keşif dışındaki projede keşif sonucu aranmaz.</summary>
    [Fact]
    public async Task NonSurveyProject_HasNoOutcome()
    {
        var (context, _) = await CreateSurveyAsync();

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = await db.Projects.SingleAsync(x => x.Id == context.ProjectId);
            project.Status = ProjectStatus.Active;
            await db.SaveChangesAsync();
        }

        var client = await ClientWithAsync(TechnicalPermissions);

        Assert.Equal(HttpStatusCode.BadRequest, (await SetOutcomeAsync(
            client, context.ProjectId, 2, "Fiyat tutmadı")).StatusCode);
    }

    /// <summary>
    /// NEGATİF TEST: proje düzenleme yetkisi olmayan kullanıcı keşif
    /// sonucunu giremiyor — karar bir işi aktife alıyor ya da iptale
    /// çekiyor.
    /// </summary>
    [Fact]
    public async Task WithoutProjectsEdit_OutcomeIsForbidden()
    {
        var (context, _) = await CreateSurveyAsync();

        var reader = await ClientWithAsync(
            [PermissionCatalog.Keys.ProjectsView, PermissionCatalog.Keys.PersonnelView]);

        Assert.Equal(HttpStatusCode.Forbidden, (await SetOutcomeAsync(
            reader, context.ProjectId, 2, "Fiyat tutmadı")).StatusCode);
    }

    /// <summary>
    /// NEGATİF TEST: saha personeli raporu okuyabilir ama raporda
    /// tutar yoktur — harcırah ve masraf görevlendirme uçlarında ve
    /// extra_payment.view maskelemesine tabidir.
    /// </summary>
    [Fact]
    public async Task SurveyReport_CarriesNoAmounts()
    {
        var (_, dutyId) = await CreateSurveyAsync();

        var writer = await ClientWithAsync(TechnicalPermissions);
        await SaveReportAsync(writer, dutyId);

        var field = await ClientWithAsync([PermissionCatalog.Keys.PersonnelView]);

        var response = await field.GetAsync(
            $"/api/hr/gorevlendirmeler/{dutyId}/saha-raporu");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();

        // Rapor teknik belge: hiçbir masraf alanı taşımıyor.
        // (Sayı aramak yanıltıcı olurdu — GUID'ler ve ölçüm miktarları
        // rastgele rakam dizileri içerir; alan adları kesin kanıt.)
        Assert.DoesNotContain("allowance", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("travelCost", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accommodation", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("receipt", raw, StringComparison.OrdinalIgnoreCase);

        // Raporu yazmak da onun işi değil.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SaveReportAsync(field, dutyId)).StatusCode);
    }
}
