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
/// Taşeron sözleşmesi.
///
/// Asıl güvence, sözleşmenin BAĞLARINI koruması: taşeron olmayan bir
/// cariye, başka projenin kısmına ya da başka projenin şantiyesine
/// sözleşme bağlanamamalı. Bu bağlar bozulursa maliyet ve hakediş
/// yanlış projeye yazılır ve geriye dönük ayıklamak neredeyse imkânsız
/// olur.
/// </summary>
[Collection("Integration")]
public sealed class SubcontractorContractTests(DatabaseFixture fixture)
{
    private sealed record Fixture(
        Guid CompanyId,
        Guid ProjectId,
        Guid SiteId,
        Guid SubcontractorAccountId,
        Guid SupplierOnlyAccountId,
        Guid SectionId,
        Guid ForeignSectionId,
        Guid ForeignSiteId);

    private async Task<Fixture> CreateFixtureAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var subcontractor = new CurrentAccount
        {
            CompanyId = project.CompanyId,
            Code = $"TAS-{suffix}",
            Title = $"Test Taşeron {suffix}",
            Roles = CurrentAccountRoles.Subcontractor | CurrentAccountRoles.Supplier,
            Status = CurrentAccountStatus.Approved
        };

        var supplierOnly = new CurrentAccount
        {
            CompanyId = project.CompanyId,
            Code = $"TED-{suffix}",
            Title = $"Test Tedarikçi {suffix}",
            Roles = CurrentAccountRoles.Supplier,
            Status = CurrentAccountStatus.Approved
        };

        db.CurrentAccounts.AddRange(subcontractor, supplierOnly);

        var site = new ProjectSite
        {
            ProjectId = project.Id,
            Code = $"SNT-{suffix}",
            Name = $"Test Şantiye {suffix}"
        };
        db.ProjectSites.Add(site);

        var section = new ProjectHakedisSection
        {
            ProjectId = project.Id,
            Order = 1,
            Name = "Elektrik İşleri"
        };
        db.ProjectHakedisSections.Add(section);

        // Başka bir projenin kısmı ve şantiyesi — sızma denemeleri için.
        var foreignProject = await TestDataFactory.CreateProjectAsync(
            db, $"{suffix}b");

        var foreignSection = new ProjectHakedisSection
        {
            ProjectId = foreignProject.Id,
            Order = 1,
            Name = "Başka Projenin Kısmı"
        };
        db.ProjectHakedisSections.Add(foreignSection);

        var foreignSite = new ProjectSite
        {
            ProjectId = foreignProject.Id,
            Code = $"SNT-{suffix}b",
            Name = "Başka Projenin Şantiyesi"
        };
        db.ProjectSites.Add(foreignSite);

        await db.SaveChangesAsync();

