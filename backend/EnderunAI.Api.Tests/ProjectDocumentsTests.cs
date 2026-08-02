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

[Collection("Integration")]
public sealed class ProjectDocumentsTests(DatabaseFixture fixture)
{
    private static HttpContent BuildFilePart(string fileName, string content = "test-icerik") =>
        new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content));

    private async Task<(string Username, string Password, Guid UserId)> CreateUserWithRolesAsync(
        string suffix,
        string[] roleNames,
        IEnumerable<Guid>? siteScopeIds = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        var username = $"test-pd-{suffix}-{Guid.NewGuid():N}"[..40];
        const string password = "TestProjectDoc!2026";
        var hash = passwordService.Hash(password);

        var user = new AppUser
        {
            Username = username,
            FullName = $"Test {suffix}",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            IsActive = true,
            // Bu testler mesai saati mantığını değil izin/versiyonlama
            // mantığını doğruluyor, testin çalıştığı saatten bağımsız
            // olması için istisna işaretleniyor.
            WorkHoursExempt = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var roles = await db.Roles.Where(r => roleNames.Contains(r.Name)).ToListAsync();
        db.UserRoles.AddRange(roles.Select(r => new UserRole { UserId = user.Id, RoleId = r.Id }));

        var siteIds = siteScopeIds?.ToArray() ?? [];
        if (siteIds.Length > 0)
        {
            foreach (var siteId in siteIds)
            {
                db.UserDataScopes.Add(new UserDataScope
                {
                    UserId = user.Id,
                    ScopeType = DataScopeType.Site,
                    ProjectSiteId = siteId
                });
            }
        }
        else
        {
            db.UserDataScopes.Add(new UserDataScope { UserId = user.Id, ScopeType = DataScopeType.All });
        }

        await db.SaveChangesAsync();

        return (username, password, user.Id);
    }

    private static async Task<HttpClient> LoginAsAsync(string username, string password, TestWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, username, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task CreateProject_KesifStatus_WithoutEmployer_Succeeds()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (company, branch, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/projects", new
        {
            companyId = company.Id,
            branchId = branch.Id,
            employerCurrentAccountId = (Guid?)null,
            code = $"KESIF-{suffix}",
            name = "Keşif Projesi",
            currencyCode = "TRY",
            vatRate = 20m,
            increaseRate = 0m,
            cashRetentionRate = 0m,
            withholdingTaxRate = 0m,
            materialDeductionRate = 0m,
            status = 0 // Kesif
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateProject_ActiveStatus_WithoutEmployer_ReturnsBadRequest()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (company, branch, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/projects", new
        {
            companyId = company.Id,
            branchId = branch.Id,
            employerCurrentAccountId = (Guid?)null,
            code = $"AKTIF-{suffix}",
            name = "Aktif Projesi",
            currencyCode = "TRY",
            vatRate = 20m,
            increaseRate = 0m,
            cashRetentionRate = 0m,
            withholdingTaxRate = 0m,
            materialDeductionRate = 0m,
            status = 2 // Active
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_SameFileNameTwice_CreatesVersionTwo_AndVersionOneStillDownloadable()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        async Task<HttpResponseMessage> UploadAsync(string content)
        {
            using var form = new MultipartFormDataContent();
            using var fileContent = BuildFilePart("belge.pdf", content);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(fileContent, "Files", "belge.pdf");
            form.Add(new StringContent("Şartnameler"), "Folder");
            return await client.PostAsync($"/api/projects/{project.Id}/documents", form);
        }

        var first = await UploadAsync("v1-icerik");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await UploadAsync("v2-icerik");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var listResponse = await client.GetAsync($"/api/projects/{project.Id}/documents?folder=Şartnameler");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var current = list.EnumerateArray().Single(x => x.GetProperty("fileName").GetString() == "belge.pdf");
        Assert.Equal(2, current.GetProperty("versionNumber").GetInt32());

        var documentId = current.GetProperty("id").GetGuid();
        var versionsResponse = await client.GetAsync(
            $"/api/projects/{project.Id}/documents/{documentId}/versions");
        Assert.Equal(HttpStatusCode.OK, versionsResponse.StatusCode);
        var versions = await versionsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, versions.GetArrayLength());

        var version1Id = versions.EnumerateArray()
            .Single(x => x.GetProperty("versionNumber").GetInt32() == 1)
            .GetProperty("id").GetGuid();

        var downloadV1 = await client.GetAsync(
            $"/api/projects/{project.Id}/documents/{version1Id}/download");
        Assert.Equal(HttpStatusCode.OK, downloadV1.StatusCode);
        var v1Bytes = await downloadV1.Content.ReadAsStringAsync();
        Assert.Equal("v1-icerik", v1Bytes);
    }

    [Fact]
    public async Task Upload_DisallowedExtension_ReturnsBadRequest()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        using var form = new MultipartFormDataContent();
        using var fileContent = BuildFilePart("virus.exe");
        form.Add(fileContent, "Files", "virus.exe");
        form.Add(new StringContent("Diğer"), "Folder");

        var response = await client.PostAsync($"/api/projects/{project.Id}/documents", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_CurrentVersion_PromotesPreviousVersionToCurrent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        async Task<Guid> UploadAsync(string content)
        {
            using var form = new MultipartFormDataContent();
            using var fileContent = BuildFilePart("plan.dwg", content);
            form.Add(fileContent, "Files", "plan.dwg");
            form.Add(new StringContent("Çizimler"), "Folder");
            var response = await client.PostAsync($"/api/projects/{project.Id}/documents", form);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var listResponse = await client.GetAsync($"/api/projects/{project.Id}/documents?folder=Çizimler");
            var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
            return list.EnumerateArray().Single().GetProperty("id").GetGuid();
        }

        await UploadAsync("v1");
        var v2Id = await UploadAsync("v2");

        var deleteResponse = await client.DeleteAsync($"/api/projects/{project.Id}/documents/{v2Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var listAfterDelete = await client.GetAsync($"/api/projects/{project.Id}/documents?folder=Çizimler");
        var itemsAfterDelete = await listAfterDelete.Content.ReadFromJsonAsync<JsonElement>();
        var currentItem = itemsAfterDelete.EnumerateArray().Single();
        Assert.Equal(1, currentItem.GetProperty("versionNumber").GetInt32());
    }

    [Fact]
    public async Task SantiyeSefi_CannotSeeUnassignedProjectDocuments_ButCanSeeAssignedProjectDocuments()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var assignedProject = await TestDataFactory.CreateProjectAsync(db, $"assigned-{suffix}");
        var unassignedProject = await TestDataFactory.CreateProjectAsync(db, $"other-{suffix}");

        var site = new ProjectSite
        {
            ProjectId = assignedProject.Id,
            Code = $"SITE-{suffix}",
            Name = "Atanan Şantiye"
        };
        db.ProjectSites.Add(site);
        await db.SaveChangesAsync();

        var adminClient = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        using (var form = new MultipartFormDataContent())
        using (var fileContent = BuildFilePart("genel.pdf"))
        {
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(fileContent, "Files", "genel.pdf");
            form.Add(new StringContent("Şartnameler"), "Folder");
            var uploadResponse = await adminClient.PostAsync(
                $"/api/projects/{assignedProject.Id}/documents", form);
            Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        }

        var (username, password, _) = await CreateUserWithRolesAsync(
            "santiyesefi-docs", ["Şantiye Şefi"], [site.Id]);
        var client = await LoginAsAsync(username, password, fixture.Factory);

        var unassignedResponse = await client.GetAsync(
            $"/api/projects/{unassignedProject.Id}/documents");
        Assert.Equal(HttpStatusCode.NotFound, unassignedResponse.StatusCode);

        var assignedResponse = await client.GetAsync(
            $"/api/projects/{assignedProject.Id}/documents");
        Assert.Equal(HttpStatusCode.OK, assignedResponse.StatusCode);
        var items = await assignedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, items.GetArrayLength());
    }
}
