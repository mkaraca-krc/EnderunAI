using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Çek erteleme/değişim zinciri.
///
/// Ertelemenin muhasebedeki anlamı şudur: eski çek ters kayıtla kapanır,
/// yeni çek kendi kaydını üretir; net etki yalnızca vadenin değişmesidir.
/// Eski çek açık kalsaydı hem nakit akışında hem cari bakiyesinde aynı
/// borç iki kez görünürdü.
/// </summary>
[Collection("Integration")]
public sealed class ChequeReplacementTests(DatabaseFixture fixture)
{
    private sealed record TestContext(
        Guid CompanyId, Guid ProjectId, Guid SupplierId, Guid EmployerId);

    private static async Task SeedChartOfAccountsAsync(AppDbContext db, Guid companyId)
    {
        db.AccountingAccounts.AddRange(
            new AccountingAccount
            {
                CompanyId = companyId, Code = "101.01", Name = "Portföydeki Çekler",
                Nature = AccountingAccountNature.Debit, Level = 4, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "103.01", Name = "Verilen Çekler",
                Nature = AccountingAccountNature.Credit, Level = 4, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "120", Name = "Alıcılar",
                Nature = AccountingAccountNature.Debit, Level = 3, IsPostingAllowed = true
            },
            new AccountingAccount
            {
                CompanyId = companyId, Code = "320", Name = "Satıcılar",
                Nature = AccountingAccountNature.Credit, Level = 3, IsPostingAllowed = true
            });

        await db.SaveChangesAsync();
    }

    private async Task<TestContext> CreateContextAsync(string suffix)
    {
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
        await db.SaveChangesAsync();

        return new TestContext(
            project.CompanyId, project.Id, supplier.Id,
            project.EmployerCurrentAccountId!.Value);
    }

