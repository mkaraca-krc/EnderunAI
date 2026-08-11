using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Expenses;
using EnderunAI.Api.Models.FinancialInstruments;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Expenses;
using EnderunAI.Api.Services.FinancialInstruments;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Finansal araçlar: barter, kredi kartı, banka kredisi.
///
/// ORTAK MODEL: üçü de IFinancialInstrumentSource uyguluyor ve nakit
/// akış hepsini AYNI okuyor. Bu testler her aracın ÇİFT SAYIM
/// noktasını ayrı ayrı sabitliyor:
/// - barter nakit sütununa girmiyor,
/// - kart harcaması gider tarihinde nakit çıkarmıyor, ekstre gününde
///   TEK kez çıkarıyor,
/// - kredi taksitinin tamamı gider sayılmıyor, yalnız faizi.
/// </summary>
[Collection("Integration")]
public sealed class FinancialInstrumentTests(DatabaseFixture fixture)
{
    private sealed record Context(Guid CompanyId, Guid BranchId, Guid ProjectId);

    private static readonly DateTime Today = DateTime.UtcNow.Date;

    private static readonly string[] FinancePermissions =
    [
        PermissionCatalog.Keys.FinanceView,
        PermissionCatalog.Keys.FinanceEdit,
        PermissionCatalog.Keys.CashFlowView,
        PermissionCatalog.Keys.ExpenseView,
        PermissionCatalog.Keys.ExpenseManage,
        PermissionCatalog.Keys.ExtraPaymentView,
        PermissionCatalog.Keys.HakedisView
    ];

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

        db.CashAccounts.Add(new CashAccount
        {
            CompanyId = project.CompanyId,
            Type = CashAccountType.Bank,
            Code = $"BNK-{suffix}",
            Name = $"Banka {suffix}",
            CurrencyCode = "TRY",
            OpeningBalance = openingBalance,
            AccountingAccountId = accounting.Id
        });

        await db.SaveChangesAsync();

        await ExpenseCategoryProvisioner.EnsureAsync(
            db, project.CompanyId, CancellationToken.None);

