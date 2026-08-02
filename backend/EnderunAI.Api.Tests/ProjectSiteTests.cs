using System.Net;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

[Collection("Integration")]
public sealed class ProjectSiteTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task CreateSite_AssignPersonnel_SecondActiveAssignmentIsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        var personnel = await TestDataFactory.CreatePersonnelAsync(db, project.CompanyId, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var createSiteResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/sites", new
        {
            code = $"STE-{suffix}",
            name = $"Test Şantiyesi {suffix}",
            location = "Test Lokasyon",
            notes = (string?)null
        });

        Assert.Equal(HttpStatusCode.OK, createSiteResponse.StatusCode);
        var site = await createSiteResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var siteId = site.GetProperty("id").GetGuid();

        var firstAssignResponse = await client.PostAsJsonAsync($"/api/project-sites/{siteId}/assignments", new
        {
            personnelId = personnel.Id,
            startDate = DateTime.UtcNow.Date,
            endDate = (DateTime?)null,
            role = "Usta",
            notes = (string?)null
        });

        Assert.Equal(HttpStatusCode.OK, firstAssignResponse.StatusCode);

        // Aynı personel için, ilk atama hâlâ aktifken (EndDate=null) ikinci
        // bir aktif atama denemesi — kural bunu reddetmeli (409 Conflict).
        var secondAssignResponse = await client.PostAsJsonAsync($"/api/project-sites/{siteId}/assignments", new
        {
            personnelId = personnel.Id,
            startDate = DateTime.UtcNow.Date,
            endDate = (DateTime?)null,
            role = "Formen",
            notes = (string?)null
        });

        Assert.Equal(HttpStatusCode.Conflict, secondAssignResponse.StatusCode);
    }
}
