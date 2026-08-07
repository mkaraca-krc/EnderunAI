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
/// Elden ödemenin TÜM entegrasyonları: bordro göstergesi, gerçek
/// yevmiye ve elden ödeme kasası.
///
/// Bu paketin iki ayrı görevi var ve ikisi de kritik:
///
/// 1. GÖSTERİM DOĞRU MU — yetkili kullanıcı resmî net, elden ve toplam
///    ele geçeni birlikte görebilmeli. Görülemezse rakam ekranda yok
///    demektir ve kullanıcı hesabı elle yapmaya başlar.
///
/// 2. İZOLASYON BOZULMADI MI — elden tutar resmî bordro rakamını, SGK
///    matrahını, muhasebe fişini, kasa hareketini ve proje maliyet
///    defterini DEĞİŞTİRMEMELİ. Gösterim eklerken bu sınırın
///    aşılması, hatanın en sinsi hâli olurdu: ekran doğru görünür,
///    defter bozulur.
/// </summary>
[Collection("Integration")]
public sealed class ExtraPaymentIntegrationTests(DatabaseFixture fixture)
{
    private const decimal OfficialNet = 33_058.43m;
    private const decimal ExtraMonthly = 15_000m;

    private sealed record Context(
        Guid CompanyId, Guid PersonnelId, Guid PayrollId);

    /// <summary>
    /// Ücret kartı, elden ödemesi ve tek aylık bordro kaydı olan bir
    /// personel kurar.
    /// </summary>
    private async Task<Context> CreateContextAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var personnel = new Personnel
        {
            CompanyId = company.Id,
            EmployeeNumber = $"EP-{suffix}",
            FirstName = "Entegrasyon",
            LastName = "Testi",
            EmploymentStartDate = start,
            Status = PersonnelStatus.Active
        };
        db.Personnel.Add(personnel);

        db.PersonnelExtraPayments.Add(new PersonnelExtraPayment
        {
            CompanyId = company.Id,
            PersonnelId = personnel.Id,
            MonthlyAmount = ExtraMonthly,
            EffectiveStartDate = start,
            Note = "Entegrasyon testi"
        });

        db.CompanyPayrollSettings.Add(new CompanyPayrollSettings
        {
            CompanyId = company.Id,
            Year = 2026,
            MinimumWageGross = 33_030m,
            MinimumWageNet = 28_075.50m,
            SgkBaseFloor = 33_030m,
            SgkBaseCeiling = 297_270m,
            SeveranceCeiling = 53_919.68m,
            DailyWorkHours = 7.5m,
            VerifiedAtUtc = DateTime.UtcNow,
            TaxBrackets =
            [
                new() { Order = 1, LowerBound = 0m, UpperBound = 190_000m, Rate = 15m },
                new() { Order = 2, LowerBound = 190_000m, UpperBound = null, Rate = 20m }
            ]
        });

        await db.SaveChangesAsync();

        hrDb.SalaryDefinitions.Add(new HrSalaryDefinition
        {
            CompanyId = company.Id,
            PersonnelId = personnel.Id,
            EffectiveStartDate = start,
            GrossSalary = 40_000m,
            NetSalary = OfficialNet,
            DailyRate = 1_200m,
            HourlyRate = 160m
        });

        var payroll = new HrPayrollRecord
        {
            CompanyId = company.Id,
            PersonnelId = personnel.Id,
            Year = 2026,
            Month = 3,
            GrossSalary = 40_000m,
            TotalEarnings = 40_000m,
            SgkBase = 40_000m,
            OfficialNetPayableAmount = OfficialNet,
            ActualPayableAmount = OfficialNet,
            NetPayableAmount = OfficialNet,
            CurrencyCode = "TRY",
            Status = PayrollStatus.Calculated
        };

        hrDb.PayrollRecords.Add(payroll);
        await hrDb.SaveChangesAsync();

