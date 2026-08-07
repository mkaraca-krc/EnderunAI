using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Subcontractors;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Taşeron ödemeleri, avansları ve ELDEN İZOLASYONU.
///
/// Bu paketin en hassas kısmı: elden ödeme resmî muhasebeye hiçbir fiş
/// yazmamalı, proje maliyeti defterine satır açmamalı ve
/// extra_payment.view olmayan hiç kimseye — taşeronu yönetebilenlere
/// bile — görünmemeli.
/// </summary>
[Collection("Integration")]
public sealed class SubcontractorLedgerTests(DatabaseFixture fixture)
{
    private sealed record Fixture(
        Guid CompanyId, Guid ProjectId, Guid AccountId, Guid SectionId);

    private async Task<Fixture> CreateFixtureAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var account = new CurrentAccount
        {
            CompanyId = project.CompanyId,
            Code = $"TAS-{suffix}",
            Title = $"Test Taşeron {suffix}",
            Roles = CurrentAccountRoles.Subcontractor,
            Status = CurrentAccountStatus.Approved
        };
        db.CurrentAccounts.Add(account);

        var section = new ProjectHakedisSection
        {
            ProjectId = project.Id,
            Order = 1,
            Name = "Elektrik İşleri"
        };
        db.ProjectHakedisSections.Add(section);

        await db.SaveChangesAsync();

