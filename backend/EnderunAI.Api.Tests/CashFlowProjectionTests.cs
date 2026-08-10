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
/// Likidite takvimi: tarih bazlı yürüyen bakiye ve finansman açığı.
///
/// Mevcut nakit akışı 30/60/90 KOVASI üretiyordu; iki tahsilat
/// arasındaki çukur kovanın içinde kayboluyordu. Bu testler
/// bakiyenin gün gün yürüdüğünü, en derin noktanın bulunduğunu ve
/// iptal edilen hiçbir şeyin sayılmadığını sabitliyor.
/// </summary>
[Collection("Integration")]
public sealed class CashFlowProjectionTests(DatabaseFixture fixture)
{
    private sealed record Context(
        Guid CompanyId, Guid ProjectId, Guid EmployerId, Guid BankAccountId);

    private static readonly DateTime Today = DateTime.UtcNow.Date;

    private async Task<Context> CreateContextAsync(
        string suffix, decimal openingBalance = 0m)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var accounting = new AccountingAccount
        {
            CompanyId = project.CompanyId,
            Code = "102",
            Name = "Bankalar",
            Nature = AccountingAccountNature.Debit,
            Level = 1,
            IsPostingAllowed = true
        };

        db.AccountingAccounts.Add(accounting);
        await db.SaveChangesAsync();

        var bank = new CashAccount
        {
            CompanyId = project.CompanyId,
            Type = CashAccountType.Bank,
            Code = $"BNK-{suffix}",
            Name = $"Test Banka {suffix}",
            CurrencyCode = "TRY",
            OpeningBalance = openingBalance,
            AccountingAccountId = accounting.Id
        };

        db.CashAccounts.Add(bank);
        await db.SaveChangesAsync();

