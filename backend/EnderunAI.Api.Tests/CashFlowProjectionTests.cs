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
    /// Eski stopgap satırı (artık yalnızca veritabanından doğuyor)
    /// takvime düşüyor ve silinince çıkıyor. Uç kapatıldı ama
    /// KALAN SATIRLAR sayılmaya devam ediyor: okunmasaydı taşınmamış
    /// bir kira sessizce takvimden düşerdi.
    /// </summary>
    [Fact]
    public async Task LegacyEstimatedExpenseRows_AffectTheProjection()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, openingBalance: 500_000m);

        Guid expenseId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var expense = new CashFlowEstimatedExpense
            {
                CompanyId = context.CompanyId,
                Description = $"Kira {suffix}",
                Amount = 40_000m,
                StartYear = Today.Year,
                StartMonth = Today.Month,
                RecurrenceCount = 2,
                PaymentDay = 28
            };

            db.CashFlowEstimatedExpenses.Add(expense);
            await db.SaveChangesAsync();

            expenseId = expense.Id;
        }

        var client = await ClientWithAsync(CashFlowPermissions);

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
    /// NEGATİF TEST: tahmini gider uçları da dar kapıda — satırlar
    /// likidite tablosunu doğrudan değiştiriyor. POST kapatılmış
    /// olsa da yetki filtresi uçtan ÖNCE çalışıyor: yetkisiz
    /// kullanıcı 410 değil 403 görür.
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

    // ---------------- Gider merkezi devri ----------------

    /// <summary>
    /// DEVİR: tekrarlayan gider artık Gider Merkezi'nden geliyor ve
    /// takvimde ÇIKIŞ olarak görünüyor.
    ///
    /// Gider kaydı muhasebeye/kasaya yazmıyor ama projeksiyon onu
    /// OKUYOR — okuma, resmî deftere postalama değil.
    /// </summary>
    [Fact]
    public async Task RecurringExpense_FromTheExpenseCentreFlowsIntoTheProjection()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, openingBalance: 200_000m);

        var client = await ClientWithAsync(CashFlowPermissions);

        Guid branchId, categoryId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            branchId = await db.Projects
                .Where(x => x.Id == context.ProjectId)
                .Select(x => x.BranchId)
                .SingleAsync();

            await EnderunAI.Api.Services.Expenses.ExpenseCategoryProvisioner
                .EnsureAsync(db, context.CompanyId, CancellationToken.None);

            categoryId = await db.ExpenseCategories
                .Where(x => x.CompanyId == context.CompanyId &&
                            x.Code == EnderunAI.Api.Services.Expenses
                                .ExpenseCategoryCatalog.Rent)
                .Select(x => x.Id)
                .SingleAsync();

            // Gelecek ayın 15'inde 30.000 kira.
            var next = new DateTime(Today.Year, Today.Month, 1, 0, 0, 0,
                DateTimeKind.Utc).AddMonths(1);

            db.RecurringExpenseTemplates.Add(
                new EnderunAI.Api.Models.Expenses.RecurringExpenseTemplate
                {
                    CompanyId = context.CompanyId,
                    CenterType = EnderunAI.Api.Models.Expenses.ExpenseCenterType.Branch,
                    BranchId = branchId,
                    ExpenseCategoryId = categoryId,
                    Description = $"Ofis kirası {suffix}",
                    EstimatedAmount = 30_000m,
                    PaymentMethod = EnderunAI.Api.Models.Expenses
                        .ExpensePaymentMethod.Bank,
                    StartYear = next.Year,
                    StartMonth = next.Month,
                    PaymentDay = 15
                });

            await db.SaveChangesAsync();
        }

        var payload = await ProjectionAsync(client, context);

        var items = payload.GetProperty("days").EnumerateArray()
            .SelectMany(x => x.GetProperty("items").EnumerateArray())
            .Where(x => x.GetProperty("kind").GetString() == "RecurringExpense")
            .ToList();

        Assert.NotEmpty(items);
        Assert.All(items, x =>
            Assert.Equal("Tahmini", x.GetProperty("certaintyName").GetString()));
    }

    /// <summary>
    /// R6 ÇİFT SAYIM: bir dönemin gerçekleşeni girilmişse o ay
    /// tahmini olarak TEKRAR akmıyor. Aksi halde aynı kira takvimde
    /// iki kez çıkardı.
    /// </summary>
    [Fact]
    public async Task ConfirmedMonth_DoesNotFlowTwiceIntoTheProjection()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, openingBalance: 200_000m);

        var client = await ClientWithAsync(CashFlowPermissions);

        var next = new DateTime(Today.Year, Today.Month, 1, 0, 0, 0,
            DateTimeKind.Utc).AddMonths(1);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var branchId = await db.Projects
                .Where(x => x.Id == context.ProjectId)
                .Select(x => x.BranchId)
                .SingleAsync();

            await EnderunAI.Api.Services.Expenses.ExpenseCategoryProvisioner
                .EnsureAsync(db, context.CompanyId, CancellationToken.None);

            var categoryId = await db.ExpenseCategories
                .Where(x => x.CompanyId == context.CompanyId &&
                            x.Code == EnderunAI.Api.Services.Expenses
                                .ExpenseCategoryCatalog.Utilities)
                .Select(x => x.Id)
                .SingleAsync();

            var template = new EnderunAI.Api.Models.Expenses.RecurringExpenseTemplate
            {
                CompanyId = context.CompanyId,
                CenterType = EnderunAI.Api.Models.Expenses.ExpenseCenterType.Branch,
                BranchId = branchId,
                ExpenseCategoryId = categoryId,
                Description = $"Elektrik {suffix}",
                EstimatedAmount = 5_000m,
                PaymentMethod = EnderunAI.Api.Models.Expenses
                    .ExpensePaymentMethod.Bank,
                StartYear = next.Year,
                StartMonth = next.Month,
                PaymentDay = 15
            };

            db.RecurringExpenseTemplates.Add(template);
            await db.SaveChangesAsync();

            // Aynı ayın gerçekleşeni girildi: 6.240.
            db.ExpenseEntries.Add(new EnderunAI.Api.Models.Expenses.ExpenseEntry
            {
                CompanyId = context.CompanyId,
                CenterType = EnderunAI.Api.Models.Expenses.ExpenseCenterType.Branch,
                BranchId = branchId,
                ExpenseCategoryId = categoryId,
                ExpenseDate = new DateTime(next.Year, next.Month, 15, 0, 0, 0,
                    DateTimeKind.Utc),
                Amount = 6_240m,
                Description = $"Elektrik {suffix}",
                PaymentMethod = EnderunAI.Api.Models.Expenses
                    .ExpensePaymentMethod.Bank,
                DocumentType = EnderunAI.Api.Models.Expenses
                    .ExpenseDocumentType.Invoice,
                RecurringTemplateId = template.Id,
                PeriodYear = next.Year,
                PeriodMonth = next.Month
            });

            await db.SaveChangesAsync();
        }

        var payload = await ProjectionAsync(client, context);

        // Yalnız KESİNLEŞEN AY'a bakılıyor: bitişsiz şablon ufuk
        // boyunca her ay için dönem üretiyor, diğer aylar hâlâ
        // tahmini olarak akmalı — devrin kuralı "kesinleşen ay iki
        // kez akmasın", "şablon sussun" değil.
        var confirmedDay = new DateTime(next.Year, next.Month, 15, 0, 0, 0,
            DateTimeKind.Utc);

        var expenseItems = payload.GetProperty("days").EnumerateArray()
            .Where(x => x.GetProperty("date").GetDateTime().Date == confirmedDay.Date)
            .SelectMany(x => x.GetProperty("items").EnumerateArray())
            .Where(x => x.GetProperty("kind").GetString() is
                            "RecurringExpense" or "ExpenseEntry")
            .ToList();

        // TEK kalem: gerçekleşen. O ayın tahminisi akmıyor.
        Assert.Single(expenseItems);
        Assert.Equal("ExpenseEntry", expenseItems[0].GetProperty("kind").GetString());
        Assert.Equal(6_240m, expenseItems[0].GetProperty("amount").GetDecimal());
    }

    /// <summary>
    /// Eski stopgap ucu KAPALI: yeni satır açılamıyor, 410 ile
    /// nereye gidileceğini söylüyor. Açılabilseydi aynı kira iki
    /// yerde durur ve çift sayılırdı.
    /// </summary>
    [Fact]
    public async Task LegacyEstimatedExpenseEndpoint_IsClosed()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await ClientWithAsync(CashFlowPermissions);

        var response = await client.PostAsJsonAsync(
            "/api/cash-flow/tahmini-giderler", new
            {
                companyId = context.CompanyId,
                description = "Eski yöntemle kira",
                amount = 10_000m,
                startYear = Today.Year,
                startMonth = Today.Month,
                recurrenceCount = 3,
                paymentDay = 1,
                projectId = (Guid?)null
            });

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Contains("Gider Merkezi", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Taşınmamış eski satırlar SESSİZCE DÜŞMÜYOR: sayılmaya devam
    /// ediyor ve uyarı çıkıyor. Okunmasaydı taşınmamış bir kira
    /// takvimden kaybolur, tablo yeniden iyimser olurdu.
    /// </summary>
    [Fact]
    public async Task LegacyEstimatedExpenses_AreStillCountedWithAWarning()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, openingBalance: 100_000m);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.CashFlowEstimatedExpenses.Add(new CashFlowEstimatedExpense
            {
                CompanyId = context.CompanyId,
                Description = $"Eski kira {suffix}",
                Amount = 12_000m,
                StartYear = Today.Year,
                StartMonth = Today.Month,
                RecurrenceCount = 2,
                PaymentDay = Math.Min(28, Today.Day)
            });

            await db.SaveChangesAsync();
        }

        var client = await ClientWithAsync(CashFlowPermissions);
        var payload = await ProjectionAsync(client, context);

        Assert.Contains(
            payload.GetProperty("days").EnumerateArray()
                .SelectMany(x => x.GetProperty("items").EnumerateArray()),
            x => x.GetProperty("kind").GetString() == "EstimatedExpense");

        Assert.Contains(payload.GetProperty("notes").EnumerateArray(),
            x => x.GetString()!.Contains("çift sayılırlar"));
    }
}
