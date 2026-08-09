using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Personel kartının fazla mesai paneli.
///
/// Yetki ayrımı H2/Block 1 çizgisinde: saat, döküm ve muvafakat
/// personnel.view ile açık (şantiye şefi ve formen kendi ekibinin
/// mesaisini görmeden çalışamaz). TUTAR ise ELDEN ödemedir ve elden
/// izolasyonuna tabidir: yalnızca extra_payment.view olan kullanıcı
/// görür. payroll.view tek başına yetmez.
/// </summary>
[Collection("Integration")]
public sealed class PersonnelOvertimePanelTests(DatabaseFixture fixture)
{
    // "Bu ay" bloğu bugüne bağlı; yıl da bugünden alınıyor ki test
    // yıl dönümünde kırılmasın.
    private static readonly int Year = DateTime.UtcNow.Year;

    private static readonly DateTime CurrentMonthDay = new(
        Year, DateTime.UtcNow.Month, 5, 0, 0, 0, DateTimeKind.Utc);

    // Taban ele geçen = resmî net 45.000 + manuel elden 9.000 = 54.000
    // Saatlik = 54.000 / (30 × 7,5) = 240
    private const decimal OfficialNet = 45_000m;
    private const decimal ManualExtra = 9_000m;
    private const decimal ExpectedHourly = 240m;

    private sealed record Context(Guid CompanyId, Guid PersonnelId);

    /// <summary>
    /// Resmî neti 45.000, manuel eldeni 9.000 olan personel:
    /// taban 54.000 → saatlik 240 TL.
    /// 10 saat fazla çalışma (1,5× → 3.600) ve 8 saat genel tatil
    /// (2× → 3.840).
    /// </summary>
    private async Task<Context> CreateContextAsync(
        string suffix, decimal? annualLimit = null, int? consentYear = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, suffix);

        personnel.OvertimeConsentYear = consentYear;

        db.CompanyPayrollSettings.Add(new CompanyPayrollSettings
        {
            CompanyId = project.CompanyId,
            Year = Year,
            MinimumWageGross = 33_030m,
            SgkBaseFloor = 33_030m,
            SgkBaseCeiling = 247_725m,
            DailyWorkHours = 7.5m,
            AnnualOvertimeHourLimit = annualLimit
        });

        db.PersonnelExtraPayments.Add(new PersonnelExtraPayment
        {
            CompanyId = project.CompanyId,
            PersonnelId = personnel.Id,
            MonthlyAmount = ManualExtra,
            EffectiveStartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        await db.SaveChangesAsync();

        hrDb.SalaryDefinitions.Add(new HrSalaryDefinition
        {
            CompanyId = project.CompanyId,
            PersonnelId = personnel.Id,
            EffectiveStartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            SalaryBasis = SalaryBasis.Net,
            TargetNetSalary = OfficialNet,
            GrossSalary = 60_000m,
            OvertimeMultiplier = 1.5m,
            SundayMultiplier = 2m,
            PublicHolidayMultiplier = 2m,
            CurrencyCode = "TRY"
        });

        hrDb.OvertimeRequests.AddRange(
            new HrOvertimeRequest
            {
                CompanyId = project.CompanyId,
                PersonnelId = personnel.Id,
                WorkDate = new DateTime(Year, 3, 10, 0, 0, 0, DateTimeKind.Utc),
                RequestedHours = 10m,
                ApprovedHours = 10m,
                Status = HrApprovalStatus.Approved,
                Reason = "Termin",
                AttendanceRecordId = Guid.NewGuid()
            },
            new HrOvertimeRequest
            {
                CompanyId = project.CompanyId,
                PersonnelId = personnel.Id,
                WorkDate = new DateTime(Year, 4, 23, 0, 0, 0, DateTimeKind.Utc),
                RequestedHours = 8m,
                ApprovedHours = 8m,
                IsPublicHolidayWork = true,
                Status = HrApprovalStatus.Approved,
                Reason = "Bayram vardiyası"
            },
            // Bu ayın mesaisi: ele geçen hesabına bu satır girer.
            new HrOvertimeRequest
            {
                CompanyId = project.CompanyId,
                PersonnelId = personnel.Id,
                WorkDate = CurrentMonthDay,
                RequestedHours = 5m,
                ApprovedHours = 5m,
                Status = HrApprovalStatus.Approved,
                Reason = "Bu ay mesai",
                AttendanceRecordId = Guid.NewGuid()
            });

        await hrDb.SaveChangesAsync();

        return new Context(project.CompanyId, personnel.Id);
    }

    private async Task<HttpClient> ClientWithAsync(params string[] permissionKeys)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "TestMesai!2026";
        string username;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();

            var role = new AppRole { Name = $"TestMesai-{suffix}" };
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

            username = $"mesai-{suffix}";
            var hash = passwords.Hash(password);

            var user = new AppUser
            {
                Username = username,
                FullName = "Saha Kullanıcısı",
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

    private static string Url(Context context) =>
        $"/api/hr/personel/{context.PersonnelId}/fazla-mesai?year={Year}";

    // ---------------- Saat ve döküm ----------------

    /// <summary>
    /// Yıllık kümülatif ve tür kırılımı köprüdeki kuralla aynı:
    /// sayıma yalnızca FAZLA ÇALIŞMA girer, tatil çalışması girmez.
    /// </summary>
    [Fact]
    public async Task Panel_SeparatesOvertimeFromHolidayWork()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, annualLimit: 270m);

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelView);

