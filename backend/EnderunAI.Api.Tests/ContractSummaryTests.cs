using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
/// Sözleşme icmali: kısım hiyerarşisi, üç bileşenli birim fiyat, onayda
/// kilitlenme, Excel içe aktarma ve revizyon.
///
/// İcmal ayrı bir varlık olarak açılmadı: mevcut keşif (ProjectBoq)
/// zaten revizyon, onay ve sözleşme tabanı kavramlarını taşıyordu ve
/// metraj takip ile hakediş oradan besleniyordu. İkinci bir taban
/// açmak "tek kaynak" güvencesini bozardı.
/// </summary>
[Collection("Integration")]
public sealed class ContractSummaryTests(DatabaseFixture fixture)
{
    private sealed record Context(
        Guid CompanyId, Guid ProjectId, Guid PanoSectionId, Guid TavaSectionId);

    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var pano = new ProjectHakedisSection
        {
            ProjectId = project.Id,
            Order = 1,
            Name = "Panolar / Tablolar"
        };

        var tava = new ProjectHakedisSection
        {
            ProjectId = project.Id,
            Order = 2,
            Name = "Kablo Tava"
        };

        db.ProjectHakedisSections.AddRange(pano, tava);
        await db.SaveChangesAsync();

        return new Context(project.CompanyId, project.Id, pano.Id, tava.Id);
    }

    private async Task<Guid> CreateSummaryAsync(
        HttpClient client, Context context, string suffix)
    {
        var response = await client.PostAsJsonAsync("/api/project-boqs", new
        {
            companyId = context.CompanyId,
            projectId = context.ProjectId,
            boqNumber = $"ICM-{suffix}",
            name = "Sözleşme İcmali",
            revisionNumber = 1,
            currencyCode = "TRY",
            description = (string?)null,
            notes = (string?)null,
            items = new object[]
            {
                new
                {
                    engineeringPositionId = (Guid?)null,
                    positionCode = "P.01",
                    description = "Ana dağıtım panosu",
                    unit = "Adet",
                    contractQuantity = 4m,
                    unitPrice = 0m,
                    itemType = 0,
                    category = (string?)null,
                    notes = (string?)null,
                    projectHakedisSectionId = context.PanoSectionId,
                    materialUnitPrice = 18_500m,
                    laborUnitPrice = 4_200m,
                    overheadUnitPrice = 2_300m
                },
                new
                {
                    engineeringPositionId = (Guid?)null,
                    positionCode = "KT.01",
                    description = "200 mm kablo tavası",
                    unit = "Metre",
                    contractQuantity = 100m,
                    unitPrice = 460m,
                    itemType = 0,
                    category = (string?)null,
                    notes = (string?)null,
                    projectHakedisSectionId = context.TavaSectionId,
                    materialUnitPrice = (decimal?)null,
                    laborUnitPrice = (decimal?)null,
                    overheadUnitPrice = (decimal?)null
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Summary_ComputesSectionSubtotalsAndGrandTotal()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var id = await CreateSummaryAsync(client, context, suffix);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/project-boqs/{id}");

        // 4 × (18.500 + 4.200 + 2.300) = 100.000
        // 100 × 460 = 46.000
        Assert.Equal(146_000m, detail.GetProperty("totalAmount").GetDecimal());

        var sections = detail.GetProperty("sections").EnumerateArray().ToList();

        var pano = sections.Single(x =>
            x.GetProperty("id").GetGuid() == context.PanoSectionId);
        Assert.Equal(100_000m, pano.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(74_000m, pano.GetProperty("materialAmount").GetDecimal());
        Assert.Equal(16_800m, pano.GetProperty("laborAmount").GetDecimal());
        Assert.Equal(9_200m, pano.GetProperty("overheadAmount").GetDecimal());

        var tava = sections.Single(x =>
            x.GetProperty("id").GetGuid() == context.TavaSectionId);
        Assert.Equal(46_000m, tava.GetProperty("totalAmount").GetDecimal());

        Assert.Equal(0, detail.GetProperty("unsectionedItemCount").GetInt32());
    }

    /// <summary>
    /// Bileşen verilmeyen kalemde tek birim fiyat malzemeye yazılır ve
    /// toplam değişmez — eski istemcilerin gönderdiği kayıtlar aynen
    /// çalışmaya devam etmeli.
    /// </summary>
    [Fact]
    public async Task Summary_SinglePriceGoesToMaterialWithoutChangingTotal()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var id = await CreateSummaryAsync(client, context, suffix);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/project-boqs/{id}");

        var line = detail.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("positionCode").GetString() == "KT.01");

        Assert.Equal(460m, line.GetProperty("materialUnitPrice").GetDecimal());
        Assert.Equal(0m, line.GetProperty("laborUnitPrice").GetDecimal());
        Assert.Equal(460m, line.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(46_000m, line.GetProperty("totalAmount").GetDecimal());
    }

    [Fact]
    public async Task Summary_ApprovedIsLockedForEditing()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var id = await CreateSummaryAsync(client, context, suffix);

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/project-boqs/{id}/approve", null)).StatusCode);

        var update = await client.PutAsJsonAsync($"/api/project-boqs/{id}", new
        {
            name = "Değiştirilmiş",
            currencyCode = "TRY",
            description = (string?)null,
            notes = (string?)null,
            items = new object[]
            {
                new
                {
                    engineeringPositionId = (Guid?)null,
                    positionCode = "P.01",
                    description = "Ana dağıtım panosu",
                    unit = "Adet",
                    contractQuantity = 99m,
                    unitPrice = 1m,
                    itemType = 0,
                    category = (string?)null,
                    notes = (string?)null
                }
            }
        });

        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        Assert.Contains("revizyon",
            await update.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/project-boqs/{id}");
        Assert.True(detail.GetProperty("isLocked").GetBoolean());
        // Miktar oynamadı.
        Assert.Equal(146_000m, detail.GetProperty("totalAmount").GetDecimal());
    }

    [Fact]
    public async Task Summary_RejectsSectionFromAnotherProject()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var other = await CreateContextAsync(Guid.NewGuid().ToString("N")[..8]);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/project-boqs", new
        {
            companyId = context.CompanyId,
            projectId = context.ProjectId,
            boqNumber = $"ICM-X-{suffix}",
            name = "Hatalı icmal",
            revisionNumber = 1,
            currencyCode = "TRY",
            description = (string?)null,
            notes = (string?)null,
            items = new object[]
            {
                new
                {
                    engineeringPositionId = (Guid?)null,
                    positionCode = "P.01",
                    description = "Pano",
                    unit = "Adet",
                    contractQuantity = 1m,
                    unitPrice = 100m,
                    itemType = 0,
                    category = (string?)null,
                    notes = (string?)null,
                    // Başka projenin kısmı.
                    projectHakedisSectionId = other.PanoSectionId
                }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Revision_ClonesItemsAndFreezesSource()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var id = await CreateSummaryAsync(client, context, suffix);
        await client.PostAsync($"/api/project-boqs/{id}/approve", null);

        var response = await client.PostAsJsonAsync(
            $"/api/project-boqs/{id}/revizyon",
            new
            {
                amendmentNumber = "ZEY-01",
                amendmentDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                reason = "İlave kat imalatı"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var revisionId = payload.GetProperty("id").GetGuid();

        Assert.Equal(2, payload.GetProperty("revisionNumber").GetInt32());
        Assert.Equal(2, payload.GetProperty("itemCount").GetInt32());
        Assert.Equal(146_000m, payload.GetProperty("totalAmount").GetDecimal());

        var revision = await client.GetFromJsonAsync<JsonElement>(
            $"/api/project-boqs/{revisionId}");

        // Yeni revizyon taslak: düzenlenebilir.
        Assert.False(revision.GetProperty("isLocked").GetBoolean());
        Assert.True(revision.GetProperty("isCurrentRevision").GetBoolean());

        var source = await client.GetFromJsonAsync<JsonElement>($"/api/project-boqs/{id}");

        // Kaynak SİLİNMEDİ, donduruldu: geçmiş hakedişler ona dayanıyor.
        Assert.False(source.GetProperty("isCurrentRevision").GetBoolean());
        Assert.Equal(146_000m, source.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(2, source.GetProperty("itemCount").GetInt32());
    }

    [Fact]
    public async Task Revision_CannotBeCreatedTwiceFromSameSummary()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var id = await CreateSummaryAsync(client, context, suffix);
        await client.PostAsync($"/api/project-boqs/{id}/approve", null);

        var body = new
        {
            amendmentNumber = (string?)null,
            amendmentDate = (DateTime?)null,
            reason = (string?)null
        };

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync($"/api/project-boqs/{id}/revizyon", body)).StatusCode);

        var second = await client.PostAsJsonAsync($"/api/project-boqs/{id}/revizyon", body);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task SectionTemplates_ExposeMultipleProjectTypes()
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var templates = await client.GetFromJsonAsync<JsonElement>(
            "/api/hakedis-section-templates");

        var keys = templates.EnumerateArray()
            .Select(x => x.GetProperty("key").GetString())
            .ToList();

        Assert.Contains("konut", keys);
        Assert.Contains("endustriyel", keys);
        Assert.Contains("otel", keys);
        Assert.Contains("hastane", keys);

        var hastane = templates.EnumerateArray()
            .Single(x => x.GetProperty("key").GetString() == "hastane");

        var sections = hastane.GetProperty("sections").EnumerateArray()
            .Select(x => x.GetProperty("name").GetString() ?? string.Empty)
            .ToList();

        Assert.Contains(sections, x => x.Contains("İzole Güç"));
        Assert.Contains(sections, x => x.Contains("UPS"));
    }

    /// <summary>Eski uç NATURA listesini düz dizi olarak döndürmeye devam etmeli.</summary>
    [Fact]
    public async Task LegacySectionTemplateEndpoint_StillReturnsNaturaList()
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var legacy = await client.GetFromJsonAsync<JsonElement>(
            "/api/hakedis-section-template");

        Assert.Equal(12, legacy.GetArrayLength());
        Assert.Equal("Panolar / Tablolar",
            legacy.EnumerateArray().First().GetProperty("name").GetString());
    }

    // --- Excel içe aktarma ---

    private static MultipartFormDataContent BuildExcelUpload(byte[] bytes)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);

        file.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        content.Add(file, "file", "icmal.xlsx");
        return content;
    }

    [Fact]
    public async Task ExcelImport_PreviewReadsTemplateAndWritesNothing()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var id = await CreateSummaryAsync(client, context, suffix);

        // Şablonun kendisi geçerli bir girdi olmalı: örnek satırlarla
        // birlikte iniyor, kullanıcı onların üzerine yazıyor.
        var template = ContractSummaryExcelParser.BuildTemplate();

        var response = await client.PostAsync(
            $"/api/project-boqs/{id}/icmal-aktar/onizleme",
            BuildExcelUpload(template));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var preview = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2, preview.GetProperty("sectionCount").GetInt32());
        Assert.Equal(3, preview.GetProperty("itemCount").GetInt32());
        Assert.Equal(0, preview.GetProperty("errors").GetArrayLength());

        // ÖNİZLEME HİÇBİR ŞEY YAZMAZ: icmal olduğu gibi kalmalı.
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/project-boqs/{id}");
        Assert.Equal(2, detail.GetProperty("itemCount").GetInt32());
        Assert.Equal(146_000m, detail.GetProperty("totalAmount").GetDecimal());
    }

    [Fact]
    public async Task ExcelImport_CommitReplacesItemsAndCreatesSections()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var id = await CreateSummaryAsync(client, context, suffix);

        var response = await client.PostAsync(
            $"/api/project-boqs/{id}/icmal-aktar",
            BuildExcelUpload(ContractSummaryExcelParser.BuildTemplate()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/project-boqs/{id}");

        Assert.Equal(3, detail.GetProperty("itemCount").GetInt32());

        // Şablondaki "Panolar / Tablolar" projede zaten vardı; ada göre
        // eşleşmeli, ikinci bir kısım açılmamalı.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var panoCount = await db.ProjectHakedisSections
            .CountAsync(x => x.ProjectId == context.ProjectId &&
                             x.Name == "Panolar / Tablolar");

        Assert.Equal(1, panoCount);

        // 4×25.000 + 12×8.700 + 850×460 = 100.000 + 104.400 + 391.000
        Assert.Equal(595_400m, detail.GetProperty("totalAmount").GetDecimal());
    }

    [Fact]
    public async Task ExcelImport_IsRejectedOnApprovedSummary()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var id = await CreateSummaryAsync(client, context, suffix);
        await client.PostAsync($"/api/project-boqs/{id}/approve", null);

        var response = await client.PostAsync(
            $"/api/project-boqs/{id}/icmal-aktar",
            BuildExcelUpload(ContractSummaryExcelParser.BuildTemplate()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}

/// <summary>
/// Excel okuyucusunun saf birim testleri — veritabanı gerekmez.
/// </summary>
public sealed class ContractSummaryExcelParserTests
{
    private static Stream BuildWorkbook(Action<ClosedXML.Excel.IXLWorksheet> fill)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var sheet = workbook.Worksheets.Add("İcmal");

        sheet.Cell(1, 1).Value = "Kısım";
        sheet.Cell(1, 2).Value = "Poz No";
        sheet.Cell(1, 3).Value = "Tanım";
        sheet.Cell(1, 4).Value = "Birim";
        sheet.Cell(1, 5).Value = "Miktar";

        fill(sheet);

        var buffer = new MemoryStream();
        workbook.SaveAs(buffer);
        buffer.Position = 0;
        return buffer;
    }

    [Fact]
    public void Parser_RecognisesSectionHeaderByHashMark()
    {
        using var stream = BuildWorkbook(sheet =>
        {
            sheet.Cell(2, 2).Value = "# Busbar";
            sheet.Cell(3, 2).Value = "B.01";
            sheet.Cell(3, 3).Value = "Busbar montajı";
            sheet.Cell(3, 4).Value = "Metre";
            sheet.Cell(3, 5).Value = 10;
            sheet.Cell(3, 6).Value = 100;
        });

        var result = ContractSummaryExcelParser.Parse(stream);

        Assert.Equal(1, result.SectionCount);
        Assert.Equal(1, result.ItemCount);
        Assert.Equal("Busbar", result.Lines.Single(x => x.IsSectionHeader).SectionName);
        Assert.Equal("Busbar", result.Lines.Single(x => !x.IsSectionHeader).SectionName);
    }

    /// <summary>
    /// Bozuk satır tüm dosyayı reddettirmez: o satır hataya yazılır,
    /// kalanlar okunmaya devam eder.
    /// </summary>
    [Fact]
    public void Parser_IsolatesBadRowsWithoutLosingGoodOnes()
    {
        using var stream = BuildWorkbook(sheet =>
        {
            sheet.Cell(2, 2).Value = "A.01";
            sheet.Cell(2, 3).Value = "Geçerli satır";
            sheet.Cell(2, 4).Value = "Adet";
            sheet.Cell(2, 5).Value = 5;
            sheet.Cell(2, 6).Value = 200;

            // Miktar sayı değil.
            sheet.Cell(3, 2).Value = "A.02";
            sheet.Cell(3, 3).Value = "Bozuk satır";
            sheet.Cell(3, 4).Value = "Adet";
            sheet.Cell(3, 5).Value = "abc";

            // Birim boş.
            sheet.Cell(4, 2).Value = "A.03";
            sheet.Cell(4, 3).Value = "Birimsiz satır";

            sheet.Cell(5, 2).Value = "A.04";
            sheet.Cell(5, 3).Value = "İkinci geçerli satır";
            sheet.Cell(5, 4).Value = "Metre";
            sheet.Cell(5, 5).Value = 3;
            sheet.Cell(5, 6).Value = 100;
        });

        var result = ContractSummaryExcelParser.Parse(stream);

        Assert.Equal(2, result.ItemCount);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, x => x.RowNumber == 3);
        Assert.Contains(result.Errors, x => x.RowNumber == 4);
        Assert.Equal(1_300m, result.TotalAmount);
    }

    /// <summary>
    /// Metin hücrede hem Türkçe ("1.234,56") hem invariant ("1234.56")
    /// biçim okunabilmeli. Noktayı koşulsuz silmek ikincisini 123456
    /// yapardı.
    /// </summary>
    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1234.56", 1234.56)]
    [InlineData("1234", 1234)]
    public void Parser_ReadsBothDecimalFormats(string text, double expected)
    {
        using var stream = BuildWorkbook(sheet =>
        {
            sheet.Cell(2, 2).Value = "A.01";
            sheet.Cell(2, 3).Value = "Fiyat biçimi";
            sheet.Cell(2, 4).Value = "Adet";
            sheet.Cell(2, 5).Value = 1;
            sheet.Cell(2, 6).SetValue(text);
        });

        var result = ContractSummaryExcelParser.Parse(stream);

        Assert.Empty(result.Errors);
        Assert.Equal((decimal)expected,
            result.Lines.Single(x => !x.IsSectionHeader).MaterialUnitPrice);
    }
}
