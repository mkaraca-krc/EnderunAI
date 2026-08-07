using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Taşeron hakedişi ve taşeron ekibi (SGK bizde).
///
/// Asıl güvenceler:
/// - Kesinti KALEMLERİ sözleşmenin kapsam tiklerinden gelir; kapalı bir
///   tik hakedişte kalem üretmez.
/// - Hesap MUTABAKAT rakamıyla yapılır; sahadan gelen öneri toplamı
///   etkilemez.
/// - Onaylanmış hakediş kilitlidir.
/// </summary>
[Collection("Integration")]
public sealed class SubcontractorProgressPaymentTests(DatabaseFixture fixture)
{
    private sealed record Fixture(
        Guid CompanyId,
        Guid ProjectId,
        Guid AccountId,
        Guid SectionId,
        Guid PersonnelId);

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

        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, suffix);

        await db.SaveChangesAsync();

        return new Fixture(
            project.CompanyId, project.Id, account.Id, section.Id, personnel.Id);
    }

    private async Task<HttpClient> CreateClientForRoleAsync(string roleName)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        const string password = "SubHakedis!2026";
        var username = $"test-subhk-{Guid.NewGuid():N}"[..40];
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

    private static object ContractRequest(
        Fixture data,
        int contractType = (int)ProjectContractType.UnitPrice,
        decimal retentionRate = 5m,
        int meal = (int)SubcontractorResponsibility.Subcontractor,
        int accommodation = (int)SubcontractorResponsibility.Subcontractor,
        int socialSecurity = (int)SubcontractorResponsibility.Subcontractor,
        int material = (int)SubcontractorResponsibility.Subcontractor,
        int ohs = (int)SubcontractorResponsibility.Subcontractor,
        object[]? sections = null) =>
        new
        {
            companyId = data.CompanyId,
            currentAccountId = data.AccountId,
            projectId = data.ProjectId,
            projectSiteId = (Guid?)null,
            contractNumber = $"TS-{Guid.NewGuid():N}"[..12],
            workDescription = "Kaba elektrik tesisatı",
            contractType,
            contractAmount = 500_000m,
            currencyCode = "TRY",
            startDate = "2026-01-01",
            endDate = "2026-12-31",
            retentionRate,
            withholdingNumerator = 4,
            withholdingDenominator = 10,
            mealResponsibility = meal,
            accommodationResponsibility = accommodation,
            socialSecurityResponsibility = socialSecurity,
            materialResponsibility = material,
            ohsResponsibility = ohs,
            notes = (string?)null,
            sections = sections ?? []
        };

    private static async Task<Guid> CreateContractAsync(
        HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync(
            "/api/subcontractor-contracts", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreatePaymentAsync(
        HttpClient client, Guid contractId)
    {
        var response = await client.PostAsJsonAsync(
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

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();
    }

    // ---------- Kesinti kalemleri sözleşmeden gelir ----------

    /// <summary>
    /// Kapsam tiklerinin hepsi taşerondaysa yalnızca teminat kalemi
    /// açılır: bizim yapmadığımız masraf hakedişte hiç görünmemeli.
    /// </summary>
    [Fact]
    public async Task Create_OpensOnlyRetentionWhenEverythingIsOnSubcontractor()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var contractId = await CreateContractAsync(client, ContractRequest(data));
        var paymentId = await CreatePaymentAsync(client, contractId);

        var payload = await (await client
                .GetAsync($"/api/subcontractor-progress-payments/{paymentId}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var deduction = Assert.Single(payload.GetProperty("deductions").EnumerateArray());
        Assert.Equal(
            (int)HakedisDeductionType.PerformanceBond,
            deduction.GetProperty("deductionType").GetInt32());
    }

    /// <summary>
    /// Tikler bizdeyse her biri için kesinti kalemi açılır — kullanıcı
    /// listeyi elle kurmaz.
    /// </summary>
    [Fact]
    public async Task Create_OpensADeductionForEveryScopeThatIsOnUs()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var contractId = await CreateContractAsync(client, ContractRequest(
            data,
            meal: (int)SubcontractorResponsibility.Us,
            accommodation: (int)SubcontractorResponsibility.Us,
            socialSecurity: (int)SubcontractorResponsibility.Us,
            material: (int)SubcontractorResponsibility.Us,
            ohs: (int)SubcontractorResponsibility.Us));

        var paymentId = await CreatePaymentAsync(client, contractId);

        var payload = await (await client
                .GetAsync($"/api/subcontractor-progress-payments/{paymentId}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var types = payload.GetProperty("deductions").EnumerateArray()
            .Select(x => x.GetProperty("deductionType").GetInt32())
            .ToHashSet();

        Assert.Contains((int)HakedisDeductionType.PerformanceBond, types);
        Assert.Contains((int)HakedisDeductionType.Meal, types);
        Assert.Contains((int)HakedisDeductionType.Accommodation, types);
        Assert.Contains((int)HakedisDeductionType.MaterialDeduction, types);
        Assert.Contains((int)HakedisDeductionType.OhsContribution, types);
        // SGK/işçilik kalemi serbest türde açılıyor.
        Assert.Contains((int)HakedisDeductionType.Other, types);
    }

    /// <summary>
    /// Öneri üretilemediğinde tutar sıfır kalır ama SEBEBİ yazılır:
    /// "hesaplanamadı" demek yetmez, kullanıcı eksik olanı görmeden
    /// düzeltemez.
    /// </summary>
    [Fact]
    public async Task Create_ExplainsWhyASuggestionCouldNotBeProduced()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var contractId = await CreateContractAsync(client, ContractRequest(
            data, ohs: (int)SubcontractorResponsibility.Us));

        var paymentId = await CreatePaymentAsync(client, contractId);

        var payload = await (await client
                .GetAsync($"/api/subcontractor-progress-payments/{paymentId}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var ohs = payload.GetProperty("deductions").EnumerateArray()
            .Single(x => x.GetProperty("deductionType").GetInt32() ==
                         (int)HakedisDeductionType.OhsContribution);

        Assert.Equal(0m, ohs.GetProperty("amount").GetDecimal());
        Assert.False(string.IsNullOrWhiteSpace(
            ohs.GetProperty("suggestionBasis").GetString()));
        Assert.Contains("İSG", ohs.GetProperty("suggestionBasis").GetString());
    }

    // ---------- Hesap mutabakatla yapılır ----------

    [Fact]
    public async Task Update_CalculatesFromAgreedQuantityNotSuggested()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var contractId = await CreateContractAsync(
            client, ContractRequest(data, retentionRate: 0m));
        var paymentId = await CreatePaymentAsync(client, contractId);

        var response = await client.PutAsJsonAsync(
            $"/api/subcontractor-progress-payments/{paymentId}",
            new
            {
                items = new[]
                {
                    new
                    {
                        id = (Guid?)null,
                        projectHakedisSectionId = data.SectionId,
                        projectBoqItemId = (Guid?)null,
                        positionCode = "EL-001",
                        description = "NYA kablo çekimi",
                        unit = "m",
                        contractQuantity = 1_000m,
                        // Saha 120 diyor, mutabakat 100.
                        suggestedQuantity = 120m,
                        agreedQuantity = 100m,
                        unitPrice = 250m,
                        notes = (string?)null
                    }
                },
                sections = Array.Empty<object>(),
                deductions = Array.Empty<object>(),
                notes = (string?)null
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        // 100 × 250 = 25.000 — 120 × 250 = 30.000 DEĞİL.
        Assert.Equal(25_000m, payload.GetProperty("currentAmount").GetDecimal());
    }

    /// <summary>
    /// İkinci dönemde yalnızca fark ödenir; "önceki" değeri kullanıcıdan
    /// değil kayıttan okunur.
    /// </summary>
    [Fact]
    public async Task SecondPeriod_PaysOnlyTheDifference()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var contractId = await CreateContractAsync(
            client, ContractRequest(data, retentionRate: 0m));

        var first = await CreatePaymentAsync(client, contractId);

        await client.PutAsJsonAsync(
            $"/api/subcontractor-progress-payments/{first}",
            BuildItemPayload(data.SectionId, agreed: 100m));

        var approve = await client.PostAsync(
            $"/api/subcontractor-progress-payments/{first}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var second = await CreatePaymentAsync(client, contractId);

        var response = await client.PutAsJsonAsync(
            $"/api/subcontractor-progress-payments/{second}",
            BuildItemPayload(data.SectionId, agreed: 260m));

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Kümülatif 260, önceki 100 → bu dönem 160 × 250 = 40.000.
        Assert.Equal(40_000m, payload.GetProperty("currentAmount").GetDecimal());
    }

    private static object BuildItemPayload(Guid sectionId, decimal agreed) =>
        new
        {
            items = new[]
            {
                new
                {
                    id = (Guid?)null,
                    projectHakedisSectionId = sectionId,
                    projectBoqItemId = (Guid?)null,
                    positionCode = "EL-001",
                    description = "NYA kablo çekimi",
                    unit = "m",
                    contractQuantity = 1_000m,
                    suggestedQuantity = agreed,
                    agreedQuantity = agreed,
                    unitPrice = 250m,
                    notes = (string?)null
                }
            },
            sections = Array.Empty<object>(),
            deductions = Array.Empty<object>(),
            notes = (string?)null
        };

    // ---------- Götürü ----------

    [Fact]
    public async Task LumpSum_CopiesContractSectionsAndPaysWeightedProgress()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var contractId = await CreateContractAsync(client, ContractRequest(
            data,
            contractType: (int)ProjectContractType.LumpSum,
            retentionRate: 0m,
            sections:
            [
                new
                {
                    projectHakedisSectionId = data.SectionId,
                    sectionAmount = 400_000m,
                    order = 1
                }
            ]));

        var paymentId = await CreatePaymentAsync(client, contractId);

        var response = await client.PutAsJsonAsync(
            $"/api/subcontractor-progress-payments/{paymentId}",
            new
            {
                items = Array.Empty<object>(),
                sections = new[]
                {
                    new
                    {
                        projectHakedisSectionId = data.SectionId,
                        sectionAmount = 400_000m,
                        suggestedProgressRate = 35m,
                        agreedProgressRate = 30m,
                        notes = (string?)null
                    }
                },
                deductions = Array.Empty<object>(),
                notes = (string?)null
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        // 400.000 × %30 = 120.000 (öneri %35 değil).
        Assert.Equal(120_000m, payload.GetProperty("currentAmount").GetDecimal());
    }

    /// <summary>
    /// İlerleme geriye alınamaz: önceki dönemde kabul edilmiş yüzdenin
    /// altına inmek, ödenmiş işi geri istemektir ve mutabakat konusudur.
    /// </summary>
    [Fact]
    public async Task LumpSum_RejectsProgressBelowPreviousPeriod()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var contractId = await CreateContractAsync(client, ContractRequest(
            data,
            contractType: (int)ProjectContractType.LumpSum,
            retentionRate: 0m,
            sections:
            [
                new
                {
                    projectHakedisSectionId = data.SectionId,
                    sectionAmount = 400_000m,
                    order = 1
                }
            ]));

        var first = await CreatePaymentAsync(client, contractId);

        await client.PutAsJsonAsync(
            $"/api/subcontractor-progress-payments/{first}",
            BuildSectionPayload(data.SectionId, agreed: 60m));

        await client.PostAsync(
            $"/api/subcontractor-progress-payments/{first}/approve", null);

        var second = await CreatePaymentAsync(client, contractId);

        var response = await client.PutAsJsonAsync(
            $"/api/subcontractor-progress-payments/{second}",
            BuildSectionPayload(data.SectionId, agreed: 45m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("geriye alınamaz",
            payload.GetProperty("message").GetString());
    }

    private static object BuildSectionPayload(Guid sectionId, decimal agreed) =>
        new
        {
            items = Array.Empty<object>(),
            sections = new[]
            {
                new
                {
                    projectHakedisSectionId = sectionId,
                    sectionAmount = 400_000m,
                    suggestedProgressRate = agreed,
                    agreedProgressRate = agreed,
                    notes = (string?)null
                }
            },
            deductions = Array.Empty<object>(),
            notes = (string?)null
        };

    // ---------- Kilit ----------

    [Fact]
    public async Task Update_RejectsApprovedProgressPayment()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var contractId = await CreateContractAsync(
            client, ContractRequest(data, retentionRate: 0m));
        var paymentId = await CreatePaymentAsync(client, contractId);

        await client.PostAsync(
            $"/api/subcontractor-progress-payments/{paymentId}/approve", null);

        var response = await client.PutAsJsonAsync(
            $"/api/subcontractor-progress-payments/{paymentId}",
            BuildItemPayload(data.SectionId, agreed: 50m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Teknik Koordinatör hakediş hazırlar ama ONAYLAYAMAZ — ödemeyi
    /// tetikleyen adım finansta kalıyor.
    /// </summary>
    [Fact]
    public async Task TechnicalCoordinator_CannotApprove()
    {
        var data = await CreateFixtureAsync();
        var manager = await CreateClientForRoleAsync("Genel Müdür");
        var coordinator = await CreateClientForRoleAsync("Teknik Koordinatör");

        var contractId = await CreateContractAsync(manager, ContractRequest(data));
        var paymentId = await CreatePaymentAsync(manager, contractId);

        var update = await coordinator.PutAsJsonAsync(
            $"/api/subcontractor-progress-payments/{paymentId}",
            BuildItemPayload(data.SectionId, agreed: 40m));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var approve = await coordinator.PostAsync(
            $"/api/subcontractor-progress-payments/{paymentId}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);
    }

    [Theory]
    [InlineData("Şantiye Şefi")]
    [InlineData("Formen")]
    [InlineData("Teknik Ofis")]
    public async Task RolesWithoutSubcontractorPermission_CannotList(string roleName)
    {
        var client = await CreateClientForRoleAsync(roleName);

        var response = await client.GetAsync("/api/subcontractor-progress-payments");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- Taşeron ekibi (T3) ----------

    /// <summary>
    /// SGK taşerondaysa ekip bağlanamaz: o işçiler bizim bordromuzda
    /// değil, bağ kurmak hakedişte olmayan bir kesinti üretirdi.
    /// </summary>
    [Fact]
    public async Task Team_RejectedWhenSocialSecurityIsOnSubcontractor()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var contractId = await CreateContractAsync(client, ContractRequest(data));

        var response = await client.PutAsJsonAsync(
            $"/api/subcontractor-contracts/{contractId}/team",
            new { personnelIds = new[] { data.PersonnelId } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("SGK", payload.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Team_StoresMembersWhenSocialSecurityIsOnUs()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var contractId = await CreateContractAsync(client, ContractRequest(
            data, socialSecurity: (int)SubcontractorResponsibility.Us));

        var response = await client.PutAsJsonAsync(
            $"/api/subcontractor-contracts/{contractId}/team",
            new { personnelIds = new[] { data.PersonnelId } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await (await client
                .GetAsync($"/api/subcontractor-contracts/{contractId}/team"))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(payload.GetProperty("socialSecurityWithUs").GetBoolean());
        var member = Assert.Single(payload.GetProperty("members").EnumerateArray());
        Assert.Equal(data.PersonnelId, member.GetProperty("id").GetGuid());
    }

    /// <summary>
    /// Ekip tam liste olarak gönderilir: boş liste gönderildiğinde
    /// mevcut üyelerin bağı kopar.
    /// </summary>
    [Fact]
    public async Task Team_EmptyListClearsMembership()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var contractId = await CreateContractAsync(client, ContractRequest(
            data, socialSecurity: (int)SubcontractorResponsibility.Us));

        await client.PutAsJsonAsync(
            $"/api/subcontractor-contracts/{contractId}/team",
            new { personnelIds = new[] { data.PersonnelId } });

        var cleared = await client.PutAsJsonAsync(
            $"/api/subcontractor-contracts/{contractId}/team",
            new { personnelIds = Array.Empty<Guid>() });

        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);

        var payload = await (await client
                .GetAsync($"/api/subcontractor-contracts/{contractId}/team"))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Empty(payload.GetProperty("members").EnumerateArray());
    }

    /// <summary>
    /// Bir personel aynı anda iki taşeron ekibinde olamaz: bordro
    /// maliyeti iki sözleşmeden birden kesilirdi.
    /// </summary>
    [Fact]
    public async Task Team_RejectsPersonnelAlreadyInAnotherTeam()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var firstContract = await CreateContractAsync(client, ContractRequest(
            data, socialSecurity: (int)SubcontractorResponsibility.Us));
        var secondContract = await CreateContractAsync(client, ContractRequest(
            data, socialSecurity: (int)SubcontractorResponsibility.Us));

        await client.PutAsJsonAsync(
            $"/api/subcontractor-contracts/{firstContract}/team",
            new { personnelIds = new[] { data.PersonnelId } });

        var response = await client.PutAsJsonAsync(
            $"/api/subcontractor-contracts/{secondContract}/team",
            new { personnelIds = new[] { data.PersonnelId } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("başka bir taşeron ekibinde",
            payload.GetProperty("message").GetString());
    }
}