        return new Fixture(
            project.CompanyId,
            project.Id,
            site.Id,
            subcontractor.Id,
            supplierOnly.Id,
            section.Id,
            foreignSection.Id,
            foreignSite.Id);
    }

    private async Task<HttpClient> CreateClientForRoleAsync(string roleName)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        const string password = "Subcontractor!2026";
        var username = $"test-sub-{Guid.NewGuid():N}"[..40];
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

    private static object BuildRequest(
        Fixture data,
        Guid? currentAccountId = null,
        int contractType = (int)ProjectContractType.UnitPrice,
        decimal contractAmount = 500_000m,
        Guid? projectSiteId = null,
        object[]? sections = null,
        int withholdingNumerator = 4,
        int withholdingDenominator = 10,
        string? contractNumber = null) =>
        new
        {
            companyId = data.CompanyId,
            currentAccountId = currentAccountId ?? data.SubcontractorAccountId,
            projectId = data.ProjectId,
            projectSiteId,
            contractNumber = contractNumber ?? $"TS-{Guid.NewGuid():N}"[..12],
            workDescription = "Kaba elektrik tesisatı",
            contractType,
            contractAmount,
            currencyCode = "TRY",
            startDate = "2026-01-01",
            endDate = "2026-12-31",
            retentionRate = 5m,
            withholdingNumerator,
            withholdingDenominator,
            mealResponsibility = (int)SubcontractorResponsibility.Us,
            accommodationResponsibility = (int)SubcontractorResponsibility.Us,
            socialSecurityResponsibility =
                (int)SubcontractorResponsibility.Subcontractor,
            materialResponsibility = (int)SubcontractorResponsibility.Us,
            ohsResponsibility = (int)SubcontractorResponsibility.Us,
            notes = (string?)null,
            sections = sections ?? []
        };

    // ---------- Mutlu yol ----------

    [Fact]
    public async Task Create_StoresContractWithScopeFlags()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var response = await client.PostAsJsonAsync(
            "/api/subcontractor-contracts",
            BuildRequest(
                data,
                projectSiteId: data.SiteId,
                sections:
                [
                    new
                    {
                        projectHakedisSectionId = data.SectionId,
                        sectionAmount = 300_000m,
                        order = 1
                    }
                ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        var detail = await client.GetAsync($"/api/subcontractor-contracts/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        var payload = await detail.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Birim fiyatlı", payload.GetProperty("contractTypeName").GetString());
        Assert.Equal(500_000m, payload.GetProperty("contractAmount").GetDecimal());
        Assert.Equal(4, payload.GetProperty("withholdingNumerator").GetInt32());
        Assert.Equal(10, payload.GetProperty("withholdingDenominator").GetInt32());

        // Kapsam tikleri: yemek/konaklama/malzeme/İSG bizde, SGK taşeronda.
        Assert.Equal((int)SubcontractorResponsibility.Us,
            payload.GetProperty("mealResponsibility").GetInt32());
        Assert.Equal((int)SubcontractorResponsibility.Us,
            payload.GetProperty("ohsResponsibility").GetInt32());
        Assert.Equal((int)SubcontractorResponsibility.Subcontractor,
            payload.GetProperty("socialSecurityResponsibility").GetInt32());

        var section = Assert.Single(payload.GetProperty("sections").EnumerateArray());
        Assert.Equal(data.SectionId,
            section.GetProperty("projectHakedisSectionId").GetGuid());
        Assert.Equal("Elektrik İşleri", section.GetProperty("sectionName").GetString());
        Assert.Equal(300_000m, section.GetProperty("sectionAmount").GetDecimal());
    }

    /// <summary>
    /// Kısımlar tam liste olarak gönderilir: listeden çıkarılan kısım
    /// kayıttan da düşer. Fark hesabı yerine tam liste, ekranın
    /// gösterdiğiyle kaydın birebir aynı kalmasını sağlıyor.
    /// </summary>
    [Fact]
    public async Task Update_ReplacesSectionListWithWhatWasSent()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var contractNumber = $"TS-{Guid.NewGuid():N}"[..12];

        var create = await client.PostAsJsonAsync(
            "/api/subcontractor-contracts",
            BuildRequest(
                data,
                contractNumber: contractNumber,
                sections:
                [
                    new
                    {
                        projectHakedisSectionId = data.SectionId,
                        sectionAmount = 300_000m,
                        order = 1
                    }
                ]));

        var id = (await create.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var update = await client.PutAsJsonAsync(
            $"/api/subcontractor-contracts/{id}",
            BuildRequest(data, contractNumber: contractNumber, sections: []));

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var payload = await (await client
                .GetAsync($"/api/subcontractor-contracts/{id}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Empty(payload.GetProperty("sections").EnumerateArray());
    }

    // ---------- Bağların korunması ----------

    /// <summary>
    /// Taşeron olarak işaretlenmemiş cariye sözleşme bağlanamaz: aksi
    /// halde müşteri ya da banka carisine taşeron hakedişi yazılırdı.
    /// </summary>
    [Fact]
    public async Task Create_RejectsCurrentAccountThatIsNotMarkedSubcontractor()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var response = await client.PostAsJsonAsync(
            "/api/subcontractor-contracts",
            BuildRequest(data, currentAccountId: data.SupplierOnlyAccountId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("taşeron olarak işaretli değil",
            payload.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Create_RejectsSectionFromAnotherProject()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var response = await client.PostAsJsonAsync(
            "/api/subcontractor-contracts",
            BuildRequest(
                data,
                sections:
                [
                    new
                    {
                        projectHakedisSectionId = data.ForeignSectionId,
                        sectionAmount = 100_000m,
                        order = 1
                    }
                ]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_RejectsSiteFromAnotherProject()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var response = await client.PostAsJsonAsync(
            "/api/subcontractor-contracts",
            BuildRequest(data, projectSiteId: data.ForeignSiteId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Sözleşme tipi ----------

    /// <summary>
    /// Götürüde ilerleme kısım bazında giriliyor; kısım seçilmemişse
    /// hakediş hiç hesaplanamaz, o yüzden kayıt aşamasında durduruluyor.
    /// </summary>
    [Fact]
    public async Task Create_RejectsLumpSumWithoutSections()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var response = await client.PostAsJsonAsync(
            "/api/subcontractor-contracts",
            BuildRequest(
                data,
                contractType: (int)ProjectContractType.LumpSum,
                sections: []));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("kısım", payload.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData((int)ProjectContractType.Mixed)]
    [InlineData((int)ProjectContractType.Undetermined)]
    public async Task Create_RejectsMixedAndUndeterminedContractTypes(int contractType)
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var response = await client.PostAsJsonAsync(
            "/api/subcontractor-contracts",
            BuildRequest(data, contractType: contractType));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Kısım bedelleri toplamı sözleşme bedelini aşarsa götürüdeki
    /// ağırlıklı ilerleme %100'ü geçerdi.
    /// </summary>
    [Fact]
    public async Task Create_RejectsSectionTotalAboveContractAmount()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var response = await client.PostAsJsonAsync(
            "/api/subcontractor-contracts",
            BuildRequest(
                data,
                contractAmount: 100_000m,
                sections:
                [
                    new
                    {
                        projectHakedisSectionId = data.SectionId,
                        sectionAmount = 150_000m,
                        order = 1
                    }
                ]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Tevkifat ----------

    /// <summary>
    /// Tevkifat oranı yarım bırakılamaz: payı girilip paydası
    /// boş bırakılan bir oran, faturada sessizce sıfır tevkifat
    /// üretir ve KDV beyanı tutmaz.
    /// </summary>
    [Theory]
    [InlineData(4, 0)]
    [InlineData(0, 10)]
    [InlineData(12, 10)]
    public async Task Create_RejectsIncoherentWithholdingRate(
        int numerator, int denominator)
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var response = await client.PostAsJsonAsync(
            "/api/subcontractor-contracts",
            BuildRequest(
                data,
                withholdingNumerator: numerator,
                withholdingDenominator: denominator));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_AllowsNoWithholdingAtAll()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var response = await client.PostAsJsonAsync(
            "/api/subcontractor-contracts",
            BuildRequest(data, withholdingNumerator: 0, withholdingDenominator: 0));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- Değişmezler ----------

    /// <summary>
    /// Cari ve proje değiştirilemez: değişseydi bu sözleşmeye bağlı
    /// hakediş ve maliyet kayıtları başka bir projeye sessizce taşınırdı.
    /// </summary>
    [Fact]
    public async Task Update_RejectsChangingProject()
    {
        var data = await CreateFixtureAsync();
        var other = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var contractNumber = $"TS-{Guid.NewGuid():N}"[..12];

        var create = await client.PostAsJsonAsync(
            "/api/subcontractor-contracts",
            BuildRequest(data, contractNumber: contractNumber));

        var id = (await create.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var update = await client.PutAsJsonAsync(
            $"/api/subcontractor-contracts/{id}",
            BuildRequest(other, contractNumber: contractNumber));

        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
    }

    [Fact]
    public async Task Create_RejectsDuplicateContractNumberInSameCompany()
    {
        var data = await CreateFixtureAsync();
        var client = await CreateClientForRoleAsync("Genel Müdür");

        var contractNumber = $"TS-{Guid.NewGuid():N}"[..12];

        var first = await client.PostAsJsonAsync(
            "/api/subcontractor-contracts",
            BuildRequest(data, contractNumber: contractNumber));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync(
            "/api/subcontractor-contracts",
            BuildRequest(data, contractNumber: contractNumber));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    // ---------- Yetki ----------

    /// <summary>
    /// Taşeron sözleşmesi ticari sır taşır (birim fiyat, bedel). Saha ve
    /// ofis rolleri görmemeli.
    /// </summary>
    [Theory]
    [InlineData("Şantiye Şefi")]
    [InlineData("Formen")]
    [InlineData("Sekreterya")]
    [InlineData("Teknik Ofis")]
    [InlineData("Depo Sorumlusu")]
    public async Task RolesWithoutSubcontractorPermission_CannotList(string roleName)
    {
        var client = await CreateClientForRoleAsync(roleName);

        var response = await client.GetAsync("/api/subcontractor-contracts");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Genel Müdür")]
    [InlineData("Finans Sorumlusu")]
    [InlineData("Teknik Koordinatör")]
    public async Task AuthorizedRoles_CanList(string roleName)
    {
        var client = await CreateClientForRoleAsync(roleName);

        var response = await client.GetAsync("/api/subcontractor-contracts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Teknik Koordinatör sahadaki taşeronu yönetir ama hakedişi
    /// ONAYLAYAMAZ — ödemeyi tetikleyen adım finansta kalıyor.
    /// </summary>
    [Fact]
    public void TechnicalCoordinator_ManagesButDoesNotApprove()
    {
        var role = RoleCatalog.Roles.Single(x => x.Name == "Teknik Koordinatör");

        Assert.Contains(PermissionCatalog.Keys.SubcontractorManage, role.PermissionKeys);
        Assert.DoesNotContain(
            PermissionCatalog.Keys.SubcontractorApprove, role.PermissionKeys);
    }

    /// <summary>
    /// Taşeronu yöneten her rol onu görebilmeli de; aksi halde
    /// kaydettiğini okuyamayan bir rol doğardı.
    /// </summary>
    [Fact]
    public void EverySubcontractorManagingRole_AlsoViews()
    {
        var offenders = RoleCatalog.Roles
            .Where(role => role.PermissionKeys.Contains(
                PermissionCatalog.Keys.SubcontractorManage,
                StringComparer.OrdinalIgnoreCase))
            .Where(role => !role.PermissionKeys.Contains(
                PermissionCatalog.Keys.SubcontractorView,
                StringComparer.OrdinalIgnoreCase))
            .Select(role => role.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Taşeronu yöneten ama göremeyen rol(ler): " +
            string.Join(", ", offenders));
    }
}
