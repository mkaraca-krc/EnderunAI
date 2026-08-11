using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Expenses;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Expenses;
using EnderunAI.Api.Services.HumanResources;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Gider merkezi raporu — paketin ana kuralının testi.
///
/// Rapor otomatik kaynakları OKUR, KOPYALAMAZ. Bu testler aynı
/// giderin iki kaynaktan iki kez sayılmadığını nokta nokta
/// sabitliyor: görev masrafı, taşeron, işçilik, tekrarlayan gider,
/// mal kabullü fatura ve ödemeler.
/// </summary>
[Collection("Integration")]
public sealed class ExpenseCenterReportTests(DatabaseFixture fixture)
{
    private sealed record Context(
        Guid CompanyId, Guid BranchId, Guid ProjectId, Guid SiteId);

    private static readonly DateTime Today = DateTime.UtcNow.Date;

    private static readonly string[] FullPermissions =
    [
        PermissionCatalog.Keys.ExpenseView,
        PermissionCatalog.Keys.ExpenseManage,
        PermissionCatalog.Keys.ExtraPaymentView
    ];

    private static readonly string[] WithoutCashPermissions =
    [
        PermissionCatalog.Keys.ExpenseView,
        PermissionCatalog.Keys.ExpenseManage
    ];

    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var site = new ProjectSite
        {
            ProjectId = project.Id,
            Code = $"SNT-{suffix}",
            Name = $"Şantiye {suffix}"
        };

        db.ProjectSites.Add(site);
        await db.SaveChangesAsync();

        await ExpenseCategoryProvisioner.EnsureAsync(
            db, project.CompanyId, CancellationToken.None);

