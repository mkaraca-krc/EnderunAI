using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.HumanResources;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Görev masrafı ve harcırah mahsubu.
///
/// Yol, konaklama ve harcırah AYRI kategoriler olarak defterlenir;
/// tek toplama çökertilmez — gider merkezi tarafı kırılımı soracak.
/// Mükerrer yansıma ReferenceType + ReferenceId ile engellenir.
/// </summary>
[Collection("Integration")]
public sealed class DutyExpenseSettlementTests(DatabaseFixture fixture)
{
    private const decimal DailyAllowance = 1_000m;
    private const decimal Travel = 2_500m;
    private const decimal Accommodation = 4_000m;

    // 8-12 Haziran = 5 gün → harcırah 5.000
    private static readonly DateTime Start =
        new(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime End =
        new(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc);

    private sealed record Context(
        Guid CompanyId, Guid PersonnelId, Guid TargetProjectId);

    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        project.Status = ProjectStatus.Active;

        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, suffix);

        await db.SaveChangesAsync();

        return new Context(project.CompanyId, personnel.Id, project.Id);
    }

    private async Task<HttpClient> ClientWithAsync(
        string[] permissionKeys, string? roleName = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "TestMasraf!2026";
        string username;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();

            var role = new AppRole { Name = $"TestMasraf-{suffix}" };
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

            username = $"masraf-{suffix}";
            var hash = passwords.Hash(password);

            var user = new AppUser
            {
                Username = username,
                FullName = "Masraf Test Kullanıcısı",
                PasswordHash = hash.Hash,
                PasswordSalt = hash.Salt,
                IsActive = true,
                WorkHoursExempt = true
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });

            if (roleName is not null)
            {
                var named = await db.Roles.SingleOrDefaultAsync(x => x.Name == roleName);

                if (named is null)
                {
                    named = new AppRole { Name = roleName };
                    db.Roles.Add(named);
                    await db.SaveChangesAsync();
                }

                db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = named.Id });
            }

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

    private static readonly string[] HrPermissions =
        [PermissionCatalog.Keys.PersonnelView, PermissionCatalog.Keys.PersonnelEdit,
         PermissionCatalog.Keys.ExtraPaymentView];

    /// <summary>Onaylı görev kurar; onay akışı Blok 1'de sınandı.</summary>
    private async Task<Guid> CreateApprovedDutyAsync(
        Context context, PersonnelDutyType dutyType = PersonnelDutyType.Work)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var duty = new PersonnelDuty
        {
            CompanyId = context.CompanyId,
            PersonnelId = context.PersonnelId,
            DutyType = dutyType,
            TargetProjectId = context.TargetProjectId,
            StartDate = Start,
            EndDate = End,
            IsOutOfCity = true,
            DailyAllowance = DailyAllowance,
            Purpose = "Şantiye denetimi",
            Status = PersonnelDutyStatus.Approved,
            ApprovedAtUtc = DateTime.UtcNow
        };

        db.PersonnelDuties.Add(duty);
        await db.SaveChangesAsync();

        return duty.Id;
    }

    private async Task<List<ProjectCostTransaction>> LoadCostsAsync(Context context)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.ProjectCostTransactions.AsNoTracking()
            .Where(x => x.ProjectId == context.TargetProjectId)
            .ToListAsync();
    }

    private static async Task<HttpResponseMessage> SaveExpenseAsync(
        HttpClient client, Guid dutyId,
        decimal travel = Travel,
        decimal accommodation = Accommodation,
        decimal receipt = 0m) =>
        await client.PostAsJsonAsync($"/api/hr/gorevlendirmeler/{dutyId}/masraf", new
        {
            travelCost = travel,
            accommodationCost = accommodation,
            receiptAmount = receipt
        });

    // ---------------- Kategori kırılımı ----------------

    /// <summary>
    /// Üç masraf AYRI kategori satırı olarak defterleniyor. Tek
    /// toplama çökertilseydi gider merkezi "ne kadar yol, ne kadar
    /// konaklama" sorusunu cevaplayamazdı.
    /// </summary>
    [Fact]
    public async Task ThreeExpenses_ArePostedAsSeparateCategories()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var dutyId = await CreateApprovedDutyAsync(context);

        var client = await ClientWithAsync(HrPermissions);

        Assert.Equal(HttpStatusCode.OK,
            (await SaveExpenseAsync(client, dutyId)).StatusCode);

        var costs = await LoadCostsAsync(context);

        var travel = costs.Single(
            x => x.ReferenceType == DutyExpensePostingService.TravelReference);
        var accommodation = costs.Single(
            x => x.ReferenceType == DutyExpensePostingService.AccommodationReference);
        var allowance = costs.Single(
            x => x.ReferenceType == DutyExpensePostingService.AllowanceReference);

        Assert.Equal(Travel, travel.Amount);
        Assert.Equal(Accommodation, accommodation.Amount);

        // 5 gün × 1.000 = 5.000
        Assert.Equal(5_000m, allowance.Amount);

        // Hepsi aynı göreve bağlı ve aynı sınıfta.
        Assert.All(costs, x =>
        {
            Assert.Equal(dutyId, x.ReferenceId);
            Assert.Equal(ProjectCostClass.Overhead, x.CostClass);
            Assert.StartsWith(
                DutyExpensePostingService.ReferencePrefix, x.ReferenceType);
        });
    }

    /// <summary>
    /// MÜKERRER YOK: masraf ikinci kez kaydedilince satır GÜNCELLENİR,
    /// yenisi açılmaz.
    /// </summary>
    [Fact]
    public async Task RepostingExpense_UpdatesInsteadOfDuplicating()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var dutyId = await CreateApprovedDutyAsync(context);

        var client = await ClientWithAsync(HrPermissions);

        await SaveExpenseAsync(client, dutyId);
        await SaveExpenseAsync(client, dutyId, travel: 3_000m);
        await SaveExpenseAsync(client, dutyId, travel: 3_000m);

        var costs = await LoadCostsAsync(context);

        Assert.Equal(3, costs.Count);
        Assert.Equal(3_000m, costs
            .Single(x => x.ReferenceType == DutyExpensePostingService.TravelReference)
            .Amount);

        // Toplam da bir kez sayılıyor: 3.000 + 4.000 + 5.000
        Assert.Equal(12_000m, costs.Sum(x => x.Amount));
    }

    /// <summary>
    /// Sıfıra düşen kalem defterden kalkıyor: sıfır tutarlı satır
    /// "yansıtıldı ama sıfır" ile "hiç yansıtılmadı" ayrımını
    /// bulanıklaştırırdı.
    /// </summary>
    [Fact]
    public async Task ZeroedExpense_IsRemovedFromTheLedger()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var dutyId = await CreateApprovedDutyAsync(context);

        var client = await ClientWithAsync(HrPermissions);

        await SaveExpenseAsync(client, dutyId);
        await SaveExpenseAsync(client, dutyId, travel: 0m);

        var costs = await LoadCostsAsync(context);

        Assert.DoesNotContain(costs, x =>
            x.ReferenceType == DutyExpensePostingService.TravelReference);
        Assert.Equal(2, costs.Count);
    }

    /// <summary>
    /// ONAYSIZ görev defterleme yapmıyor: talep aşamasındaki bir görev
    /// projenin kârını değiştirmemeli.
    /// </summary>
    [Fact]
    public async Task UnapprovedDuty_PostsNothing()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        Guid dutyId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var duty = new PersonnelDuty
            {
                CompanyId = context.CompanyId,
                PersonnelId = context.PersonnelId,
                DutyType = PersonnelDutyType.Work,
                TargetProjectId = context.TargetProjectId,
                StartDate = Start,
                EndDate = End,
                DailyAllowance = DailyAllowance,
                Purpose = "Onay bekliyor",
                Status = PersonnelDutyStatus.Requested
            };

            db.PersonnelDuties.Add(duty);
            await db.SaveChangesAsync();
            dutyId = duty.Id;
        }

        var client = await ClientWithAsync(HrPermissions);

        await SaveExpenseAsync(client, dutyId);

        Assert.Empty(await LoadCostsAsync(context));
    }

    /// <summary>
    /// Keşif görevinin masrafı da hedefe yansıyor: işçilik kaymasa da
    /// masraf gider.
    /// </summary>
    [Fact]
    public async Task VisitDuty_StillPostsExpenses()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var dutyId = await CreateApprovedDutyAsync(context, PersonnelDutyType.Visit);

        var client = await ClientWithAsync(HrPermissions);

        await SaveExpenseAsync(client, dutyId);

        var costs = await LoadCostsAsync(context);

        Assert.Equal(3, costs.Count);
        Assert.Equal(11_500m, costs.Sum(x => x.Amount));
    }

    // ---------------- Mahsup ----------------

    /// <summary>Fark = harcırah − fiş; fiş azsa mahsup bekliyor.</summary>
    [Fact]
    public async Task ReceiptBelowAllowance_LeavesAGap()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var dutyId = await CreateApprovedDutyAsync(context);

        var client = await ClientWithAsync(HrPermissions);

        var raw = await (await SaveExpenseAsync(client, dutyId, receipt: 3_200m))
            .Content.ReadAsStringAsync();

        var payload = JsonDocument.Parse(raw).RootElement;

        // 5.000 − 3.200 = 1.800
        Assert.Equal(1_800m, payload.GetProperty("settlementGap").GetDecimal());
        Assert.True(payload.GetProperty("settlementPending").GetBoolean());
    }

    /// <summary>
    /// Fiş harcırahı aşarsa fark sıfır: fazlası mahsup konusu değil.
    /// </summary>
    [Fact]
    public async Task ReceiptAboveAllowance_HasNoGap()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var dutyId = await CreateApprovedDutyAsync(context);

        var client = await ClientWithAsync(HrPermissions);

        var payload = JsonDocument.Parse(
            await (await SaveExpenseAsync(client, dutyId, receipt: 6_000m))
                .Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(0m, payload.GetProperty("settlementGap").GetDecimal());
        Assert.False(payload.GetProperty("settlementPending").GetBoolean());
    }

    /// <summary>
    /// "Personelden düş": kesinti YENİ yoldan değil, avans zincirinden
    /// yürüyor ve "Harcırah Mahsubu" etiketiyle açılıyor — personelin
    /// gerçek avans talepleriyle karışmasın.
    /// </summary>
    [Fact]
    public async Task DeductFromPersonnel_OpensALabelledAdvance()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var dutyId = await CreateApprovedDutyAsync(context);

        var client = await ClientWithAsync(HrPermissions);

        await SaveExpenseAsync(client, dutyId, receipt: 3_200m);

        var response = await client.PostAsJsonAsync(
            $"/api/hr/gorevlendirmeler/{dutyId}/mahsup",
            new { decision = 0, note = "Fiş getirilmedi", installmentCount = 2 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var advance = await hrDb.AdvanceRequests.AsNoTracking()
            .SingleAsync(x => x.PersonnelId == context.PersonnelId);

        Assert.Equal(1_800m, advance.ApprovedAmount);
        Assert.StartsWith("Harcırah Mahsubu", advance.Reason);
        Assert.Equal(2, advance.DeductionInstallmentCount);

        // Bordronun kesintiyi görmesi için ödenmiş sayılıyor.
        Assert.Equal(HrApprovalStatus.Paid, advance.Status);
    }

    /// <summary>
    /// KESİNTİ BİR KEZ: ikinci mahsup kararı reddediliyor, ikinci
    /// avans açılmıyor.
    /// </summary>
    [Fact]
    public async Task Settlement_IsProcessedOnlyOnce()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var dutyId = await CreateApprovedDutyAsync(context);

        var client = await ClientWithAsync(HrPermissions);

        await SaveExpenseAsync(client, dutyId, receipt: 3_200m);

        var body = new { decision = 0, note = "Fiş getirilmedi", installmentCount = 1 };

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/hr/gorevlendirmeler/{dutyId}/mahsup", body)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(
            $"/api/hr/gorevlendirmeler/{dutyId}/mahsup", body)).StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        Assert.Equal(1, await hrDb.AdvanceRequests
            .CountAsync(x => x.PersonnelId == context.PersonnelId));
    }

    /// <summary>
    /// "Gider kabul et" avans açmıyor ama karar kayıt altına alınıyor.
    /// </summary>
    [Fact]
    public async Task AcceptAsExpense_RecordsDecisionWithoutAdvance()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var dutyId = await CreateApprovedDutyAsync(context);

        var client = await ClientWithAsync(HrPermissions);

        await SaveExpenseAsync(client, dutyId, receipt: 3_200m);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/hr/gorevlendirmeler/{dutyId}/mahsup",
            new { decision = 1, note = "Uzak şantiye, fişsiz gider kabul" }))
            .StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var duty = await db.PersonnelDuties.AsNoTracking()
            .SingleAsync(x => x.Id == dutyId);

        Assert.Equal(DutySettlementDecision.AcceptAsExpense, duty.SettlementDecision);
        Assert.NotNull(duty.SettlementByUserId);
        Assert.NotNull(duty.SettlementAtUtc);
        Assert.Null(duty.SettlementAdvanceId);

        Assert.Equal(0, await hrDb.AdvanceRequests
            .CountAsync(x => x.PersonnelId == context.PersonnelId));
    }

    /// <summary>Gerekçesiz mahsup kararı reddediliyor.</summary>
    [Fact]
    public async Task Settlement_RequiresAReason()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var dutyId = await CreateApprovedDutyAsync(context);

        var client = await ClientWithAsync(HrPermissions);

        await SaveExpenseAsync(client, dutyId, receipt: 3_200m);

        var response = await client.PostAsJsonAsync(
            $"/api/hr/gorevlendirmeler/{dutyId}/mahsup",
            new { decision = 0, note = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------- Tutar gizliliği ----------------

    /// <summary>
    /// NEGATİF TEST: saha personeli masraf kaydedemiyor ve tutarları
    /// görmüyor.
    /// </summary>
    [Fact]
    public async Task PersonnelViewOnly_CannotSaveOrSeeAmounts()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var dutyId = await CreateApprovedDutyAsync(context);

        var hr = await ClientWithAsync(HrPermissions);
        await SaveExpenseAsync(hr, dutyId);

        var field = await ClientWithAsync([PermissionCatalog.Keys.PersonnelView]);

        // Masraf kaydedemez.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SaveExpenseAsync(field, dutyId)).StatusCode);

        // Listede tutar görmez.
        var raw = await (await field.GetAsync(
            $"/api/hr/gorevlendirmeler?personnelId={context.PersonnelId}"))
            .Content.ReadAsStringAsync();

        // Görevin varlığını ve amacını görür.
        Assert.Contains("Şantiye denetimi", raw);

        // Tutarlar gizlenmiyor, hiç gelmiyor.
        Assert.Contains("\"amountsHidden\":true", raw);
        Assert.Contains("\"dailyAllowance\":null", raw);
        Assert.Contains("\"totalAllowance\":null", raw);

        // Aynı kayıt yetkili kullanıcıda tutarı taşıyor: maskeleme
        // testi boş listeye bakıp yanılmıyor.
        var visible = await (await hr.GetAsync(
            $"/api/hr/gorevlendirmeler?personnelId={context.PersonnelId}"))
            .Content.ReadAsStringAsync();

        Assert.Contains("\"totalAllowance\":5000", visible);
    }

    // ---------------- Tutar yazma kapısı ----------------

    /// <summary>
    /// NEGATİF TEST: ek ödeme yetkisi olmayan personnel.edit kullanıcısı
    /// artık tutar YAZAMIYOR. Görmediği bir rakamı yazabilen kullanıcı
    /// yanlışını bir daha fark edemezdi.
    /// </summary>
    [Fact]
    public async Task WithoutExtraPaymentView_AmountWritesAreForbidden()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var dutyId = await CreateApprovedDutyAsync(context);

        var editor = await ClientWithAsync(
            [PermissionCatalog.Keys.PersonnelView,
             PermissionCatalog.Keys.PersonnelEdit]);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await SaveExpenseAsync(editor, dutyId)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PostAsJsonAsync(
            $"/api/hr/gorevlendirmeler/{dutyId}/harcirah",
            new { dailyAllowance = 1_200m, note = "Düzeltme" })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PostAsJsonAsync(
            $"/api/hr/gorevlendirmeler/{dutyId}/mahsup",
            new { decision = 1, note = "Kabul" })).StatusCode);

        // Hiçbiri deftere düşmedi.
        Assert.Empty(await LoadCostsAsync(context));
    }

    /// <summary>
    /// Talebi ek ödeme yetkisi olmayan İК açabiliyor ama harcırah
    /// SESSİZCE düşmüyor: sıfır kaydediliyor ve cevapta söyleniyor.
    /// Sessizce kaydedilseydi kimse tutarın kaybolduğunu fark etmezdi.
    /// </summary>
    [Fact]
    public async Task WithoutExtraPaymentView_DutyOpensWithoutTheAllowance()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var editor = await ClientWithAsync(
            [PermissionCatalog.Keys.PersonnelView,
             PermissionCatalog.Keys.PersonnelEdit]);

        var response = await editor.PostAsJsonAsync("/api/hr/gorevlendirmeler", new
        {
            companyId = context.CompanyId,
            personnelId = context.PersonnelId,
            dutyType = 0,
            targetProjectId = context.TargetProjectId,
            startDate = Start,
            endDate = End,
            isOutOfCity = true,
            dailyAllowance = DailyAllowance,
            purpose = "Talep İК'dan"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync()).RootElement;

        Assert.True(payload.GetProperty("allowanceDeferred").GetBoolean());

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var duty = await db.PersonnelDuties.AsNoTracking()
            .SingleAsync(x => x.Id == payload.GetProperty("id").GetGuid());

        Assert.Equal(0m, duty.DailyAllowance);
    }

    // ---------------- Harcırah düzeltme ----------------

    /// <summary>
    /// Düzeltme defterdeki harcırah satırını da yeni tutara çekiyor;
    /// satır güncelleniyor, ikincisi açılmıyor.
    /// </summary>
    [Fact]
    public async Task RevisingAllowance_UpdatesTheLedgerRowInPlace()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var dutyId = await CreateApprovedDutyAsync(context);

        var client = await ClientWithAsync(HrPermissions);

        await SaveExpenseAsync(client, dutyId);

        var response = await client.PostAsJsonAsync(
            $"/api/hr/gorevlendirmeler/{dutyId}/harcirah",
            new { dailyAllowance = 1_400m, note = "Şehir dışı tarifesi uygulandı" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(1_000m,
            payload.GetProperty("previousDailyAllowance").GetDecimal());

        // 5 gün × 1.400
        Assert.Equal(7_000m, payload.GetProperty("totalAllowance").GetDecimal());

        var costs = await LoadCostsAsync(context);

        Assert.Equal(3, costs.Count);
        Assert.Equal(7_000m, costs
            .Single(x => x.ReferenceType == DutyExpensePostingService.AllowanceReference)
            .Amount);

        // Yol ve konaklama düzeltmeden etkilenmedi.
        Assert.Equal(13_500m, costs.Sum(x => x.Amount));
    }

    /// <summary>Düzeltme iz bırakıyor: kim, ne zaman, neden.</summary>
    [Fact]
    public async Task RevisingAllowance_LeavesAnAuditTrail()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var dutyId = await CreateApprovedDutyAsync(context);

        var client = await ClientWithAsync(HrPermissions);

        await client.PostAsJsonAsync(
            $"/api/hr/gorevlendirmeler/{dutyId}/harcirah",
            new { dailyAllowance = 750m, note = "Şehir içi göreve çevrildi" });

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var duty = await db.PersonnelDuties.AsNoTracking()
            .SingleAsync(x => x.Id == dutyId);

        Assert.Equal(750m, duty.DailyAllowance);
        Assert.NotNull(duty.AllowanceRevisedAtUtc);
        Assert.NotNull(duty.AllowanceRevisedByUserId);
        Assert.Equal("Şehir içi göreve çevrildi", duty.AllowanceRevisionNote);
    }

    /// <summary>Gerekçesiz tutar değişimi denetlenemez.</summary>
    [Fact]
    public async Task RevisingAllowance_RequiresAReason()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var dutyId = await CreateApprovedDutyAsync(context);

        var client = await ClientWithAsync(HrPermissions);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            $"/api/hr/gorevlendirmeler/{dutyId}/harcirah",
            new { dailyAllowance = 750m, note = "  " })).StatusCode);
    }

    /// <summary>
    /// Mahsubu karara bağlanmış görevin harcırahı değişmiyor: kapanmış
    /// bir hesabı geriye dönük açardı — avans zaten eski farka göre
    /// açılmıştı.
    /// </summary>
    [Fact]
    public async Task AfterSettlement_AllowanceIsFrozen()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var dutyId = await CreateApprovedDutyAsync(context);

        var client = await ClientWithAsync(HrPermissions);

        await SaveExpenseAsync(client, dutyId, receipt: 3_200m);

        await client.PostAsJsonAsync(
            $"/api/hr/gorevlendirmeler/{dutyId}/mahsup",
            new { decision = 1, note = "Gider kabul edildi" });

        var response = await client.PostAsJsonAsync(
            $"/api/hr/gorevlendirmeler/{dutyId}/harcirah",
            new { dailyAllowance = 400m, note = "Düzeltme denemesi" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(DailyAllowance, (await db.PersonnelDuties.AsNoTracking()
            .SingleAsync(x => x.Id == dutyId)).DailyAllowance);
    }

    // ---------------- Detay ucu (ekranın beslediği yer) ----------------

    /// <summary>
    /// Ekran masraf kırılımını detay ucundan okuyor; liste her satırda
    /// masraf taşımıyor.
    /// </summary>
    [Fact]
    public async Task Detail_CarriesTheExpenseBreakdown()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var dutyId = await CreateApprovedDutyAsync(context);

        var client = await ClientWithAsync(HrPermissions);

        await SaveExpenseAsync(client, dutyId, receipt: 3_200m);

        var payload = JsonDocument.Parse(await (await client.GetAsync(
            $"/api/hr/gorevlendirmeler/{dutyId}"))
            .Content.ReadAsStringAsync()).RootElement;

        Assert.False(payload.GetProperty("amountsHidden").GetBoolean());
        Assert.Equal(Travel, payload.GetProperty("travelCost").GetDecimal());
        Assert.Equal(Accommodation,
            payload.GetProperty("accommodationCost").GetDecimal());
        Assert.Equal(5_000m, payload.GetProperty("totalAllowance").GetDecimal());
        Assert.Equal(11_500m, payload.GetProperty("totalExpense").GetDecimal());
        Assert.Equal(1_800m, payload.GetProperty("settlementGap").GetDecimal());
        Assert.True(payload.GetProperty("settlementPending").GetBoolean());

        // Keşif olmayan görevde rapor da beklenmiyor.
        Assert.False(payload.GetProperty("hasSurveyReport").GetBoolean());
    }

    /// <summary>
    /// NEGATİF TEST: saha personeli detayı açabiliyor ama TEK BİR
    /// tutar alanı bile dolu gelmiyor — mahsup bekliyor bilgisi dahil,
    /// çünkü o da "bu kişiye fark borcu var" demektir.
    /// </summary>
    [Fact]
    public async Task Detail_HidesEveryAmountFromFieldStaff()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var dutyId = await CreateApprovedDutyAsync(context);

        var hr = await ClientWithAsync(HrPermissions);
        await SaveExpenseAsync(hr, dutyId, receipt: 3_200m);

        var field = await ClientWithAsync([PermissionCatalog.Keys.PersonnelView]);

        var response = await field.GetAsync($"/api/hr/gorevlendirmeler/{dutyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync()).RootElement;

        // Görevi ve tarihini görüyor.
        Assert.Equal("Şantiye denetimi", payload.GetProperty("purpose").GetString());
        Assert.Equal(5, payload.GetProperty("dayCount").GetInt32());

        Assert.True(payload.GetProperty("amountsHidden").GetBoolean());

        foreach (var field_ in new[]
        {
            "dailyAllowance", "totalAllowance", "travelCost",
            "accommodationCost", "receiptAmount", "totalExpense",
            "settlementGap", "settlementPending", "settlementDecision",
            "settlementNote", "settlementAdvanceId",
            "allowanceRevisedAtUtc", "allowanceRevisionNote"
        })
        {
            Assert.Equal(
                JsonValueKind.Null, payload.GetProperty(field_).ValueKind);
        }
    }
}
