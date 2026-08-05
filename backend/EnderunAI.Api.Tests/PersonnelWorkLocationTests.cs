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
/// Personel görev yeri: merkez mi şantiye mi, ve "atama bekliyor"
/// işareti.
///
/// Kritik kural: bir personelin aynı anda tek aktif şantiye ataması
/// olur. Görev yeri değişince eskisi kapanmalı, aksi halde personel
/// iki şantiyenin de puantajında görünürdü.
/// </summary>
[Collection("Integration")]
public sealed class PersonnelWorkLocationTests(DatabaseFixture fixture)
{
    private sealed record Context(
        Guid CompanyId, Guid PersonnelId, Guid SiteAId, Guid SiteBId, Guid BranchId);

    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, suffix);

        var siteA = new ProjectSite
        {
            ProjectId = project.Id, Code = $"SA-{suffix}", Name = "Şantiye A"
        };
        var siteB = new ProjectSite
        {
            ProjectId = project.Id, Code = $"SB-{suffix}", Name = "Şantiye B"
        };
        db.ProjectSites.AddRange(siteA, siteB);
        await db.SaveChangesAsync();

        var branchId = await db.Branches
            .Where(x => x.CompanyId == project.CompanyId)
            .Select(x => x.Id)
            .FirstAsync();

        return new Context(project.CompanyId, personnel.Id, siteA.Id, siteB.Id, branchId);
    }

    private static object SitePayload(Guid siteId) => new
    {
        workLocationType = 2,
        projectSiteId = siteId,
        branchId = (Guid?)null,
        startDate = DateTime.UtcNow.Date,
        role = "Usta",
        notes = (string?)null
    };

    private async Task<JsonElement> GetPersonnelAsync(HttpClient client, Guid personnelId)
    {
        return await client.GetFromJsonAsync<JsonElement>($"/api/personnel/{personnelId}");
    }

    [Fact]
    public async Task NewPersonnel_IsAwaitingWorkLocation()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var person = await GetPersonnelAsync(client, context.PersonnelId);

        Assert.Equal(0, person.GetProperty("workLocationType").GetInt32());
        Assert.True(person.GetProperty("isAwaitingWorkLocation").GetBoolean());
    }

    [Fact]
    public async Task AssigningToSite_CreatesActiveAssignment()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PutAsJsonAsync(
            $"/api/personnel/{context.PersonnelId}/gorev-yeri",
            SitePayload(context.SiteAId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var person = await GetPersonnelAsync(client, context.PersonnelId);

        Assert.Equal(2, person.GetProperty("workLocationType").GetInt32());
        Assert.False(person.GetProperty("isAwaitingWorkLocation").GetBoolean());
        Assert.Equal(context.SiteAId,
            person.GetProperty("activeSiteAssignment").GetProperty("projectSiteId").GetGuid());
    }

    [Fact]
    public async Task ChangingSite_ClosesPreviousAssignment()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await client.PutAsJsonAsync(
            $"/api/personnel/{context.PersonnelId}/gorev-yeri",
            SitePayload(context.SiteAId));

        var response = await client.PutAsJsonAsync(
            $"/api/personnel/{context.PersonnelId}/gorev-yeri",
            SitePayload(context.SiteBId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var assignments = await db.ProjectSiteAssignments.AsNoTracking()
            .Where(x => x.PersonnelId == context.PersonnelId)
            .ToListAsync();

        // İki kayıt: eskisi kapalı, yenisi açık. Tek aktif atama kuralı.
        Assert.Equal(2, assignments.Count);

        var active = Assert.Single(assignments.Where(x => x.IsActive && x.EndDate == null));
        Assert.Equal(context.SiteBId, active.ProjectSiteId);

        var closed = Assert.Single(assignments.Where(x => !x.IsActive));
        Assert.Equal(context.SiteAId, closed.ProjectSiteId);
        Assert.NotNull(closed.EndDate);
    }

    [Fact]
    public async Task MovingToHeadOffice_ClosesSiteAssignment()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await client.PutAsJsonAsync(
            $"/api/personnel/{context.PersonnelId}/gorev-yeri",
            SitePayload(context.SiteAId));

        var response = await client.PutAsJsonAsync(
            $"/api/personnel/{context.PersonnelId}/gorev-yeri",
            new
            {
                workLocationType = 1,
                projectSiteId = (Guid?)null,
                branchId = context.BranchId,
                startDate = (DateTime?)null,
                role = (string?)null,
                notes = (string?)null
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var person = await GetPersonnelAsync(client, context.PersonnelId);

        Assert.Equal(1, person.GetProperty("workLocationType").GetInt32());
        Assert.False(person.GetProperty("isAwaitingWorkLocation").GetBoolean());
        // Merkeze geçince şantiyede görünmemeli.
        Assert.Equal(JsonValueKind.Null,
            person.GetProperty("activeSiteAssignment").ValueKind);
    }

    /// <summary>
    /// Birim seçilmeden merkeze atama: personel şirketin merkez ofisine
    /// bağlanır. Daha önce burada personelin eski şubesi ne ise o
    /// kalıyordu — şantiye şubesindeki biri merkeze alınınca defterde
    /// hâlâ şantiyenin masraf merkezine yazılıyordu.
    /// </summary>
    [Fact]
    public async Task HeadOfficeWithoutBranch_FallsBackToCompanyHeadOffice()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PutAsJsonAsync(
            $"/api/personnel/{context.PersonnelId}/gorev-yeri",
            new
            {
                workLocationType = 1,
                projectSiteId = (Guid?)null,
                branchId = (Guid?)null,
                startDate = (DateTime?)null,
                role = (string?)null,
                notes = (string?)null
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var personnel = await db.Personnel
            .AsNoTracking()
            .SingleAsync(x => x.Id == context.PersonnelId);

        var headOfficeId = await db.Branches
            .Where(x => x.CompanyId == context.CompanyId && x.IsHeadOffice)
            .Select(x => x.Id)
            .SingleAsync();

        Assert.Equal(headOfficeId, personnel.BranchId);
        Assert.Equal(WorkLocationType.HeadOffice, personnel.WorkLocationType);
    }

    [Fact]
    public async Task SiteWithoutSiteId_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PutAsJsonAsync(
            $"/api/personnel/{context.PersonnelId}/gorev-yeri",
            new
            {
                workLocationType = 2,
                projectSiteId = (Guid?)null,
                branchId = (Guid?)null,
                startDate = (DateTime?)null,
                role = (string?)null,
                notes = (string?)null
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SiteSelectedButAssignmentClosed_CountsAsAwaiting()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await client.PutAsJsonAsync(
            $"/api/personnel/{context.PersonnelId}/gorev-yeri",
            SitePayload(context.SiteAId));

        // Atama şantiye ekranından kapatılırsa personel yine atama
        // bekliyor olmalı — "şantiye" seçili olması yetmez.
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var assignment = await db.ProjectSiteAssignments
                .SingleAsync(x => x.PersonnelId == context.PersonnelId && x.IsActive);

            assignment.IsActive = false;
            assignment.EndDate = DateTime.UtcNow.Date;
            await db.SaveChangesAsync();
        }

        var person = await GetPersonnelAsync(client, context.PersonnelId);

        Assert.Equal(2, person.GetProperty("workLocationType").GetInt32());
        Assert.True(person.GetProperty("isAwaitingWorkLocation").GetBoolean());
    }

    [Fact]
    public async Task SiteFromAnotherCompany_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var other = await CreateContextAsync($"{suffix}b");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PutAsJsonAsync(
            $"/api/personnel/{context.PersonnelId}/gorev-yeri",
            SitePayload(other.SiteAId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InvalidLocationType_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PutAsJsonAsync(
            $"/api/personnel/{context.PersonnelId}/gorev-yeri",
            new
            {
                workLocationType = 9,
                projectSiteId = (Guid?)null,
                branchId = (Guid?)null,
                startDate = (DateTime?)null,
                role = (string?)null,
                notes = (string?)null
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SiteAssignedPersonnel_StillAppearsInSiteHeadcount()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await client.PutAsJsonAsync(
            $"/api/personnel/{context.PersonnelId}/gorev-yeri",
            SitePayload(context.SiteAId));

        // Mevcut bağlantı korunmalı: şantiye günlük raporu personel
        // sayısını ProjectSiteAssignment'tan okuyor.
        var day = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");

        var headcount = await client.GetFromJsonAsync<JsonElement>(
            $"/api/project-sites/{context.SiteAId}/daily-reports/suggested-headcount?date={day}");

        var total =
            headcount.GetProperty("engineerCount").GetInt32() +
            headcount.GetProperty("foremanCount").GetInt32() +
            headcount.GetProperty("craftsmanCount").GetInt32() +
            headcount.GetProperty("workerCount").GetInt32() +
            headcount.GetProperty("otherCount").GetInt32();

        Assert.Equal(1, total);
    }
}
