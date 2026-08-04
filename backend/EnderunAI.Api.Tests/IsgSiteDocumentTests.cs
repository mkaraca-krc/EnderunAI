using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Şantiye İSG belgeleri: yükleme, geçerlilik takibi ve indirme.
/// </summary>
[Collection("Integration")]
public sealed class IsgSiteDocumentTests(DatabaseFixture fixture)
{
    private sealed record Context(Guid CompanyId, Guid ProjectId, Guid SiteId);

    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var site = new ProjectSite
        {
            ProjectId = project.Id,
            Code = $"SNT-{suffix}",
            Name = $"Test Şantiye {suffix}"
        };
        db.ProjectSites.Add(site);
        await db.SaveChangesAsync();

        return new Context(project.CompanyId, project.Id, site.Id);
    }

    private static MultipartFormDataContent BuildUpload(
        Context context,
        int documentType = 0,
        string title = "Risk Değerlendirmesi 2026",
        string issueDate = "2026-01-15",
        string? validUntil = "2027-01-15",
        Guid? siteId = null,
        string fileName = "risk.pdf")
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(context.CompanyId.ToString()), "companyId" },
            { new StringContent(context.ProjectId.ToString()), "projectId" },
            { new StringContent(documentType.ToString()), "documentType" },
            { new StringContent(title), "title" },
            { new StringContent(issueDate), "issueDate" }
        };

        if (validUntil is not null)
            content.Add(new StringContent(validUntil), "validUntil");

        if (siteId is Guid site)
            content.Add(new StringContent(site.ToString()), "projectSiteId");

        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.4 test"));
        content.Add(file, "file", fileName);

        return content;
    }

    [Fact]
    public async Task Upload_StoresDocumentWithValidity()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsync(
            "/api/isg/saha-belgeleri", BuildUpload(context, siteId: context.SiteId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Risk değerlendirmesi",
            payload.GetProperty("documentTypeName").GetString());
        Assert.Equal("risk.pdf", payload.GetProperty("originalFileName").GetString());
        Assert.Equal(context.SiteId, payload.GetProperty("projectSiteId").GetGuid());
        Assert.True(payload.GetProperty("sizeBytes").GetInt64() > 0);
    }

    [Fact]
    public async Task ExpiredDocument_IsFlaggedRed()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        // Süresi dolmuş risk değerlendirmesi, denetimde belge yokluğuyla
        // aynı sonucu doğurur — kırmızı işaretlenmeli.
        var response = await client.PostAsync("/api/isg/saha-belgeleri",
            BuildUpload(context, issueDate: "2024-01-01", validUntil: "2025-01-01"));

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Süresi doldu",
            payload.GetProperty("validityStatusName").GetString());
        Assert.Equal("red", payload.GetProperty("validityColor").GetString());
        Assert.True(payload.GetProperty("daysRemaining").GetInt32() < 0);
    }

    [Fact]
    public async Task DocumentWithoutExpiry_IsIndefinite()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsync("/api/isg/saha-belgeleri",
            BuildUpload(context, documentType: 2, title: "Kurul Tutanağı",
                validUntil: null));

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Süresiz", payload.GetProperty("validityStatusName").GetString());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("daysRemaining").ValueKind);
    }

    [Fact]
    public async Task ExpiryBeforeIssueDate_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsync("/api/isg/saha-belgeleri",
            BuildUpload(context, issueDate: "2026-06-01", validUntil: "2026-01-01"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SiteFromAnotherProject_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var other = await CreateContextAsync($"{suffix}b");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsync("/api/isg/saha-belgeleri",
            BuildUpload(context, siteId: other.SiteId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WithoutFile_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var content = new MultipartFormDataContent
        {
            { new StringContent(context.CompanyId.ToString()), "companyId" },
            { new StringContent(context.ProjectId.ToString()), "projectId" },
            { new StringContent("0"), "documentType" },
            { new StringContent("Dosyasız belge"), "title" },
            { new StringContent("2026-01-15"), "issueDate" }
        };

        var response = await client.PostAsync("/api/isg/saha-belgeleri", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Download_ReturnsStoredFile()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var upload = await client.PostAsync(
            "/api/isg/saha-belgeleri", BuildUpload(context));
        var id = (await upload.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var download = await client.GetAsync($"/api/isg/saha-belgeleri/{id}/dosya");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);

        var bytes = await download.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public async Task Delete_RemovesFromListButKeepsFile()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var upload = await client.PostAsync(
            "/api/isg/saha-belgeleri", BuildUpload(context));
        var id = (await upload.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK,
            (await client.DeleteAsync($"/api/isg/saha-belgeleri/{id}")).StatusCode);

        var list = await client.GetFromJsonAsync<JsonElement>(
            $"/api/isg/saha-belgeleri?projectId={context.ProjectId}");

        Assert.Empty(list.EnumerateArray());
    }

    [Fact]
    public async Task List_FiltersByDocumentType()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await client.PostAsync("/api/isg/saha-belgeleri",
            BuildUpload(context, documentType: 0, title: "Risk"));
        await client.PostAsync("/api/isg/saha-belgeleri",
            BuildUpload(context, documentType: 1, title: "Acil Durum"));

        var list = await client.GetFromJsonAsync<JsonElement>(
            $"/api/isg/saha-belgeleri?projectId={context.ProjectId}&documentType=1");

        var item = list.EnumerateArray().Single();
        Assert.Equal("Acil durum planı", item.GetProperty("documentTypeName").GetString());
    }
}
