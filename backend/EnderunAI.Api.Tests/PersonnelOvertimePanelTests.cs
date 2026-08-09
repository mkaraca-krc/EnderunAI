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
/// mesaisini görmeden çalışamaz), TL TUTAR yalnızca payroll.view ile.
/// Sahaya mesai tutarı sızmamalı.
/// </summary>
[Collection("Integration")]
public sealed class PersonnelOvertimePanelTests(DatabaseFixture fixture)
{
    private const int Year = 2026;
    private const decimal HourlyRate = 200m;

    private sealed record Context(Guid CompanyId, Guid PersonnelId);

    /// <summary>
    /// Saatlik ücreti 200 TL olan bir personel; 10 saat fazla çalışma
    /// (1,5× → 3.000 TL) ve 8 saat genel tatil (2× → 3.200 TL).
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

        await db.SaveChangesAsync();

        hrDb.SalaryDefinitions.Add(new HrSalaryDefinition
        {
            CompanyId = project.CompanyId,
            PersonnelId = personnel.Id,
            EffectiveStartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            GrossSalary = 60_000m,
            HourlyRate = HourlyRate,
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

        Assert.Equal(10m, payload.GetProperty("overtimeHours").GetDecimal());
        Assert.Equal(8m, payload.GetProperty("publicHolidayHours").GetDecimal());
        Assert.Equal(0m, payload.GetProperty("sundayHours").GetDecimal());
        Assert.Equal("ok", payload.GetProperty("limitStatus").GetString());
        Assert.True(payload.GetProperty("limitCountsOvertimeOnly").GetBoolean());
    }

    /// <summary>Sınır aşımı ve yaklaşma durumları.</summary>
    [Theory]
    [InlineData(5, "exceeded")]
    [InlineData(11, "near")]
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

        Assert.Equal(2, lines.Count);

        var holiday = lines.Single(x => x.GetProperty("kind").GetInt32() == 2);
        Assert.Equal("Genel tatil çalışması",
            holiday.GetProperty("kindName").GetString());
        Assert.Equal(2m, holiday.GetProperty("multiplier").GetDecimal());
        Assert.False(holiday.GetProperty("landedOnAttendance").GetBoolean());

        var overtime = lines.Single(x => x.GetProperty("kind").GetInt32() == 0);
        Assert.Equal(1.5m, overtime.GetProperty("multiplier").GetDecimal());
        Assert.True(overtime.GetProperty("landedOnAttendance").GetBoolean());
        Assert.Equal("2026-03",
            overtime.GetProperty("attendanceMonth").GetString());

        Assert.Equal(1, payload.GetProperty("notLandedCount").GetInt32());
    }

    /// <summary>Muvafakat durumu yıla göre değerlendiriliyor.</summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData(2025, false)]
    [InlineData(Year, true)]
    public async Task Consent_IsValidOnlyForTheSameYear(int? consentYear, bool valid)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, consentYear: consentYear);

        var client = await ClientWithAsync(PermissionCatalog.Keys.PersonnelView);

        var payload = JsonDocument.Parse(await (await client.GetAsync(Url(context)))
            .Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(
            valid, payload.GetProperty("consent").GetProperty("isValid").GetBoolean());
    }

    // ---------------- Tutar sızıntısı ----------------

    /// <summary>
    /// NEGATİF TEST: personnel.view olan saha kullanıcısı mesai
    /// TUTARINI göremiyor. Yanıtın HAM METNİNDE tutar aranıyor —
    /// alan adı değişse bile sızıntı yakalanır.
    ///
    /// 10 saat × 200 × 1,5 = 3.000 · 8 saat × 200 × 2 = 3.200
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
        Assert.Contains("\"overtimeHours\":10", raw);

        // Tutar hiç gelmiyor.
        Assert.DoesNotContain("3000", raw);
        Assert.DoesNotContain("3200", raw);
        Assert.DoesNotContain("6200", raw);

        var payload = JsonDocument.Parse(raw).RootElement;

        Assert.True(payload.GetProperty("amountsHidden").GetBoolean());
        Assert.Equal(
            JsonValueKind.Null, payload.GetProperty("totalAmount").ValueKind);

        foreach (var line in payload.GetProperty("lines").EnumerateArray())
            Assert.Equal(JsonValueKind.Null, line.GetProperty("amount").ValueKind);
    }

    /// <summary>
    /// OLUMLU KONTROL: payroll.view olan kullanıcı tutarı görüyor.
    /// Maskeleme her şeyi boşaltmıyor ve tutar bordroyla aynı
    /// kaynaktan (saatlik ücret × çarpan) hesaplanıyor.
    /// </summary>
    [Fact]
    public async Task PayrollViewer_SeesTheAmounts()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, annualLimit: 270m);

        var client = await ClientWithAsync(
            PermissionCatalog.Keys.PersonnelView, PermissionCatalog.Keys.PayrollView);

        var raw = await (await client.GetAsync(Url(context))).Content
            .ReadAsStringAsync();

        var payload = JsonDocument.Parse(raw).RootElement;

        Assert.False(payload.GetProperty("amountsHidden").GetBoolean());
        Assert.Equal(6_200m, payload.GetProperty("totalAmount").GetDecimal());

        var lines = payload.GetProperty("lines").EnumerateArray().ToList();

        Assert.Equal(3_000m,
            lines.Single(x => x.GetProperty("kind").GetInt32() == 0)
                .GetProperty("amount").GetDecimal());

        Assert.Equal(3_200m,
            lines.Single(x => x.GetProperty("kind").GetInt32() == 2)
                .GetProperty("amount").GetDecimal());
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