    private static object BuildChequePayload(
        TestContext context, ChequeDirection direction, int dueInDays = 30,
        object[]? allocations = null) => new
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
            amount = 100_000m,
            currencyCode = "TRY",
            issueDate = DateTime.UtcNow.Date,
            dueDate = DateTime.UtcNow.Date.AddDays(dueInDays),
            progressPaymentId = (Guid?)null,
            supplierInvoiceId = (Guid?)null,
            description = "Test çeki",
            allocations
        };

    private async Task<Guid> CreateChequeAsync(
        HttpClient client, TestContext context, ChequeDirection direction,
        object[]? allocations = null)
    {
        var response = await client.PostAsJsonAsync("/api/cheques",
            BuildChequePayload(context, direction, allocations: allocations));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> DetailAsync(HttpClient client, Guid id) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/cheques/{id}");

    /// <summary>Çeki erteler, yerine geçen çekin kimliğini döndürür.</summary>
    private static async Task<Guid> ReplaceAsync(
        HttpClient client, Guid chequeId, string newNumber, int dueInDays)
    {
        var response = await client.PostChequeAsync(
            $"/api/cheques/{chequeId}/replace", chequeId,
            new
            {
                chequeNumber = newNumber,
                dueDate = DateTime.UtcNow.Date.AddDays(dueInDays),
                movementDate = DateTime.UtcNow.Date,
                description = "vade uzatması"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> VoidAsync(
        HttpClient client, Guid chequeId, JsonElement detail) =>
        await client.PostChequeAsync($"/api/cheques/{chequeId}/iptal", chequeId, new
        {
            reason = "yerine geçen çek geçersiz",
            reasonKind = (int)ChequeVoidReason.Other,
            rowVersion = detail.GetProperty("rowVersion").GetDateTime()
        });

    // ---------------------------------------------------------------
    // ERTELEME ZİNCİRİ — YERİNE GEÇEN İPTAL EDİLİRSE
    // ---------------------------------------------------------------

    /// <summary>
    /// YERİNE GEÇEN ÇEK İPTAL EDİLİNCE ORİJİNAL AÇILIR.
    ///
    /// Yoksa ortada geçerli bir çek kalmadığı hâlde borç duruyor ve
    /// orijinal "Ertelendi"de kaldığı için portföyden, vade raporundan
    /// ve defterden birden düşüyor — gerçek bir alacak sistemde
    /// görünmez oluyor. Bu, çek numarası sorunundan daha tehlikeli:
    /// kimse fark etmiyor.
    ///
    /// ÖNCEKİ DURUM TAHMİN EDİLMİYOR: çek bankada tahsildeyken
    /// ertelendiyse "Bankada"ya döner, "Portföyde"ye değil.
    /// </summary>
    [Fact]
    public async Task YerineGecenCekIptalEdilince_OrijinalOncekiDurumunaDoner()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var originalId = await CreateChequeAsync(
            client, context, ChequeDirection.Received);

        // Çek BANKAYA VERİLİYOR: geri dönüşün "Portföyde" değil
        // "Bankada" olması gerektiğini bu adım kanıtlıyor.
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var bankAccountingId = await db.AccountingAccounts
                .Where(x => x.CompanyId == context.CompanyId && x.Code == "101.01")
                .Select(x => x.Id)
                .SingleAsync();

            // "Tahsildeki çekler" hesabı bu senaryoya özel: bankaya
            // verme geçişi 101.02'ye yazıyor.
            db.AccountingAccounts.Add(new AccountingAccount
            {
                CompanyId = context.CompanyId,
                Code = "101.02",
                Name = "Tahsildeki Çekler",
                Nature = AccountingAccountNature.Debit,
                Level = 4,
                IsPostingAllowed = true
            });

            var bank = new CashAccount
            {
                CompanyId = context.CompanyId,
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

            var moved = await client.PostChequeAsync($"/api/cheques/{originalId}/status", originalId, new
            {
                toStatus = (int)ChequeStatus.AtBank,
                movementDate = DateTime.UtcNow.Date,
                cashAccountId = bank.Id,
                description = "tahsile verildi"
            });

            Assert.True(
                moved.StatusCode == HttpStatusCode.OK,
                await moved.Content.ReadAsStringAsync());
        }

        var replacementId = await ReplaceAsync(
            client, originalId, $"ERT{suffix}", dueInDays: 60);

        var beforeVoid = await DetailAsync(client, originalId);
        Assert.Equal((int)ChequeStatus.Replaced, beforeVoid.GetProperty("status").GetInt32());

        // İPTAL EKRANI ÖNCEDEN UYARABİLSİN: yanıt neyin açılacağını
        // söylüyor. Kural sunucudaki geri dönüşle aynı kaynaktan.
        var replacementDetail = await DetailAsync(client, replacementId);

        Assert.Equal(
            beforeVoid.GetProperty("chequeNumber").GetString(),
            replacementDetail.GetProperty("voidRestoresChequeNumber").GetString());
        Assert.Equal(
            "Bankada (tahsilde)",
            replacementDetail.GetProperty("voidRestoresStatusName").GetString());

        var voided = await VoidAsync(client, replacementId, replacementDetail);
        Assert.Equal(HttpStatusCode.OK, voided.StatusCode);

        var afterVoid = await DetailAsync(client, originalId);

        Assert.Equal((int)ChequeStatus.AtBank, afterVoid.GetProperty("status").GetInt32());
        Assert.Equal(
            JsonValueKind.Null,
            afterVoid.GetProperty("replacedByChequeId").ValueKind);

        // SESSİZ DEĞİL: hareket kaydı ve denetim kaydı bırakıyor.
        var restoreMovement = afterVoid.GetProperty("movements").EnumerateArray()
            .Last();

        Assert.Equal("Ertelendi (değiştirildi)", restoreMovement.GetProperty("fromStatusName").GetString());
        Assert.Contains("Erteleme geri alındı", restoreMovement.GetProperty("description").GetString()!);

        var log = afterVoid.GetProperty("changeLog").EnumerateArray()
            .Single(x => x.GetProperty("fieldName").GetString() == "Status");

        Assert.True(log.GetProperty("affectsAccounting").GetBoolean());
        Assert.Equal("Bankada (tahsilde)", log.GetProperty("newValue").GetString());
    }

    /// <summary>
    /// DEFTER DE GERİ GELİR. Erteleme orijinali ters kayıtla defterden
    /// çıkarıyor; yalnız durumu geri almak çeki raporlarda gösterip
    /// mizanda göstermezdi — iki kaynak sessizce ayrışırdı.
    /// </summary>
    [Fact]
    public async Task YerineGecenIptalEdilince_OrijinalinDefterKaydiGeriGelir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var originalId = await CreateChequeAsync(
            client, context, ChequeDirection.Received);

        var replacementId = await ReplaceAsync(
            client, originalId, $"DFT{suffix}", dueInDays: 45);

        await VoidAsync(client, replacementId, await DetailAsync(client, replacementId));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Ertelenme hareketi ters kayıtla kapanmış olmalı.
        var replacedMovement = await db.ChequeMovements
            .AsNoTracking()
            .Where(x => x.ChequeId == originalId && x.ToStatus == ChequeStatus.Replaced)
            .SingleAsync();

        Assert.NotNull(replacedMovement.ReversedAtUtc);
        Assert.NotNull(replacedMovement.ReversalVoucherId);

        // 101 ailesinin bakiyesi orijinal çeğin tutarına eşit: çek
        // yeniden defterde, yerine geçen çek defterden çıkmış.
        var lines = await db.AccountingVoucherLines
            .AsNoTracking()
            .Where(x => x.AccountingAccount.CompanyId == context.CompanyId
                        && x.AccountingAccount.Code.StartsWith("101"))
            .Select(x => new { x.DebitAmountLocal, x.CreditAmountLocal })
            .ToListAsync();

        var balance = lines.Sum(x => x.DebitAmountLocal - x.CreditAmountLocal);

        var original = await db.Cheques.AsNoTracking().SingleAsync(x => x.Id == originalId);

        Assert.Equal(original.AmountTry, balance);
    }

    /// <summary>
    /// ZİNCİRE DOKUNULMAZ: A→B→C zincirinde C iptal edilirse B açılır,
    /// A "Ertelendi" kalır. A zaten B'yi işaret ediyor; onu da açmak
    /// aynı borcu iki çekle birden göstermek olurdu.
    /// </summary>
    [Fact]
    public async Task ZincirliErteleme_YalnizSonHalkaninOncekiCekiAcilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var aId = await CreateChequeAsync(client, context, ChequeDirection.Received);
        var bId = await ReplaceAsync(client, aId, $"B{suffix}", dueInDays: 30);
        var cId = await ReplaceAsync(client, bId, $"C{suffix}", dueInDays: 60);

        await VoidAsync(client, cId, await DetailAsync(client, cId));

        var a = await DetailAsync(client, aId);
        var b = await DetailAsync(client, bId);

        // B açıldı.
        Assert.Equal((int)ChequeStatus.Portfolio, b.GetProperty("status").GetInt32());

        // A'ya DOKUNULMADI ve hâlâ B'yi işaret ediyor.
        Assert.Equal((int)ChequeStatus.Replaced, a.GetProperty("status").GetInt32());
        Assert.Equal(bId, a.GetProperty("replacedByChequeId").GetGuid());
    }

    /// <summary>
    /// Verilen çek ertelendiğinde: eski çek "Ertelendi" olur, ters kaydı
    /// kesilir; yeni çek yeni vadeyle açılır ve zincire bağlanır.
    /// İkisinin fişlerinin net etkisi vadenin değişmesinden ibarettir.
    /// </summary>
    [Fact]
    public async Task ReplaceIssuedCheque_ClosesOldAndOpensNewWithChain()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var oldChequeId = await CreateChequeAsync(client, context, ChequeDirection.Issued);
        var newNumber = $"YENI{Guid.NewGuid():N}"[..10];
        var newDueDate = DateTime.UtcNow.Date.AddDays(90);

        var response = await client.PostChequeAsync(
            $"/api/cheques/{oldChequeId}/replace", oldChequeId,
            new
            {
                chequeNumber = newNumber,
                dueDate = newDueDate,
                movementDate = DateTime.UtcNow.Date,
                description = "Tedarikçi vade uzatması istedi"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var replacement = await response.Content.ReadFromJsonAsync<JsonElement>();
        var newChequeId = replacement.GetProperty("id").GetGuid();

        Assert.Equal((int)ChequeStatus.Issued, replacement.GetProperty("status").GetInt32());
        Assert.Equal(newNumber, replacement.GetProperty("chequeNumber").GetString());
        Assert.Equal(100_000m, replacement.GetProperty("amount").GetDecimal());
        Assert.Equal(oldChequeId, replacement.GetProperty("replacesChequeId").GetGuid());
        Assert.Equal(1, replacement.GetProperty("renewalCount").GetInt32());

        var old = await client.GetFromJsonAsync<JsonElement>($"/api/cheques/{oldChequeId}");

        Assert.Equal((int)ChequeStatus.Replaced, old.GetProperty("status").GetInt32());
        Assert.Equal("Ertelendi (değiştirildi)", old.GetProperty("statusName").GetString());
        Assert.Equal(newChequeId, old.GetProperty("replacedByChequeId").GetGuid());

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var vouchers = await db.AccountingVouchers
            .Include(x => x.Lines)
            .Where(x => x.SourceModule == "Cheque" &&
                        (x.SourceEntityId == oldChequeId || x.SourceEntityId == newChequeId))
            .ToListAsync();

        // Eski çekin girişi + ters kaydı + yeni çekin girişi.
        Assert.Equal(3, vouchers.Count);
        Assert.All(vouchers, voucher =>
        {
            Assert.Equal(AccountingVoucherStatus.Posted, voucher.Status);
            Assert.Equal(voucher.TotalDebit, voucher.TotalCredit);
        });

        // Eski çeke ait iki fişin net etkisi sıfır olmalı.
        var oldNet = vouchers
            .Where(x => x.SourceEntityId == oldChequeId)
            .SelectMany(x => x.Lines)
            .GroupBy(x => x.AccountingAccountId)
            .Select(g => g.Sum(x => x.DebitAmount - x.CreditAmount));

        Assert.All(oldNet, net => Assert.Equal(0m, net));

        var newCheque = await db.Cheques.SingleAsync(x => x.Id == newChequeId);
        Assert.Equal(newDueDate, newCheque.DueDate);
    }

    /// <summary>
    /// Alınan çek de ertelenebilir: müşteri eski çeki geri alıp yeni
    /// vadeli çek verir.
    /// </summary>
    [Fact]
    public async Task ReplaceReceivedCheque_IsSupported()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var oldChequeId = await CreateChequeAsync(client, context, ChequeDirection.Received);

        var response = await client.PostChequeAsync(
            $"/api/cheques/{oldChequeId}/replace", oldChequeId,
            new
            {
                chequeNumber = $"YENI{Guid.NewGuid():N}"[..10],
                dueDate = DateTime.UtcNow.Date.AddDays(120),
                movementDate = DateTime.UtcNow.Date
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var replacement = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal((int)ChequeStatus.Portfolio, replacement.GetProperty("status").GetInt32());
        Assert.Equal(oldChequeId, replacement.GetProperty("replacesChequeId").GetGuid());
    }

    /// <summary>
    /// Üst üste erteleme zinciri ve sayacı — sürekli ertelenen çek risk
    /// sinyalidir, sayı kaybolmamalı.
    /// </summary>
    [Fact]
    public async Task RepeatedReplacement_IncrementsRenewalCount()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var currentId = await CreateChequeAsync(client, context, ChequeDirection.Issued);

        for (var round = 1; round <= 3; round++)
        {
            var response = await client.PostChequeAsync(
                $"/api/cheques/{currentId}/replace", currentId,
                new
                {
                    chequeNumber = $"Y{round}{Guid.NewGuid():N}"[..10],
                    dueDate = DateTime.UtcNow.Date.AddDays(30 * (round + 1)),
                    movementDate = DateTime.UtcNow.Date
                });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(round, payload.GetProperty("renewalCount").GetInt32());
            currentId = payload.GetProperty("id").GetGuid();
        }
    }

    /// <summary>
    /// Ertelenen çek nakit akışında görünmemeli; yerini yeni vadeli çek
    /// almalı. Eski çek de listede kalsaydı aynı ödeme iki kez planlanmış
    /// görünürdü.
    /// </summary>
    [Fact]
    public async Task ReplacedCheque_LeavesCashFlowAndNewDueDateApplies()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var oldChequeId = await CreateChequeAsync(client, context, ChequeDirection.Issued);

        var newDueDate = DateTime.UtcNow.Date.AddDays(200);

        var response = await client.PostChequeAsync(
            $"/api/cheques/{oldChequeId}/replace", oldChequeId,
            new
            {
                chequeNumber = $"YENI{Guid.NewGuid():N}"[..10],
                dueDate = newDueDate,
                movementDate = DateTime.UtcNow.Date
            });

        var newChequeId = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var cashFlow = await client.GetFromJsonAsync<JsonElement>(
            $"/api/cash-flow?companyId={context.CompanyId}");

        var chequeItems = cashFlow.GetProperty("outflows").EnumerateArray()
            .Where(x => x.GetProperty("kind").GetString() == "IssuedCheque")
            .ToList();

        Assert.Single(chequeItems);
        Assert.Equal(newChequeId, chequeItems[0].GetProperty("sourceId").GetGuid());
        Assert.Equal(newDueDate.Date,
            chequeItems[0].GetProperty("expectedDate").GetDateTime().Date);
    }

    /// <summary>
    /// Dağılım yeni çeke taşınır: yeni çek aynı projeleri karşılıyor,
    /// taşınmasaydı masraf merkezi kırılımı kaybolurdu.
    /// </summary>
    [Fact]
    public async Task Replacement_CarriesAllocationsToNewCheque()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var oldChequeId = await CreateChequeAsync(client, context, ChequeDirection.Issued,
            allocations:
            [
                new { amount = 70_000m, projectId = context.ProjectId },
                new { amount = 30_000m, costCenterCode = "MERKEZ" }
            ]);

        var response = await client.PostChequeAsync(
            $"/api/cheques/{oldChequeId}/replace", oldChequeId,
            new
            {
                chequeNumber = $"YENI{Guid.NewGuid():N}"[..10],
                dueDate = DateTime.UtcNow.Date.AddDays(75),
                movementDate = DateTime.UtcNow.Date
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var replacement = await response.Content.ReadFromJsonAsync<JsonElement>();
        var allocations = replacement.GetProperty("allocations").EnumerateArray().ToList();

        Assert.Equal(2, allocations.Count);
        Assert.Contains(allocations, x => x.GetProperty("amount").GetDecimal() == 30_000m &&
                                          x.GetProperty("costCenterCode").GetString() == "MERKEZ");

        // Yeni çekin fişinin cari tarafı da dağılıma göre bölünmeli.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var newChequeId = replacement.GetProperty("id").GetGuid();

        var voucher = await db.AccountingVouchers
            .Include(x => x.Lines).ThenInclude(x => x.AccountingAccount)
            .SingleAsync(x => x.SourceModule == "Cheque" && x.SourceEntityId == newChequeId);

        var payableLines = voucher.Lines
            .Where(x => x.AccountingAccount.Code == "320")
            .ToList();

        Assert.Equal(2, payableLines.Count);
        Assert.Equal(voucher.TotalDebit, voucher.TotalCredit);
    }

    /// <summary>
    /// Aynı çek iki kez ertelenemez; ikinci istek zinciri çatallandırır.
    /// </summary>
    [Fact]
    public async Task Replace_AlreadyReplacedCheque_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var oldChequeId = await CreateChequeAsync(client, context, ChequeDirection.Issued);

        Assert.Equal(HttpStatusCode.OK, (await client.PostChequeAsync(
            $"/api/cheques/{oldChequeId}/replace", oldChequeId,
            new
            {
                chequeNumber = $"YENI{Guid.NewGuid():N}"[..10],
                dueDate = DateTime.UtcNow.Date.AddDays(60),
                movementDate = DateTime.UtcNow.Date
            })).StatusCode);

        var second = await client.PostChequeAsync(
            $"/api/cheques/{oldChequeId}/replace", oldChequeId,
            new
            {
                chequeNumber = $"BASKA{Guid.NewGuid():N}"[..10],
                dueDate = DateTime.UtcNow.Date.AddDays(90),
                movementDate = DateTime.UtcNow.Date
            });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    /// <summary>
    /// "Ertelendi" düz durum değişikliğiyle seçilemez: yerine geçen çek
    /// açılmadan bu duruma geçilirse borç ortadan kaybolur ve nakit
    /// akışında hiçbir yerde görünmez.
    /// </summary>
    [Fact]
    public async Task ChangeStatus_DirectlyToReplaced_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var chequeId = await CreateChequeAsync(client, context, ChequeDirection.Issued);

        var response = await client.PostChequeAsync(
            $"/api/cheques/{chequeId}/status", chequeId,
            new
            {
                toStatus = (int)ChequeStatus.Replaced,
                movementDate = DateTime.UtcNow.Date,
                cashAccountId = (Guid?)null,
                description = "Ertelendi"
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("erteleme işlemini kullanın",
            await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Tahsil edilmiş çek ertelenemez: kapanmış bir işlem geri açılamaz.
    /// </summary>
    [Fact]
    public async Task Replace_CollectedCheque_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var chequeId = await CreateChequeAsync(client, context, ChequeDirection.Received);

        Guid cashAccountId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var bankAccountingId = await db.AccountingAccounts
                .Where(x => x.CompanyId == context.CompanyId && x.Code == "101.01")
                .Select(x => x.Id)
                .SingleAsync();

            var bank = new CashAccount
            {
                CompanyId = context.CompanyId,
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
            cashAccountId = bank.Id;
        }

        Assert.Equal(HttpStatusCode.OK, (await client.PostChequeAsync(
            $"/api/cheques/{chequeId}/status", chequeId,
            new
            {
                toStatus = (int)ChequeStatus.Collected,
                movementDate = DateTime.UtcNow.Date,
                cashAccountId,
                description = "Tahsil edildi"
            })).StatusCode);

        var replace = await client.PostChequeAsync(
            $"/api/cheques/{chequeId}/replace", chequeId,
            new
            {
                chequeNumber = $"YENI{Guid.NewGuid():N}"[..10],
                dueDate = DateTime.UtcNow.Date.AddDays(60),
                movementDate = DateTime.UtcNow.Date
            });

        Assert.Equal(HttpStatusCode.Conflict, replace.StatusCode);
        Assert.Contains("ertelenemez", await replace.Content.ReadAsStringAsync());
    }
}
