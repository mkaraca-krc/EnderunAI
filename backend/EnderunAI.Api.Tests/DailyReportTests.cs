using System.Net;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

[Collection("Integration")]
public sealed class DailyReportTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Create_Then_SecondReportSameSiteAndDate_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var createSiteResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/sites", new
        {
            code = $"STE-{suffix}",
            name = $"Rapor Testi Şantiyesi {suffix}",
            location = (string?)null,
            notes = (string?)null
        });
        var site = await createSiteResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var siteId = site.GetProperty("id").GetGuid();

        var reportDate = DateTime.UtcNow.Date;

        var firstReportResponse = await client.PostAsJsonAsync(
            $"/api/project-sites/{siteId}/daily-reports",
            new
            {
                reportDate,
                weatherCondition = "Güneşli",
                engineerCount = 1,
                foremanCount = 1,
                craftsmanCount = 2,
                workerCount = 5,
                otherCount = 0,
                notes = "İlk rapor",
                workItems = Array.Empty<object>()
            });

        Assert.Equal(HttpStatusCode.OK, firstReportResponse.StatusCode);

        var secondReportResponse = await client.PostAsJsonAsync(
            $"/api/project-sites/{siteId}/daily-reports",
            new
            {
                reportDate,
                weatherCondition = "Yağmurlu",
                engineerCount = 2,
                foremanCount = 1,
                craftsmanCount = 1,
                workerCount = 3,
                otherCount = 0,
                notes = "Aynı gün ikinci rapor denemesi",
                workItems = Array.Empty<object>()
            });

        Assert.Equal(HttpStatusCode.Conflict, secondReportResponse.StatusCode);
    }
}
