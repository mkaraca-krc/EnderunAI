using ClosedXML.Excel;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Engineering;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Sdk;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Hazır eşleme profilleri: TEDAŞ (Excel) ve ÇŞB (PDF).
///
/// TEDAŞ tarafında asıl güvence grup başlıklarının poz sanılmaması ve
/// dört fiyat kolonunun ayrı bileşenler olarak korunması. Malzeme ve
/// montajı tek sayıya indirmek keşifte malzeme/montaj ayrımını
/// imkânsız kılardı.
///
/// ÇŞB tarafında asıl güvence fiyatların doğru kolona düşmesi:
/// elektrik sayfalarında en sağdaki kolon "montaj bedeli"dir ve keşif
/// birim fiyatı DEĞİLDİR. Ayrıca fiyatlar sağa dayalı olduğu için
/// kolon eşlemesi sol kenardan yapılamaz.
/// </summary>
[Collection("Integration")]
public sealed class BookImportProfileTests(DatabaseFixture fixture)
{
    /// <summary>Gerçek kitap; yoksa PDF testleri atlanır.</summary>
    private const string CsbBookPath = "/var/www/enderun-ai/poz-dosyalari/2026.pdf";

    /// <summary>TEDAŞ kitabının düzenini birebir taklit eden dosya.</summary>
    private static MemoryStream BuildTedasWorkbook()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("2026BF");

        sheet.Cell(1, 1).Value = "2026 YILI TEDAŞ BİRİM FİYAT";
        sheet.Cell(2, 1).Value = "ESKİ POZ";
        sheet.Cell(2, 2).Value = "YENİ POZ NO";
        sheet.Cell(3, 1).Value = "NO";

        void Write(
            int row, string? oldCode, int tedas, int main, int sub,
            string name, string? unit,
            double? material, double? labor, double? dismantle, double? remount,
            string? note = null)
        {
            if (oldCode is not null) sheet.Cell(row, 1).Value = oldCode;
            sheet.Cell(row, 2).Value = tedas;
            sheet.Cell(row, 3).Value = main;
            sheet.Cell(row, 4).Value = sub;
            sheet.Cell(row, 5).Value = name;
            if (unit is not null) sheet.Cell(row, 6).Value = unit;
            if (material is not null) sheet.Cell(row, 7).Value = material.Value;
            if (labor is not null) sheet.Cell(row, 8).Value = labor.Value;
            if (dismantle is not null) sheet.Cell(row, 9).Value = dismantle.Value;
            if (remount is not null) sheet.Cell(row, 10).Value = remount.Value;
            if (note is not null) sheet.Cell(row, 11).Value = note;
        }

        // Kategori başlıkları: fiyatsız ve alt numarası yüzün katı.
        Write(4, "5", 85, 105, 1000, "DİREKLER", null, null, null, null, null);
        Write(5, "5.8", 85, 105, 1200, "AĞAÇ DİREKLER", null, null, null, null, null);

        // Gerçek pozlar.
        Write(6, null, 85, 105, 1201, "8-8.5 mt. Ağaç Direkler", "Ad",
            2397.3149999999996, 3041.2795223615944, 829.4919429977057, 2419.5721170480074);
        Write(7, null, 85, 105, 1202, "9-9.5 mt. Ağaç Direkler", "Ad",
            3172.5, 3944.0594985079592, 1086.4820824306958, 3218.5412450952886);

        // Yalnız demontaj bedeli olan poz (kitapta gerçekten var).
        Write(8, null, 85, 105, 1101, "Demir Direkler", "kg",
            null, null, 30.564047999999996, null);

        // Fiyatsız ama alt numarası yüzün katı DEĞİL: şüpheli.
        Write(9, null, 85, 105, 1305, "Klemens Kilidi", "Ad", null, null, null, null);

        var memory = new MemoryStream();
        workbook.SaveAs(memory);
        memory.Position = 0;