        return new Context(
            project.CompanyId, project.Id,
            project.EmployerCurrentAccountId!.Value, bank.Id);
    }

    /// <summary>
    /// Nakit akış izni olan istemci. Kapı DAR: cashflow.view olmadan
    /// projeksiyon açılmıyor.
    /// </summary>
    private async Task<HttpClient> ClientWithAsync(string[] permissionKeys)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "TestNakit!2026";
        string username;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();

            var role = new AppRole { Name = $"TestNakit-{suffix}" };
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

            username = $"nakit-{suffix}";
            var hash = passwords.Hash(password);

            db.Users.Add(new AppUser
            {
                Username = username,
                FullName = "Nakit Test Kullanıcısı",
                PasswordHash = hash.Hash,
                PasswordSalt = hash.Salt,
                IsActive = true,
                WorkHoursExempt = true
            });

            await db.SaveChangesAsync();

            var user = await db.Users.SingleAsync(x => x.Username == username);

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

    private static readonly string[] CashFlowPermissions =
        [PermissionCatalog.Keys.FinanceView, PermissionCatalog.Keys.CashFlowView];

    private static async Task<JsonElement> ProjectionAsync(
        HttpClient client, Context context, int months = 6,
        DateTime? targetDate = null)
    {
        var query = targetDate is DateTime target
            ? $"&targetDate={target:yyyy-MM-dd}"
            : "";

        var response = await client.GetAsync(
            $"/api/cash-flow/projeksiyon?companyId={context.CompanyId}" +
            $"&months={months}{query}");

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Verilen çek (çıkış) ya da alınan çek (giriş) açar.</summary>
    private async Task<Guid> AddChequeAsync(
        Context context, ChequeDirection direction, decimal amount,
        int dueInDays, ChequeStatus status)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cheque = new Cheque
        {
            CompanyId = context.CompanyId,
            Direction = direction,
            Status = status,
            InternalNumber = $"T{Guid.NewGuid():N}"[..12],
            ChequeNumber = $"C{Guid.NewGuid():N}"[..10],
            BankName = "Test Bankası",
            CurrentAccountId = context.EmployerId,
            ProjectId = context.ProjectId,
            Amount = amount,
            AmountTry = amount,
            ExchangeRate = 1m,
            CurrencyCode = "TRY",
            IssueDate = Today,
            DueDate = Today.AddDays(dueInDays)
        };

        db.Cheques.Add(cheque);
        await db.SaveChangesAsync();

        return cheque.Id;
    }

    // ---------------- Yürüyen bakiye ----------------

    /// <summary>
    /// ANA TEST: bakiye gün gün yürüyor ve iki tahsilat arasındaki
    /// ÇUKUR görünüyor. Kova bazlı görünümde bu çukur kaybolurdu:
    /// 90 günlük kova toplamı pozitif çıkar, arada batıldığı
    /// anlaşılmazdı.
    /// </summary>
    [Fact]
    public async Task Projection_RunsBalanceDayByDayAndFindsTheDip()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, openingBalance: 100_000m);

        // 10. gün 300.000 çıkış → bakiye −200.000
        await AddChequeAsync(context, ChequeDirection.Issued, 300_000m, 10,
            ChequeStatus.Issued);

        // 40. gün 500.000 giriş → bakiye +300.000
        await AddChequeAsync(context, ChequeDirection.Received, 500_000m, 40,
            ChequeStatus.Portfolio);

        var client = await ClientWithAsync(CashFlowPermissions);
        var payload = await ProjectionAsync(client, context);

        Assert.Equal(100_000m, payload.GetProperty("openingBalance").GetDecimal());
        Assert.Equal(300_000m, payload.GetProperty("closingBalance").GetDecimal());

        var days = payload.GetProperty("days").EnumerateArray().ToList();

        Assert.Equal(2, days.Count);
        Assert.Equal(-200_000m, days[0].GetProperty("runningBalance").GetDecimal());
        Assert.Equal(300_000m, days[1].GetProperty("runningBalance").GetDecimal());

        // FİNANSMAN AÇIĞI: ilk negatif gün ve en derin nokta.
        var shortfall = payload.GetProperty("shortfall");

        Assert.Equal(-200_000m, shortfall.GetProperty("peakBalance").GetDecimal());
        Assert.Equal(200_000m,
            shortfall.GetProperty("requiredFinancing").GetDecimal());

        Assert.Equal(
            Today.AddDays(10).ToString("yyyy-MM-dd"),
            shortfall.GetProperty("firstNegativeDate").GetDateTime()
                .ToString("yyyy-MM-dd"));
    }

    /// <summary>
    /// Açık yoksa finansman açığı da yok: pozitif seyreden bir takvim
    /// uyarı üretmemeli.
    /// </summary>
    [Fact]
    public async Task Projection_WithoutNegativeDays_HasNoShortfall()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, openingBalance: 500_000m);

        await AddChequeAsync(context, ChequeDirection.Issued, 100_000m, 15,
            ChequeStatus.Issued);

        var client = await ClientWithAsync(CashFlowPermissions);
        var payload = await ProjectionAsync(client, context);

        Assert.Equal(JsonValueKind.Null,
            payload.GetProperty("shortfall").ValueKind);
    }

    /// <summary>
    /// EN DERİN NOKTA doğru: art arda iki çıkış varken gereken
    /// finansman ilk negatif güne göre değil, çukurun dibine göre
    /// hesaplanıyor. İlk günü kapatmak yetmez.
    /// </summary>
    [Fact]
    public async Task Shortfall_UsesTheDeepestPointNotTheFirstNegativeDay()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, openingBalance: 50_000m);

        await AddChequeAsync(context, ChequeDirection.Issued, 100_000m, 5,
            ChequeStatus.Issued);

        await AddChequeAsync(context, ChequeDirection.Issued, 200_000m, 20,
            ChequeStatus.Issued);

        var client = await ClientWithAsync(CashFlowPermissions);
        var payload = await ProjectionAsync(client, context);

        var shortfall = payload.GetProperty("shortfall");

        // İlk negatif: −50.000 · en derin: −250.000
        Assert.Equal(-50_000m,
            shortfall.GetProperty("firstNegativeBalance").GetDecimal());

        Assert.Equal(-250_000m, shortfall.GetProperty("peakBalance").GetDecimal());
        Assert.Equal(250_000m,
            shortfall.GetProperty("requiredFinancing").GetDecimal());
    }

    // ---------------- Kesin / tahmini ----------------

    /// <summary>
    /// Çek vadesi KESİN; hakediş projedeki vadeden hesaplanınca
    /// TAHMİNİ, hakedişte ezme varsa KESİN.
    /// </summary>
    [Fact]
    public async Task Certainty_SeparatesConfirmedFromEstimated()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddChequeAsync(context, ChequeDirection.Received, 10_000m, 20,
            ChequeStatus.Portfolio);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var project = await db.Projects.SingleAsync(x => x.Id == context.ProjectId);
            project.CollectionTermDays = 30;

            db.ProgressPayments.Add(new ProgressPayment
            {
                CompanyId = context.CompanyId,
                ProjectId = context.ProjectId,
                ProgressPaymentNumber = $"HK-{suffix}",
                ProgressPaymentDate = Today,
                NetPayableAmount = 250_000m,
                Status = ProgressPaymentStatus.Posted
            });

            await db.SaveChangesAsync();
        }

        var client = await ClientWithAsync(CashFlowPermissions);
        var payload = await ProjectionAsync(client, context);

        var items = payload.GetProperty("days").EnumerateArray()
            .SelectMany(x => x.GetProperty("items").EnumerateArray())
            .ToList();

        var cheque = items.Single(x =>
            x.GetProperty("kind").GetString() == "ReceivedCheque");

        Assert.Equal("Kesin", cheque.GetProperty("certaintyName").GetString());

        var progress = items.Single(x =>
            x.GetProperty("kind").GetString() == "ProgressPayment");

        Assert.Equal("Tahmini", progress.GetProperty("certaintyName").GetString());

        // Vade projeden: hakediş tarihi + 30 gün.
        Assert.Equal(
            Today.AddDays(30).ToString("yyyy-MM-dd"),
            progress.GetProperty("date").GetDateTime().ToString("yyyy-MM-dd"));
    }

    /// <summary>
    /// Hakedişteki ezme projedeki vadeyi geçersiz kılıyor ve kalem
    /// KESİN sayılıyor: işverenle konuşulmuş tarih, formülden iyidir.
    /// </summary>
    [Fact]
    public async Task ExpectedCollectionDate_OverridesTermAndIsConfirmed()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var project = await db.Projects.SingleAsync(x => x.Id == context.ProjectId);
            project.CollectionTermDays = 30;

            db.ProgressPayments.Add(new ProgressPayment
            {
                CompanyId = context.CompanyId,
                ProjectId = context.ProjectId,
                ProgressPaymentNumber = $"HK-{suffix}",
                ProgressPaymentDate = Today,
                ExpectedCollectionDate = Today.AddDays(50),
                NetPayableAmount = 90_000m,
                Status = ProgressPaymentStatus.Posted
            });

            await db.SaveChangesAsync();
        }

        var client = await ClientWithAsync(CashFlowPermissions);
        var payload = await ProjectionAsync(client, context);

        var progress = payload.GetProperty("days").EnumerateArray()
            .SelectMany(x => x.GetProperty("items").EnumerateArray())
            .Single(x => x.GetProperty("kind").GetString() == "ProgressPayment");

        Assert.Equal("Kesin", progress.GetProperty("certaintyName").GetString());
        Assert.Equal(
            Today.AddDays(50).ToString("yyyy-MM-dd"),
            progress.GetProperty("date").GetDateTime().ToString("yyyy-MM-dd"));
    }

    // ---------------- Elemeler ----------------

    /// <summary>
    /// İPTAL, ERTELENEN ve KARŞILIKSIZ çek takvime girmiyor: mali
    /// etkileri geri alındı ya da hiç gerçekleşmeyecek.
    /// </summary>
    [Fact]
    public async Task CancelledAndReplacedCheques_AreExcluded()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, openingBalance: 10_000m);

        await AddChequeAsync(context, ChequeDirection.Issued, 500_000m, 10,
            ChequeStatus.Voided);

        await AddChequeAsync(context, ChequeDirection.Issued, 400_000m, 12,
            ChequeStatus.Replaced);

        await AddChequeAsync(context, ChequeDirection.Received, 300_000m, 14,
            ChequeStatus.Bounced);

        var client = await ClientWithAsync(CashFlowPermissions);
        var payload = await ProjectionAsync(client, context);

        Assert.Empty(payload.GetProperty("days").EnumerateArray());
        Assert.Equal(10_000m, payload.GetProperty("closingBalance").GetDecimal());
    }

    /// <summary>
    /// İPTAL EDİLEN GÖREVLENDİRME çıkış saymıyor; onaylı olan sayıyor.
    /// </summary>
    [Fact]
    public async Task CancelledDuty_IsExcludedButApprovedCounts()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, openingBalance: 100_000m);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var personnel = await TestDataFactory.CreatePersonnelAsync(
                db, context.CompanyId, suffix);

            db.PersonnelDuties.AddRange(
                new PersonnelDuty
                {
                    CompanyId = context.CompanyId,
                    PersonnelId = personnel.Id,
                    DutyType = PersonnelDutyType.Work,
                    TargetProjectId = context.ProjectId,
                    StartDate = Today.AddDays(7),
                    EndDate = Today.AddDays(9),
                    DailyAllowance = 1_000m,
                    TravelCost = 2_000m,
                    AccommodationCost = 3_000m,
                    Purpose = "Onaylı görev",
                    Status = PersonnelDutyStatus.Approved
                },
                new PersonnelDuty
                {
                    CompanyId = context.CompanyId,
                    PersonnelId = personnel.Id,
                    DutyType = PersonnelDutyType.Work,
                    TargetProjectId = context.ProjectId,
                    StartDate = Today.AddDays(20),
                    EndDate = Today.AddDays(22),
                    DailyAllowance = 5_000m,
                    TravelCost = 9_000m,
                    AccommodationCost = 9_000m,
                    Purpose = "İptal görev",
                    Status = PersonnelDutyStatus.Cancelled
                });

            await db.SaveChangesAsync();
        }

        var client = await ClientWithAsync(CashFlowPermissions);
        var payload = await ProjectionAsync(client, context);

        var duties = payload.GetProperty("days").EnumerateArray()
            .SelectMany(x => x.GetProperty("items").EnumerateArray())
            .Where(x => x.GetProperty("kind").GetString() == "PersonnelDuty")
            .ToList();

        // 2.000 + 3.000 + (3 gün × 1.000) = 8.000 — yalnız onaylı görev.
        var duty = Assert.Single(duties);

        Assert.Equal(8_000m, duty.GetProperty("amount").GetDecimal());
    }

    // ---------------- Tahmini gider ----------------

    /// <summary>
    /// Tekrarlayan tahmini gider, tekrar sayısı kadar ay için çıkış
    /// üretiyor ve TAHMİNİ işaretli.
    /// </summary>
    [Fact]
    public async Task EstimatedExpense_RepeatsForItsRecurrenceCount()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, openingBalance: 1_000_000m);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.CashFlowEstimatedExpenses.Add(new CashFlowEstimatedExpense
            {
                CompanyId = context.CompanyId,
                Description = $"Ofis kirası {suffix}",
                Amount = 50_000m,
                StartYear = Today.Year,
                StartMonth = Today.Month,
                RecurrenceCount = 3,
                // Ayın 28'i: her ayda var, kısa ay sorunu çıkmaz.
                PaymentDay = 28
            });

            await db.SaveChangesAsync();
        }

        var client = await ClientWithAsync(CashFlowPermissions);
        var payload = await ProjectionAsync(client, context);

        var expenses = payload.GetProperty("days").EnumerateArray()
            .SelectMany(x => x.GetProperty("items").EnumerateArray())
            .Where(x => x.GetProperty("kind").GetString() == "EstimatedExpense")
            .ToList();

        // Bugünün ayının 28'i geçmişse o tekrar düşer; en az iki kalır.
        Assert.InRange(expenses.Count, 2, 3);

        Assert.All(expenses, x =>
        {
            Assert.Equal(50_000m, x.GetProperty("amount").GetDecimal());
            Assert.Equal("Tahmini", x.GetProperty("certaintyName").GetString());
            Assert.False(x.GetProperty("isInflow").GetBoolean());
        });
    }

    // ---------------- Ufuk ve hedef ----------------

    /// <summary>
    /// Ufuk dışındaki hareket takvime girmiyor; 3 ay seçilince 5 ay
    /// sonraki çek görünmüyor.
    /// </summary>
    [Fact]
    public async Task Horizon_ExcludesMovementsBeyondTheWindow()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddChequeAsync(context, ChequeDirection.Received, 70_000m, 150,
            ChequeStatus.Portfolio);

        var client = await ClientWithAsync(CashFlowPermissions);

        Assert.Empty((await ProjectionAsync(client, context, months: 3))
            .GetProperty("days").EnumerateArray());

        Assert.Single((await ProjectionAsync(client, context, months: 6))
            .GetProperty("days").EnumerateArray());
    }

    /// <summary>
    /// Hedef tarih: o güne kadar kümülatif ve gereken finansman.
    /// Hedeften sonraki hareket sayılmıyor.
    /// </summary>
    [Fact]
    public async Task TargetDate_SummarisesUpToThatDayOnly()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, openingBalance: 20_000m);

        await AddChequeAsync(context, ChequeDirection.Issued, 60_000m, 10,
            ChequeStatus.Issued);

        // Hedeften SONRA: kümülatife girmemeli.
        await AddChequeAsync(context, ChequeDirection.Received, 900_000m, 40,
            ChequeStatus.Portfolio);

        var client = await ClientWithAsync(CashFlowPermissions);

        var payload = await ProjectionAsync(
            client, context, targetDate: Today.AddDays(25));

        var target = payload.GetProperty("target");

        Assert.Equal(0m, target.GetProperty("inflow").GetDecimal());
        Assert.Equal(60_000m, target.GetProperty("outflow").GetDecimal());
        Assert.Equal(-40_000m, target.GetProperty("closingBalance").GetDecimal());
        Assert.Equal(40_000m, target.GetProperty("requiredFinancing").GetDecimal());
    }

    // ---------------- Yetki ----------------

    /// <summary>
    /// NEGATİF TEST: cashflow.view olmadan projeksiyon açılmıyor.
    /// finance.view yetmiyor — tablo elden dahil bordro taşıyor.
    /// </summary>
    [Fact]
    public async Task WithoutCashFlowPermission_ProjectionIsForbidden()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var limited = await ClientWithAsync([PermissionCatalog.Keys.FinanceView]);

        var response = await limited.GetAsync(
            $"/api/cash-flow/projeksiyon?companyId={context.CompanyId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Kova görünümü finance.view ile açık kalmaya devam ediyor:
        // yeni izin mevcut ekranı kilitlemedi.
        var legacy = await limited.GetAsync(
            $"/api/cash-flow?companyId={context.CompanyId}");

        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
    }

    // ---------------- Tahmini gider uçları ----------------

    /// <summary>
    /// Tahmini gider ucundan eklenince takvime düşüyor ve
    /// kaldırılınca çıkıyor: satır doğrudan bakiyeyi etkiliyor.
    /// </summary>
    [Fact]
    public async Task EstimatedExpenseEndpoints_AffectTheProjection()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, openingBalance: 500_000m);

        var client = await ClientWithAsync(CashFlowPermissions);

        var created = await client.PostAsJsonAsync(
            "/api/cash-flow/tahmini-giderler", new
            {
                companyId = context.CompanyId,
                description = $"Kira {suffix}",
                amount = 40_000m,
                startYear = Today.Year,
                startMonth = Today.Month,
                recurrenceCount = 2,
                paymentDay = 28,
                projectId = (Guid?)null
            });

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var expenseId = JsonDocument
            .Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();

        var withExpense = await ProjectionAsync(client, context);

        var count = withExpense.GetProperty("days").EnumerateArray()
            .SelectMany(x => x.GetProperty("items").EnumerateArray())
            .Count(x => x.GetProperty("kind").GetString() == "EstimatedExpense");

        Assert.InRange(count, 1, 2);

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync(
            $"/api/cash-flow/tahmini-giderler/{expenseId}")).StatusCode);

        var without = await ProjectionAsync(client, context);

        Assert.DoesNotContain(
            without.GetProperty("days").EnumerateArray()
                .SelectMany(x => x.GetProperty("items").EnumerateArray()),
            x => x.GetProperty("kind").GetString() == "EstimatedExpense");
    }

    /// <summary>
    /// SÜRESİZ TEKRAR YOK: gözden geçirilmeyen bir varsayıma
    /// dönüşürdü. Üst sınır en uzun ufkun iki katı.
    /// </summary>
    [Fact]
    public async Task EstimatedExpense_RejectsUnboundedRecurrence()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(CashFlowPermissions);

        var response = await client.PostAsJsonAsync(
            "/api/cash-flow/tahmini-giderler", new
            {
                companyId = context.CompanyId,
                description = "Süresiz kira",
                amount = 10_000m,
                startYear = Today.Year,
                startMonth = Today.Month,
                recurrenceCount = 120,
                paymentDay = 1,
                projectId = (Guid?)null
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// NEGATİF TEST: tahmini gider uçları da dar kapıda — satırlar
    /// likidite tablosunu doğrudan değiştiriyor.
    /// </summary>
    [Fact]
    public async Task EstimatedExpenseEndpoints_RequireCashFlowPermission()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var limited = await ClientWithAsync([PermissionCatalog.Keys.FinanceView]);

        Assert.Equal(HttpStatusCode.Forbidden, (await limited.GetAsync(
            $"/api/cash-flow/tahmini-giderler?companyId={context.CompanyId}"))
            .StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await limited.PostAsJsonAsync(
            "/api/cash-flow/tahmini-giderler", new
            {
                companyId = context.CompanyId,
                description = "Yetkisiz",
                amount = 1_000m,
                startYear = Today.Year,
                startMonth = Today.Month,
                recurrenceCount = 1,
                paymentDay = 1,
                projectId = (Guid?)null
            })).StatusCode);
    }
}
