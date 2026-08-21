using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Accounting;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Çek düzeltme, durum geri alma ve iptal.
///
/// Yanlış işaretlenen "Ödendi" gerçek bir olay ve bugüne kadar tek
/// çare çeki silmekti — banka hareketi ve muhasebe fişi ortada
/// kalıyordu. Geri alma SİLMEZ: fişi ters kayıtla kapatır, banka
/// hareketini karşıt bir hareketle dengeler, çeki önceki durumuna
/// döndürür ve izini bırakır.
/// </summary>
[Collection("Integration")]
public sealed class ChequeReversalTests(DatabaseFixture fixture)
{
    private sealed record Context(
        Guid CompanyId, Guid ProjectId, Guid SupplierId, Guid BankAccountId);

    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        foreach (var (code, name) in new[]
        {
            ("102", "Bankalar"), ("103", "Verilen Çekler"),
            ("320", "Satıcılar"), ("101", "Alınan Çekler"),
            ("101.01", "Portföy"), ("101.02", "Tahsildeki Çekler"),
            ("120", "Alıcılar"), ("780.01.01", "Finansman Giderleri")
        })
        {
            db.AccountingAccounts.Add(new AccountingAccount
            {
                CompanyId = project.CompanyId,
                Code = code,
                Name = name,
                Nature = AccountingAccountNature.Debit,
                Level = code.Length > 3 ? 5 : 1,
                IsPostingAllowed = true
            });
        }

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