        return new Fixture(
            project.CompanyId, project.Id, account.Id, section.Id);
    }

    private async Task<HttpClient> CreateClientForRoleAsync(
        string roleName, string? deniedPermissionKey = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        const string password = "SubLedger!2026";
        var username = $"test-subld-{Guid.NewGuid():N}"[..40];
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

    private static async Task<Guid> CreateContractAsync(
        HttpClient client, Fixture data, decimal contractAmount = 500_000m)
    {
        var response = await client.PostAsJsonAsync(
            "/api/subcontractor-contracts",
            new
            {
                companyId = data.CompanyId,
                currentAccountId = data.AccountId,
                projectId = data.ProjectId,
                projectSiteId = (Guid?)null,
                contractNumber = $"TS-{Guid.NewGuid():N}"[..12],
                workDescription = "Kaba elektrik tesisatı",
                contractType = (int)ProjectContractType.UnitPrice,
                contractAmount,
                currencyCode = "TRY",
                startDate = "2026-01-01",
                endDate = "2026-12-31",
                retentionRate = 0m,
                withholdingNumerator = 4,
                withholdingDenominator = 10,
                mealResponsibility = (int)SubcontractorResponsibility.Subcontractor,
                accommodationResponsibility = (int)SubcontractorResponsibility.Subcontractor,
                socialSecurityResponsibility = (int)SubcontractorResponsibility.Subcontractor,
                materialResponsibility = (int)SubcontractorResponsibility.Subcontractor,
                ohsResponsibility = (int)SubcontractorResponsibility.Subcontractor,
                notes = (string?)null,
                sections = new[]
                {
                    new
                    {
                        projectHakedisSectionId = data.SectionId,
                        sectionAmount = contractAmount,
                        order = 1
                    }
                }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();
    }

    // ---------- Faturalı ödeme ----------

    /// <summary>
    /// Tevkifat oranı SÖZLEŞMEDEN geliyor: 4/10 oranında, %20 KDV'li
    /// 100.000 TL ödemede KDV 20.000, tevkifat 8.000, taşerona ödenecek
    /// 112.000.
    /// </summary>
    [Fact]
    public async Task Payment_AppliesWithholdingRateFromContract()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");
        var contractId = await CreateContractAsync(client, data);

        var response = await client.PostAsJsonAsync(
            "/api/subcontractor-ledger",
            new
            {
                subcontractorContractId = contractId,
                subcontractorProgressPaymentId = (Guid?)null,
                kind = (int)SubcontractorLedgerKind.Payment,
                entryDate = "2026-03-31",
                amount = 100_000m,
                vatRate = 20m,
                projectHakedisSectionId = data.SectionId,
                supplierInvoiceId = (Guid?)null,
                description = "Mart hakediş ödemesi"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(8_000m, payload.GetProperty("withholdingAmount").GetDecimal());
        Assert.Equal(112_000m, payload.GetProperty("payableAmount").GetDecimal());
    }

    /// <summary>
    /// Faturalı ödeme proje maliyetine taşeron işçiliği sınıfında
    /// yazılır — kâr analizi bu satırdan besleniyor.
    /// </summary>
    [Fact]
    public async Task Payment_WritesProjectCostAsSubcontractorLabor()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");
        var contractId = await CreateContractAsync(client, data);

        await client.PostAsJsonAsync(
            "/api/subcontractor-ledger",
            BuildPayment(contractId, data.SectionId, 75_000m));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cost = await db.ProjectCostTransactions
            .SingleAsync(x => x.ProjectId == data.ProjectId &&
                              x.ReferenceType == "SubcontractorLedgerEntry");

        Assert.Equal(ProjectCostClass.SubcontractorLabor, cost.CostClass);
        Assert.Equal(75_000m, cost.Amount);
        Assert.Equal(data.SectionId, cost.ProjectHakedisSectionId);
    }

    /// <summary>
    /// Avans maliyet DEĞİLDİR: iş yapılmadan verilen para, hakediş
    /// mahsup edilince zaten maliyetleşir. Avansı da yazmak aynı
    /// işçiliği iki kez saymak olurdu.
    /// </summary>
    [Fact]
    public async Task Advance_DoesNotWriteProjectCost()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");
        var contractId = await CreateContractAsync(client, data);

        await client.PostAsJsonAsync(
            "/api/subcontractor-ledger",
            BuildPayment(
                contractId, data.SectionId, 50_000m,
                kind: (int)SubcontractorLedgerKind.Advance));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var costs = await db.ProjectCostTransactions
            .Where(x => x.ProjectId == data.ProjectId &&
                        x.ReferenceType == "SubcontractorLedgerEntry")
            .ToListAsync();

        Assert.Empty(costs);
    }

    private static object BuildPayment(
        Guid contractId,
        Guid sectionId,
        decimal amount,
        int kind = (int)SubcontractorLedgerKind.Payment) =>
        new
        {
            subcontractorContractId = contractId,
            subcontractorProgressPaymentId = (Guid?)null,
            kind,
            entryDate = "2026-03-31",
            amount,
            vatRate = 20m,
            projectHakedisSectionId = sectionId,
            supplierInvoiceId = (Guid?)null,
            description = (string?)null
        };

    // ---------- Elden izolasyonu ----------

    /// <summary>
    /// Elden ödeme resmî muhasebeye fiş yazmaz ve proje maliyeti
    /// defterine satır açmaz.
    /// </summary>
    [Fact]
    public async Task CashPayment_TouchesNeitherAccountingNorProjectCost()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");
        var contractId = await CreateContractAsync(client, data);

        var response = await client.PostAsJsonAsync(
            "/api/subcontractor-ledger/cash",
            new
            {
                subcontractorContractId = contractId,
                subcontractorProgressPaymentId = (Guid?)null,
                kind = (int)SubcontractorLedgerKind.Payment,
                entryDate = "2026-03-31",
                amount = 40_000m,
                description = "Elden ödeme"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Empty(await db.ProjectCostTransactions
            .Where(x => x.ProjectId == data.ProjectId)
            .ToListAsync());

        Assert.Empty(await db.AccountingVouchers
            .Where(x => x.CompanyId == data.CompanyId)
            .ToListAsync());
    }

    /// <summary>
    /// Taşeronu YÖNETEBİLEN ama ek ödeme izni olmayan rol (Teknik
    /// Koordinatör) elden kaydı ne yazabilir ne okuyabilir.
    /// </summary>
    [Fact]
    public async Task CashEntry_ForbiddenForSubcontractorManagerWithoutExtraPayment()
    {
        var data = await CreateFixtureAsync();
        var manager = await CreateClientForRoleAsync("Genel Müdür");
        var coordinator = await CreateClientForRoleAsync("Teknik Koordinatör");

        var contractId = await CreateContractAsync(manager, data);

        var write = await coordinator.PostAsJsonAsync(
            "/api/subcontractor-ledger/cash",
            new
            {
                subcontractorContractId = contractId,
                subcontractorProgressPaymentId = (Guid?)null,
                kind = (int)SubcontractorLedgerKind.Payment,
                entryDate = "2026-03-31",
                amount = 10_000m,
                description = (string?)null
            });

        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    /// <summary>
    /// Asıl sızma yüzeyi: özet ucu. Ek ödeme izni olmayan kullanıcıya
    /// elden toplamlar NULL gelir, elden hareket listesi hiç dönmez ve
    /// cashHidden ile eksik gördüğünü bilir.
    /// </summary>
    [Fact]
    public async Task Summary_HidesCashTotalsFromUsersWithoutExtraPayment()
    {
        var data = await CreateFixtureAsync();
        var manager = await CreateClientForRoleAsync("Genel Müdür");
        var contractId = await CreateContractAsync(manager, data);

        await manager.PostAsJsonAsync(
            "/api/subcontractor-ledger/cash",
            new
            {
                subcontractorContractId = contractId,
                subcontractorProgressPaymentId = (Guid?)null,
                kind = (int)SubcontractorLedgerKind.Payment,
                entryDate = "2026-03-31",
                amount = 40_000m,
                description = "Elden ödeme"
            });

        var coordinator = await CreateClientForRoleAsync("Teknik Koordinatör");

        var payload = await (await coordinator
                .GetAsync($"/api/subcontractor-ledger/{contractId}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(payload.GetProperty("cashHidden").GetBoolean());
        Assert.Equal(JsonValueKind.Null,
            payload.GetProperty("cashPaymentTotal").ValueKind);
        Assert.Equal(JsonValueKind.Null,
            payload.GetProperty("cashAdvanceTotal").ValueKind);
        Assert.Equal(JsonValueKind.Null,
            payload.GetProperty("cashEntries").ValueKind);
    }

    [Fact]
    public async Task Summary_ShowsCashTotalsToAuthorizedUser()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");
        var contractId = await CreateContractAsync(client, data);

        await client.PostAsJsonAsync(
            "/api/subcontractor-ledger/cash",
            new
            {
                subcontractorContractId = contractId,
                subcontractorProgressPaymentId = (Guid?)null,
                kind = (int)SubcontractorLedgerKind.Payment,
                entryDate = "2026-03-31",
                amount = 40_000m,
                description = "Elden ödeme"
            });

        var payload = await (await client
                .GetAsync($"/api/subcontractor-ledger/{contractId}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(payload.GetProperty("cashHidden").GetBoolean());
        Assert.Equal(40_000m,
            payload.GetProperty("cashPaymentTotal").GetDecimal());
        Assert.Single(payload.GetProperty("cashEntries").EnumerateArray());
    }

    /// <summary>
    /// Maskeleme role değil İZNE bağlı: Genel Müdür'de bile ek ödeme
    /// izni kullanıcı bazında kapatılırsa elden tutarlar gizlenir.
    /// </summary>
    [Fact]
    public async Task Summary_HidesCashWhenPermissionDeniedOnUser()
    {
        var data = await CreateFixtureAsync();
        var manager = await CreateClientForRoleAsync("Genel Müdür");
        var contractId = await CreateContractAsync(manager, data);

        await manager.PostAsJsonAsync(
            "/api/subcontractor-ledger/cash",
            new
            {
                subcontractorContractId = contractId,
                subcontractorProgressPaymentId = (Guid?)null,
                kind = (int)SubcontractorLedgerKind.Payment,
                entryDate = "2026-03-31",
                amount = 40_000m,
                description = (string?)null
            });

        var restricted = await CreateClientForRoleAsync(
            "Genel Müdür",
            deniedPermissionKey: PermissionCatalog.Keys.ExtraPaymentView);

        var payload = await (await restricted
                .GetAsync($"/api/subcontractor-ledger/{contractId}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(payload.GetProperty("cashHidden").GetBoolean());
        Assert.Equal(JsonValueKind.Null,
            payload.GetProperty("cashPaymentTotal").ValueKind);
    }

    [Theory]
    [InlineData("Şantiye Şefi")]
    [InlineData("Formen")]
    [InlineData("Teknik Ofis")]
    public async Task RolesWithoutSubcontractorPermission_CannotReadLedger(
        string roleName)
    {
        var client = await CreateClientForRoleAsync(roleName);

        var response = await client.GetAsync(
            $"/api/subcontractor-ledger/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- Avans ve mahsup (T6) ----------

    [Fact]
    public async Task Advance_ShowsAsOpenBalanceUntilOffset()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");
        var contractId = await CreateContractAsync(client, data);

        await client.PostAsJsonAsync(
            "/api/subcontractor-ledger",
            BuildPayment(
                contractId, data.SectionId, 60_000m,
                kind: (int)SubcontractorLedgerKind.Advance));

        var payload = await (await client
                .GetAsync($"/api/subcontractor-ledger/{contractId}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(60_000m, payload.GetProperty("invoicedAdvanceTotal").GetDecimal());
        Assert.Equal(60_000m, payload.GetProperty("openAdvance").GetDecimal());
        Assert.Equal(0m, payload.GetProperty("offsetTotal").GetDecimal());
    }

    /// <summary>
    /// Açık avans varsa hakedişte mahsup kalemi otomatik açılır — bir
    /// dönem unutulan mahsup, geri alınması zor bir para kaybıdır.
    /// </summary>
    [Fact]
    public async Task ProgressPayment_OpensAdvanceOffsetLineWhenAdvanceIsOpen()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");
        var contractId = await CreateContractAsync(client, data);

        await client.PostAsJsonAsync(
            "/api/subcontractor-ledger",
            BuildPayment(
                contractId, data.SectionId, 60_000m,
                kind: (int)SubcontractorLedgerKind.Advance));

        var created = await client.PostAsJsonAsync(
            "/api/subcontractor-progress-payments",
            new
            {
                subcontractorContractId = contractId,
                progressPaymentNumber = (string?)null,
                periodStartDate = "2026-03-01",
                periodEndDate = "2026-03-31",
                progressPaymentDate = "2026-04-05",
                notes = (string?)null
            });

        var paymentId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var payload = await (await client
                .GetAsync($"/api/subcontractor-progress-payments/{paymentId}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var offset = payload.GetProperty("deductions").EnumerateArray()
            .Single(x => x.GetProperty("deductionType").GetInt32() ==
                         (int)HakedisDeductionType.AdvanceOffset);

        // Türkçe biçim: binlik nokta, ondalık virgül. "60,000.00"
        // yazılsaydı Türkçe okuyan kullanıcı bunu ALTMIŞ diye anlardı.
        Assert.Contains("60.000,00", offset.GetProperty("suggestionBasis").GetString());
    }

    /// <summary>
    /// Açık avans kalan işi aşarsa uyarı çıkar: taşerona kalan işinden
    /// fazlasını ödemişiz demektir ve bu tahsil riski taşır.
    /// </summary>
    [Fact]
    public async Task Summary_WarnsWhenOpenAdvanceExceedsRemainingWork()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");
        var contractId = await CreateContractAsync(client, data, contractAmount: 50_000m);

        await client.PostAsJsonAsync(
            "/api/subcontractor-ledger",
            BuildPayment(
                contractId, data.SectionId, 80_000m,
                kind: (int)SubcontractorLedgerKind.Advance));

        var payload = await (await client
                .GetAsync($"/api/subcontractor-ledger/{contractId}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var warning = payload.GetProperty("overAdvanceWarning").GetString();

        Assert.False(string.IsNullOrWhiteSpace(warning));
        Assert.Contains("Açık avans", warning);
    }

    [Fact]
    public async Task Summary_NoWarningWhenAdvanceIsWithinRemainingWork()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");
        var contractId = await CreateContractAsync(client, data);

        await client.PostAsJsonAsync(
            "/api/subcontractor-ledger",
            BuildPayment(
                contractId, data.SectionId, 60_000m,
                kind: (int)SubcontractorLedgerKind.Advance));

        var payload = await (await client
                .GetAsync($"/api/subcontractor-ledger/{contractId}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(JsonValueKind.Null,
            payload.GetProperty("overAdvanceWarning").ValueKind);
    }

    /// <summary>
    /// Silinen ödemenin proje maliyeti de düşer; kalırsa proje
    /// maliyeti silinmiş bir ödemeyi taşımaya devam ederdi.
    /// </summary>
    [Fact]
    public async Task Delete_AlsoRemovesProjectCost()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");
        var contractId = await CreateContractAsync(client, data);

        var created = await client.PostAsJsonAsync(
            "/api/subcontractor-ledger",
            BuildPayment(contractId, data.SectionId, 30_000m));

        var entryId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var deleted = await client.DeleteAsync($"/api/subcontractor-ledger/{entryId}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Empty(await db.ProjectCostTransactions
            .Where(x => x.ProjectId == data.ProjectId)
            .ToListAsync());
    }
}

/// <summary>
/// Avans mahsup önerisi ve aşırı avans uyarısının saf kuralları.
/// </summary>
public sealed class SubcontractorAdvanceRuleTests
{
    /// <summary>
    /// Açık avans kalan işi aşmıyorsa uyarı üretilmez.
    /// </summary>
    [Fact]
    public void OverAdvanceWarning_SilentWhenAdvanceFitsRemainingWork()
    {
        var warning = SubcontractorLedgerService.BuildOverAdvanceWarning(
            openAdvance: 50_000m,
            contractAmount: 500_000m,
            cumulativeWorkAmount: 200_000m);

        Assert.Null(warning);
    }

    /// <summary>
    /// Sözleşme neredeyse bittiğinde küçük bir avans bile riskli hale
    /// gelir: kalan iş 20.000 iken 30.000 açık avans mahsup edilemez.
    /// </summary>
    [Fact]
    public void OverAdvanceWarning_FiresWhenRemainingWorkIsSmaller()
    {
        var warning = SubcontractorLedgerService.BuildOverAdvanceWarning(
            openAdvance: 30_000m,
            contractAmount: 500_000m,
            cumulativeWorkAmount: 480_000m);

        Assert.NotNull(warning);
        Assert.Contains("30.000,00", warning);
        Assert.Contains("20.000,00", warning);
    }

    [Fact]
    public void OverAdvanceWarning_SilentWhenThereIsNoOpenAdvance()
    {
        var warning = SubcontractorLedgerService.BuildOverAdvanceWarning(
            openAdvance: 0m,
            contractAmount: 100_000m,
            cumulativeWorkAmount: 100_000m);

        Assert.Null(warning);
    }
}