        var response = await client.GetAsync(Url(context));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(15m, payload.GetProperty("overtimeHours").GetDecimal());
        Assert.Equal(8m, payload.GetProperty("publicHolidayHours").GetDecimal());
        Assert.Equal(0m, payload.GetProperty("sundayHours").GetDecimal());
        Assert.Equal("ok", payload.GetProperty("limitStatus").GetString());
        Assert.True(payload.GetProperty("limitCountsOvertimeOnly").GetBoolean());
    }

    /// <summary>Sınır aşımı ve yaklaşma durumları.</summary>
    [Theory]
    [InlineData(10, "exceeded")]
    [InlineData(16, "near")]
    [InlineData(100, "ok")]
    public async Task LimitStatus_ReflectsTheAnnualCap(int limit, string expected)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, annualLimit: limit);

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelView);

        var payload = JsonDocument.Parse(await (await client.GetAsync(Url(context)))
            .Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(expected, payload.GetProperty("limitStatus").GetString());
    }

    /// <summary>
    /// Sınır tanımlı değilse "girilmedi" denir — koda gömülü bir 270
    /// varsayılmaz; bordro ön kontrolüyle aynı dil.
    /// </summary>
    [Fact]
    public async Task WithoutLimit_StatusIsUndefined()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelView);

        var raw = await (await client.GetAsync(Url(context))).Content
            .ReadAsStringAsync();

        var payload = JsonDocument.Parse(raw).RootElement;

        Assert.Equal("undefined", payload.GetProperty("limitStatus").GetString());
        Assert.Equal(
            JsonValueKind.Null, payload.GetProperty("annualLimit").ValueKind);
        Assert.Contains("girilmedi", raw);
    }

    /// <summary>
    /// Döküm satırları tür, çarpan ve puantaja düşüp düşmediğini
    /// taşıyor. Puantaja düşmeyen saat bordroya girmez; kartta
    /// görünmesi gerekir.
    /// </summary>
    [Fact]
    public async Task Lines_CarryKindAndAttendanceLanding()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelView);

        var payload = JsonDocument.Parse(await (await client.GetAsync(Url(context)))
            .Content.ReadAsStringAsync()).RootElement;

        var lines = payload.GetProperty("lines").EnumerateArray().ToList();

        Assert.Equal(3, lines.Count);

        var holiday = lines.Single(x => x.GetProperty("kind").GetInt32() == 2);
        Assert.Equal("Genel tatil çalışması",
            holiday.GetProperty("kindName").GetString());
        Assert.Equal(2m, holiday.GetProperty("multiplier").GetDecimal());
        Assert.False(holiday.GetProperty("landedOnAttendance").GetBoolean());

        var overtime = lines.Single(
            x => x.GetProperty("kind").GetInt32() == 0 &&
                 x.GetProperty("hours").GetDecimal() == 10m);
        Assert.Equal(1.5m, overtime.GetProperty("multiplier").GetDecimal());
        Assert.True(overtime.GetProperty("landedOnAttendance").GetBoolean());
        Assert.Equal("2026-03",
            overtime.GetProperty("attendanceMonth").GetString());

        Assert.Equal(1, payload.GetProperty("notLandedCount").GetInt32());
    }

    /// <summary>
    /// Muvafakat yalnızca AYNI yıl için geçerli: geçen yılın onayı bu
    /// yılın mesaisini karşılamaz.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(2000)]
    public async Task Consent_IsInvalidWithoutMatchingYear(int? consentYear)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, consentYear: consentYear);

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelView);

        var payload = JsonDocument.Parse(await (await client.GetAsync(Url(context)))
            .Content.ReadAsStringAsync()).RootElement;

        Assert.False(
            payload.GetProperty("consent").GetProperty("isValid").GetBoolean());
    }

    [Fact]
    public async Task Consent_IsValidForTheSameYear()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, consentYear: Year);

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelView);

        var payload = JsonDocument.Parse(await (await client.GetAsync(Url(context)))
            .Content.ReadAsStringAsync()).RootElement;

        Assert.True(
            payload.GetProperty("consent").GetProperty("isValid").GetBoolean());
    }

    // ---------------- Tutar sızıntısı ----------------

    /// <summary>
    /// NEGATİF TEST: mesai tutarı ELDEN ödemedir ve elden
    /// izolasyonuna tabidir. personnel.view olan saha kullanıcısı
    /// saatleri görüyor ama hiçbir tutarı görmüyor. Yanıtın HAM
    /// METNİNDE tutar aranıyor — alan adı değişse bile sızıntı
    /// yakalanır.
    ///
    /// Taban 54.000 → saatlik 240. 10×240×1,5 = 3.600 ·
    /// 8×240×2 = 3.840 · 5×240×1,5 = 1.800.
    /// </summary>
    [Fact]
    public async Task PersonnelViewOnly_SeesHoursButNoAmount()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, annualLimit: 270m);

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelView);

        var raw = await (await client.GetAsync(Url(context))).Content
            .ReadAsStringAsync();

        // Saatler görünüyor: panelin işi bu.
        Assert.Contains("\"overtimeHours\":15", raw);

        // Hiçbir tutar gelmiyor — resmî net ve elden dahil.
        foreach (var amount in new[]
                 { "3600", "3840", "1800", "9000", "45000", "54000", "240" })
        {
            Assert.DoesNotContain(amount, raw);
        }

        var payload = JsonDocument.Parse(raw).RootElement;

        Assert.True(payload.GetProperty("amountsHidden").GetBoolean());
        Assert.Equal(
            JsonValueKind.Null, payload.GetProperty("totalAmount").ValueKind);

        var takeHome = payload.GetProperty("takeHome");

        Assert.Equal(JsonValueKind.Null, takeHome.GetProperty("officialNet").ValueKind);
        Assert.Equal(
            JsonValueKind.Null, takeHome.GetProperty("totalTakeHome").ValueKind);
        Assert.Equal(
            JsonValueKind.Null, takeHome.GetProperty("hourlyRate").ValueKind);

        foreach (var line in payload.GetProperty("lines").EnumerateArray())
            Assert.Equal(JsonValueKind.Null, line.GetProperty("amount").ValueKind);
    }

    /// <summary>
    /// Mesai saat ücreti RESMÎ NET + MANUEL ELDEN üzerinden yürüyor,
    /// yalnız resmî tutar değil.
    ///
    /// Taban = 45.000 + 9.000 = 54.000 → saatlik = 54.000 / (30 × 7,5)
    /// = 240. Salt resmî netten yürüseydi saatlik 200 çıkardı.
    /// </summary>
    [Fact]
    public async Task HourlyRate_IsBasedOnNetPlusCash()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(
            PermissionCatalog.Keys.PersonnelView,
            PermissionCatalog.Keys.ExtraPaymentView);

        var payload = JsonDocument.Parse(await (await client.GetAsync(Url(context)))
            .Content.ReadAsStringAsync()).RootElement;

        var takeHome = payload.GetProperty("takeHome");

        Assert.Equal(OfficialNet, takeHome.GetProperty("officialNet").GetDecimal());
        Assert.Equal(
            ManualExtra, takeHome.GetProperty("manualExtraMonthly").GetDecimal());
        Assert.Equal(ExpectedHourly, takeHome.GetProperty("hourlyRate").GetDecimal());

        // 10 × 240 × 1,5 = 3.600
        var lines = payload.GetProperty("lines").EnumerateArray().ToList();

        Assert.Equal(3_600m, lines
            .Single(x => x.GetProperty("kind").GetInt32() == 0 &&
                         x.GetProperty("hours").GetDecimal() == 10m)
            .GetProperty("amount").GetDecimal());

        // 8 × 240 × 2 = 3.840
        Assert.Equal(3_840m, lines
            .Single(x => x.GetProperty("kind").GetInt32() == 2)
            .GetProperty("amount").GetDecimal());
    }

    /// <summary>
    /// ÇİFT SAYIM TESTİ: mesai toplam eldene BİR KEZ giriyor.
    ///
    /// toplam elden = manuel elden + bu ayın mesaisi
    /// ele geçen = resmî net + toplam elden
    ///
    /// Ayrıca mesai tabana GERİ BESLENMİYOR: saatlik ücret hâlâ
    /// 54.000 tabanından türüyor, mesai eklenmiş 55.800'den değil.
    /// </summary>
    [Fact]
    public async Task OvertimeEntersCashExactlyOnce()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(
            PermissionCatalog.Keys.PersonnelView,
            PermissionCatalog.Keys.ExtraPaymentView);

        var payload = JsonDocument.Parse(await (await client.GetAsync(Url(context)))
            .Content.ReadAsStringAsync()).RootElement;

        // Bu ayın mesaisi: 5 saat × 240 × 1,5 = 1.800
        var currentMonth = payload.GetProperty("currentMonth");

        Assert.Equal(5m, currentMonth.GetProperty("hours").GetDecimal());
        Assert.Equal(1_800m, currentMonth.GetProperty("amount").GetDecimal());

        var takeHome = payload.GetProperty("takeHome");

        var manual = takeHome.GetProperty("manualExtraMonthly").GetDecimal();
        var overtime = takeHome.GetProperty("overtimeExtra").GetDecimal();
        var totalExtra = takeHome.GetProperty("totalExtra").GetDecimal();
        var totalTakeHome = takeHome.GetProperty("totalTakeHome").GetDecimal();

        Assert.Equal(9_000m, manual);
        Assert.Equal(1_800m, overtime);

        // Toplam elden manuel + mesai; mesai iki kez sayılmıyor.
        Assert.Equal(manual + overtime, totalExtra);
        Assert.Equal(10_800m, totalExtra);

        // Ele geçen = resmî net + toplam elden.
        Assert.Equal(OfficialNet + totalExtra, totalTakeHome);
        Assert.Equal(55_800m, totalTakeHome);

        // Mesai tabana geri beslenmiyor: saatlik hâlâ 240.
        Assert.Equal(ExpectedHourly, takeHome.GetProperty("hourlyRate").GetDecimal());
        Assert.True(takeHome.GetProperty("baseExcludesOvertime").GetBoolean());
    }

    /// <summary>
    /// ELDEN İZOLASYONU: payroll.view tek başına yetmiyor. Mesai
    /// tutarı elden ödemedir; bordroyu yöneten ama elden ödeme
    /// yetkisi olmayan kullanıcı da göremez.
    /// </summary>
    [Fact]
    public async Task PayrollViewWithoutExtraPayment_StillSeesNoAmount()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(
            PermissionCatalog.Keys.PersonnelView, PermissionCatalog.Keys.PayrollView);

        var raw = await (await client.GetAsync(Url(context))).Content
            .ReadAsStringAsync();

        Assert.DoesNotContain("3600", raw);
        Assert.DoesNotContain("54000", raw);

        var payload = JsonDocument.Parse(raw).RootElement;

        Assert.True(payload.GetProperty("amountsHidden").GetBoolean());
    }

    /// <summary>Yetkisiz kullanıcı panele hiç giremiyor.</summary>
    [Fact]
    public async Task WithoutPersonnelView_PanelIsClosed()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(PermissionCatalog.Keys.ProjectsView);

        var response = await client.GetAsync(Url(context));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