        return new Context(project.CompanyId, project.BranchId, project.Id, site.Id);
    }

    private async Task<HttpClient> ClientWithAsync(string[] permissionKeys)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "TestRapor!2026";
        string username;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();

            var role = new AppRole { Name = $"TestRapor-{suffix}" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();

            var permissions = await db.Permissions
                .Where(x => permissionKeys.Contains(x.Key))
                .ToListAsync();

            foreach (var permission in permissions)
                db.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.Id,
                    PermissionId = permission.Id
                });

            username = $"rapor-{suffix}";
            var hash = passwords.Hash(password);

            db.Users.Add(new AppUser
            {
                Username = username,
                FullName = "Gider Raporu Test",
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

    private async Task<Guid> CategoryIdAsync(Guid companyId, string code)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.ExpenseCategories
            .Where(x => x.CompanyId == companyId && x.Code == code)
            .Select(x => x.Id)
            .SingleAsync();
    }

    private async Task<JsonElement> ReportAsync(HttpClient client, Context context)
    {
        var from = new DateTime(Today.Year, Today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddMonths(1).AddDays(-1);

        var response = await client.GetAsync(
            $"/api/expenses/rapor?companyId={context.CompanyId}" +
            $"&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static decimal CategoryAmount(JsonElement report, string code) =>
        report.GetProperty("categoryTotals").EnumerateArray()
            .Where(x => x.GetProperty("categoryCode").GetString() == code)
            .Select(x => x.GetProperty("amount").GetDecimal())
            .DefaultIfEmpty(0m)
            .Single();

    // ---------------- R3: görev masrafı ----------------

    /// <summary>
    /// R3: onaylı görevin masrafı maliyet defterinde ZATEN var.
    /// Rapor onu OKUYOR, ikinci kez yazmıyor — üç kalem üç ayrı
    /// kategoriye düşüyor ve toplam bir kez sayılıyor.
    /// </summary>
    [Fact]
    public async Task DutyExpense_IsReadOnceFromTheLedger()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Görev masrafının defterdeki hali: üç ayrı referans.
            db.ProjectCostTransactions.AddRange(
                LedgerRow(context, DutyExpensePostingService.TravelReference,
                    ProjectCostClass.Overhead, 2_500m),
                LedgerRow(context, DutyExpensePostingService.AccommodationReference,
                    ProjectCostClass.Overhead, 4_000m),
                LedgerRow(context, DutyExpensePostingService.AllowanceReference,
                    ProjectCostClass.Overhead, 3_000m));

            await db.SaveChangesAsync();
        }

        var client = await ClientWithAsync(FullPermissions);
        var report = await ReportAsync(client, context);

        Assert.Equal(9_500m, report.GetProperty("total").GetDecimal());

        // Üç kalem üç kategoriye ayrılmış: kırılım sonradan
        // ayrıştırılamaz, bu yüzden kaynağında ayrı tutuluyor.
        Assert.Equal(2_500m, CategoryAmount(report, ExpenseCategoryCatalog.Travel));
        Assert.Equal(4_000m, CategoryAmount(report, ExpenseCategoryCatalog.Accommodation));
        Assert.Equal(3_000m, CategoryAmount(report, ExpenseCategoryCatalog.Allowance));

        // Otomatik kalem raporda DÜZELTİLEMEZ.
        Assert.All(report.GetProperty("rows").EnumerateArray(),
            x => Assert.False(x.GetProperty("isEditableHere").GetBoolean()));
    }

    /// <summary>
    /// Aynı görev masrafı bir de elle girilmeye kalkılırsa kategori
    /// kapısı engelliyor — çift sayımın en kolay yolu kapalı.
    /// </summary>
    [Fact]
    public async Task TheSameDutyCostCannotBeAddedByHand()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var travel = await CategoryIdAsync(
            context.CompanyId, ExpenseCategoryCatalog.Travel);

        var response = await client.PostAsJsonAsync("/api/expenses/kayitlar", new
        {
            companyId = context.CompanyId,
            centerType = (int)ExpenseCenterType.Project,
            centerId = context.ProjectId,
            expenseCategoryId = travel,
            expenseDate = Today,
            amount = 2_500m,
            description = "Görev yol gideri (elle)",
            paymentMethod = (int)ExpensePaymentMethod.Bank,
            documentType = (int)ExpenseDocumentType.Receipt,
            documentNumber = (string?)null,
            supplierCurrentAccountId = (Guid?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------- R2: işçilik ----------------

    /// <summary>
    /// R2: işçilik YALNIZ köprüden (hr_project_labor_costs) geliyor.
    /// Bordro maliyet defterine satır yazmadığı için köprü + defter
    /// aynı ücreti iki kez saymıyor.
    /// </summary>
    [Fact]
    public async Task Labor_IsCountedOnceFromTheBridge()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        Guid personnelId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var personnel = await TestDataFactory.CreatePersonnelAsync(
                db, context.CompanyId, suffix);

            personnelId = personnel.Id;

            db.HrProjectLaborCosts.Add(new HrProjectLaborCost
            {
                CompanyId = context.CompanyId,
                ProjectId = context.ProjectId,
                PersonnelId = personnelId,
                WorkDate = Today,
                TotalLaborCost = 10_000m
            });

            await db.SaveChangesAsync();
        }

        var client = await ClientWithAsync(FullPermissions);
        var report = await ReportAsync(client, context);

        var labor = CategoryAmount(report, ExpenseCategoryCatalog.Labor);

        // İşveren yükü çarpanı uygulanıyor; çarpan en az 1.
        Assert.True(labor >= 10_000m);

        // Tek satır: köprü bir kez okundu.
        var laborRows = report.GetProperty("rows").EnumerateArray()
            .Where(x => x.GetProperty("categoryCode").GetString() ==
                        ExpenseCategoryCatalog.Labor)
            .ToList();

        Assert.Single(laborRows);
        Assert.Equal("Puantaj/bordro", laborRows[0].GetProperty("source").GetString());

        // Varsayım gizlenmiyor.
        Assert.Contains(report.GetProperty("notes").EnumerateArray(),
            x => x.GetString()!.Contains("işveren yükü"));
    }

    // ---------------- R5: tekrarlayan gider ----------------

    /// <summary>
    /// R5: gerçekleşen onaylanınca o dönemin TAHMİNİSİ düşer.
    /// Onaydan önce tahmini, sonra yalnızca gerçekleşen sayılıyor —
    /// ikisi birden asla.
    /// </summary>
    [Fact]
    public async Task RecurringEstimate_IsReplacedByTheActualNotAddedToIt()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var utilities = await CategoryIdAsync(
            context.CompanyId, ExpenseCategoryCatalog.Utilities);

        var created = await client.PostAsJsonAsync("/api/expenses/tekrarlayan", new
        {
            companyId = context.CompanyId,
            centerType = (int)ExpenseCenterType.Branch,
            centerId = context.BranchId,
            expenseCategoryId = utilities,
            description = "Ofis elektriği",
            estimatedAmount = 5_000m,
            paymentMethod = (int)ExpensePaymentMethod.Bank,
            supplierCurrentAccountId = (Guid?)null,
            startYear = Today.Year,
            startMonth = Today.Month,
            endYear = (int?)null,
            endMonth = (int?)null,
            paymentDay = 15
        });

        created.EnsureSuccessStatusCode();

        var templateId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        // Onaydan önce: tahmini sayılıyor ve TAHMİNİ işaretli.
        var before = await ReportAsync(client, context);

        Assert.Equal(5_000m, before.GetProperty("total").GetDecimal());

        var estimatedRow = before.GetProperty("rows").EnumerateArray().Single();
        Assert.True(estimatedRow.GetProperty("isEstimated").GetBoolean());

        // Gerçekleşen: 6.240.
        var confirm = await client.PostAsJsonAsync(
            $"/api/expenses/tekrarlayan/{templateId}/gerceklesen", new
            {
                year = Today.Year,
                month = Today.Month,
                actualAmount = 6_240m,
                documentType = (int)ExpenseDocumentType.Invoice,
                documentNumber = "ELK-1"
            });

        confirm.EnsureSuccessStatusCode();

        // Onaydan sonra: SADECE gerçekleşen. 5.000 + 6.240 = 11.240
        // OLMAMALI.
        var after = await ReportAsync(client, context);

        Assert.Equal(6_240m, after.GetProperty("total").GetDecimal());

        var actualRow = after.GetProperty("rows").EnumerateArray().Single();
        Assert.False(actualRow.GetProperty("isEstimated").GetBoolean());
        Assert.True(actualRow.GetProperty("isEditableHere").GetBoolean());
    }

    // ---------------- R7: ödeme sayılmaz ----------------

    /// <summary>
    /// R7: rapor TAHAKKUK esaslı. Bir gideri ödeyen çek ya da kasa
    /// hareketi ayrıca sayılmıyor; sayılsaydı fatura ve ödemesi iki
    /// ayrı gider olurdu.
    /// </summary>
    [Fact]
    public async Task PaymentsAreNotCountedAsExpenses()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);

        await client.PostAsJsonAsync("/api/expenses/kayitlar", new
        {
            companyId = context.CompanyId,
            centerType = (int)ExpenseCenterType.Branch,
            centerId = context.BranchId,
            expenseCategoryId = rent,
            expenseDate = Today,
            amount = 30_000m,
            description = "Ofis kirası",
            paymentMethod = (int)ExpensePaymentMethod.Bank,
            documentType = (int)ExpenseDocumentType.Invoice,
            documentNumber = "KIRA-1",
            supplierCurrentAccountId = (Guid?)null
        });

        // Aynı gideri ödeyen bir çek açılıyor.
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var supplierId = await db.CurrentAccounts
                .Where(x => x.CompanyId == context.CompanyId)
                .Select(x => x.Id)
                .FirstAsync();

            db.Cheques.Add(new Cheque
            {
                CompanyId = context.CompanyId,
                Direction = ChequeDirection.Issued,
                Status = ChequeStatus.Issued,
                InternalNumber = $"I{Guid.NewGuid():N}"[..12],
                ChequeNumber = $"C{Guid.NewGuid():N}"[..10],
                BankName = "Test Bankası",
                CurrentAccountId = supplierId,
                ProjectId = context.ProjectId,
                Amount = 30_000m,
                AmountTry = 30_000m,
                ExchangeRate = 1m,
                CurrencyCode = "TRY",
                IssueDate = Today,
                DueDate = Today.AddDays(20)
            });

            await db.SaveChangesAsync();
        }

        var report = await ReportAsync(client, context);

        // Kira bir kez: 30.000. Çek 60.000 yapmıyor.
        Assert.Equal(30_000m, report.GetProperty("total").GetDecimal());
    }

    // ---------------- Merkez × kategori ekseni ----------------

    /// <summary>
    /// Raporun asıl sorusu: "ofise ne harcadık, şantiyeye ne
    /// harcadık". Merkez ve kategori toplamları ayrı ayrı çıkıyor.
    /// </summary>
    [Fact]
    public async Task Report_SplitsByCenterAndCategory()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);
        var supplies = await CategoryIdAsync(
            context.CompanyId, ExpenseCategoryCatalog.Supplies);

        await client.PostAsJsonAsync("/api/expenses/kayitlar", new
        {
            companyId = context.CompanyId,
            centerType = (int)ExpenseCenterType.Branch,
            centerId = context.BranchId,
            expenseCategoryId = rent,
            expenseDate = Today,
            amount = 40_000m,
            description = "Ofis kirası",
            paymentMethod = (int)ExpensePaymentMethod.Bank,
            documentType = (int)ExpenseDocumentType.Invoice,
            documentNumber = (string?)null,
            supplierCurrentAccountId = (Guid?)null
        });

        await client.PostAsJsonAsync("/api/expenses/kayitlar", new
        {
            companyId = context.CompanyId,
            centerType = (int)ExpenseCenterType.ProjectSite,
            centerId = context.SiteId,
            expenseCategoryId = supplies,
            expenseDate = Today,
            amount = 6_000m,
            description = "Şantiye çay-şeker",
            paymentMethod = (int)ExpensePaymentMethod.Bank,
            documentType = (int)ExpenseDocumentType.Receipt,
            documentNumber = (string?)null,
            supplierCurrentAccountId = (Guid?)null
        });

        var report = await ReportAsync(client, context);

        Assert.Equal(46_000m, report.GetProperty("total").GetDecimal());

        var byCenter = report.GetProperty("centerTotals").EnumerateArray().ToList();

        Assert.Equal(40_000m, byCenter
            .Single(x => x.GetProperty("centerType").GetString() == "Branch")
            .GetProperty("amount").GetDecimal());

        Assert.Equal(6_000m, byCenter
            .Single(x => x.GetProperty("centerType").GetString() == "ProjectSite")
            .GetProperty("amount").GetDecimal());

        Assert.Equal(40_000m, CategoryAmount(report, ExpenseCategoryCatalog.Rent));
        Assert.Equal(6_000m, CategoryAmount(report, ExpenseCategoryCatalog.Supplies));
    }

    /// <summary>
    /// Dönem dışı gider raporda yok: rapor kümülatif değil, sorulan
    /// aralığı anlatıyor.
    /// </summary>
    [Fact]
    public async Task Report_HonoursThePeriod()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);

        // Geçen ayın gideri.
        await client.PostAsJsonAsync("/api/expenses/kayitlar", new
        {
            companyId = context.CompanyId,
            centerType = (int)ExpenseCenterType.Branch,
            centerId = context.BranchId,
            expenseCategoryId = rent,
            expenseDate = new DateTime(Today.Year, Today.Month, 1,
                0, 0, 0, DateTimeKind.Utc).AddMonths(-1),
            amount = 15_000m,
            description = "Geçen ay kirası",
            paymentMethod = (int)ExpensePaymentMethod.Bank,
            documentType = (int)ExpenseDocumentType.Invoice,
            documentNumber = (string?)null,
            supplierCurrentAccountId = (Guid?)null
        });

        var report = await ReportAsync(client, context);

        Assert.Equal(0m, report.GetProperty("total").GetDecimal());
    }

    // ---------------- Elden maskesi ----------------

    /// <summary>
    /// Elden kalem raporda da yetkisize HİÇ GELMİYOR ve toplam
    /// yalnızca görünenlerden. Tam toplam verilseydi fark, gizlenen
    /// tutarı birebir ele verirdi.
    /// </summary>
    [Fact]
    public async Task Report_HidesCashItemsAndTotalsOnlyWhatIsVisible()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var privileged = await ClientWithAsync(FullPermissions);

        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);
        var meals = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Meals);

        await privileged.PostAsJsonAsync("/api/expenses/kayitlar", new
        {
            companyId = context.CompanyId,
            centerType = (int)ExpenseCenterType.Branch,
            centerId = context.BranchId,
            expenseCategoryId = rent,
            expenseDate = Today,
            amount = 25_000m,
            description = "Ofis kirası",
            paymentMethod = (int)ExpensePaymentMethod.Bank,
            documentType = (int)ExpenseDocumentType.Invoice,
            documentNumber = (string?)null,
            supplierCurrentAccountId = (Guid?)null
        });

        await privileged.PostAsJsonAsync("/api/expenses/kayitlar", new
        {
            companyId = context.CompanyId,
            centerType = (int)ExpenseCenterType.Branch,
            centerId = context.BranchId,
            expenseCategoryId = meals,
            expenseDate = Today,
            amount = 9_000m,
            description = "Elden yemek",
            paymentMethod = (int)ExpensePaymentMethod.Cash,
            documentType = (int)ExpenseDocumentType.None,
            documentNumber = (string?)null,
            supplierCurrentAccountId = (Guid?)null
        });

        var full = await ReportAsync(privileged, context);
        Assert.Equal(34_000m, full.GetProperty("total").GetDecimal());
        Assert.Equal(0, full.GetProperty("hiddenCount").GetInt32());

        var limited = await ClientWithAsync(WithoutCashPermissions);
        var masked = await ReportAsync(limited, context);

        Assert.Equal(25_000m, masked.GetProperty("total").GetDecimal());
        Assert.Equal(1, masked.GetProperty("hiddenCount").GetInt32());

        // Kategori toplamlarında da yok.
        Assert.Equal(0m, CategoryAmount(masked, ExpenseCategoryCatalog.Meals));
    }

    [Fact]
    public async Task Report_RequiresExpenseView()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var outsider = await ClientWithAsync(
            [PermissionCatalog.Keys.ProjectsView, PermissionCatalog.Keys.FinanceView]);

        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(
            $"/api/expenses/rapor?companyId={context.CompanyId}")).StatusCode);
    }

    private static ProjectCostTransaction LedgerRow(
        Context context, string referenceType,
        ProjectCostClass costClass, decimal amount) =>
        new()
        {
            ProjectId = context.ProjectId,
            CostType = ProjectCostType.Overhead,
            CostClass = costClass,
            CostDate = Today,
            Amount = amount,
            Description = referenceType,
            ReferenceType = referenceType,
            ReferenceId = Guid.NewGuid()
        };
}
