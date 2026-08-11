using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Expenses;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Expenses;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Şahıs / ortak carisi ve faturasız gider.
///
/// AKIŞ: şirketten şahsa para çıkar (avans, borç artar) → şahıs o
/// parayla faturasız gider yapar → gider merkezinde kategorize
/// edilir ve şahsın borcundan DÜŞER.
///
/// ANA KURAL: gider şirket nakdini TEKRAR etkilemez. Para avansta
/// zaten çıktı; nakit akışta ikinci kez çıkış yazılsaydı aynı para
/// iki kez gitmiş görünürdü. Gider merkezinde ise sayılır — orası
/// tahakkuk, nakit akış nakit.
/// </summary>
[Collection("Integration")]
public sealed class PartnerAccountTests(DatabaseFixture fixture)
{
    private sealed record Context(Guid CompanyId, Guid BranchId, Guid ProjectId);

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

        await ExpenseCategoryProvisioner.EnsureAsync(
            db, project.CompanyId, CancellationToken.None);

        return new Context(project.CompanyId, project.BranchId, project.Id);
    }

    private async Task<HttpClient> ClientWithAsync(string[] permissionKeys)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "TestSahis!2026";
        string username;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();

            var role = new AppRole { Name = $"TestSahis-{suffix}" };
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

            username = $"sahis-{suffix}";
            var hash = passwords.Hash(password);

            db.Users.Add(new AppUser
            {
                Username = username,
                FullName = "Şahıs Cari Test",
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

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<Guid> CreatePartnerAsync(HttpClient client, Context context)
    {
        var created = await ReadAsync(await client.PostAsJsonAsync(
            "/api/expenses/sahis-cari", new
            {
                companyId = context.CompanyId,
                fullName = "Test Ortak",
                title = "Ortak",
                notes = (string?)null
            }));

        return created.GetProperty("id").GetGuid();
    }

    private static object ExpensePayload(
        Context context, Guid categoryId, decimal amount,
        int paymentMethod, Guid? partnerId, string description,
        DateTime? date = null) =>
        new
        {
            companyId = context.CompanyId,
            centerType = (int)ExpenseCenterType.Branch,
            centerId = context.BranchId,
            expenseCategoryId = categoryId,
            expenseDate = date ?? Today,
            amount,
            description,
            paymentMethod,
            documentType = (int)ExpenseDocumentType.None,
            documentNumber = (string?)null,
            supplierCurrentAccountId = (Guid?)null,
            partnerAccountId = partnerId
        };

    // ---------------- Akış ----------------

    /// <summary>
    /// ANA TEST: avans borcu artırıyor, faturasız gider mahsup edip
    /// borcu düşürüyor. Bakiye = avans − mahsup.
    /// </summary>
    [Fact]
    public async Task Advance_IncreasesDebt_AndUninvoicedExpenseSettlesIt()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var partnerId = await CreatePartnerAsync(client, context);

        // Şirketten şahsa 50.000 çıktı.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/expenses/sahis-cari/{partnerId}/hareketler", new
            {
                kind = (int)PartnerAccountEntryKind.Advance,
                entryDate = Today,
                amount = 50_000m,
                description = "Ortak avansı"
            })).StatusCode);

        var supplies = await CategoryIdAsync(
            context.CompanyId, ExpenseCategoryCatalog.Supplies);

        // Şahıs 12.000'lik faturasız gider yaptı.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            "/api/expenses/kayitlar",
            ExpensePayload(context, supplies, 12_000m,
                (int)ExpensePaymentMethod.PartnerAccount, partnerId,
                "Faturasız temizlik malzemesi"))).StatusCode);

        var balances = await ReadAsync(await client.GetAsync(
            $"/api/expenses/sahis-cari?companyId={context.CompanyId}"));

        var partner = balances.EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == partnerId);

        Assert.Equal(50_000m, partner.GetProperty("advanceTotal").GetDecimal());
        Assert.Equal(12_000m, partner.GetProperty("settlementTotal").GetDecimal());
        Assert.Equal(38_000m, partner.GetProperty("balance").GetDecimal());

        // Mahsup hareketi giderin kategorisini de taşıyor: "para
        // nereye gitti" sorusunun cevabı defterde duruyor.
        var entries = await ReadAsync(await client.GetAsync(
            $"/api/expenses/sahis-cari/{partnerId}/hareketler"));

        var settlement = entries.EnumerateArray()
            .Single(x => x.GetProperty("kind").GetString() == "ExpenseSettlement");

        Assert.Equal(12_000m, settlement.GetProperty("amount").GetDecimal());
        Assert.False(string.IsNullOrWhiteSpace(
            settlement.GetProperty("categoryName").GetString()));
    }

    /// <summary>
    /// ÇİFT SAYIM: faturasız gider GİDER MERKEZİNDE sayılır ama
    /// NAKİT AKIŞTA çıkış üretmez — para avansta zaten çıktı.
    /// </summary>
    [Fact]
    public async Task PartnerSettledExpense_CountsAsExpenseButNotAsCashOutflow()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(
        [
            PermissionCatalog.Keys.ExpenseView,
            PermissionCatalog.Keys.ExpenseManage,
            PermissionCatalog.Keys.ExtraPaymentView,
            PermissionCatalog.Keys.FinanceView,
            PermissionCatalog.Keys.CashFlowView
        ]);

        var partnerId = await CreatePartnerAsync(client, context);

        var meals = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Meals);

        // GELECEK tarihli: nakit akış ufkunun içinde kalsın.
        var future = Today.AddDays(10);

        await ReadAsync(await client.PostAsJsonAsync(
            "/api/expenses/kayitlar",
            ExpensePayload(context, meals, 9_000m,
                (int)ExpensePaymentMethod.PartnerAccount, partnerId,
                "Faturasız yemek", future)));

        // Gider merkezinde SAYILIYOR.
        var report = await ReadAsync(await client.GetAsync(
            $"/api/expenses/rapor?companyId={context.CompanyId}" +
            $"&from={Today:yyyy-MM-dd}&to={future.AddDays(5):yyyy-MM-dd}"));

        Assert.Equal(9_000m, report.GetProperty("total").GetDecimal());

        // Nakit akışta ÇIKIŞ YOK.
        var projection = await ReadAsync(await client.GetAsync(
            $"/api/cash-flow/projeksiyon?companyId={context.CompanyId}&months=6"));

        Assert.DoesNotContain(
            projection.GetProperty("days").EnumerateArray()
                .SelectMany(x => x.GetProperty("items").EnumerateArray()),
            x => x.GetProperty("kind").GetString() == "ExpenseEntry");
    }

    /// <summary>
    /// Ödeme şekli bankaya çevrilirse mahsup KALKAR: borç yeniden
    /// yükselir. Kalsaydı şirket parayı hem bankadan ödemiş hem
    /// şahsın borcundan düşmüş olurdu.
    /// </summary>
    [Fact]
    public async Task ChangingPaymentMethodAwayFromPartner_RemovesTheSettlement()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var partnerId = await CreatePartnerAsync(client, context);
        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);

        var created = await ReadAsync(await client.PostAsJsonAsync(
            "/api/expenses/kayitlar",
            ExpensePayload(context, rent, 5_000m,
                (int)ExpensePaymentMethod.PartnerAccount, partnerId, "Faturasız kira")));

        var expenseId = created.GetProperty("id").GetGuid();

        var before = await ReadAsync(await client.GetAsync(
            $"/api/expenses/sahis-cari?companyId={context.CompanyId}"));

        Assert.Equal(-5_000m, before.EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == partnerId)
            .GetProperty("balance").GetDecimal());

        // Bankaya çevir.
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            $"/api/expenses/kayitlar/{expenseId}",
            ExpensePayload(context, rent, 5_000m,
                (int)ExpensePaymentMethod.Bank, null, "Bankadan kira"))).StatusCode);

        var after = await ReadAsync(await client.GetAsync(
            $"/api/expenses/sahis-cari?companyId={context.CompanyId}"));

        Assert.Equal(0m, after.EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == partnerId)
            .GetProperty("balance").GetDecimal());
    }

    /// <summary>
    /// Gider silinince mahsup da kalkıyor: sahipsiz bir mahsup
    /// bakiyeyi olduğundan düşük gösterirdi.
    /// </summary>
    [Fact]
    public async Task DeletingTheExpense_RemovesTheSettlement()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var partnerId = await CreatePartnerAsync(client, context);
        var other = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Other);

        var created = await ReadAsync(await client.PostAsJsonAsync(
            "/api/expenses/kayitlar",
            ExpensePayload(context, other, 3_000m,
                (int)ExpensePaymentMethod.PartnerAccount, partnerId, "Faturasız kalem")));

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync(
            $"/api/expenses/kayitlar/{created.GetProperty("id").GetGuid()}")).StatusCode);

        var balances = await ReadAsync(await client.GetAsync(
            $"/api/expenses/sahis-cari?companyId={context.CompanyId}"));

        Assert.Equal(0m, balances.EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == partnerId)
            .GetProperty("settlementTotal").GetDecimal());
    }

    // ---------------- Kurallar ----------------

    /// <summary>
    /// Mahsup ELLE girilmez: gider merkezinde görünmeyen bir kalem
    /// bakiyeyi düşürürse "para nereye gitti" cevapsız kalır.
    /// </summary>
    [Fact]
    public async Task SettlementCannotBeEnteredByHand()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var partnerId = await CreatePartnerAsync(client, context);

        var response = await client.PostAsJsonAsync(
            $"/api/expenses/sahis-cari/{partnerId}/hareketler", new
            {
                kind = (int)PartnerAccountEntryKind.ExpenseSettlement,
                entryDate = Today,
                amount = 1_000m,
                description = "Elle mahsup denemesi"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Gider Merkezi", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// AÇIKLAMA ZORUNLU: bu defter resmî belgeye dayanmıyor,
    /// açıklamasız bir hareket aylar sonra açıklanamayan bir bakiye
    /// bırakır.
    /// </summary>
    [Fact]
    public async Task DescriptionIsMandatory()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var partnerId = await CreatePartnerAsync(client, context);

        var response = await client.PostAsJsonAsync(
            $"/api/expenses/sahis-cari/{partnerId}/hareketler", new
            {
                kind = (int)PartnerAccountEntryKind.Advance,
                entryDate = Today,
                amount = 1_000m,
                description = "   "
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Sahibi seçilmeyen mahsup reddediliyor.</summary>
    [Fact]
    public async Task PartnerIsMandatoryForASettledExpense()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);

        var response = await client.PostAsJsonAsync(
            "/api/expenses/kayitlar",
            ExpensePayload(context, rent, 1_000m,
                (int)ExpensePaymentMethod.PartnerAccount, null, "Sahipsiz mahsup"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// TAMAMI MASKELİ: extra_payment.view olmayan kullanıcı şahıs
    /// carisine hiç giremiyor ve faturasız gider kalemini gider
    /// listesinde de göremiyor.
    /// </summary>
    [Fact]
    public async Task PartnerLedgerIsFullyMaskedWithoutExtraPaymentView()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var privileged = await ClientWithAsync(FullPermissions);
        var partnerId = await CreatePartnerAsync(privileged, context);

        var supplies = await CategoryIdAsync(
            context.CompanyId, ExpenseCategoryCatalog.Supplies);
        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);

        await ReadAsync(await privileged.PostAsJsonAsync(
            "/api/expenses/kayitlar",
            ExpensePayload(context, supplies, 7_000m,
                (int)ExpensePaymentMethod.PartnerAccount, partnerId, "Faturasız sarf")));

        await ReadAsync(await privileged.PostAsJsonAsync(
            "/api/expenses/kayitlar",
            ExpensePayload(context, rent, 20_000m,
                (int)ExpensePaymentMethod.Bank, null, "Bankadan kira")));

        var limited = await ClientWithAsync(WithoutCashPermissions);

        Assert.Equal(HttpStatusCode.Forbidden, (await limited.GetAsync(
            $"/api/expenses/sahis-cari?companyId={context.CompanyId}")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await limited.GetAsync(
            $"/api/expenses/sahis-cari/{partnerId}/hareketler")).StatusCode);

        // Gider listesinde de yok; toplam yalnız görünen kalem.
        var masked = await ReadAsync(await limited.GetAsync(
            $"/api/expenses/kayitlar?companyId={context.CompanyId}"));

        Assert.Equal(1, masked.GetProperty("items").GetArrayLength());
        Assert.Equal(20_000m, masked.GetProperty("total").GetDecimal());
        Assert.Equal(1, masked.GetProperty("hiddenCount").GetInt32());

        // Raporda da gizli.
        var report = await ReadAsync(await limited.GetAsync(
            $"/api/expenses/rapor?companyId={context.CompanyId}" +
            $"&from={Today.AddDays(-1):yyyy-MM-dd}&to={Today.AddDays(1):yyyy-MM-dd}"));

        Assert.Equal(20_000m, report.GetProperty("total").GetDecimal());
        Assert.Equal(1, report.GetProperty("hiddenCount").GetInt32());
    }

    /// <summary>
    /// Faturasız gider kaydını YAZMAK da maskede: yetkisiz kullanıcı
    /// bir gideri şahıs carisine atıp kendi görüşünden kaçıramaz.
    /// </summary>
    [Fact]
    public async Task WritingASettledExpenseRequiresExtraPaymentView()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var privileged = await ClientWithAsync(FullPermissions);
        var partnerId = await CreatePartnerAsync(privileged, context);

        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);

        var limited = await ClientWithAsync(WithoutCashPermissions);

        Assert.Equal(HttpStatusCode.Forbidden, (await limited.PostAsJsonAsync(
            "/api/expenses/kayitlar",
            ExpensePayload(context, rent, 1_000m,
                (int)ExpensePaymentMethod.PartnerAccount, partnerId,
                "Gizlemeye çalış"))).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await limited.PostAsJsonAsync(
            "/api/expenses/sahis-cari", new
            {
                companyId = context.CompanyId,
                fullName = "Yetkisiz ortak",
                title = (string?)null,
                notes = (string?)null
            })).StatusCode);
    }

    /// <summary>
    /// Faturasız gider RESMÎ DEFTERE YAZMIYOR: muhasebe fişi ve kasa
    /// hareketi üretmiyor.
    /// </summary>
    [Fact]
    public async Task SettledExpenseWritesNoVoucherAndNoCashTransaction()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var partnerId = await CreatePartnerAsync(client, context);
        var other = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Other);

        int vouchersBefore, cashBefore;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            vouchersBefore = await db.AccountingVouchers
                .CountAsync(x => x.CompanyId == context.CompanyId);
            cashBefore = await db.CashTransactions
                .CountAsync(x => x.CashAccount.CompanyId == context.CompanyId);
        }

        await ReadAsync(await client.PostAsJsonAsync(
            "/api/expenses/kayitlar",
            ExpensePayload(context, other, 4_500m,
                (int)ExpensePaymentMethod.PartnerAccount, partnerId, "Faturasız kalem")));

        await ReadAsync(await client.PostAsJsonAsync(
            $"/api/expenses/sahis-cari/{partnerId}/hareketler", new
            {
                kind = (int)PartnerAccountEntryKind.Advance,
                entryDate = Today,
                amount = 10_000m,
                description = "Avans"
            }));

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            Assert.Equal(vouchersBefore, await db.AccountingVouchers
                .CountAsync(x => x.CompanyId == context.CompanyId));

            Assert.Equal(cashBefore, await db.CashTransactions
                .CountAsync(x => x.CashAccount.CompanyId == context.CompanyId));
        }
    }

    // ---------------- Aralık uçları (G2 dersi) ----------------

    /// <summary>
    /// G2 DERSİ: filtre uçları GERÇEK from/to ile sınanıyor.
    ///
    /// Boş çağrı yeterli değildi — query string'den gelen tarih
    /// Kind=Unspecified olduğu için Npgsql timestamptz
    /// karşılaştırmasında patlıyordu ve bu ancak parametre
    /// geçildiğinde görülüyor.
    /// </summary>
    [Fact]
    public async Task RangeFilters_WorkWithRealFromAndToParameters()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var partnerId = await CreatePartnerAsync(client, context);
        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);

        var inRange = Today;
        var outOfRange = Today.AddDays(-40);

        await ReadAsync(await client.PostAsJsonAsync(
            "/api/expenses/kayitlar",
            ExpensePayload(context, rent, 10_000m,
                (int)ExpensePaymentMethod.Bank, null, "Bu ayki kira", inRange)));

        await ReadAsync(await client.PostAsJsonAsync(
            "/api/expenses/kayitlar",
            ExpensePayload(context, rent, 8_000m,
                (int)ExpensePaymentMethod.Bank, null, "Eski kira", outOfRange)));

        await ReadAsync(await client.PostAsJsonAsync(
            $"/api/expenses/sahis-cari/{partnerId}/hareketler", new
            {
                kind = (int)PartnerAccountEntryKind.Advance,
                entryDate = inRange,
                amount = 5_000m,
                description = "Bu ayki avans"
            }));

        await ReadAsync(await client.PostAsJsonAsync(
            $"/api/expenses/sahis-cari/{partnerId}/hareketler", new
            {
                kind = (int)PartnerAccountEntryKind.Advance,
                entryDate = outOfRange,
                amount = 3_000m,
                description = "Eski avans"
            }));

        var from = Today.AddDays(-5);
        var to = Today.AddDays(5);

        // Gider kayıtları: aralık uygulanıyor.
        var entries = await ReadAsync(await client.GetAsync(
            $"/api/expenses/kayitlar?companyId={context.CompanyId}" +
            $"&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"));

        Assert.Equal(1, entries.GetProperty("items").GetArrayLength());
        Assert.Equal(10_000m, entries.GetProperty("total").GetDecimal());

        // Şahıs cari hareketleri: aralık uygulanıyor.
        var movements = await ReadAsync(await client.GetAsync(
            $"/api/expenses/sahis-cari/{partnerId}/hareketler" +
            $"?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"));

        Assert.Equal(1, movements.GetArrayLength());
        Assert.Equal(5_000m, movements[0].GetProperty("amount").GetDecimal());

        // Rapor: aralık uygulanıyor.
        var report = await ReadAsync(await client.GetAsync(
            $"/api/expenses/rapor?companyId={context.CompanyId}" +
            $"&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"));

        Assert.Equal(10_000m, report.GetProperty("total").GetDecimal());

        // Geniş aralıkta ikisi de görünüyor — filtre gerçekten
        // çalışıyor, her şeyi elemiyor.
        var wide = await ReadAsync(await client.GetAsync(
            $"/api/expenses/kayitlar?companyId={context.CompanyId}" +
            $"&from={Today.AddDays(-60):yyyy-MM-dd}&to={to:yyyy-MM-dd}"));

        Assert.Equal(2, wide.GetProperty("items").GetArrayLength());
        Assert.Equal(18_000m, wide.GetProperty("total").GetDecimal());
    }

    /// <summary>
    /// Merkez ve kategori filtreleri de gerçek değerlerle
    /// sınanıyor: aralıkla birlikte kullanıldıklarında da doğru
    /// daraltıyorlar.
    /// </summary>
    [Fact]
    public async Task CentreAndCategoryFilters_NarrowTheListCorrectly()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientWithAsync(FullPermissions);

        var rent = await CategoryIdAsync(context.CompanyId, ExpenseCategoryCatalog.Rent);
        var supplies = await CategoryIdAsync(
            context.CompanyId, ExpenseCategoryCatalog.Supplies);

        await ReadAsync(await client.PostAsJsonAsync(
            "/api/expenses/kayitlar",
            ExpensePayload(context, rent, 30_000m,
                (int)ExpensePaymentMethod.Bank, null, "Ofis kirası")));

        await ReadAsync(await client.PostAsJsonAsync(
            "/api/expenses/kayitlar", new
            {
                companyId = context.CompanyId,
                centerType = (int)ExpenseCenterType.Project,
                centerId = context.ProjectId,
                expenseCategoryId = supplies,
                expenseDate = Today,
                amount = 4_000m,
                description = "Proje sarfı",
                paymentMethod = (int)ExpensePaymentMethod.Bank,
                documentType = (int)ExpenseDocumentType.Receipt,
                documentNumber = (string?)null,
                supplierCurrentAccountId = (Guid?)null,
                partnerAccountId = (Guid?)null
            }));

        var from = Today.AddDays(-1);
        var to = Today.AddDays(1);

        var byCentre = await ReadAsync(await client.GetAsync(
            $"/api/expenses/kayitlar?companyId={context.CompanyId}" +
            $"&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}" +
            $"&centerType={(int)ExpenseCenterType.Branch}&centerId={context.BranchId}"));

        Assert.Equal(30_000m, byCentre.GetProperty("total").GetDecimal());

        var byCategory = await ReadAsync(await client.GetAsync(
            $"/api/expenses/kayitlar?companyId={context.CompanyId}" +
            $"&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&categoryId={supplies}"));

        Assert.Equal(4_000m, byCategory.GetProperty("total").GetDecimal());
    }
}
