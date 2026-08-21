using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Market;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Dövizli çek (D4).
///
/// Bu paketin sebebi somut bir hata: çek fişleri kur alanı sabit 1 ile
/// kesiliyordu, yani 10.000 dolarlık bir çek deftere 10.000 TL olarak
/// giriyordu. Çekin para birimi vardı ama kuru YOKTU.
///
/// İki güvence:
/// 1. Çek keşide kuruyla deftere girer; kur bulunamazsa çek kaydedilmez.
/// 2. Tahsil/ödeme günü kur farklıysa GERÇEKLEŞMİŞ fark 646/656'ya
///    yazılır ve fiş TL tarafında dengeli kalır.
/// </summary>
[Collection("Integration")]
public sealed class ForeignCurrencyChequeTests(DatabaseFixture fixture)
{
    private sealed record TestContext(
        Guid CompanyId, Guid ProjectId, Guid EmployerId, Guid SupplierId,
        Guid BankAccountId);

    private static async Task SeedChartOfAccountsAsync(
        AppDbContext db, Guid companyId)
    {
        (string Code, string Name, AccountingAccountNature Nature)[] accounts =
        [
            ("102", "Bankalar", AccountingAccountNature.Debit),
            ("101.01", "Portföydeki Çekler", AccountingAccountNature.Debit),
            ("101.02", "Tahsildeki Çekler", AccountingAccountNature.Debit),
            ("103.01", "Verilen Çekler", AccountingAccountNature.Credit),
            ("120", "Alıcılar", AccountingAccountNature.Debit),
            ("320", "Satıcılar", AccountingAccountNature.Credit),
            ("646.01", "Kambiyo Kârı", AccountingAccountNature.Credit),
            ("656.01", "Kambiyo Zararı", AccountingAccountNature.Debit)
        ];

        foreach (var (code, name, nature) in accounts)
        {
            db.AccountingAccounts.Add(new AccountingAccount
            {
                CompanyId = companyId,
                Code = code,
                Name = name,
                Nature = nature,
                Level = code.Contains('.') ? 4 : 3,
                IsPostingAllowed = true
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task<TestContext> CreateContextAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        await SeedChartOfAccountsAsync(db, project.CompanyId);

        var supplier = new CurrentAccount
        {
            CompanyId = project.CompanyId,
            Code = $"TED-{suffix}",
            Title = $"Test Tedarikçi {suffix}",
            Roles = CurrentAccountRoles.Supplier,
            Status = CurrentAccountStatus.Approved
        };
        db.CurrentAccounts.Add(supplier);

        var bankAccountingId = await db.AccountingAccounts
            .Where(x => x.CompanyId == project.CompanyId && x.Code == "102")
            .Select(x => x.Id)
            .SingleAsync();

        var bank = new CashAccount
        {
            CompanyId = project.CompanyId,
            Type = CashAccountType.Bank,
            Code = $"BNK-{suffix}",
            Name = $"Test Banka {suffix}",
            BankName = "Test Bankası",
            CurrencyCode = "TRY",
            OpeningBalance = 0m,
            AccountingAccountId = bankAccountingId
        };
        db.CashAccounts.Add(bank);

        await db.SaveChangesAsync();

        return new TestContext(
            project.CompanyId, project.Id,
            project.EmployerCurrentAccountId!.Value, supplier.Id, bank.Id);
    }

    /// <summary>TCMB arşivine bir güne kur yazar.</summary>
    private async Task SeedRateAsync(DateTime date, decimal buying)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var day = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);

        if (await db.ExchangeRates.AnyAsync(
                x => x.RateDate == day && x.CurrencyCode == "USD"))
        {
            return;
        }

        db.ExchangeRates.Add(new ExchangeRate
        {
            RateDate = day,
            CurrencyCode = "USD",
            Unit = 1,
            ForexBuying = buying,
            ForexSelling = buying + 0.1m,
            Source = "TCMB"
        });

        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> CreateClientAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        const string password = "FxCheque!2026";
        var username = $"test-fxchq-{Guid.NewGuid():N}"[..40];
        var hash = passwordService.Hash(password);

        var user = new AppUser
        {
            Username = username,
            FullName = "Test Genel Müdür",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            IsActive = true,
            WorkHoursExempt = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var role = await db.Roles.SingleAsync(x => x.Name == "Genel Müdür");
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        db.UserDataScopes.Add(new UserDataScope
        {
            UserId = user.Id,
            ScopeType = DataScopeType.All
        });
        await db.SaveChangesAsync();

        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, username, password);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    /// <summary>
    /// Her teste AYRI tarih çifti.
    ///
    /// Kur arşivi şirketten bağımsız GLOBAL bir tablo: iki test aynı
    /// güne farklı kur yazmak isterse ikincisi birincinin kurunu görür
    /// ve senaryo sessizce bozulur. Tarihi teste özel yapmak bu
    /// bağımlılığı tamamen kaldırıyor.
    /// </summary>
    private static (DateTime Issue, DateTime Settlement) Days(int year) =>
        (new DateTime(year, 3, 2, 0, 0, 0, DateTimeKind.Utc),
         new DateTime(year, 4, 15, 0, 0, 0, DateTimeKind.Utc));

    private static object BuildPayload(
        TestContext context,
        ChequeDirection direction,
        string currencyCode = "USD",
        decimal amount = 10_000m,
        decimal? exchangeRate = null,
        DateTime? issueDate = null,
        DateTime? dueDate = null) => new
        {
            companyId = context.CompanyId,
            direction = (int)direction,
            chequeNumber = $"CK{Guid.NewGuid():N}"[..10],
            bankName = "Test Bankası",
            bankBranch = "Merkez",
            drawer = "Test Keşideci",
            currentAccountId = direction == ChequeDirection.Received
                ? context.EmployerId
                : context.SupplierId,
            projectId = context.ProjectId,
            amount,
            currencyCode,
            issueDate,
            dueDate,
            progressPaymentId = (Guid?)null,
            supplierInvoiceId = (Guid?)null,
            description = "Dövizli test çeki",
            exchangeRate
        };

    private async Task<(decimal Debit, decimal Credit,
        IReadOnlyList<(string Code, decimal Debit, decimal Credit)> Lines)>
        ReadVoucherAsync(Guid chequeId, int index)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var voucher = await db.AccountingVouchers
            .Include(x => x.Lines)
            .ThenInclude(x => x.AccountingAccount)
            .Where(x => x.SourceModule == "Cheque" && x.SourceEntityId == chequeId)
            .OrderBy(x => x.CreatedAtUtc)
            .Skip(index)
            .FirstAsync();

        var lines = voucher.Lines
            .Select(x => (x.AccountingAccount.Code,
                          x.DebitAmountLocal, x.CreditAmountLocal))
            .ToList();

        return (voucher.TotalDebit, voucher.TotalCredit, lines);
    }

    // ---------- Keşide: defter değeri ----------

    /// <summary>
    /// Asıl hatanın testi: dolarlık çek TL karşılığıyla deftere girmeli.
    /// 10.000 USD × 35 = 350.000 TL — 10.000 TL değil.
    /// </summary>
    [Fact]
    public async Task ForeignCheque_IsBookedAtItsTryValue()
    {
        var context = await CreateContextAsync();
        var (issueDay, settlementDay) = Days(2026);
        await SeedRateAsync(issueDay, 35m);

        var client = await CreateClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/cheques", BuildPayload(context, ChequeDirection.Received,
                issueDate: issueDay, dueDate: settlementDay));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var chequeId = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cheque = await db.Cheques.SingleAsync(x => x.Id == chequeId);

