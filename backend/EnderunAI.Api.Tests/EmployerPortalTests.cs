using System.Net;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

[Collection("Integration")]
public sealed class EmployerPortalTests(DatabaseFixture fixture)
{
    private static readonly string[] ForbiddenFieldNames =
    [
        "cost", "maliyet", "budget", "butce", "bütçe", "hakedis", "hakediş",
        "salary", "maas", "maaş", "supplier", "tedarikci", "tedarikçi",
        "amount", "tutar", "price", "fiyat"
    ];

    [Fact]
    public async Task ValidToken_ReturnsData_RevokedToken_Returns404_HiddenPhotoNeverLeaks_NoCostFields()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var siteResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/sites", new
        {
            code = $"STE-{suffix}",
            name = $"Portal Testi Şantiyesi {suffix}",
            location = (string?)null,
            notes = (string?)null
        });
        var site = await siteResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var siteId = site.GetProperty("id").GetGuid();

        var reportResponse = await client.PostAsJsonAsync(
            $"/api/project-sites/{siteId}/daily-reports",
            new
            {
                reportDate = DateTime.UtcNow.Date,
                weatherCondition = "Açık",
                engineerCount = 1,
                foremanCount = 1,
                craftsmanCount = 3,
                workerCount = 8,
                otherCount = 0,
                notes = "Portal testi raporu",
                workItems = new[]
                {
                    new { description = "Test imalatı", quantity = 12.5, unit = "m2" }
                }
            });
        var report = await reportResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var reportId = report.GetProperty("id").GetGuid();

        // Görünür foto
        var visibleUpload = await UploadTinyPngAsync(client, siteId, reportId, isVisible: true);
        Assert.Equal(HttpStatusCode.OK, visibleUpload.StatusCode);
        var visiblePhoto = await visibleUpload.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var visiblePhotoId = visiblePhoto.GetProperty("id").GetGuid();

        // Gizli foto
        var hiddenUpload = await UploadTinyPngAsync(client, siteId, reportId, isVisible: false);
        Assert.Equal(HttpStatusCode.OK, hiddenUpload.StatusCode);
        var hiddenPhoto = await hiddenUpload.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var hiddenPhotoId = hiddenPhoto.GetProperty("id").GetGuid();

        var linkResponse = await client.PostAsync($"/api/projects/{project.Id}/employer-portal-link", null);
        Assert.Equal(HttpStatusCode.OK, linkResponse.StatusCode);
        var link = await linkResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var token = link.GetProperty("token").GetString()!;

        var anonymousClient = fixture.Factory.CreateClient();

        // --- Geçerli token: proje bilgisi dönüyor ---
        var projectResponse = await anonymousClient.GetAsync($"/api/portal/{token}");
        Assert.Equal(HttpStatusCode.OK, projectResponse.StatusCode);

        // --- Raporlar: içerikte maliyet/bütçe/hakediş vb. alan adı YOK ---
        var reportsResponse = await anonymousClient.GetAsync($"/api/portal/{token}/reports");
        Assert.Equal(HttpStatusCode.OK, reportsResponse.StatusCode);
        var reportsJson = await reportsResponse.Content.ReadAsStringAsync();

        foreach (var forbidden in ForbiddenFieldNames)
        {
            Assert.DoesNotContain(forbidden, reportsJson, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(visiblePhotoId.ToString(), reportsJson);
        Assert.DoesNotContain(hiddenPhotoId.ToString(), reportsJson);

        // --- Görünür foto binary erişilebilir ---
        var visiblePhotoResponse = await anonymousClient.GetAsync($"/api/portal/{token}/photos/{visiblePhotoId}");
        Assert.Equal(HttpStatusCode.OK, visiblePhotoResponse.StatusCode);

        // --- Gizli foto hiçbir yolla açılamaz ---
        var hiddenPhotoResponse = await anonymousClient.GetAsync($"/api/portal/{token}/photos/{hiddenPhotoId}");
        Assert.Equal(HttpStatusCode.NotFound, hiddenPhotoResponse.StatusCode);

        // --- İptal edilen token: 404 ---
        var revokeResponse = await client.PostAsync($"/api/projects/{project.Id}/employer-portal-link/revoke", null);
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        var revokedProjectResponse = await anonymousClient.GetAsync($"/api/portal/{token}");
        Assert.Equal(HttpStatusCode.NotFound, revokedProjectResponse.StatusCode);

        var revokedPhotoResponse = await anonymousClient.GetAsync($"/api/portal/{token}/photos/{visiblePhotoId}");
        Assert.Equal(HttpStatusCode.NotFound, revokedPhotoResponse.StatusCode);
    }

    private static async Task<HttpResponseMessage> UploadTinyPngAsync(
        HttpClient client,
        Guid siteId,
        Guid reportId,
        bool isVisible)
    {
        // 1x1 şeffaf PNG
        var pngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(pngBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");

        form.Add(fileContent, "file", "test.png");
        form.Add(new StringContent(isVisible.ToString()), "isVisibleToEmployer");
        form.Add(new StringContent("Test fotoğrafı"), "caption");

        return await client.PostAsync(
            $"/api/project-sites/{siteId}/daily-reports/{reportId}/photos",
            form);
    }
}
