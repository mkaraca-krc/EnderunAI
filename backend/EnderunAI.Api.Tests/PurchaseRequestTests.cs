using System.Net;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

[Collection("Integration")]
public sealed class PurchaseRequestTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Create_Then_List_Then_Detail_Works()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var createResponse = await client.PostAsJsonAsync("/api/purchase-requests", new
        {
            companyId = project.CompanyId,
            projectId = project.Id,
            requestDate = DateTime.UtcNow.Date,
            neededByDate = (DateTime?)null,
            requestedByName = "Test Kullanıcı",
            description = $"Entegrasyon testi talebi {suffix}",
            priority = 1,
            items = new[]
            {
                new
                {
                    materialDescription = "Test malzeme",
                    quantity = 10,
                    unit = "adet",
                    requestedDeliveryDate = (DateTime?)null,
                    notes = (string?)null
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var requestId = created.GetProperty("id").GetGuid();

        var listResponse = await client.GetAsync($"/api/purchase-requests?projectId={project.Id}");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var list = await listResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(list.GetArrayLength() >= 1);
        Assert.Contains(
            list.EnumerateArray(),
            item => item.GetProperty("id").GetGuid() == requestId);

        var detailResponse = await client.GetAsync($"/api/purchase-requests/{requestId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        var detail = await detailResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(requestId, detail.GetProperty("id").GetGuid());
        Assert.Equal(1, detail.GetProperty("items").GetArrayLength());
    }
}