        return new Context(company.Id, personnel.Id, payroll.Id);
    }

    /// <summary>
    /// Verilen rolde, isteğe bağlı olarak tek bir izni kapatılmış
    /// kullanıcı oluşturur.
    /// </summary>
    private async Task<HttpClient> CreateClientForRoleAsync(
        string roleName, string? deniedPermissionKey = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        const string password = "ExtraPayment!2026";
        var username = $"test-epi-{Guid.NewGuid():N}"[..40];
        var hash = passwordService.Hash(password);

        var user = new AppUser
        {
            Username = username,
            FullName = $"Test {roleName}",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            IsActive = true,
            WorkHoursExempt = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var role = await db.Roles.SingleAsync(x => x.Name == roleName);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        db.UserDataScopes.Add(new UserDataScope
        {
            UserId = user.Id,
            ScopeType = DataScopeType.All
        });

        if (deniedPermissionKey is not null)
        {
            var permission = await db.Permissions
                .SingleAsync(x => x.Key == deniedPermissionKey);

            db.UserPermissionOverrides.Add(new UserPermissionOverride
            {
                UserId = user.Id,
                PermissionId = permission.Id,
                Effect = PermissionOverrideEffect.Deny
            });
        }

        await db.SaveChangesAsync();

        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, username, password);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    // ---------- 1) BORDRO ----------

    /// <summary>
    /// Yetkili kullanıcı bordroda resmî net + elden + toplam ele geçeni
    /// birlikte görmeli.
    /// </summary>
    [Fact]
    public async Task Payroll_ShowsTakeHomeBreakdownToAuthorizedRole()
    {
        var context = await CreateContextAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var payroll = await client.GetFromJsonAsync<JsonElement>(
            $"/api/hr/payroll/records/{context.PayrollId}");

        Assert.False(payroll.GetProperty("extraPaymentHidden").GetBoolean());
        Assert.Equal(
            OfficialNet, payroll.GetProperty("officialNetPayableAmount").GetDecimal());
        Assert.Equal(
            ExtraMonthly, payroll.GetProperty("extraPaymentAmount").GetDecimal());
        Assert.Equal(
            OfficialNet + ExtraMonthly,
            payroll.GetProperty("totalTakeHome").GetDecimal());
    }

    /// <summary>
    /// Maaşı gören ama elden ödeme izni OLMAYAN kullanıcı yalnızca
    /// resmî tutarı görmeli; elden alanları null ve "gizlendi"
    /// olmalı.
    /// </summary>
    [Fact]
    public async Task Payroll_HidesExtraPaymentWhenPermissionDenied()
    {
        var context = await CreateContextAsync();

        var client = await CreateClientForRoleAsync(
            "Genel Müdür", PermissionCatalog.Keys.ExtraPaymentView);

        var payroll = await client.GetFromJsonAsync<JsonElement>(
            $"/api/hr/payroll/records/{context.PayrollId}");

        Assert.True(payroll.GetProperty("extraPaymentHidden").GetBoolean());
        Assert.Equal(
            JsonValueKind.Null, payroll.GetProperty("extraPaymentAmount").ValueKind);
        Assert.Equal(
            JsonValueKind.Null, payroll.GetProperty("totalTakeHome").ValueKind);

        // Resmî tutar her hâlükârda görünür
        Assert.Equal(
            OfficialNet, payroll.GetProperty("officialNetPayableAmount").GetDecimal());
    }

    /// <summary>
    /// KRİTİK: gösterim eklendi diye bordro kaydının kendisi
    /// DEĞİŞMEMELİ. Elden tutar veritabanına yazılsaydı salary.view
    /// olan herkese sızardı ve SGK matrahı bozulurdu.
    /// </summary>
    [Fact]
    public async Task Payroll_RecordItselfIsUnchanged()
    {
        var context = await CreateContextAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        await client.GetFromJsonAsync<JsonElement>(
            $"/api/hr/payroll/records/{context.PayrollId}");

        using var verify = fixture.Factory.Services.CreateScope();
        var hrDb = verify.ServiceProvider.GetRequiredService<HrDbContext>();

        var record = await hrDb.PayrollRecords
            .AsNoTracking()
            .SingleAsync(x => x.Id == context.PayrollId);

        Assert.Equal(OfficialNet, record.OfficialNetPayableAmount);
        Assert.Equal(OfficialNet, record.ActualPayableAmount);
        Assert.Equal(OfficialNet, record.NetPayableAmount);
        // SGK matrahı elden tutardan etkilenmemeli
        Assert.Equal(40_000m, record.SgkBase);
        Assert.Equal(40_000m, record.GrossSalary);
    }

    // ---------- 2) PUANTAJ / GERÇEK YEVMİYE ----------

    /// <summary>
    /// Gerçek yevmiye = resmî günlük + (elden aylık ÷ 30).
    /// </summary>
    [Fact]
    public async Task DailyWage_IncludesExtraPaymentForAuthorizedRole()
    {
        var context = await CreateContextAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var wage = await client.GetFromJsonAsync<JsonElement>(
            $"/api/hr/attendance/daily-wage?personnelId={context.PersonnelId}" +
            "&asOf=2026-03-15");

        Assert.False(wage.GetProperty("extraPaymentHidden").GetBoolean());
        Assert.Equal(1_200m, wage.GetProperty("officialDailyRate").GetDecimal());
        // 15.000 / 30 = 500
        Assert.Equal(500m, wage.GetProperty("extraDailyRate").GetDecimal());
        Assert.Equal(1_700m, wage.GetProperty("actualDailyRate").GetDecimal());
        // 1.700 / 7,5 = 226,67
        Assert.Equal(226.67m, wage.GetProperty("actualHourlyRate").GetDecimal());
    }

    /// <summary>
    /// Elden izni olmayan kullanıcı yalnızca resmî yevmiyeyi görmeli.
    /// </summary>
    [Fact]
    public async Task DailyWage_HidesExtraPaymentWhenPermissionDenied()
    {
        var context = await CreateContextAsync();

        var client = await CreateClientForRoleAsync(
            "Genel Müdür", PermissionCatalog.Keys.ExtraPaymentView);

        var wage = await client.GetFromJsonAsync<JsonElement>(
            $"/api/hr/attendance/daily-wage?personnelId={context.PersonnelId}" +
            "&asOf=2026-03-15");

        Assert.True(wage.GetProperty("extraPaymentHidden").GetBoolean());
        Assert.Equal(1_200m, wage.GetProperty("officialDailyRate").GetDecimal());
        Assert.Equal(
            JsonValueKind.Null, wage.GetProperty("actualDailyRate").ValueKind);
    }

    /// <summary>
    /// Ücret görmeyen rol yevmiye ucuna hiç erişememeli: burada dönen
    /// bir gün sayısı değil, ücret.
    /// </summary>
    [Theory]
    [InlineData("Şantiye Şefi")]
    [InlineData("Formen")]
    [InlineData("Teknik Koordinatör")]
    public async Task DailyWage_IsForbiddenForRolesWithoutSalaryPermission(
        string roleName)
    {
        var context = await CreateContextAsync();
        var client = await CreateClientForRoleAsync(roleName);

        var response = await client.GetAsync(
            $"/api/hr/attendance/daily-wage?personnelId={context.PersonnelId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- 3) ELDEN ÖDEME KASASI ----------

    [Theory]
    [InlineData("Şantiye Şefi")]
    [InlineData("Formen")]
    [InlineData("Teknik Ofis")]
    [InlineData("Sekreterya")]
    [InlineData("Teknik Koordinatör")]
    public async Task CashPayments_AreForbiddenForRestrictedRoles(string roleName)
    {
        var client = await CreateClientForRoleAsync(roleName);

        var list = await client.GetAsync("/api/personnel-cash-payments");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        var create = await client.PostAsJsonAsync(
            "/api/personnel-cash-payments",
            new
            {
                personnelId = Guid.NewGuid(),
                kind = 0,
                paymentDate = new DateTime(2026, 3, 31),
                amount = 1_000m,
                periodYear = 2026,
                periodMonth = 3,
                note = (string?)null
            });

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    /// <summary>
    /// KRİTİK İZOLASYON TESTİ: elden ödeme kaydı muhasebe fişi, kasa
    /// hareketi ve proje maliyet kaydı ÜRETMEMELİ. Bunlardan biri
    /// oluşsaydı elden tutar resmî deftere ya da projects.view ile
    /// okunan bir tabloya sızardı.
    /// </summary>
    [Fact]
    public async Task CashPayment_WritesNothingToAccountingOrCashOrProjectCost()
    {
        var context = await CreateContextAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        int vouchersBefore, cashBefore, projectCostBefore;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            vouchersBefore = await db.AccountingVouchers
                .CountAsync(x => x.CompanyId == context.CompanyId);
            cashBefore = await db.CashTransactions.CountAsync();
            projectCostBefore = await db.ProjectCostTransactions.CountAsync();
        }

        var response = await client.PostAsJsonAsync(
            "/api/personnel-cash-payments",
            new
            {
                personnelId = context.PersonnelId,
                kind = 0,
                paymentDate = new DateTime(2026, 3, 31),
                amount = ExtraMonthly,
                periodYear = 2026,
                periodMonth = 3,
                note = "Mart elden"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var verify = fixture.Factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        // Kayıt gerçekten yazıldı
        Assert.Equal(1, await verifyDb.PersonnelCashPayments
            .CountAsync(x => x.PersonnelId == context.PersonnelId));

        // ...ama defterlere hiçbir şey düşmedi
        Assert.Equal(vouchersBefore, await verifyDb.AccountingVouchers
            .CountAsync(x => x.CompanyId == context.CompanyId));
        Assert.Equal(cashBefore, await verifyDb.CashTransactions.CountAsync());
        Assert.Equal(projectCostBefore,
            await verifyDb.ProjectCostTransactions.CountAsync());
    }

    /// <summary>
    /// Dönem özeti tanımlanan ile fiilen ödeneni karşılaştırmalı;
    /// eksik ödeme sessiz kalmamalı.
    /// </summary>
    [Fact]
    public async Task CashPaymentSummary_ComparesDefinedAgainstPaid()
    {
        var context = await CreateContextAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        // Tanımlı 15.000, ödenen 10.000 → 5.000 eksik
        await client.PostAsJsonAsync(
            "/api/personnel-cash-payments",
            new
            {
                personnelId = context.PersonnelId,
                kind = 0,
                paymentDate = new DateTime(2026, 3, 31),
                amount = 10_000m,
                periodYear = 2026,
                periodMonth = 3,
                note = (string?)null
            });

        var summary = await client.GetFromJsonAsync<JsonElement>(
            $"/api/personnel-cash-payments/summary?companyId={context.CompanyId}" +
            "&year=2026&month=3");

        Assert.Equal(15_000m, summary.GetProperty("definedTotal").GetDecimal());
        Assert.Equal(10_000m, summary.GetProperty("paidTotal").GetDecimal());
        Assert.Equal(1, summary.GetProperty("unpaidCount").GetInt32());

        var row = summary.GetProperty("rows").EnumerateArray()
            .Single(x => x.GetProperty("personnelId").GetGuid() == context.PersonnelId);

        Assert.Equal(-5_000m, row.GetProperty("difference").GetDecimal());
    }

    /// <summary>
    /// Sıfır ya da negatif tutar kaydedilememeli.
    /// </summary>
    [Fact]
    public async Task CashPayment_RejectsNonPositiveAmount()
    {
        var context = await CreateContextAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var response = await client.PostAsJsonAsync(
            "/api/personnel-cash-payments",
            new
            {
                personnelId = context.PersonnelId,
                kind = 0,
                paymentDate = new DateTime(2026, 3, 31),
                amount = 0m,
                periodYear = 2026,
                periodMonth = 3,
                note = (string?)null
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