        return new Context(project.CompanyId, project.BranchId, project.Id);
    }

    private async Task<HttpClient> ClientWithAsync(string[] permissionKeys)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "TestArac!2026";
        string username;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();

            var role = new AppRole { Name = $"TestArac-{suffix}" };
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

            username = $"arac-{suffix}";
            var hash = passwords.Hash(password);

            db.Users.Add(new AppUser
            {
                Username = username,
                FullName = "Finansal Araç Test",
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

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
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

    private static async Task<JsonElement> ProjectionAsync(
        HttpClient client, Context context, int months = 6)
    {
        var response = await client.GetAsync(
            $"/api/cash-flow/projeksiyon?companyId={context.CompanyId}&months={months}");

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<JsonElement> ItemsOfKind(JsonElement projection, string kind) =>
        projection.GetProperty("days").EnumerateArray()
            .SelectMany(x => x.GetProperty("items").EnumerateArray())
            .Where(x => x.GetProperty("kind").GetString() == kind)
            .ToList();

    // ---------------- Saf hesaplayıcı ----------------

    /// <summary>
    /// Taksit planının anapara toplamı çekilen tutara BİREBİR eşit.
    /// Eşit olmasaydı kredi kapandığında bakiye sıfıra inmezdi.
    /// </summary>
    [Theory]
    [InlineData(100_000, 2.5, 12)]
    [InlineData(750_000, 3.79, 36)]
    [InlineData(15_000, 0, 6)]
    [InlineData(1_000_000, 4.25, 60)]
    public void LoanSchedule_PrincipalSumsToTheDrawnAmount(
        decimal principal, decimal rate, int count)
    {
        var lines = LoanScheduleCalculator.Build(
            principal, rate, count, new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(count, lines.Count);
        Assert.Equal(principal, lines.Sum(x => x.PrincipalAmount));

        // Faiz negatif olamaz ve faizsiz kredide sıfırdır.
        Assert.All(lines, x => Assert.True(x.InterestAmount >= 0m));

        if (rate == 0m)
            Assert.Equal(0m, lines.Sum(x => x.InterestAmount));
        else
            Assert.True(lines.Sum(x => x.InterestAmount) > 0m);

        // Vadeler birer ay artıyor.
        Assert.Equal(new DateTime(2026, 3, 10), lines[0].DueDate);
        Assert.Equal(new DateTime(2026, 4, 10), lines[1].DueDate);
    }

    /// <summary>
    /// Faiz her ay AZALAN bakiye üzerinden: sabit kalsaydı kredi
    /// kapanmasına rağmen faiz ödenmeye devam ederdi.
    /// </summary>
    [Fact]
    public void LoanSchedule_InterestFallsAsPrincipalIsRepaid()
    {
        var lines = LoanScheduleCalculator.Build(
            120_000m, 3m, 12, new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc));

        Assert.True(lines[0].InterestAmount > lines[^1].InterestAmount);
    }

    // ---------------- Kredi ----------------

    /// <summary>
    /// ÇEKİLİŞ GİRİŞ, TAKSİT ÇIKIŞ. İkisi de aynı araçtan doğuyor
    /// ama farklı tarihlerde; tek satır olsaydı kredinin likiditeye
    /// etkisi görünmezdi.
    /// </summary>
    [Fact]
    public async Task Loan_DrawdownIsAnInflowAndInstallmentsAreOutflows()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, openingBalance: 100_000m);
        var client = await ClientWithAsync(FinancePermissions);

        var created = await ReadAsync(await client.PostAsJsonAsync(
            "/api/finansal-araclar/krediler", new
            {
                companyId = context.CompanyId,
                name = $"İşletme kredisi {suffix}",
                contractNumber = "KR-1",
                bankCurrentAccountId = (Guid?)null,
                cashAccountId = (Guid?)null,
                projectId = (Guid?)null,
                principalAmount = 300_000m,
                monthlyInterestRate = 3m,
                installmentCount = 12,
                drawdownDate = Today.AddDays(5),
                firstInstallmentDate = Today.AddDays(35),
                notes = (string?)null
            }));

        var loanId = created.GetProperty("id").GetGuid();

        var installments = await ReadAsync(await client.GetAsync(
            $"/api/finansal-araclar/krediler/{loanId}/taksitler"));

        Assert.Equal(12, installments.GetArrayLength());

        var projection = await ProjectionAsync(client, context);

        var drawdown = ItemsOfKind(projection, BankLoanService.DrawdownKind);
        Assert.Single(drawdown);
        Assert.Equal(300_000m, drawdown[0].GetProperty("amount").GetDecimal());
        Assert.True(drawdown[0].GetProperty("isInflow").GetBoolean());

        var payments = ItemsOfKind(projection, BankLoanService.InstallmentKind);
        Assert.NotEmpty(payments);
        Assert.All(payments, x => Assert.False(x.GetProperty("isInflow").GetBoolean()));
    }

    /// <summary>
    /// ÇEKİLMİŞ kredi tekrar giriş yazmıyor: para hesaba girdi ve
    /// açılış bakiyesinin içinde. ÖDENMİŞ taksit de çıkış yazmıyor.
    /// İkisi de sayılsaydı aynı para iki kez hareket ederdi.
    /// </summary>
    [Fact]
    public async Task DrawnLoanAndPaidInstallments_AreNotCountedAgain()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, openingBalance: 500_000m);
        var client = await ClientWithAsync(FinancePermissions);

        var created = await ReadAsync(await client.PostAsJsonAsync(
            "/api/finansal-araclar/krediler", new
            {
                companyId = context.CompanyId,
                name = $"Çekilmiş kredi {suffix}",
                contractNumber = (string?)null,
                bankCurrentAccountId = (Guid?)null,
                cashAccountId = (Guid?)null,
                projectId = (Guid?)null,
                principalAmount = 200_000m,
                monthlyInterestRate = 2m,
                installmentCount = 6,
                drawdownDate = Today.AddDays(3),
                firstInstallmentDate = Today.AddDays(30),
                notes = (string?)null
            }));

        var loanId = created.GetProperty("id").GetGuid();

        // Kredi çekildi.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(
            $"/api/finansal-araclar/krediler/{loanId}/durum" +
            $"?status={(int)BankLoanStatus.Active}&isDrawn=true", null)).StatusCode);

        // İlk taksit ödendi.
        var installments = await ReadAsync(await client.GetAsync(
            $"/api/finansal-araclar/krediler/{loanId}/taksitler"));

        var first = installments[0];

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            $"/api/finansal-araclar/taksitler/{first.GetProperty("id").GetGuid()}", new
            {
                principalAmount = first.GetProperty("principalAmount").GetDecimal(),
                interestAmount = first.GetProperty("interestAmount").GetDecimal(),
                dueDate = first.GetProperty("dueDate").GetDateTime(),
                isPaid = true,
                paidDate = Today
            })).StatusCode);

        var projection = await ProjectionAsync(client, context);

        Assert.Empty(ItemsOfKind(projection, BankLoanService.DrawdownKind));

        var payments = ItemsOfKind(projection, BankLoanService.InstallmentKind);

        Assert.Equal(5, payments.Count);
    }

    /// <summary>
    /// İPTAL kredi hiç sayılmıyor — kapatılan bir kaydın mali etkisi
    /// de kalkmalı (çekteki iptal dersi).
    /// </summary>
    [Fact]
    public async Task CancelledLoan_ProducesNoMovementsAtAll()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, openingBalance: 50_000m);
        var client = await ClientWithAsync(FinancePermissions);

        var created = await ReadAsync(await client.PostAsJsonAsync(
            "/api/finansal-araclar/krediler", new
            {
                companyId = context.CompanyId,
                name = $"İptal kredi {suffix}",
                contractNumber = (string?)null,
                bankCurrentAccountId = (Guid?)null,
                cashAccountId = (Guid?)null,
                projectId = (Guid?)null,
                principalAmount = 400_000m,
                monthlyInterestRate = 3m,
                installmentCount = 10,
                drawdownDate = Today.AddDays(2),
                firstInstallmentDate = Today.AddDays(32),
                notes = (string?)null
            }));

        var loanId = created.GetProperty("id").GetGuid();

        var before = await ProjectionAsync(client, context);
        Assert.NotEmpty(ItemsOfKind(before, BankLoanService.DrawdownKind));

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(
            $"/api/finansal-araclar/krediler/{loanId}/durum" +
            $"?status={(int)BankLoanStatus.Cancelled}", null)).StatusCode);

        var after = await ProjectionAsync(client, context);

        Assert.Empty(ItemsOfKind(after, BankLoanService.DrawdownKind));
        Assert.Empty(ItemsOfKind(after, BankLoanService.InstallmentKind));
    }

    /// <summary>
    /// ÇİFT SAYIM: gider merkezine taksitin TAMAMI değil yalnızca
    /// FAİZİ giriyor. Anapara geri ödemesi borcun kapanmasıdır, gider
    /// değildir; tamamı sayılsaydı merkez toplamı kredinin kendisi
    /// kadar şişerdi.
    /// </summary>
    [Fact]
    public async Task OnlyInterestReachesTheExpenseCentre_NotThePrincipal()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FinancePermissions);

        var created = await ReadAsync(await client.PostAsJsonAsync(
            "/api/finansal-araclar/krediler", new
            {
                companyId = context.CompanyId,
                name = $"Gider kredisi {suffix}",
                contractNumber = (string?)null,
                bankCurrentAccountId = (Guid?)null,
                cashAccountId = (Guid?)null,
                projectId = (Guid?)null,
                principalAmount = 120_000m,
                monthlyInterestRate = 3m,
                installmentCount = 12,
                drawdownDate = Today,
                firstInstallmentDate = Today.AddDays(2),
                notes = (string?)null
            }));

        var loanId = created.GetProperty("id").GetGuid();

        var installments = await ReadAsync(await client.GetAsync(
            $"/api/finansal-araclar/krediler/{loanId}/taksitler"));

        var firstDue = installments[0].GetProperty("dueDate").GetDateTime();
        var expectedInterest = installments[0].GetProperty("interestAmount").GetDecimal();
        var principal = installments[0].GetProperty("principalAmount").GetDecimal();

        var report = await ReadAsync(await client.GetAsync(
            $"/api/expenses/rapor?companyId={context.CompanyId}" +
            $"&from={firstDue:yyyy-MM-dd}&to={firstDue:yyyy-MM-dd}"));

        var financing = report.GetProperty("categoryTotals").EnumerateArray()
            .Where(x => x.GetProperty("categoryCode").GetString() ==
                        ExpenseCategoryCatalog.Financing)
            .Select(x => x.GetProperty("amount").GetDecimal())
            .Single();

        Assert.Equal(expectedInterest, financing);

        // Anapara raporda YOK: toplam faizden büyük değil.
        Assert.Equal(expectedInterest, report.GetProperty("total").GetDecimal());
        Assert.True(principal > 0m);
    }

    // ---------------- Kredi kartı ----------------

    private async Task<Guid> CreateCardAsync(
        HttpClient client, Context context, int statementDay, int dueDay,
        CreditCardOwnership ownership = CreditCardOwnership.Company,
        Guid? partnerId = null)
    {
        var created = await ReadAsync(await client.PostAsJsonAsync(
            "/api/finansal-araclar/kartlar", new
            {
                companyId = context.CompanyId,
                name = ownership == CreditCardOwnership.Company
                    ? "Şirket kartı"
                    : "Şahıs kartı",
                bankName = "Test Bankası",
                lastFourDigits = "1234",
                ownership = (int)ownership,
                partnerAccountId = partnerId,
                cashAccountId = (Guid?)null,
                statementDay,
                dueDay,
                isActive = true
            }));

        return created.GetProperty("id").GetGuid();
    }

    private async Task AddCardExpenseAsync(
        HttpClient client, Context context, Guid cardId, Guid categoryId,
        decimal amount, DateTime date, string description)
    {
        var response = await client.PostAsJsonAsync("/api/expenses/kayitlar", new
        {
            companyId = context.CompanyId,
            centerType = (int)ExpenseCenterType.Branch,
            centerId = context.BranchId,
            expenseCategoryId = categoryId,
            expenseDate = date,
            amount,
            description,
            paymentMethod = (int)ExpensePaymentMethod.CreditCard,
            documentType = (int)ExpenseDocumentType.Receipt,
            documentNumber = (string?)null,
            supplierCurrentAccountId = (Guid?)null,
            partnerAccountId = (Guid?)null,
            creditCardId = cardId
        });

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// ANA TEST — kartın iki tarihi: harcama günü GİDER doğuyor,
    /// nakit çıkışı EKSTRE son ödeme gününde oluyor.
    ///
    /// Harcama tarihinde de nakit çıkışı yazılsaydı aynı harcama
    /// nakit akışta iki kez düşerdi.
    /// </summary>
    [Fact]
    public async Task CardExpense_IsAnExpenseTodayButCashOnlyOnTheStatementDueDate()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, openingBalance: 200_000m);
        var client = await ClientWithAsync(FinancePermissions);

        // Kesim ayın 20'si, ödeme ayın 5'i (ertesi ay).
        var cardId = await CreateCardAsync(client, context, statementDay: 20, dueDay: 5);

        var supplies = await CategoryIdAsync(
            context.CompanyId, ExpenseCategoryCatalog.Supplies);

        // Harcama: bu ayın 10'u (kesimden önce → bu ayın ekstresi).
        var spendDate = new DateTime(Today.Year, Today.Month, 10, 0, 0, 0,
            DateTimeKind.Utc);

        await AddCardExpenseAsync(
            client, context, cardId, supplies, 12_000m, spendDate, "Kartla kırtasiye");

        // GİDER: harcama gününde sayılıyor.
        var report = await ReadAsync(await client.GetAsync(
            $"/api/expenses/rapor?companyId={context.CompanyId}" +
            $"&from={spendDate:yyyy-MM-dd}&to={spendDate:yyyy-MM-dd}"));

        Assert.Equal(12_000m, report.GetProperty("total").GetDecimal());

        // NAKİT: harcama gününde ÇIKIŞ YOK.
        var projection = await ProjectionAsync(client, context);

        Assert.Empty(ItemsOfKind(projection, "ExpenseEntry"));

        // Ekstre: kesim 20'si, ödeme ertesi ayın 5'i.
        var statements = await ReadAsync(await client.GetAsync(
            $"/api/finansal-araclar/kartlar/ekstreler?companyId={context.CompanyId}"));

        var statement = statements.EnumerateArray()
            .Single(x => x.GetProperty("creditCardId").GetGuid() == cardId);

        Assert.Equal(12_000m, statement.GetProperty("amount").GetDecimal());
        Assert.Equal(20, statement.GetProperty("periodEnd").GetDateTime().Day);
        Assert.Equal(5, statement.GetProperty("dueDate").GetDateTime().Day);
        Assert.True(statement.GetProperty("producesCashOutflow").GetBoolean());
    }

    /// <summary>
    /// Ekstre nakit akışta TEK çıkış üretiyor: aynı dönemin bütün
    /// harcamaları tek satırda toplanıyor, harcama başına ayrı çıkış
    /// yazılmıyor.
    /// </summary>
    [Fact]
    public async Task Statement_ProducesOneCashOutflowForThePeriod()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, openingBalance: 300_000m);
        var client = await ClientWithAsync(FinancePermissions);

        // Kesim ayın 1'i, ödeme ayın 15'i: bugünden sonraki ekstre
        // ufuk içinde kalsın.
        var cardId = await CreateCardAsync(client, context, statementDay: 1, dueDay: 15);

        var supplies = await CategoryIdAsync(
            context.CompanyId, ExpenseCategoryCatalog.Supplies);

        var stationery = await CategoryIdAsync(
            context.CompanyId, ExpenseCategoryCatalog.Stationery);

        // Aynı ekstre dönemine üç harcama.
        var spend = Today.AddDays(2);

        await AddCardExpenseAsync(client, context, cardId, supplies, 5_000m, spend, "A");
        await AddCardExpenseAsync(client, context, cardId, stationery, 3_000m, spend, "B");
        await AddCardExpenseAsync(client, context, cardId, supplies, 2_000m,
            spend.AddDays(1), "C");

        var projection = await ProjectionAsync(client, context);

        var statements = ItemsOfKind(projection, CreditCardService.StatementKind);

        Assert.Single(statements);
        Assert.Equal(10_000m, statements[0].GetProperty("amount").GetDecimal());
        Assert.False(statements[0].GetProperty("isInflow").GetBoolean());
    }

    /// <summary>
    /// ŞAHIS KARTI: şirketin nakdi HİÇ çıkmıyor (ekstreyi kişi
    /// ödüyor) ve harcama o kişinin carisine yazılıyor — şirket ona
    /// borçlanıyor.
    ///
    /// Nakit çıkışı yazılsaydı şirket ödemediği bir parayı ödemiş
    /// görünürdü.
    /// </summary>
    [Fact]
    public async Task PersonalCardExpense_GoesToThePartnerLedgerAndNeverToCash()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, openingBalance: 100_000m);
        var client = await ClientWithAsync(FinancePermissions);

        var partner = await ReadAsync(await client.PostAsJsonAsync(
            "/api/expenses/sahis-cari", new
            {
                companyId = context.CompanyId,
                fullName = "Kart Sahibi Ortak",
                title = "Ortak",
                notes = (string?)null
            }));

        var partnerId = partner.GetProperty("id").GetGuid();

        var cardId = await CreateCardAsync(
            client, context, statementDay: 1, dueDay: 15,
            CreditCardOwnership.Personal, partnerId);

        var meals = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Meals);

        await AddCardExpenseAsync(
            client, context, cardId, meals, 4_500m, Today.AddDays(2), "Şahıs kartı yemek");

        // Nakit akışta ekstre çıkışı YOK.
        var projection = await ProjectionAsync(client, context);

        Assert.Empty(ItemsOfKind(projection, CreditCardService.StatementKind));
        Assert.Empty(ItemsOfKind(projection, "ExpenseEntry"));

        // Şahıs carisinde mahsup var: bakiye negatif, yani şirket
        // kişiye borçlu.
        var balances = await ReadAsync(await client.GetAsync(
            $"/api/expenses/sahis-cari?companyId={context.CompanyId}"));

        var balance = balances.EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == partnerId);

        Assert.Equal(4_500m, balance.GetProperty("settlementTotal").GetDecimal());
        Assert.Equal(-4_500m, balance.GetProperty("balance").GetDecimal());
    }

    /// <summary>Kartı seçilmeyen kart harcaması reddediliyor.</summary>
    [Fact]
    public async Task CardIsMandatoryForACardExpense()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FinancePermissions);

        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);

        var response = await client.PostAsJsonAsync("/api/expenses/kayitlar", new
        {
            companyId = context.CompanyId,
            centerType = (int)ExpenseCenterType.Branch,
            centerId = context.BranchId,
            expenseCategoryId = rent,
            expenseDate = Today,
            amount = 1_000m,
            description = "Kartsız kart harcaması",
            paymentMethod = (int)ExpensePaymentMethod.CreditCard,
            documentType = (int)ExpenseDocumentType.None,
            documentNumber = (string?)null,
            supplierCurrentAccountId = (Guid?)null,
            partnerAccountId = (Guid?)null,
            creditCardId = (Guid?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------- Barter ----------------

    /// <summary>
    /// BARTER NAKİT DEĞİL: satır görünüyor ama yürüyen bakiyeye
    /// GİRMİYOR. Nakit sayılsaydı tablo, eline hiç geçmeyecek bir
    /// parayı likidite gibi okurdu.
    /// </summary>
    [Fact]
    public async Task BarterReceivable_IsShownButNeverEntersTheCashBalance()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, openingBalance: 80_000m);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.BarterLedgerEntries.Add(new BarterLedgerEntry
            {
                ProjectId = context.ProjectId,
                EntryType = BarterEntryType.Deduction,
                EntryDate = Today.AddDays(5),
                Amount = 250_000m,
                Description = "Hakediş barter kesintisi"
            });

            await db.SaveChangesAsync();
        }

        var client = await ClientWithAsync(FinancePermissions);
        var projection = await ProjectionAsync(client, context);

        var barter = ItemsOfKind(projection, BarterInstrumentService.ReceivableKind);

        Assert.Single(barter);
        Assert.Equal(250_000m, barter[0].GetProperty("amount").GetDecimal());
        Assert.Equal("Nakit değil", barter[0].GetProperty("certaintyName").GetString());

        // BAKİYEYE GİRMİYOR: barter gününün bakiyesi açılışla aynı.
        var day = projection.GetProperty("days").EnumerateArray()
            .Single(x => x.GetProperty("date").GetDateTime().Date == Today.AddDays(5));

        Assert.Equal(0m, day.GetProperty("inflow").GetDecimal());
        Assert.Equal(80_000m, day.GetProperty("runningBalance").GetDecimal());
    }

    /// <summary>
    /// TESLİM ALINAN DÜŞER: karşılığı gelmiş barter artık beklenen
    /// bir alacak değildir.
    /// </summary>
    [Fact]
    public async Task ReceivedBarter_ReducesTheOpenReceivable()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix, openingBalance: 10_000m);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.BarterLedgerEntries.Add(new BarterLedgerEntry
            {
                ProjectId = context.ProjectId,
                EntryType = BarterEntryType.Deduction,
                EntryDate = Today.AddDays(3),
                Amount = 100_000m,
                Description = "Kesinti"
            });

            db.BarterLedgerEntries.Add(new BarterLedgerEntry
            {
                ProjectId = context.ProjectId,
                EntryType = BarterEntryType.Receipt,
                EntryDate = Today.AddDays(4),
                Amount = 70_000m,
                Description = "Daire teslim alındı"
            });

            await db.SaveChangesAsync();
        }

        var client = await ClientWithAsync(FinancePermissions);
        var projection = await ProjectionAsync(client, context);

        var barter = ItemsOfKind(projection, BarterInstrumentService.ReceivableKind);

        Assert.Single(barter);
        Assert.Equal(30_000m, barter[0].GetProperty("amount").GetDecimal());
    }

    // ---------------- Yetki ----------------

    /// <summary>
    /// Yeni anahtar AÇILMADI: krediler ve kartlar mevcut
    /// finance.view / finance.edit kapısında.
    /// </summary>
    [Fact]
    public async Task Endpoints_UseTheExistingFinancePermissions()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var outsider = await ClientWithAsync([PermissionCatalog.Keys.ProjectsView]);

        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(
            $"/api/finansal-araclar/krediler?companyId={context.CompanyId}")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(
            $"/api/finansal-araclar/kartlar?companyId={context.CompanyId}")).StatusCode);

        // Okuyabilen ama düzenleyemeyen kullanıcı kredi açamıyor.
        var readOnly = await ClientWithAsync([PermissionCatalog.Keys.FinanceView]);

        Assert.Equal(HttpStatusCode.OK, (await readOnly.GetAsync(
            $"/api/finansal-araclar/krediler?companyId={context.CompanyId}")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await readOnly.PostAsJsonAsync(
            "/api/finansal-araclar/krediler", new
            {
                companyId = context.CompanyId,
                name = "Yetkisiz kredi",
                contractNumber = (string?)null,
                bankCurrentAccountId = (Guid?)null,
                cashAccountId = (Guid?)null,
                projectId = (Guid?)null,
                principalAmount = 1_000m,
                monthlyInterestRate = 1m,
                installmentCount = 2,
                drawdownDate = Today,
                firstInstallmentDate = Today.AddDays(30),
                notes = (string?)null
            })).StatusCode);
    }
}
