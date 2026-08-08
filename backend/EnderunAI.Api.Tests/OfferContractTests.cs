using System.Net;
using System.Net.Http.Headers;
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
/// Kazanılan tekliften sözleşme, proje ve icmal (T2).
///
/// Asıl güvenceler:
/// - Sözleşme yalnız KAZANILMIŞ teklif için açılır; verilmemiş bir
///   teklif proje doğuramaz.
/// - Yeni projede sözleşme künyesi, şantiye deposu ve icmal TEK
///   İŞLEMde oluşur; icmal aktarılamazsa sahipsiz proje kalmaz.
/// - EK İŞte mevcut projenin sözleşme künyesi KORUNUR: asıl sözleşme
///   no, bedeli ve termini ek işin künyesiyle ezilirse projenin mali
///   geçmişi yalan söyler.
/// - Zincir iki yönlü kurulur: teklif projeyi, proje teklifi,
///   icmal de kaynak teklifi gösterir.
/// </summary>
[Collection("Integration")]
public sealed class OfferContractTests(DatabaseFixture fixture)
{
    private sealed record Context(
        Guid CompanyId, Guid BranchId, Guid AccountId, string Suffix);

    private async Task<Context> CreateContextAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, branch, account) =
            await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        return new Context(company.Id, branch.Id, account.Id, suffix);
    }

    private async Task<HttpClient> ClientAsync() =>
        await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    /// <summary>
    /// Karşı tarafı belli, kazanılmış bir teklif üretir — sözleşme
    /// açmaya hazır.
    /// </summary>
    private async Task<Guid> WonOfferAsync(
        HttpClient client, Context context, decimal listPrice = 500m)
    {
        var created = await client.PostAsJsonAsync("/api/offers", new
        {
            companyId = context.CompanyId,
            title = "Trafo montajı",
            offerDate = new DateTime(2026, 6, 1),
            currency = "TRY",
            exchangeRate = 1m,
            counterpartyCurrentAccountId = context.AccountId,
            counterpartyRole = (int)OfferCounterpartyRole.MainContractor,
            kind = (int)OfferKind.UnitPrice,
            items = new[]
            {
                new
                {
                    description = "Trafo montajı",
                    quantity = 4m,
                    unit = "AD",
                    listPrice,
                    discountRate = 0m,
                    freightRate = 0m,
                    wasteRate = 0m,
                    financeRate = 0m,
                    generalExpenseRate = 0m,
                    profitRate = 0m,
                    materialUnitPrice = listPrice * 0.6m,
                    laborUnitPrice = listPrice * 0.3m,
                    overheadUnitPrice = listPrice * 0.1m
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var offerId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        foreach (var status in new[] { OfferStatus.Submitted, OfferStatus.Won })
        {
            var response = await client.PostAsJsonAsync(
                $"/api/offers/{offerId}/durum",
                new { status = (int)status, lostReason = 0 });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        return offerId;
    }

    private static object ContractPayload(
        Context context,
        string codeSuffix,
        Guid? projectId = null,
        bool transferToBoq = true) => new
        {
            projectId,
            branchId = context.BranchId,
            code = $"PRJ-{codeSuffix}",
            name = "Trafo Montaj İşi",
            contractNumber = $"SZL-{codeSuffix}",
            contractDate = new DateTime(2026, 6, 15),
            contractAmount = (decimal?)null,
            contractType = (int?)null,
            plannedStartDate = new DateTime(2026, 7, 1),
            plannedEndDate = new DateTime(2026, 12, 31),
            cashRetentionRate = 5m,
            vatRate = 20m,
            withholdingTaxRate = 3m,
            materialDeductionRate = 0m,
            progressPaymentPeriod = (int)ProjectProgressPaymentPeriod.Monthly,
            paymentTerms = "Hakediş onayından 30 gün sonra ödeme.",
            city = "Ankara",
            district = "Çankaya",
            address = "OSB 5. Cadde",
            transferToBoq,
            boqName = (string?)null
        };

    // ---------- Yeni proje ----------

    /// <summary>
    /// Kazanılan teklif sözleşme künyesiyle projeyi, şantiye deposunu
    /// ve icmali tek çağrıda üretir.
    /// </summary>
    [Fact]
    public async Task Contract_CreatesProjectWarehouseAndBoq()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();
        var offerId = await WonOfferAsync(client, context);

        var response = await client.PostAsJsonAsync(
            $"/api/offers/{offerId}/sozlesme",
            ContractPayload(context, context.Suffix));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("projectCreated").GetBoolean());
        Assert.NotEqual(
            JsonValueKind.Null, body.GetProperty("warehouseId").ValueKind);
        Assert.Equal(1, body.GetProperty("boqItemCount").GetInt32());

        // 4 adet x 500 = 2.000
        Assert.Equal(2000m, body.GetProperty("boqTotalAmount").GetDecimal());

        var projectId = body.GetProperty("projectId").GetGuid();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await db.Projects.SingleAsync(x => x.Id == projectId);

        Assert.Equal($"SZL-{context.Suffix}", project.ContractNumber);
        Assert.Equal(ProjectStatus.Active, project.Status);
        Assert.Equal(context.AccountId, project.EmployerCurrentAccountId);
        Assert.Equal(5m, project.CashRetentionRate);
        Assert.Equal(
            ProjectProgressPaymentPeriod.Monthly, project.ProgressPaymentPeriod);
        Assert.Contains("30 gün", project.PaymentTerms!);

        // Sözleşme tipi belirtilmedi; teklif tipinden türedi.
        Assert.Equal(ProjectContractType.UnitPrice, project.ContractType);

        // Bedel verilmedi; teklif tutarı esas alındı.
        Assert.Equal(2000m, project.ContractAmount);

        // İcmalle yürüyen proje.
        Assert.True(project.UsesContractSummary);

        // Şantiye deposu açıldı.
        Assert.True(await db.Warehouses.AnyAsync(
            x => x.ProjectId == projectId && x.Type == WarehouseType.Site));
    }

    /// <summary>
    /// Zincir iki yönlü kurulur: proje kaynağı teklifi, teklif de
    /// projeyi gösterir; icmal kaynak teklife bağlanır.
    /// </summary>
    [Fact]
    public async Task Contract_LinksOfferProjectAndBoqBothWays()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();
        var offerId = await WonOfferAsync(client, context);

        var body = await (await client.PostAsJsonAsync(
                $"/api/offers/{offerId}/sozlesme",
                ContractPayload(context, context.Suffix)))
            .Content.ReadFromJsonAsync<JsonElement>();

        var projectId = body.GetProperty("projectId").GetGuid();
        var boqId = body.GetProperty("projectBoqId").GetGuid();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await db.Projects.SingleAsync(x => x.Id == projectId);
        Assert.Equal(offerId, project.SourceOfferId);

        var offer = await db.Offers.SingleAsync(x => x.Id == offerId);
        Assert.Equal(projectId, offer.ProjectId);

        var boq = await db.ProjectBoqs.SingleAsync(x => x.Id == boqId);
        Assert.Equal(offerId, boq.SourceOfferId);
        Assert.Equal(projectId, boq.ProjectId);
    }

    /// <summary>
    /// Fiyat bileşenleri (malzeme/montaj/GG) icmale birebir taşınır;
    /// bu ayrım kaybolursa kâr analizi çöker.
    /// </summary>
    [Fact]
    public async Task Contract_CarriesPriceComponentsIntoBoq()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();
        var offerId = await WonOfferAsync(client, context, listPrice: 1000m);

        var body = await (await client.PostAsJsonAsync(
                $"/api/offers/{offerId}/sozlesme",
                ContractPayload(context, context.Suffix)))
            .Content.ReadFromJsonAsync<JsonElement>();

        var boqId = body.GetProperty("projectBoqId").GetGuid();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var item = await db.ProjectBoqItems.SingleAsync(x => x.ProjectBoqId == boqId);

        Assert.Equal(600m, item.MaterialUnitPrice);
        Assert.Equal(300m, item.LaborUnitPrice);
        Assert.Equal(100m, item.OverheadUnitPrice);
        Assert.Equal(1000m, item.UnitPrice);
    }

    /// <summary>
    /// İcmal aktarımı istenmezse proje açılır ama kullanıcı eksiği
    /// açıkça uyarıyla öğrenir; sessiz eksik hakediş anında patlardı.
    /// </summary>
    [Fact]
    public async Task Contract_WithoutBoq_WarnsAndLeavesProjectOffSummary()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();
        var offerId = await WonOfferAsync(client, context);

        var body = await (await client.PostAsJsonAsync(
                $"/api/offers/{offerId}/sozlesme",
                ContractPayload(context, context.Suffix, transferToBoq: false)))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(JsonValueKind.Null, body.GetProperty("projectBoqId").ValueKind);

        var warnings = body.GetProperty("warnings").EnumerateArray()
            .Select(x => x.GetString()!).ToList();

        Assert.Contains(warnings, x => x.Contains("İcmal aktarılmadı"));

        var projectId = body.GetProperty("projectId").GetGuid();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await db.Projects.SingleAsync(x => x.Id == projectId);
        Assert.False(project.UsesContractSummary);
    }

    // ---------- Ek iş (mevcut proje) ----------

    /// <summary>
    /// Aynı işverende ek iş: mevcut projenin sözleşme künyesi
    /// KORUNUR, yalnız ek icmal açılır.
    /// </summary>
    [Fact]
    public async Task Contract_OnExistingProject_KeepsOriginalContractDetails()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        // Asıl iş
        var firstOffer = await WonOfferAsync(client, context, listPrice: 500m);

        var first = await (await client.PostAsJsonAsync(
                $"/api/offers/{firstOffer}/sozlesme",
                ContractPayload(context, context.Suffix)))
            .Content.ReadFromJsonAsync<JsonElement>();

        var projectId = first.GetProperty("projectId").GetGuid();

        // Ek iş: farklı sözleşme no ve bedelle geliyor ama projeye
        // yazılmamalı.
        var extraOffer = await WonOfferAsync(client, context, listPrice: 900m);

        var second = await client.PostAsJsonAsync(
            $"/api/offers/{extraOffer}/sozlesme",
            ContractPayload(context, $"{context.Suffix}X", projectId: projectId));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var body = await second.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(body.GetProperty("projectCreated").GetBoolean());
        Assert.Equal(projectId, body.GetProperty("projectId").GetGuid());
        Assert.Equal(
            JsonValueKind.Null, body.GetProperty("warehouseId").ValueKind);

        var warnings = body.GetProperty("warnings").EnumerateArray()
            .Select(x => x.GetString()!).ToList();

        Assert.Contains(warnings, x => x.Contains("Ek iş olarak"));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await db.Projects.SingleAsync(x => x.Id == projectId);

        // Asıl künye değişmedi.
        Assert.Equal($"SZL-{context.Suffix}", project.ContractNumber);
        Assert.Equal(2000m, project.ContractAmount);

        // Kaynak teklif asıl teklif olarak kaldı.
        Assert.Equal(firstOffer, project.SourceOfferId);

        // İki ayrı icmal var; ek iş asıl icmalin üzerine yazmadı.
        var boqs = await db.ProjectBoqs
            .Where(x => x.ProjectId == projectId)
            .ToListAsync();

        Assert.Equal(2, boqs.Count);
        Assert.Contains(boqs, x => x.SourceOfferId == firstOffer);
        Assert.Contains(boqs, x => x.SourceOfferId == extraOffer);
    }

    /// <summary>Arşivlenmiş projeye ek iş bağlanamaz.</summary>
    [Fact]
    public async Task Contract_OnArchivedProject_IsRejected()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var firstOffer = await WonOfferAsync(client, context);

        var first = await (await client.PostAsJsonAsync(
                $"/api/offers/{firstOffer}/sozlesme",
                ContractPayload(context, context.Suffix)))
            .Content.ReadFromJsonAsync<JsonElement>();

        var projectId = first.GetProperty("projectId").GetGuid();

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = await db.Projects.SingleAsync(x => x.Id == projectId);
            project.IsArchived = true;
            await db.SaveChangesAsync();
        }

        var extraOffer = await WonOfferAsync(client, context);

        var response = await client.PostAsJsonAsync(
            $"/api/offers/{extraOffer}/sozlesme",
            ContractPayload(context, $"{context.Suffix}Y", projectId: projectId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Reddedilen durumlar ----------

    /// <summary>
    /// Kazanılmamış teklif proje doğuramaz.
    /// </summary>
    [Fact]
    public async Task Contract_RequiresWonOffer()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var created = await client.PostAsJsonAsync("/api/offers", new
        {
            companyId = context.CompanyId,
            title = "Henüz kazanılmadı",
            offerDate = new DateTime(2026, 6, 1),
            currency = "TRY",
            exchangeRate = 1m,
            counterpartyCurrentAccountId = context.AccountId,
            counterpartyRole = (int)OfferCounterpartyRole.Employer,
            kind = (int)OfferKind.LumpSum,
            items = new[]
            {
                new
                {
                    description = "İş",
                    quantity = 1m,
                    unit = "AD",
                    listPrice = 100m,
                    discountRate = 0m,
                    freightRate = 0m,
                    wasteRate = 0m,
                    financeRate = 0m,
                    generalExpenseRate = 0m,
                    profitRate = 0m
                }
            }
        });

        var offerId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/offers/{offerId}/sozlesme",
            ContractPayload(context, context.Suffix));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Reddedildiğinde proje de açılmamalı.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await db.Projects.AnyAsync(
            x => x.Code == $"PRJ-{context.Suffix}"));
    }

    /// <summary>
    /// Aynı teklifin sözleşmesi ikinci kez açılamaz; iki proje doğar
    /// ve hangisinin sözleşme olduğu belirsizleşirdi.
    /// </summary>
    [Fact]
    public async Task Contract_CannotBeCreatedTwiceForSameOffer()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();
        var offerId = await WonOfferAsync(client, context);

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync(
                $"/api/offers/{offerId}/sozlesme",
                ContractPayload(context, context.Suffix))).StatusCode);

        var second = await client.PostAsJsonAsync(
            $"/api/offers/{offerId}/sozlesme",
            ContractPayload(context, $"{context.Suffix}Z"));

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    /// <summary>Yeni proje için şube zorunlu.</summary>
    [Fact]
    public async Task Contract_NewProject_RequiresBranch()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();
        var offerId = await WonOfferAsync(client, context);

        var response = await client.PostAsJsonAsync(
            $"/api/offers/{offerId}/sozlesme",
            new
            {
                branchId = (Guid?)null,
                code = $"PRJ-{context.Suffix}",
                transferToBoq = true
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Termin, işe başlamadan önce olamaz.</summary>
    [Fact]
    public async Task Contract_RejectsEndDateBeforeStart()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();
        var offerId = await WonOfferAsync(client, context);

        var response = await client.PostAsJsonAsync(
            $"/api/offers/{offerId}/sozlesme",
            new
            {
                branchId = context.BranchId,
                code = $"PRJ-{context.Suffix}",
                plannedStartDate = new DateTime(2026, 9, 1),
                plannedEndDate = new DateTime(2026, 8, 1)
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Aynı proje kodu ikinci kez kullanılamaz.</summary>
    [Fact]
    public async Task Contract_RejectsDuplicateProjectCode()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var first = await WonOfferAsync(client, context);
        await client.PostAsJsonAsync(
            $"/api/offers/{first}/sozlesme",
            ContractPayload(context, context.Suffix));

        var second = await WonOfferAsync(client, context);

        var response = await client.PostAsJsonAsync(
            $"/api/offers/{second}/sozlesme",
            ContractPayload(context, context.Suffix));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Yetki ----------

    /// <summary>
    /// Yeni proje açmak için PROJE OLUŞTURMA yetkisi de gerekir.
    /// Finans huniyi yönetebiliyor ama proje açamaz; bu uç üzerinden
    /// proje oluşturma kapısı dolanılamamalı.
    /// </summary>
    [Fact]
    public async Task Contract_NewProject_RequiresProjectCreatePermission()
    {
        var context = await CreateContextAsync();
        var admin = await ClientAsync();
        var offerId = await WonOfferAsync(admin, context);

        var finance = await CreateClientForRoleAsync("Finans Sorumlusu");

        var response = await finance.PostAsJsonAsync(
            $"/api/offers/{offerId}/sozlesme",
            ContractPayload(context, context.Suffix));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "projects.create",
            body.GetProperty("requiredPermission").GetString());

        // Proje açılmadı.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await db.Projects.AnyAsync(
            x => x.Code == $"PRJ-{context.Suffix}"));
    }

    /// <summary>
    /// Teknik Koordinatör hem huniyi yönetiyor hem proje açabiliyor:
    /// kazanılan işi uçtan uca kapatabilmeli.
    /// </summary>
    [Fact]
    public async Task Contract_TechnicalCoordinatorCanCloseTheLoop()
    {
        var context = await CreateContextAsync();
        var client = await CreateClientForRoleAsync("Teknik Koordinatör");

        var offerId = await WonOfferAsync(client, context);

        var response = await client.PostAsJsonAsync(
            $"/api/offers/{offerId}/sozlesme",
            ContractPayload(context, context.Suffix));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Saha rolleri sözleşme açamaz.</summary>
    [Theory]
    [InlineData("Şantiye Şefi")]
    [InlineData("Depo Sorumlusu")]
    public async Task Contract_IsClosedToFieldRoles(string roleName)
    {
        var context = await CreateContextAsync();
        var admin = await ClientAsync();
        var offerId = await WonOfferAsync(admin, context);

        var client = await CreateClientForRoleAsync(roleName);

        var response = await client.PostAsJsonAsync(
            $"/api/offers/{offerId}/sozlesme",
            ContractPayload(context, context.Suffix));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HttpClient> CreateClientForRoleAsync(string roleName)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider
            .GetRequiredService<EnderunAI.Api.Security.PasswordService>();

        const string password = "OfferContract!2026";
        var username = $"test-contract-{Guid.NewGuid():N}"[..40];
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

        await db.SaveChangesAsync();

        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, username, password);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}
