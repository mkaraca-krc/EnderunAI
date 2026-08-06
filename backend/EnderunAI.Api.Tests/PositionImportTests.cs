using ClosedXML.Excel;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Engineering;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Poz kitabı toplu içe aktarma.
///
/// İki asıl güvence:
/// 1. <b>Sayı ayrıştırma.</b> "1.234" bin iki yüz otuz dört mü, bir
///    virgül iki üç dört mü? Yanlış yorum fiyatı bin kat şişirir. Bu
///    yüzden belirsiz kalan satır tahmin edilmez, hata sayılır.
/// 2. <b>Tekrar çalıştırılabilirlik.</b> Aynı kitabın ikinci kez
///    yüklenmesi poz çoğaltmamalı, yalnızca o yılın fiyatını
///    güncellemeli.
/// </summary>
[Collection("Integration")]
public sealed class PositionImportTests(DatabaseFixture fixture)
{
    /// <summary>Gerçek kitaplara benzeyen bir dosya kurar.</summary>
    private static MemoryStream BuildWorkbook(
        IEnumerable<(string Code, string Name, string Unit, object Price)> rows,
        string sheetName = "Birim Fiyatlar",
        int headerRow = 3)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);

        // Kitapların başında sık görülen serbest başlık satırları.
        sheet.Cell(1, 1).Value = "ÇEVRE VE ŞEHİRCİLİK BAKANLIĞI";
        sheet.Cell(2, 1).Value = "2025 YILI BİRİM FİYAT LİSTESİ";

        sheet.Cell(headerRow, 1).Value = "Poz No";
        sheet.Cell(headerRow, 2).Value = "Tanım";
        sheet.Cell(headerRow, 3).Value = "Birim";
        sheet.Cell(headerRow, 4).Value = "Birim Fiyat";

        var current = headerRow + 1;

        foreach (var row in rows)
        {
            sheet.Cell(current, 1).Value = row.Code;
            sheet.Cell(current, 2).Value = row.Name;
            sheet.Cell(current, 3).Value = row.Unit;

            switch (row.Price)
            {
                case decimal d:
                    sheet.Cell(current, 4).Value = d;
                    break;
                case double dbl:
                    sheet.Cell(current, 4).Value = dbl;
                    break;
                default:
                    sheet.Cell(current, 4).Value = row.Price?.ToString() ?? string.Empty;
                    break;
            }

            current++;
        }

        var memory = new MemoryStream();
        workbook.SaveAs(memory);
        memory.Position = 0;

        return memory;
    }

    private static PositionImportMapping DefaultMapping(int headerRow = 3) =>
        new("Birim Fiyatlar", headerRow, 1, 2, 3, 4);

    private static async Task<Guid> CreateCompanyAsync(AppDbContext db, string suffix)
    {
        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);
        return company.Id;
    }

    private static PositionImportOptions Options(Guid companyId, int year = 2025) =>
        new(companyId, year, PositionPriceInstitution.Csb,
            EngineeringPositionDiscipline.Electrical, "ÇŞB 2025");

    // ---------------------------------------------------------------
    // Sayı ayrıştırma — ağ ve veritabanı gerektirmez
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("1234,56", 1234.56)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1234.56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("168,75 TL", 168.75)]
    [InlineData("₺168,75", 168.75)]
    [InlineData("42", 42)]
    public void ParseNumericText_HandlesBothDecimalConventions(string input, double expected)
    {
        Assert.Equal((decimal)expected, PositionImportParser.ParseNumericText(input));
    }

    [Theory]
    [InlineData("1.234")]
    [InlineData("12.345")]
    public void ParseNumericText_AmbiguousThousandsGrouping_IsRejected(string input)
    {
        // "1.234" binlik ayırıcı da olabilir ondalık da. Tahmin etmek
        // fiyatı bin kat şişirebileceği için belirsizlik hata sayılır.
        Assert.Null(PositionImportParser.ParseNumericText(input));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1.2.3.4")]
    public void ParseNumericText_Garbage_ReturnsNull(string input)
    {
        Assert.Null(PositionImportParser.ParseNumericText(input));
    }

    // ---------------------------------------------------------------
    // Dosya inceleme ve ayrıştırma
    // ---------------------------------------------------------------

    [Fact]
    public void Inspect_DetectsHeaderRowAndColumns()
    {
        using var file = BuildWorkbook(
        [
            ("35.100", "NYY kablo çekilmesi", "MTR", 168.75m),
            ("35.101", "Pano montajı", "AD", 2450m)
        ]);

        var inspection = PositionImportParser.Inspect(file);

        Assert.Contains("Birim Fiyatlar", inspection.SheetNames);
        Assert.Equal(3, inspection.HeaderRowIndex);
        Assert.Equal(["Poz No", "Tanım", "Birim", "Birim Fiyat"], inspection.Headers);
        Assert.Equal(2, inspection.SampleRows.Count);
        Assert.Equal(2, inspection.TotalRowCount);
    }

    [Fact]
    public void Parse_InvalidRows_AreReportedNotDropped()
    {
        using var file = BuildWorkbook(
        [
            ("35.100", "Geçerli satır", "MTR", 168.75m),
            ("", "Poz numarası yok", "MTR", 10m),
            ("35.102", "", "MTR", 10m),
            ("35.103", "Fiyat metni bozuk", "MTR", "abc"),
            ("35.104", "Fiyat sıfır", "MTR", 0m),
            ("35.100", "Tekrar eden poz", "MTR", 99m)
        ]);

        var result = PositionImportParser.Parse(file, DefaultMapping());

        Assert.Equal(6, result.Rows.Count);
        Assert.Single(result.Rows.Where(x => x.IsValid));

        var errors = result.Rows.Where(x => !x.IsValid).ToList();
        Assert.Equal(5, errors.Count);
        Assert.Contains(errors, x => x.Error!.Contains("Poz numarası boş"));
        Assert.Contains(errors, x => x.Error!.Contains("tanımı boş"));
        Assert.Contains(errors, x => x.Error!.Contains("sayıya çevrilemedi"));
        Assert.Contains(errors, x => x.Error!.Contains("sıfır veya negatif"));
        Assert.Contains(errors, x => x.Error!.Contains("tekrar ediyor"));
    }

    [Fact]
    public void Parse_BlankSeparatorRows_AreSkippedSilently()
    {
        using var file = BuildWorkbook(
        [
            ("35.100", "Kablo", "MTR", 168.75m),
            ("", "", "", ""),
            ("35.101", "Pano", "AD", 2450m)
        ]);

        var result = PositionImportParser.Parse(file, DefaultMapping());

        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, x => Assert.True(x.IsValid));
    }

    // ---------------------------------------------------------------
    // Aktarım
    // ---------------------------------------------------------------

    [Fact]
    public async Task Preview_ShowsWhatWillHappen_WithoutWriting()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companyId = await CreateCompanyAsync(db, Guid.NewGuid().ToString("N")[..8]);

        using var file = BuildWorkbook(
        [
            ("35.100", "Kablo", "MTR", 168.75m),
            ("35.101", "", "AD", 2450m)
        ]);

        var parsed = PositionImportParser.Parse(file, DefaultMapping());
        var service = new PositionImportService(db);

        var preview = await service.PreviewAsync(parsed, Options(companyId));

        Assert.Equal(2, preview.TotalRows);
        Assert.Equal(1, preview.ValidRows);
        Assert.Equal(1, preview.InvalidRows);
        Assert.Equal(1, preview.NewPositions);

        // Önizleme hiçbir şey yazmamalı.
        Assert.False(await db.EngineeringPositions.AnyAsync(x => x.CompanyId == companyId));
    }

    [Fact]
    public async Task Commit_CreatesPositionsAndPrices()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companyId = await CreateCompanyAsync(db, Guid.NewGuid().ToString("N")[..8]);

        using var file = BuildWorkbook(
        [
            ("35.100", "NYY kablo çekilmesi", "MTR", 168.75m),
            ("35.101", "Pano montajı", "AD", 2450m)
        ]);

        var parsed = PositionImportParser.Parse(file, DefaultMapping());
        var result = await new PositionImportService(db).CommitAsync(parsed, Options(companyId));

        Assert.Equal(2, result.CreatedPositions);
        Assert.Equal(2, result.UpsertedPrices);
        Assert.Equal(0, result.SkippedRows);

        var positions = await db.EngineeringPositions
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .ToListAsync();

        Assert.Equal(2, positions.Count);
        Assert.All(positions, x => Assert.Equal("ÇŞB", x.OfficialInstitution));
        Assert.All(positions, x => Assert.Equal(EngineeringPositionSource.Official, x.Source));

        var cable = positions.Single(x => x.OfficialCode == "35.100");
        Assert.Equal("MTR", cable.Unit);
        Assert.Contains("kablo", cable.SearchKeywords!, StringComparison.OrdinalIgnoreCase);

        var price = await db.PositionUnitPrices
            .AsNoTracking()
            .SingleAsync(x => x.EngineeringPositionId == cable.Id);

        Assert.Equal(168.75m, price.UnitPrice);
        Assert.Equal(2025, price.Year);
        Assert.Equal(PositionPriceInstitution.Csb, price.Institution);
    }

    [Fact]
    public async Task Commit_SameBookTwice_DoesNotDuplicatePositions()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companyId = await CreateCompanyAsync(db, Guid.NewGuid().ToString("N")[..8]);

        var service = new PositionImportService(db);

        using (var first = BuildWorkbook([("35.100", "Kablo", "MTR", 168.75m)]))
        {
            await service.CommitAsync(
                PositionImportParser.Parse(first, DefaultMapping()), Options(companyId));
        }

        using (var second = BuildWorkbook([("35.100", "Kablo", "MTR", 175.00m)]))
        {
            var result = await service.CommitAsync(
                PositionImportParser.Parse(second, DefaultMapping()), Options(companyId));

            Assert.Equal(0, result.CreatedPositions);
            Assert.Equal(1, result.UpsertedPrices);
        }

        var positions = await db.EngineeringPositions
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .ToListAsync();

        Assert.Single(positions);

        var prices = await db.PositionUnitPrices
            .AsNoTracking()
            .Where(x => x.EngineeringPositionId == positions[0].Id)
            .ToListAsync();

        Assert.Single(prices);
        Assert.Equal(175.00m, prices[0].UnitPrice);
    }

    [Fact]
    public async Task Commit_NextYearBook_KeepsPreviousYearPrice()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companyId = await CreateCompanyAsync(db, Guid.NewGuid().ToString("N")[..8]);

        var service = new PositionImportService(db);

        using (var y2024 = BuildWorkbook([("35.100", "Kablo", "MTR", 120.00m)]))
        {
            await service.CommitAsync(
                PositionImportParser.Parse(y2024, DefaultMapping()),
                Options(companyId, 2024));
        }

        using (var y2025 = BuildWorkbook([("35.100", "Kablo", "MTR", 168.75m)]))
        {
            await service.CommitAsync(
                PositionImportParser.Parse(y2025, DefaultMapping()),
                Options(companyId, 2025));
        }

        var position = await db.EngineeringPositions
            .AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId);

        var prices = await db.PositionUnitPrices
            .AsNoTracking()
            .Where(x => x.EngineeringPositionId == position.Id)
            .OrderBy(x => x.Year)
            .ToListAsync();

        Assert.Equal(2, prices.Count);
        Assert.Equal(120.00m, prices[0].UnitPrice);
        Assert.Equal(168.75m, prices[1].UnitPrice);
    }

    [Fact]
    public async Task Preview_ChangedDescription_IsSurfacedNotSilent()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companyId = await CreateCompanyAsync(db, Guid.NewGuid().ToString("N")[..8]);

        var service = new PositionImportService(db);

        using (var first = BuildWorkbook([("35.100", "Eski tanım", "MTR", 120m)]))
        {
            await service.CommitAsync(
                PositionImportParser.Parse(first, DefaultMapping()), Options(companyId, 2024));
        }

        using var second = BuildWorkbook([("35.100", "Yeni tanım", "MTR", 168.75m)]);
        var parsed = PositionImportParser.Parse(second, DefaultMapping());

        var preview = await service.PreviewAsync(parsed, Options(companyId));

        var row = Assert.Single(preview.Rows);
        Assert.Equal(PositionImportAction.UpdatePositionAndPrice, row.Action);
        Assert.Equal("Eski tanım", row.ExistingName);
        Assert.Equal(1, preview.DescriptionChanges);
    }

    [Fact]
    public async Task Commit_InvalidRows_AreSkippedButValidOnesLand()
    {
        // Tek bozuk satır tüm kitabı reddettirmemeli.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companyId = await CreateCompanyAsync(db, Guid.NewGuid().ToString("N")[..8]);

        using var file = BuildWorkbook(
        [
            ("35.100", "Geçerli", "MTR", 168.75m),
            ("35.101", "Bozuk fiyat", "AD", "abc"),
            ("35.102", "Geçerli 2", "AD", 2450m)
        ]);

        var parsed = PositionImportParser.Parse(file, DefaultMapping());
        var result = await new PositionImportService(db).CommitAsync(parsed, Options(companyId));

        Assert.Equal(2, result.CreatedPositions);
        Assert.Equal(1, result.SkippedRows);

        Assert.Equal(
            2,
            await db.EngineeringPositions.CountAsync(x => x.CompanyId == companyId));
    }

    [Fact]
    public async Task Commit_MissingUnit_FallsBackToPieceUnit()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companyId = await CreateCompanyAsync(db, Guid.NewGuid().ToString("N")[..8]);

        using var file = BuildWorkbook([("35.100", "Birimsiz kalem", "", 168.75m)]);

        var parsed = PositionImportParser.Parse(file, DefaultMapping());

        Assert.Contains(parsed.FileWarnings, x => x.Contains("birim boş"));

        await new PositionImportService(db).CommitAsync(parsed, Options(companyId));

        var position = await db.EngineeringPositions
            .AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId);

        Assert.Equal("AD", position.Unit);
    }

    [Fact]
    public async Task Commit_CompanyInstitution_MarksPositionAsCompanySpecific()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companyId = await CreateCompanyAsync(db, Guid.NewGuid().ToString("N")[..8]);

        using var file = BuildWorkbook([("OZL-1", "Şirkete özel iş", "AD", 500m)]);

        var parsed = PositionImportParser.Parse(file, DefaultMapping());

        await new PositionImportService(db).CommitAsync(
            parsed,
            new PositionImportOptions(
                companyId, 2025, PositionPriceInstitution.Company,
                EngineeringPositionDiscipline.Electrical, null));

        var position = await db.EngineeringPositions
            .AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId);

        Assert.Equal(EngineeringPositionSource.Enderun, position.Source);
        Assert.Equal("Şirket", position.OfficialInstitution);
    }
}