        Assert.Equal(35m, cheque.ExchangeRate);
        Assert.Equal(350_000m, cheque.AmountTry);

        var (debit, credit, lines) = await ReadVoucherAsync(chequeId, 0);

        Assert.Equal(350_000m, debit);
        Assert.Equal(350_000m, credit);
        Assert.All(lines, line =>
            Assert.Equal(350_000m, line.Debit + line.Credit));
    }

    /// <summary>
    /// Elle girilen kur TCMB'yi ezer — sözleşmede sabitlenmiş kurla
    /// keşide edilen çekler için gerekli.
    /// </summary>
    [Fact]
    public async Task ExplicitRate_OverridesTheArchive()
    {
        var context = await CreateContextAsync();
        var (issueDay, settlementDay) = Days(2027);
        await SeedRateAsync(issueDay, 35m);

        var client = await CreateClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/cheques",
            BuildPayload(context, ChequeDirection.Received, exchangeRate: 32.5m,
                issueDate: issueDay, dueDate: settlementDay));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var chequeId = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cheque = await db.Cheques.SingleAsync(x => x.Id == chequeId);

        Assert.Equal(32.5m, cheque.ExchangeRate);
        Assert.Equal(325_000m, cheque.AmountTry);
    }

    /// <summary>
    /// Kur bulunamazsa çek KAYDEDİLMEZ. Kuru uydurmak ya da 1 kabul
    /// etmek, tam olarak düzeltmeye çalıştığımız hatayı üretirdi.
    /// </summary>
    [Fact]
    public async Task ChequeIsRejectedWhenNoRateIsAvailable()
    {
        var (issueDay, settlementDay) = Days(2034);
        var context = await CreateContextAsync();
        var client = await CreateClientAsync();

        // "Kur yok" durumu TARİHLE kurulamaz: arşiv araması bilinçli
        // olarak GERİYE yürüyor (hafta sonu/tatil için), yani seçilen
        // günden önceki en yakın kuru buluyor. Başka testler USD'yi
        // 2001'e kadar geriye yazdığı için hangi tarih seçilirse
        // seçilsin bir kur bulunuyordu.
        //
        // Doğru öncül PARA BİRİMİ: hiçbir testin arşive yazmadığı bir
        // kod seçiliyor.
        var response = await client.PostAsJsonAsync(
            "/api/cheques",
            BuildPayload(
                context,
                ChequeDirection.Received,
                currencyCode: "SEK",
                issueDate: issueDay,
                dueDate: settlementDay));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await db.Cheques.AnyAsync(
            x => x.CompanyId == context.CompanyId));
    }

    /// <summary>TL çekte davranış değişmiyor: kur 1, TL karşılığı tutarın kendisi.</summary>
    [Fact]
    public async Task LocalCurrencyCheque_KeepsRateOne()
    {
        var (issueDay, settlementDay) = Days(2033);
        var context = await CreateContextAsync();
        var client = await CreateClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/cheques",
            BuildPayload(context, ChequeDirection.Received,
                currencyCode: "TRY", amount: 100_000m,
                issueDate: issueDay, dueDate: settlementDay));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var chequeId = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cheque = await db.Cheques.SingleAsync(x => x.Id == chequeId);

        Assert.Equal(1m, cheque.ExchangeRate);
        Assert.Equal(100_000m, cheque.AmountTry);
    }

    // ---------- Tahsilat/ödeme: kur farkı ----------

    /// <summary>
    /// ALINAN çekte kur YÜKSELDİ: elimize 380.000 TL geçti, çekin defter
    /// değeri 350.000 TL'ydi. Aradaki 30.000 TL kambiyo KÂRI (646).
    /// </summary>
    [Fact]
    public async Task ReceivedCheque_PostsExchangeGainWhenRateRises()
    {
        var context = await CreateContextAsync();
        var (issueDay, settlementDay) = Days(2028);
        await SeedRateAsync(issueDay, 35m);
        await SeedRateAsync(settlementDay, 38m);

        var client = await CreateClientAsync();

        var chequeId = (await (await client.PostAsJsonAsync(
                    "/api/cheques", BuildPayload(context, ChequeDirection.Received,
                issueDate: issueDay, dueDate: settlementDay)))
                .Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var collect = await client.PostChequeAsync(
            $"/api/cheques/{chequeId}/status", chequeId,
            new
            {
                toStatus = (int)ChequeStatus.Collected,
                movementDate = settlementDay,
                cashAccountId = context.BankAccountId,
                description = (string?)null
            });

        Assert.Equal(HttpStatusCode.OK, collect.StatusCode);

        var (debit, credit, lines) = await ReadVoucherAsync(chequeId, 1);

        // Fiş TL tarafında dengeli.
        Assert.Equal(debit, credit);
        Assert.Equal(380_000m, debit);

        // Banka gerçek TL'yi aldı.
        var bank = lines.Single(x => x.Code == "102");
        Assert.Equal(380_000m, bank.Debit);

        // Çek defter değeriyle çıktı.
        var portfolio = lines.Single(x => x.Code == "101.01");
        Assert.Equal(350_000m, portfolio.Credit);

        // Fark kambiyo kârına.
        var gain = lines.Single(x => x.Code == "646.01");
        Assert.Equal(30_000m, gain.Credit);
        Assert.DoesNotContain(lines, x => x.Code == "656.01");
    }

    /// <summary>
    /// ALINAN çekte kur DÜŞTÜ: 320.000 TL aldık, defter değeri 350.000
    /// TL'ydi. 30.000 TL kambiyo ZARARI (656).
    /// </summary>
    [Fact]
    public async Task ReceivedCheque_PostsExchangeLossWhenRateFalls()
    {
        var context = await CreateContextAsync();
        var (issueDay, settlementDay) = Days(2029);
        await SeedRateAsync(issueDay, 35m);
        await SeedRateAsync(settlementDay, 32m);

        var client = await CreateClientAsync();

        var chequeId = (await (await client.PostAsJsonAsync(
                    "/api/cheques", BuildPayload(context, ChequeDirection.Received,
                issueDate: issueDay, dueDate: settlementDay)))
                .Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await client.PostChequeAsync(
            $"/api/cheques/{chequeId}/status", chequeId,
            new
            {
                toStatus = (int)ChequeStatus.Collected,
                movementDate = settlementDay,
                cashAccountId = context.BankAccountId,
                description = (string?)null
            });

        var (debit, credit, lines) = await ReadVoucherAsync(chequeId, 1);

        Assert.Equal(debit, credit);

        var loss = lines.Single(x => x.Code == "656.01");
        Assert.Equal(30_000m, loss.Debit);
        Assert.DoesNotContain(lines, x => x.Code == "646.01");
    }

    /// <summary>
    /// VERİLEN çekte yön TERSİNE döner: kur yükseldiyse daha çok TL
    /// ödedik, yani ZARAR. Bu testin asıl işi işaret hatasını yakalamak;
    /// aynı fark alınan çekte kâr, verilen çekte zarardır.
    /// </summary>
    [Fact]
    public async Task IssuedCheque_PostsExchangeLossWhenRateRises()
    {
        var context = await CreateContextAsync();
        var (issueDay, settlementDay) = Days(2030);
        await SeedRateAsync(issueDay, 35m);
        await SeedRateAsync(settlementDay, 38m);

        var client = await CreateClientAsync();

        var chequeId = (await (await client.PostAsJsonAsync(
                    "/api/cheques", BuildPayload(context, ChequeDirection.Issued,
                issueDate: issueDay, dueDate: settlementDay)))
                .Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var pay = await client.PostChequeAsync(
            $"/api/cheques/{chequeId}/status", chequeId,
            new
            {
                toStatus = (int)ChequeStatus.Paid,
                movementDate = settlementDay,
                cashAccountId = context.BankAccountId,
                description = (string?)null
            });

        Assert.Equal(HttpStatusCode.OK, pay.StatusCode);

        var (debit, credit, lines) = await ReadVoucherAsync(chequeId, 1);

        Assert.Equal(debit, credit);

        var loss = lines.Single(x => x.Code == "656.01");
        Assert.Equal(30_000m, loss.Debit);
        Assert.DoesNotContain(lines, x => x.Code == "646.01");
    }

    /// <summary>
    /// Para HAREKET ETMEYEN geçişte fark yazılmaz: portföyden bankaya
    /// tahsile verme aynı enstrümanı iki hesap arasında taşır, defter
    /// değeri korunur. Değerleme farkı dönem sonu işidir, bu fişin
    /// konusu değil.
    /// </summary>
    [Fact]
    public async Task MovingBetweenChequeAccounts_DoesNotRealiseAnyDifference()
    {
        var context = await CreateContextAsync();
        var (issueDay, settlementDay) = Days(2031);
        await SeedRateAsync(issueDay, 35m);
        await SeedRateAsync(settlementDay, 38m);

        var client = await CreateClientAsync();

        var chequeId = (await (await client.PostAsJsonAsync(
                    "/api/cheques", BuildPayload(context, ChequeDirection.Received,
                issueDate: issueDay, dueDate: settlementDay)))
                .Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var toBank = await client.PostChequeAsync(
            $"/api/cheques/{chequeId}/status", chequeId,
            new
            {
                toStatus = (int)ChequeStatus.AtBank,
                movementDate = settlementDay,
                cashAccountId = context.BankAccountId,
                description = (string?)null
            });

        Assert.Equal(HttpStatusCode.OK, toBank.StatusCode);

        var (debit, credit, lines) = await ReadVoucherAsync(chequeId, 1);

        Assert.Equal(350_000m, debit);
        Assert.Equal(350_000m, credit);
        Assert.DoesNotContain(lines, x => x.Code is "646.01" or "656.01");
    }

    /// <summary>
    /// Aynı kurda tahsil edilirse fark satırı HİÇ açılmaz — sıfır
    /// tutarlı bir kambiyo satırı defteri gereksiz kalabalıklaştırırdı.
    /// </summary>
    [Fact]
    public async Task NoDifferenceLineWhenRateIsUnchanged()
    {
        var context = await CreateContextAsync();
        var (issueDay, settlementDay) = Days(2032);
        await SeedRateAsync(issueDay, 35m);
        await SeedRateAsync(settlementDay, 35m);

        var client = await CreateClientAsync();

        var chequeId = (await (await client.PostAsJsonAsync(
                    "/api/cheques", BuildPayload(context, ChequeDirection.Received,
                issueDate: issueDay, dueDate: settlementDay)))
                .Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await client.PostChequeAsync(
            $"/api/cheques/{chequeId}/status", chequeId,
            new
            {
                toStatus = (int)ChequeStatus.Collected,
                movementDate = settlementDay,
                cashAccountId = context.BankAccountId,
                description = (string?)null
            });

        var (_, _, lines) = await ReadVoucherAsync(chequeId, 1);

        Assert.DoesNotContain(lines, x => x.Code is "646.01" or "656.01");
    }
}
