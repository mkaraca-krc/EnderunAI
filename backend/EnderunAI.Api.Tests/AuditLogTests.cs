using System.Net;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

[Collection("Integration")]
public sealed class AuditLogTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task CreatingEmployerPortalLink_ShowsUpInAuditLog()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var createResponse = await client.PostAsync($"/api/projects/{project.Id}/employer-portal-link", null);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var link = await createResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var linkId = link.GetProperty("id").GetGuid();

        var auditResponse = await client.GetAsync(
            $"/api/security-audit/events?entityType=EmployerPortalLink&entityId={linkId}&take=5");

        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);

        var events = await auditResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(events.GetArrayLength() >= 1);

        var entry = events[0];
        Assert.Equal("Created", entry.GetProperty("action").GetString());
        Assert.Equal("EmployerPortalLink", entry.GetProperty("entityType").GetString());
        Assert.Equal(AuthHelper.AdminUsername, entry.GetProperty("actorUsername").GetString());
    }
}
