using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Engineering;
using EnderunAI.Api.Services.Hizir;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Otomatik eşleştirmenin KESİNLİK kuralı ve toplu eşleştirme.
///
/// Asıl güvence: sistem yalnızca tartışmasız durumda kendi seçiyor.
/// Birbirine yakın iki aday arasından sistemin seçmesi, yanlış pozla
/// fiyatlanmış bir keşif üretir ve bunu sonradan fark etmek çok zordur;
/// o yüzden belirsizlikte karar insana bırakılır.
/// </summary>
[Collection("Integration")]
public sealed class PositionMatchCertaintyTests(DatabaseFixture fixture)
{
    private sealed class OffLlm : IHizirLlmClient
    {
        public bool IsConfigured => false;
        public string ModelId => "kapalı";

        public Task<LlmCompletion> CompleteAsync(
            string systemPrompt,
            IReadOnlyList<LlmMessage> messages,
            IReadOnlyList<LlmToolDefinition> tools,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static PositionMatchService CreateService(AppDbContext db)
        => new(db, new PositionPriceService(db), new OffLlm(),
            NullLogger<PositionMatchService>.Instance);

    private static async Task<Guid> SeedAsync(
        AppDbContext db, string suffix, params (string Code, string Name, string Unit)[] rows)
    {
        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        foreach (var (code, name, unit) in rows)
        {
            var position = new EngineeringPosition
            {
                CompanyId = company.Id,
                Code = $"{code}-{suffix}",
                Name = name,
                Unit = unit,
                Source = EngineeringPositionSource.Official,
                Discipline = EngineeringPositionDiscipline.Electrical,
                Status = EngineeringPositionStatus.Active,
                OfficialInstitution = "ÇŞB",
                OfficialCode = code,
                SearchKeywords = $"{code} {name}"
            };

            db.EngineeringPositions.Add(position);

            db.PositionUnitPrices.Add(new PositionUnitPrice
            {
                EngineeringPosition = position,
                Year = 2026,
                Institution = PositionPriceInstitution.Csb,
                Component = PositionPriceComponent.Total,
                UnitPrice = 100m
            });
        }

        await db.SaveChangesAsync();

        return company.Id;
    }

    [Fact]
    public async Task ExactCode_IsCertain()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var companyId = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8],
            ("35.415.1610", "Konvansiyonel harici tip yangın ihbar butonu", "Ad"),
            ("35.100.1301", "Galvanizli ilave dikili tip sac pano", "Ad"));

        var result = await CreateService(db)
            .SuggestAsync(companyId, "35.415.1610", useAi: false);

        Assert.True(result.IsCertain);
        Assert.Contains("birebir", result.CertaintyReason);
        Assert.Equal("35.415.1610", result.Suggestions[0].OfficialCode);
    }

    [Fact]
    public async Task TwoCloseCandidates_AreNotCertain()
    {
        // "40" ile "16" dışında her şeyi aynı olan iki poz: sistem
        // kendi başına seçmemeli.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var companyId = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8],
            ("35.200.4001", "NYY kablo çekilmesi tesisatta", "m"),
            ("35.200.4002", "NYY kablo çekilmesi kanalda", "m"));

        var result = await CreateService(db)
            .SuggestAsync(companyId, "NYY kablo çekilmesi", useAi: false);

        Assert.False(result.IsCertain);
        Assert.Contains("yakın", result.CertaintyReason);
        Assert.True(result.Suggestions.Count >= 2);
    }

    [Fact]
    public async Task SingleStrongCandidate_IsCertain()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var companyId = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8],
            ("35.415.1610", "Konvansiyonel harici tip yangın ihbar butonu", "Ad"),
            ("35.100.1301", "Galvanizli ilave dikili tip sac pano", "Ad"));

        var result = await CreateService(db)
            .SuggestAsync(companyId, "yangın ihbar butonu konvansiyonel harici", useAi: false);

        Assert.True(result.IsCertain);
        Assert.Equal("35.415.1610", result.Suggestions[0].OfficialCode);
    }

    [Fact]
    public async Task WeakMatch_IsNotCertain()
    {
        // Tek kelime tutan zayıf eşleşme otomatik seçilmemeli.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var companyId = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8],
            ("35.100.1301", "Galvanizli ilave dikili tip sac pano", "Ad"));

        var result = await CreateService(db)
            .SuggestAsync(companyId, "pano", useAi: false);

        Assert.False(result.IsCertain);
    }

    [Fact]
    public async Task NoCandidates_IsNotCertain()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var companyId = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8],
            ("35.100.1301", "Galvanizli ilave dikili tip sac pano", "Ad"));

        var result = await CreateService(db)
            .SuggestAsync(companyId, "zirai gübreleme", useAi: false);

        Assert.False(result.IsCertain);
        Assert.Empty(result.Suggestions);
    }

    // -----------------------------------------------------------------
    // Toplu eşleştirme
    // -----------------------------------------------------------------

    [Fact]
    public async Task Bulk_MatchesEachRowIndependently()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var companyId = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8],
            ("35.415.1610", "Konvansiyonel harici tip yangın ihbar butonu", "Ad"),
            ("35.100.1301", "Galvanizli ilave dikili tip sac pano", "Ad"),
            ("35.190.1100", "Kablo tava sistemleri", "Kg"));

        var rows = new List<(int, string)>
        {
            (5, "yangın ihbar butonu konvansiyonel harici"),
            (6, "galvanizli ilave dikili tip sac pano"),
            (7, "kablo tava sistemleri"),
            (8, "zirai gübreleme yapılması")
        };

        var results = await CreateService(db).SuggestBulkAsync(companyId, rows);

        Assert.Equal(4, results.Count);

        Assert.Equal("35.415.1610",
            results.Single(x => x.RowNumber == 5).Suggestions[0].OfficialCode);
        Assert.Equal("35.100.1301",
            results.Single(x => x.RowNumber == 6).Suggestions[0].OfficialCode);
        Assert.Equal("35.190.1100",
            results.Single(x => x.RowNumber == 7).Suggestions[0].OfficialCode);

        // Karşılığı olmayan satır boş dönmeli, zorla eşleştirilmemeli.
        var unmatched = results.Single(x => x.RowNumber == 8);
        Assert.Empty(unmatched.Suggestions);
        Assert.False(unmatched.IsCertain);
    }

    [Fact]
    public async Task Bulk_RowWithPositionCode_IsCertain()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var companyId = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8],
            ("35.415.1610", "Konvansiyonel harici tip yangın ihbar butonu", "Ad"),
            ("35.100.1301", "Galvanizli ilave dikili tip sac pano", "Ad"));

        var results = await CreateService(db).SuggestBulkAsync(
            companyId, [(3, "35.415.1610 yangın ihbar butonu")]);

        var row = Assert.Single(results);

        Assert.True(row.IsCertain);
        Assert.Equal("35.415.1610", row.Suggestions[0].OfficialCode);
    }

    [Fact]
    public async Task Bulk_EmptyInput_ReturnsEmpty()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Empty(await CreateService(db).SuggestBulkAsync(Guid.NewGuid(), []));
    }

    [Fact]
    public async Task Bulk_IncludesLibraryPrices()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var companyId = await SeedAsync(db, Guid.NewGuid().ToString("N")[..8],
            ("35.415.1610", "Konvansiyonel harici tip yangın ihbar butonu", "Ad"));

        var results = await CreateService(db).SuggestBulkAsync(
            companyId, [(1, "yangın ihbar butonu konvansiyonel")], 2026);

        Assert.Equal(100m, results[0].Suggestions[0].UnitPrice);
    }
}