        return new Context(
            project.CompanyId, project.Id, supplier.Id, bank.Id);
    }

    private Task<HttpClient> ClientAsync() =>
        AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    /// <summary>Verilen çek açar ve "Ödendi" işaretler.</summary>
    private async Task<Guid> CreatePaidChequeAsync(
        HttpClient client, Context context, decimal amount = 100_000m)
    {
        var created = await client.PostAsJsonAsync("/api/cheques", new
        {
            companyId = context.CompanyId,
            direction = (int)ChequeDirection.Issued,
            chequeNumber = $"CK{Guid.NewGuid():N}"[..10],
            bankName = "Test Bankası",
            bankBranch = "Merkez",
            drawer = "Test",
            currentAccountId = context.SupplierId,
            projectId = context.ProjectId,
            amount,
            currencyCode = "TRY",
            issueDate = DateTime.UtcNow.Date,
            dueDate = DateTime.UtcNow.Date.AddDays(30),
            progressPaymentId = (Guid?)null,
            supplierInvoiceId = (Guid?)null,
            description = "Geri alma testi"
        });

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var chequeId = JsonDocument
            .Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();

        var paid = await client.PostChequeAsync($"/api/cheques/{chequeId}/status", chequeId, new
        {
            toStatus = (int)ChequeStatus.Paid,
            movementDate = DateTime.UtcNow.Date,
            cashAccountId = context.BankAccountId,
            description = "Ödendi"
        });

        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);

        return chequeId;
    }

    /// <summary>Banka bakiyesi: açılış + girişler − çıkışlar.</summary>
    private async Task<decimal> BalanceAsync(Context context)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var opening = await db.CashAccounts.AsNoTracking()
            .Where(x => x.Id == context.BankAccountId)
            .Select(x => x.OpeningBalance)
            .SingleAsync();

        var rows = await db.CashTransactions.AsNoTracking()
            .Where(x => x.CashAccountId == context.BankAccountId)
            .Select(x => new { x.Direction, x.Amount })
            .ToListAsync();

        return opening
            + rows.Where(x => x.Direction == CashTransactionDirection.In)
                .Sum(x => x.Amount)
            - rows.Where(x => x.Direction == CashTransactionDirection.Out)
                .Sum(x => x.Amount);
    }

    private async Task<Cheque> LoadChequeAsync(Guid id)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Cheques.AsNoTracking().SingleAsync(x => x.Id == id);
    }


    /// <summary>
    /// Yalnız verilen izinlere sahip kullanıcı. Kendi rolünü açıyor;
    /// seed'li roller değiştirilmiyor.
    /// </summary>
    private async Task<HttpClient> ClientWithAsync(string[] permissionKeys)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "TestCek!2026";
        string username;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();

            var role = new AppRole { Name = $"TestCek-{suffix}" };
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

            username = $"cek-{suffix}";
            var hash = passwords.Hash(password);

            var user = new AppUser
            {
                Username = username,
                FullName = "Çek Test Kullanıcısı",
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
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    // ---------------- Durum geri alma ----------------

    /// <summary>
    /// ANA TEST: yanlış "Ödendi" geri alınıyor — çek "Verildi"ye
    /// dönüyor, banka bakiyesi ödeme öncesine geliyor ve iz kalıyor.
    /// </summary>
    [Fact]
    public async Task PaidCheque_CanBeReversedBackToIssued()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var chequeId = await CreatePaidChequeAsync(client, context);

        // Ödeme bakiyeyi düşürdü.
        Assert.Equal(-100_000m, await BalanceAsync(context));

        var response = await client.PostChequeAsync(
            $"/api/cheques/{chequeId}/durum-geri-al", chequeId,
            new { reason = "Banka dekontu başka çeke aitmiş" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cheque = await LoadChequeAsync(chequeId);

        Assert.Equal(ChequeStatus.Issued, cheque.Status);

        // Bakiye ödeme öncesine döndü: karşıt hareket dengeledi.
        Assert.Equal(0m, await BalanceAsync(context));
    }

    /// <summary>
    /// Geri alma İZ BIRAKIYOR: geri alınan hareket damgalanıyor (kim,
    /// ne zaman, neden) ve ters kaydına bağlanıyor; ayrıca yeni bir
    /// hareket satırı yazılıyor.
    /// </summary>
    [Fact]
    public async Task Reversal_LeavesAnAuditTrail()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var chequeId = await CreatePaidChequeAsync(client, context);

        await client.PostChequeAsync(
            $"/api/cheques/{chequeId}/durum-geri-al", chequeId,
            new { reason = "Yanlış işaretlendi" });

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var movements = await db.ChequeMovements.AsNoTracking()
            .Where(x => x.ChequeId == chequeId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync();

        var paidMovement = movements.Single(x => x.ToStatus == ChequeStatus.Paid);

        Assert.NotNull(paidMovement.ReversedAtUtc);
        Assert.NotNull(paidMovement.ReversedByUserId);
        Assert.Equal("Yanlış işaretlendi", paidMovement.ReversalReason);
        Assert.NotNull(paidMovement.ReversalVoucherId);

        // Geri almanın kendisi de hareket olarak yazıldı.
        Assert.Contains(movements, x =>
            x.FromStatus == ChequeStatus.Paid &&
            x.ToStatus == ChequeStatus.Issued);

        // Özgün fiş SİLİNMEDİ: ikisi de defterde.
        Assert.True(await db.AccountingVouchers.AsNoTracking()
            .AnyAsync(x => x.Id == paidMovement.AccountingVoucherId));
    }

    /// <summary>
    /// MÜKERRER ENGELİ: aynı hareket iki kez geri alınamıyor. İkinci
    /// çağrı bir önceki geri almayı geri almaya kalkardı ve bakiye
    /// yanlış yöne kayardı.
    /// </summary>
    [Fact]
    public async Task Reversal_CannotBeAppliedTwiceToTheSameMovement()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var chequeId = await CreatePaidChequeAsync(client, context);

        await client.PostChequeAsync(
            $"/api/cheques/{chequeId}/durum-geri-al", chequeId, new { reason = "İlk" });

        var balance = await BalanceAsync(context);

        // İkinci geri alma artık ödeme hareketini değil, geri almanın
        // kendisini hedefler; giriş kaydı geri alınamaz.
        var second = await client.PostChequeAsync(
            $"/api/cheques/{chequeId}/durum-geri-al", chequeId, new { reason = "İkinci" });

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // Bakiye değişmedi: geri almanın kendisinde para hareketi yok.
        Assert.Equal(balance, await BalanceAsync(context));
    }

    /// <summary>Gerekçesiz geri alma denetlenemez.</summary>
    [Fact]
    public async Task Reversal_RequiresAReason()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var chequeId = await CreatePaidChequeAsync(client, context);

        var response = await client.PostChequeAsync(
            $"/api/cheques/{chequeId}/durum-geri-al", chequeId, new { reason = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------- İptal ----------------

    /// <summary>
    /// İPTAL BANKA HAREKETİNİ DE GERİ ALIR: ödenmiş bir çek iptal
    /// edilince bakiye çekin hiç girilmemiş haline dönüyor, ortada
    /// sahipsiz banka kaydı kalmıyor.
    /// </summary>
    [Fact]
    public async Task Void_ReversesTheBankMovementInTheSameOperation()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var chequeId = await CreatePaidChequeAsync(client, context);

        Assert.Equal(-100_000m, await BalanceAsync(context));

        var response = await client.PostChequeAsync(
            $"/api/cheques/{chequeId}/iptal", chequeId,
            new
            {
                reason = "Test kaydı, yanlışlıkla girildi",
                rowVersion = await RowVersionAsync(client, chequeId),
                reasonKind = (int)ChequeVoidReason.ReturnedToParty
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cheque = await LoadChequeAsync(chequeId);

        Assert.Equal(ChequeStatus.Voided, cheque.Status);
        Assert.NotNull(cheque.VoidedAtUtc);
        Assert.NotNull(cheque.VoidedByUserId);
        Assert.Equal("Test kaydı, yanlışlıkla girildi", cheque.VoidReason);

        Assert.Equal(0m, await BalanceAsync(context));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // ORPHAN YOK: çekin ürettiği her banka hareketinin karşıtı var.
        var original = await db.CashTransactions.AsNoTracking()
            .Where(x => x.SourceModule == ChequeService.ChequeSourceModule &&
                        x.SourceEntityId == chequeId)
            .Select(x => x.Id)
            .ToListAsync();

        var reversed = await db.CashTransactions.AsNoTracking()
            .Where(x => x.SourceModule == ChequeService.ChequeReversalSourceModule &&
                        original.Contains(x.SourceEntityId!.Value))
            .CountAsync();

        Assert.Equal(original.Count, reversed);

        // Çek SİLİNMEDİ: mali geçmiş defterde duruyor.
        Assert.True(await db.Cheques.AsNoTracking().AnyAsync(x => x.Id == chequeId));
    }

    /// <summary>İkinci iptal reddediliyor; bakiye ikinci kez oynamıyor.</summary>
    [Fact]
    public async Task Void_IsRejectedTheSecondTime()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var chequeId = await CreatePaidChequeAsync(client, context);

        await client.PostChequeAsync(
            $"/api/cheques/{chequeId}/iptal", chequeId, new
            {
                reason = "İlk iptal",
                rowVersion = await RowVersionAsync(client, chequeId),
                // KAPANMIŞ çek: "Yanlış giriş" bu grupta reddediliyor
                // (ödenmiş bir çek yanlış giriş nedeniyle iptal edilmez),
                // gerçek bir neden seçiliyor.
                reasonKind = (int)ChequeVoidReason.ReturnedToParty
            });

        var balance = await BalanceAsync(context);

        var second = await client.PostChequeAsync(
            $"/api/cheques/{chequeId}/iptal", chequeId, new
            {
                reason = "İkinci iptal",
                rowVersion = await RowVersionAsync(client, chequeId),
                // KAPANMIŞ çek: "Yanlış giriş" bu grupta reddediliyor
                // (ödenmiş bir çek yanlış giriş nedeniyle iptal edilmez),
                // gerçek bir neden seçiliyor.
                reasonKind = (int)ChequeVoidReason.ReturnedToParty
            });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(balance, await BalanceAsync(context));
    }

    // ---------------- Düzeltme ----------------

    /// <summary>
    /// Açık durumdaki çekin TUTARI düzeltilebiliyor; giriş fişi ters
    /// kayıtla kapanıp yenisi kesiliyor.
    /// </summary>
    [Fact]
    public async Task OpenCheque_AmountCanBeCorrected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var chequeId = await CreatePaidChequeAsync(client, context);

        await client.PostChequeAsync(
            $"/api/cheques/{chequeId}/durum-geri-al", chequeId,
            new { reason = "Tutar düzeltilecek" });

        var before = await LoadChequeAsync(chequeId);

        var response = await client.PutAsJsonAsync($"/api/cheques/{chequeId}", new
        {
            chequeNumber = before.ChequeNumber,
            bankName = before.BankName,
            bankBranch = before.BankBranch,
            drawer = before.Drawer,
            currentAccountId = before.CurrentAccountId,
            projectId = before.ProjectId,
            amount = 75_000m,
            issueDate = before.IssueDate,
            dueDate = before.DueDate,
            progressPaymentId = (Guid?)null,
            supplierInvoiceId = (Guid?)null,
            description = "Tutar düzeltildi",
            costCenterCode = (string?)null,
            // Damga artık ZORUNLU: eşzamanlı değişiklik koruması
            // opsiyonel olsaydı atlatmak için alanı göndermemek yeterdi.
            rowVersion = await RowVersionAsync(client, chequeId)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await LoadChequeAsync(chequeId);

        Assert.Equal(75_000m, after.Amount);
        Assert.Equal(75_000m, after.AmountTry);
    }

    /// <summary>
    /// ÖDENMİŞ ÇEKTE TUTAR DEĞİŞMEZ: önce durumu geri almak gerekiyor.
    /// Aksi halde ödenen tutarla çekin tutarı ayrışırdı.
    /// </summary>
    [Fact]
    public async Task PaidCheque_AmountCannotBeChanged()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var chequeId = await CreatePaidChequeAsync(client, context);
        var cheque = await LoadChequeAsync(chequeId);

        var response = await client.PutAsJsonAsync($"/api/cheques/{chequeId}", new
        {
            chequeNumber = cheque.ChequeNumber,
            bankName = cheque.BankName,
            bankBranch = cheque.BankBranch,
            drawer = cheque.Drawer,
            currentAccountId = cheque.CurrentAccountId,
            projectId = cheque.ProjectId,
            amount = 1m,
            issueDate = cheque.IssueDate,
            dueDate = cheque.DueDate,
            progressPaymentId = (Guid?)null,
            supplierInvoiceId = (Guid?)null,
            description = cheque.Description,
            costCenterCode = (string?)null
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(100_000m, (await LoadChequeAsync(chequeId)).Amount);
    }

    // ---------------- Yetki ----------------

    /// <summary>
    /// NEGATİF TEST: finans onay yetkisi olmayan kullanıcı geri alma ve
    /// iptal yapamıyor. İkisi de banka bakiyesini ve defteri
    /// değiştiriyor; düzenleme yetkisi bunun için yeterli değil.
    /// </summary>
    [Fact]
    public async Task WithoutFinanceApprove_ReversalAndVoidAreForbidden()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var chequeId = await CreatePaidChequeAsync(client, context);

        var limited = await ClientWithAsync(
            [PermissionCatalog.Keys.FinanceView, PermissionCatalog.Keys.FinanceEdit]);

        Assert.Equal(HttpStatusCode.Forbidden, (await limited.PostChequeAsync(
            $"/api/cheques/{chequeId}/durum-geri-al", chequeId,
            new { reason = "Yetkisiz deneme" })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await limited.PostChequeAsync(
            $"/api/cheques/{chequeId}/iptal", chequeId,
            new { reason = "Yetkisiz deneme" })).StatusCode);

        // Çek hâlâ ödenmiş: yetkisiz istek hiçbir şeyi değiştirmedi.
        Assert.Equal(ChequeStatus.Paid, (await LoadChequeAsync(chequeId)).Status);
        Assert.Equal(-100_000m, await BalanceAsync(context));
    }

    // ---------------- Toplamlar ----------------

    /// <summary>
    /// İPTAL EDİLEN ÇEK ÖZET KARTLARINA GİRMEZ: "verilen açık" toplamı
    /// ve adedi iptal tutarı kadar düşüyor. Girseydi ödenmeyecek bir
    /// borç açık görünmeye devam ederdi.
    /// </summary>
    [Fact]
    public async Task VoidedCheque_IsExcludedFromSummary()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        // Üç çek: ikisi açık kalacak, biri iptal edilecek.
        var first = await CreateIssuedChequeAsync(client, context, 40_000m);
        await CreateIssuedChequeAsync(client, context, 60_000m);
        var voided = await CreateIssuedChequeAsync(client, context, 25_000m);

        var before = await SummaryAsync(client, context);

        Assert.Equal(125_000m, before.GetProperty("issuedOpenAmount").GetDecimal());
        Assert.Equal(3, before.GetProperty("issuedOpenCount").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await client.PostChequeAsync(
            $"/api/cheques/{voided}/iptal", voided,
            new
            {
                reason = "Yanlışlıkla girildi",
                rowVersion = await RowVersionAsync(client, voided),
                reasonKind = (int)ChequeVoidReason.ReturnedToParty
            })).StatusCode);

        var after = await SummaryAsync(client, context);

        // Tutar 25.000 düştü, adet bir azaldı.
        Assert.Equal(100_000m, after.GetProperty("issuedOpenAmount").GetDecimal());
        Assert.Equal(2, after.GetProperty("issuedOpenCount").GetInt32());

        // Diğer çekler etkilenmedi.
        Assert.Equal(ChequeStatus.Issued, (await LoadChequeAsync(first)).Status);
    }

    /// <summary>
    /// İptal edilen kayıt LİSTEDE KALIYOR (denetim izi) ve durum
    /// filtresiyle süzülebiliyor. Listeden düşseydi "bu çek neden
    /// yoktu" sorusu cevapsız kalırdı.
    /// </summary>
    [Fact]
    public async Task VoidedCheque_StaysVisibleAndFilterable()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var voided = await CreateIssuedChequeAsync(client, context, 10_000m);

        await client.PostChequeAsync(
            $"/api/cheques/{voided}/iptal", voided, new
            {
                reason = "Test kaydı",
                rowVersion = await RowVersionAsync(client, voided),
                // KAPANMIŞ çek: "Yanlış giriş" bu grupta reddediliyor
                // (ödenmiş bir çek yanlış giriş nedeniyle iptal edilmez),
                // gerçek bir neden seçiliyor.
                reasonKind = (int)ChequeVoidReason.ReturnedToParty
            });

        var all = await ListAsync(client, context, status: null);

        Assert.Contains(all, x => x.GetProperty("id").GetGuid() == voided);

        var filtered = await ListAsync(
            client, context, status: (int)ChequeStatus.Voided);

        var row = Assert.Single(filtered);

        Assert.Equal(voided, row.GetProperty("id").GetGuid());
        Assert.Equal("İptal edildi", row.GetProperty("statusName").GetString());
    }

    /// <summary>
    /// NAKİT AKIŞI: iptal edilen çek ne giriş ne çıkış sayılıyor.
    /// Verilen çek açık statüsünden çıktığı için çıkış listesinden de
    /// düşüyor.
    /// </summary>
    [Fact]
    public async Task VoidedCheque_LeavesTheCashFlow()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var chequeId = await CreateIssuedChequeAsync(client, context, 80_000m);

        var before = await (await client.GetAsync(
            $"/api/cash-flow?companyId={context.CompanyId}&days=365"))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Contains(
            before.GetProperty("outflows").EnumerateArray(),
            x => x.GetProperty("kind").GetString() == "IssuedCheque");

        await client.PostChequeAsync(
            $"/api/cheques/{chequeId}/iptal", chequeId, new
            {
                reason = "İptal",
                rowVersion = await RowVersionAsync(client, chequeId),
                // KAPANMIŞ çek: "Yanlış giriş" bu grupta reddediliyor
                // (ödenmiş bir çek yanlış giriş nedeniyle iptal edilmez),
                // gerçek bir neden seçiliyor.
                reasonKind = (int)ChequeVoidReason.ReturnedToParty
            });

        var after = await (await client.GetAsync(
            $"/api/cash-flow?companyId={context.CompanyId}&days=365"))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.DoesNotContain(
            after.GetProperty("outflows").EnumerateArray(),
            x => x.GetProperty("kind").GetString() == "IssuedCheque" &&
                 x.GetProperty("amount").GetDecimal() == 80_000m);
    }

    /// <summary>Verilen çek açar; durumu değiştirmeden bırakır.</summary>
    private async Task<Guid> CreateIssuedChequeAsync(
        HttpClient client, Context context, decimal amount)
    {
        var created = await client.PostAsJsonAsync("/api/cheques", new
        {
            companyId = context.CompanyId,
            direction = (int)ChequeDirection.Issued,
            chequeNumber = $"CK{Guid.NewGuid():N}"[..10],
            bankName = "Test Bankası",
            bankBranch = "Merkez",
            drawer = "Test",
            currentAccountId = context.SupplierId,
            projectId = context.ProjectId,
            amount,
            currencyCode = "TRY",
            issueDate = DateTime.UtcNow.Date,
            dueDate = DateTime.UtcNow.Date.AddDays(45),
            progressPaymentId = (Guid?)null,
            supplierInvoiceId = (Guid?)null,
            description = "Toplam testi"
        });

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        return JsonDocument.Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> SummaryAsync(
        HttpClient client, Context context) =>
        await (await client.GetAsync(
            $"/api/cheques/summary?companyId={context.CompanyId}"))
            .Content.ReadFromJsonAsync<JsonElement>();

    /*
     * İPTALLER ARTIK VARSAYILAN OLARAK GİZLİ (çek paketi).
     *
     * Bu testlerin iddiası değişmedi — "iptal edilen çek denetim izi
     * için defterde kalır" hâlâ doğru; yalnızca listede açıkça
     * istenmesi gerekiyor. Test bu yüzden silinmedi, `includeVoided`
     * ile güncellendi.
     */
    private static async Task<List<JsonElement>> ListAsync(
        HttpClient client, Context context, int? status)
    {
        var suffix = status is int value ? $"&status={value}" : "";

        var payload = await (await client.GetAsync(
            $"/api/cheques?companyId={context.CompanyId}" +
            $"&direction={(int)ChequeDirection.Issued}&includeVoided=true{suffix}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        return payload.EnumerateArray().ToList();
    }

    /// <summary>Çekin güncel eşzamanlılık damgası — iptal isteği bunu taşıyor.</summary>
    private static async Task<DateTime> RowVersionAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<JsonElement>($"/api/cheques/{id}"))
            .GetProperty("rowVersion").GetDateTime();
}