        return memory;
    }

    private static async Task<Guid> CreateCompanyAsync(AppDbContext db, string suffix)
    {
        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);
        return company.Id;
    }

    [Fact]
    public void Profiles_AreExposedWithInstitution()
    {
        var profiles = new BookImportService(null!).GetProfiles();

        Assert.Contains(profiles, x => x.Key == TedasBfkParser.ProfileKey
                                       && x.Institution == PositionPriceInstitution.Tedas);
        Assert.Contains(profiles, x => x.Key == CsbBfkPdfParser.ProfileKey
                                       && x.Institution == PositionPriceInstitution.Csb);
    }

    [Fact]
    public void Tedas_BuildsCompositeCodeFromThreeColumns()
    {
        using var file = BuildTedasWorkbook();
        var result = TedasBfkParser.Parse(file);

        var row = result.Rows.Single(x => x.Code == "85.105.1201");

        Assert.Equal("8-8.5 mt. Ağaç Direkler", row.Name);
        Assert.Equal("Ad", row.Unit);
    }

    [Fact]
    public void Tedas_KeepsFourPriceComponentsSeparately()
    {
        using var file = BuildTedasWorkbook();
        var result = TedasBfkParser.Parse(file);

        var row = result.Rows.Single(x => x.Code == "85.105.1201");

        Assert.Equal(4, row.Prices.Count);
        Assert.Equal(
            2397.3150m,
            row.Prices.Single(x => x.Component == PositionPriceComponent.Material).UnitPrice);
        Assert.Equal(
            3041.2795m,
            row.Prices.Single(x => x.Component == PositionPriceComponent.Labor).UnitPrice);
        Assert.Equal(
            829.4919m,
            row.Prices.Single(x => x.Component == PositionPriceComponent.Dismantle).UnitPrice);
        Assert.Equal(
            2419.5721m,
            row.Prices
                .Single(x => x.Component == PositionPriceComponent.RemountFromDismantled)
                .UnitPrice);
    }

    [Fact]
    public void Tedas_GroupHeaders_AreSkippedAndCarriedAsCategory()
    {
        using var file = BuildTedasWorkbook();
        var result = TedasBfkParser.Parse(file);

        Assert.Equal(2, result.GroupHeaderCount);
        Assert.DoesNotContain(result.Rows, x => x.Code == "85.105.1000");
        Assert.DoesNotContain(result.Rows, x => x.Code == "85.105.1200");

        // Başlık, kendisinden sonraki pozlara kategori olarak taşınır.
        Assert.Equal("AĞAÇ DİREKLER", result.Rows.Single(x => x.Code == "85.105.1201").Category);
    }

    [Fact]
    public void Tedas_PriceLessNonRoundRow_IsSuspiciousNotSilentlyDropped()
    {
        using var file = BuildTedasWorkbook();
        var result = TedasBfkParser.Parse(file);

        Assert.DoesNotContain(result.Rows, x => x.Code == "85.105.1305");
        Assert.Contains(result.SuspiciousLines, x => x.Contains("85.105.1305"));
    }

    [Fact]
    public void Tedas_DismantleOnlyRow_IsStillImported()
    {
        // Kitapta yalnızca sökme bedeli olan pozlar var; bunlar
        // atılmamalı, bileşeni neyse o saklanmalı.
        using var file = BuildTedasWorkbook();
        var result = TedasBfkParser.Parse(file);

        var row = result.Rows.Single(x => x.Code == "85.105.1101");

        Assert.Single(row.Prices);
        Assert.Equal(PositionPriceComponent.Dismantle, row.Prices[0].Component);
    }

    [Fact]
    public async Task Tedas_Import_WritesPositionsAndComponentPrices()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companyId = await CreateCompanyAsync(db, Guid.NewGuid().ToString("N")[..8]);

        var service = new BookImportService(db);

        using var file = BuildTedasWorkbook();
        var summary = await service.ImportAsync(
            TedasBfkParser.ProfileKey, file, companyId, 2026, "TEDAŞ 2026", null);

        Assert.Equal(3, summary.CreatedPositions);
        Assert.Equal(2, summary.GroupHeaders);
        Assert.Equal(9, summary.UpsertedPrices);

        var position = await db.EngineeringPositions
            .AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.OfficialCode == "85.105.1201");

        Assert.Equal("TEDAŞ", position.OfficialInstitution);

        var prices = await db.PositionUnitPrices
            .AsNoTracking()
            .Where(x => x.EngineeringPositionId == position.Id)
            .ToListAsync();

        Assert.Equal(4, prices.Count);
        Assert.All(prices, x => Assert.Equal(2026, x.Year));
        Assert.All(prices, x => Assert.Equal(PositionPriceInstitution.Tedas, x.Institution));
    }

    [Fact]
    public async Task Tedas_ImportTwice_DoesNotDuplicate()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companyId = await CreateCompanyAsync(db, Guid.NewGuid().ToString("N")[..8]);

        var service = new BookImportService(db);

        using (var first = BuildTedasWorkbook())
            await service.ImportAsync(
                TedasBfkParser.ProfileKey, first, companyId, 2026, null, null);

        using (var second = BuildTedasWorkbook())
        {
            var summary = await service.ImportAsync(
                TedasBfkParser.ProfileKey, second, companyId, 2026, null, null);

            Assert.Equal(0, summary.CreatedPositions);
        }

        Assert.Equal(
            3,
            await db.EngineeringPositions.CountAsync(x => x.CompanyId == companyId));

        var positionIds = await db.EngineeringPositions
            .Where(x => x.CompanyId == companyId)
            .Select(x => x.Id)
            .ToListAsync();

        Assert.Equal(
            9,
            await db.PositionUnitPrices.CountAsync(x => positionIds.Contains(x.EngineeringPositionId)));
    }

    [Fact]
    public async Task Resolve_TedasPosition_SumsMaterialAndLaborAndSaysSo()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companyId = await CreateCompanyAsync(db, Guid.NewGuid().ToString("N")[..8]);

        using var file = BuildTedasWorkbook();
        await new BookImportService(db).ImportAsync(
            TedasBfkParser.ProfileKey, file, companyId, 2026, null, null);

        var position = await db.EngineeringPositions
            .AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.OfficialCode == "85.105.1201");

        var resolution = await new PositionPriceService(db).ResolveAsync(position.Id, 2026);

        Assert.True(resolution.Found);
        Assert.Equal(2397.3150m + 3041.2795m, resolution.UnitPrice);
        Assert.Equal(2397.3150m, resolution.MaterialPrice);
        Assert.Equal(3041.2795m, resolution.LaborPrice);

        // Demontaj bedelleri toplama girmemeli ve bu açıkça söylenmeli.
        Assert.Contains("demontaj", resolution.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolve_DismantleOnlyPosition_RefusesToPriceIt()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companyId = await CreateCompanyAsync(db, Guid.NewGuid().ToString("N")[..8]);

        using var file = BuildTedasWorkbook();
        await new BookImportService(db).ImportAsync(
            TedasBfkParser.ProfileKey, file, companyId, 2026, null, null);

        var position = await db.EngineeringPositions
            .AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.OfficialCode == "85.105.1101");

        var resolution = await new PositionPriceService(db).ResolveAsync(position.Id, 2026);

        Assert.False(resolution.Found);
        Assert.Null(resolution.UnitPrice);
        Assert.Contains("demontaj", resolution.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------
    // ÇŞB PDF — gerçek kitap üzerinde regresyon koruması
    // -----------------------------------------------------------------

    /// <summary>
    /// Kitap bu makinede duruyorsa çalışır. Değerler kaynağın kendisiyle
    /// (sayfa 489 ve 271) elle karşılaştırılarak doğrulandı.
    /// </summary>
    [SkippableFact]
    public void Csb_ElectricalRows_MatchPublishedValues()
    {
        Skip.IfNot(File.Exists(CsbBookPath), "ÇŞB kitabı bu makinede yok.");

        using var file = File.OpenRead(CsbBookPath);
        var result = CsbBfkPdfParser.Parse(file, codePrefixFilter: "35.");

        var pano = result.Rows.Single(x => x.Code == "35.100.1301");

        Assert.Equal(
            29_302.49m,
            pano.Prices.Single(x => x.Component == PositionPriceComponent.Total).UnitPrice);

        // En sağdaki kolon montaj bedelidir, birim fiyat değildir.
        Assert.Equal(
            3_091.24m,
            pano.Prices.Single(x => x.Component == PositionPriceComponent.Labor).UnitPrice);

        var buton = result.Rows.Single(x => x.Code == "35.415.1610");

        Assert.Equal(
            1_782.16m,
            buton.Prices.Single(x => x.Component == PositionPriceComponent.Total).UnitPrice);
        Assert.Equal("Ad", buton.Unit);
        Assert.True(buton.UnitInherited);
    }

    [SkippableFact]
    public void Csb_RightAlignedColumns_AreMappedByRightEdge()
    {
        // 25.170.1211 fiyatları iki haneli olduğu için sol kenarları
        // kayıyor; sol kenara göre eşleme bu satırı yanlış kolona
        // atıyordu.
        Skip.IfNot(File.Exists(CsbBookPath), "ÇŞB kitabı bu makinede yok.");

        using var file = File.OpenRead(CsbBookPath);
        var result = CsbBfkPdfParser.Parse(file, codePrefixFilter: "25.");

        var row = result.Rows.Single(x => x.Code == "25.170.1211");

        Assert.Equal(
            78.50m,
            row.Prices.Single(x => x.Component == PositionPriceComponent.Total).UnitPrice);
        Assert.Equal(
            58.13m,
            row.Prices.Single(x => x.Component == PositionPriceComponent.Labor).UnitPrice);
    }

    [SkippableFact]
    public void Csb_NumbersInsideDescription_AreNotTreatedAsPrices()
    {
        // 25.175.1102 tanımında "0,02" geçiyor; bu bir fiyat değil.
        Skip.IfNot(File.Exists(CsbBookPath), "ÇŞB kitabı bu makinede yok.");

        using var file = File.OpenRead(CsbBookPath);
        var result = CsbBfkPdfParser.Parse(file, codePrefixFilter: "25.");

        var row = result.Rows.Single(x => x.Code == "25.175.1102");

        Assert.Equal(2, row.Prices.Count);
        Assert.DoesNotContain(row.Prices, x => x.UnitPrice == 0.02m);
        Assert.Equal(
            33_775.00m,
            row.Prices.Single(x => x.Component == PositionPriceComponent.Total).UnitPrice);
    }
}
