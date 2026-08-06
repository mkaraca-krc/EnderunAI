using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Hakedis;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Excel icmal aktarımında satır-poz bağı.
///
/// Asıl güvence: önizlemede görünen eşleşme aktarımda da uygulanmalı.
/// Önizlemenin "eşleşti" dediği satır kaydedilirken bağsız kalırsa,
/// kullanıcı bağlandığını sanır ve maliyet/kâr karşılaştırması sessizce
/// boş çalışır.
///
/// İkinci güvence: BELİRSİZ satır kendiliğinden bağlanmaz. Birbirine
/// yakın iki aday arasından sistemin seçmesi, yanlış pozla fiyatlanmış
/// bir icmal üretir ve bunu sonradan fark etmek çok zordur.
/// </summary>
[Collection("Integration")]
public sealed class BoqImportMatchTests(DatabaseFixture fixture)
{
    /// <summary>Aktarım öncesinde icmalde duran kalem — aktarım bunu siler.</summary>
    private const string SeedPositionCode = "SEED.01";

    private sealed record Context(Guid CompanyId, Guid ProjectId);

    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        return new Context(project.CompanyId, project.Id);
    }

    /// <summary>
    /// Şablondaki üç satıra karşılık gelen kütüphane: biri tartışmasız
    /// (ana dağıtım panosu), ikisi birbirine çok yakın (kat panosu).
    /// </summary>
    private async Task<(Guid Certain, Guid CloseA, Guid CloseB)> SeedLibraryAsync(
        Guid companyId, string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var certain = NewPosition(companyId, $"AD-{suffix}", "Ana dağıtım panosu montajı");
        var closeA = NewPosition(companyId, $"KPA-{suffix}", "Kat panosu montajı tip A");
        var closeB = NewPosition(companyId, $"KPB-{suffix}", "Kat panosu montajı tip B");

        db.EngineeringPositions.AddRange(certain, closeA, closeB);
        await db.SaveChangesAsync();

        return (certain.Id, closeA.Id, closeB.Id);
    }

    private static EngineeringPosition NewPosition(
        Guid companyId, string code, string name) => new()
        {
            CompanyId = companyId,
            Code = code,
            Name = name,
            Unit = "Ad",
            Source = EngineeringPositionSource.Official,
            Discipline = EngineeringPositionDiscipline.Electrical,
            Status = EngineeringPositionStatus.Active,
            SearchKeywords = name
        };

    private async Task<Guid> CreateSummaryAsync(
        HttpClient client, Context context, string suffix)
    {
        var response = await client.PostAsJsonAsync("/api/project-boqs", new
        {
            companyId = context.CompanyId,
            projectId = context.ProjectId,
            boqNumber = $"ICM-{suffix}",
            name = "Sözleşme icmali",
            revisionNumber = 1,
            currencyCode = "TRY",
            description = (string?)null,
            notes = (string?)null,
            items = new object[]
            {
                new
                {
                    engineeringPositionId = (Guid?)null,
                    positionCode = SeedPositionCode,
                    description = "Aktarım öncesi kalem",
                    unit = "Adet",
                    contractQuantity = 1m,
                    unitPrice = 1_000m,
                    itemType = 0,
                    category = (string?)null,
                    notes = (string?)null,
                    projectHakedisSectionId = (Guid?)null,
                    materialUnitPrice = (decimal?)null,
                    laborUnitPrice = (decimal?)null,
                    overheadUnitPrice = (decimal?)null
                }
            }
        });

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();

        return created.GetProperty("id").GetGuid();
    }

    private static MultipartFormDataContent BuildUpload(
        byte[] bytes, object[]? decisions = null)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);

        file.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        content.Add(file, "file", "icmal.xlsx");

        if (decisions is not null)
        {
            content.Add(
                new StringContent(
                    JsonSerializer.Serialize(decisions), Encoding.UTF8, "text/plain"),
                "matches");
        }

        return content;
    }

    private async Task<Dictionary<string, Guid?>> GetLinksAsync(Guid boqId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.ProjectBoqItems
            .AsNoTracking()
            .Where(x => x.ProjectBoqId == boqId)
            .ToDictionaryAsync(x => x.PositionCode, x => x.EngineeringPositionId);
    }

    [Fact]
    public async Task Commit_AutoLinksCertainMatchOnly()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var library = await SeedLibraryAsync(context.CompanyId, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var boqId = await CreateSummaryAsync(client, context, suffix);

        var response = await client.PostAsync(
            $"/api/project-boqs/{boqId}/icmal-aktar",
            BuildUpload(ContractSummaryExcelParser.BuildTemplate()));

        response.EnsureSuccessStatusCode();

        var links = await GetLinksAsync(boqId);

        // Tartışmasız satır bağlandı.
        Assert.Equal(library.Certain, links["P.01"]);

        // Birbirine yakın iki aday arasında sistem seçim yapmadı.
        Assert.Null(links["P.02"]);

        // Kütüphanede karşılığı olmayan satır da bağsız.
        Assert.Null(links["KT.01"]);
    }

    [Fact]
    public async Task Commit_HonoursExplicitDecision()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var library = await SeedLibraryAsync(context.CompanyId, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var boqId = await CreateSummaryAsync(client, context, suffix);

        // Belirsiz satırda kararı kullanıcı veriyor (şablonda 4. satır).
        var response = await client.PostAsync(
            $"/api/project-boqs/{boqId}/icmal-aktar",
            BuildUpload(
                ContractSummaryExcelParser.BuildTemplate(),
                [new { rowNumber = 4, positionId = library.CloseB }]));

        response.EnsureSuccessStatusCode();

        var links = await GetLinksAsync(boqId);

        Assert.Equal(library.CloseB, links["P.02"]);
        Assert.Equal(library.Certain, links["P.01"]);
    }

    [Fact]
    public async Task Commit_HonoursSkipDecision()
    {
        // Kesin eşleşen satır bilerek atlanabilmeli; kullanıcının
        // "bağlama" demesi sistemin otomatiğinden önce gelir.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        await SeedLibraryAsync(context.CompanyId, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var boqId = await CreateSummaryAsync(client, context, suffix);

        var response = await client.PostAsync(
            $"/api/project-boqs/{boqId}/icmal-aktar",
            BuildUpload(
                ContractSummaryExcelParser.BuildTemplate(),
                [new { rowNumber = 3, positionId = (Guid?)null }]));

        response.EnsureSuccessStatusCode();

        var links = await GetLinksAsync(boqId);

        Assert.Null(links["P.01"]);
    }

    [Fact]
    public async Task Commit_RejectsPositionOfAnotherCompany()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        await SeedLibraryAsync(context.CompanyId, suffix);

        Guid foreignPositionId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (other, _, _) = await TestDataFactory.CreateCompanyStackAsync(
                db, $"{suffix}x");

            var position = NewPosition(other.Id, $"YB-{suffix}", "Yabancı şirket pozu");
            db.EngineeringPositions.Add(position);
            await db.SaveChangesAsync();

            foreignPositionId = position.Id;
        }

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var boqId = await CreateSummaryAsync(client, context, suffix);

        var response = await client.PostAsync(
            $"/api/project-boqs/{boqId}/icmal-aktar",
            BuildUpload(
                ContractSummaryExcelParser.BuildTemplate(),
                [new { rowNumber = 4, positionId = foreignPositionId }]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Reddedilen aktarım mevcut icmale dokunmamalı.
        var links = await GetLinksAsync(boqId);
        Assert.Equal(SeedPositionCode, Assert.Single(links).Key);
    }

    [Fact]
    public async Task Commit_LinksNewlyCreatedCustomPosition()
    {
        // P4'ün asıl vaadi: kütüphanede karşılığı olmayan satır için
        // özel poz açılıyor ve aynı aktarımda o poza bağlanıyor.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var boqId = await CreateSummaryAsync(client, context, suffix);

        var custom = await client.PostAsJsonAsync("/api/engineering-positions/custom", new
        {
            companyId = context.CompanyId,
            name = "200 mm galvaniz kablo tavası",
            unit = "m",
            discipline = 0,
            unitPrice = 460m,
            year = 2026
        });

        custom.EnsureSuccessStatusCode();
        var created = await custom.Content.ReadFromJsonAsync<JsonElement>();
        var positionId = created.GetProperty("id").GetGuid();

        var response = await client.PostAsync(
            $"/api/project-boqs/{boqId}/icmal-aktar",
            BuildUpload(
                ContractSummaryExcelParser.BuildTemplate(),
                [new { rowNumber = 7, positionId }]));

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, payload.GetProperty("linkedCount").GetInt32());

        var links = await GetLinksAsync(boqId);
        Assert.Equal(positionId, links["KT.01"]);
    }

    [Fact]
    public async Task Commit_WithBrokenDecisionPayload_WritesNothing()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var boqId = await CreateSummaryAsync(client, context, suffix);

        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(ContractSummaryExcelParser.BuildTemplate());
        file.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        content.Add(file, "file", "icmal.xlsx");
        content.Add(new StringContent("bu json değil", Encoding.UTF8, "text/plain"), "matches");

        var response = await client.PostAsync(
            $"/api/project-boqs/{boqId}/icmal-aktar", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var links = await GetLinksAsync(boqId);
        Assert.Equal(SeedPositionCode, Assert.Single(links).Key);
    }
}
