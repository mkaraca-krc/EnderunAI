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
/// Serbest metinden poz eşleştirme.
///
/// Asıl güvence şu: dil modeli ADAY ÜRETEMEZ, yalnızca kütüphaneden
/// gelen listeyi sıralar. Model listede olmayan bir poz numarası
/// döndürürse doğrulamada elenir. Böylece "uydurma poz" yapısal olarak
/// imkânsız hale gelir — istem metnine güvenmek zorunda kalmayız.
/// </summary>
[Collection("Integration")]
public sealed class PositionMatchTests(DatabaseFixture fixture)
{
    /// <summary>Verilen metni aynen döndüren sahte model.</summary>
    private sealed class FakeLlm(string? response, bool configured = true) : IHizirLlmClient
    {
        public bool IsConfigured => configured;
        public string ModelId => "test";
        public int CallCount { get; private set; }

        public Task<LlmCompletion> CompleteAsync(
            string systemPrompt,
            IReadOnlyList<LlmMessage> messages,
            IReadOnlyList<LlmToolDefinition> tools,
            CancellationToken cancellationToken = default)
        {
            CallCount++;

            return Task.FromResult(new LlmCompletion(response, [], 0, 0));
        }
    }

    private sealed class ThrowingLlm : IHizirLlmClient
    {
        public bool IsConfigured => true;
        public string ModelId => "test";

        public Task<LlmCompletion> CompleteAsync(
            string systemPrompt,
            IReadOnlyList<LlmMessage> messages,
            IReadOnlyList<LlmToolDefinition> tools,
            CancellationToken cancellationToken = default)
            => throw new HttpRequestException("model erişilemedi");
    }

    private static PositionMatchService CreateService(AppDbContext db, IHizirLlmClient llm)
        => new(db, new PositionPriceService(db), llm,
            NullLogger<PositionMatchService>.Instance);

    private static async Task<Guid> SeedLibraryAsync(AppDbContext db, string suffix)
    {
        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        void Add(string officialCode, string name, string unit, decimal price)
        {
            var position = new EngineeringPosition
            {
                CompanyId = company.Id,
                Code = $"{officialCode}-{suffix}",
                Name = name,
                Unit = unit,
                Source = EngineeringPositionSource.Official,
                Discipline = EngineeringPositionDiscipline.Electrical,
                Status = EngineeringPositionStatus.Active,
                OfficialInstitution = "ÇŞB",
                OfficialCode = officialCode,
                SearchKeywords = $"{officialCode} {name}"
            };

            db.EngineeringPositions.Add(position);

            db.PositionUnitPrices.Add(new PositionUnitPrice
            {
                EngineeringPosition = position,
                Year = 2026,
                Institution = PositionPriceInstitution.Csb,
                Component = PositionPriceComponent.Total,
                UnitPrice = price
            });
        }

        Add("35.200.4001", "1x40 mm2 NYY kablo çekilmesi", "m", 412.50m);
        Add("35.200.4002", "1x16 mm2 NYY kablo çekilmesi", "m", 188.75m);
        Add("35.200.5001", "1x40 mm2 NYA kablo çekilmesi", "m", 355.20m);
        Add("35.100.1301", "Galvanizli ilave dikili tip sac pano", "Ad", 29_302.49m);
        Add("35.415.1610", "Konvansiyonel harici tip yangın ihbar butonu", "Ad", 1_782.16m);

        await db.SaveChangesAsync();

        return company.Id;
    }

    // -----------------------------------------------------------------
    // Deterministik eleme — model gerekmez
    // -----------------------------------------------------------------

    [Fact]
    public void Tokenize_SplitsApostropheAndKeepsNumbers()
    {
        var tokens = PositionMatcher.Tokenize("40'lık NYY kablo çekilmesi");

        Assert.Contains("40", tokens);
        Assert.Contains("nyy", tokens);
        Assert.Contains("kablo", tokens);
    }

    [Fact]
    public void Normalize_HandlesTurkishUppercaseCorrectly()
    {
        // ToLowerInvariant "İ"yi olduğu gibi bırakır, "I"yı "i" yapar;
        // ikisi de poz tanımlarında eşleşme kaybettirir.
        Assert.Equal("iletken", PositionMatcher.Normalize("İLETKEN"));
        Assert.Equal("ızgara", PositionMatcher.Normalize("IZGARA"));
        Assert.Equal("şalter", PositionMatcher.Normalize("ŞALTER"));
    }

    [Fact]
    public void Rank_PrefersMatchingCrossSection()
    {
        // "40" ile "16" arasındaki fark kabloda belirleyicidir.
        var candidates = new[]
        {
            new MatchCandidate(Guid.NewGuid(), "A", "35.200.4001",
                "1x40 mm2 NYY kablo çekilmesi", "m", null, null),
            new MatchCandidate(Guid.NewGuid(), "B", "35.200.4002",
                "1x16 mm2 NYY kablo çekilmesi", "m", null, null)
        };

        var ranked = PositionMatcher.Rank("40'lık NYY kablo çekilmesi", candidates);

        Assert.Equal("35.200.4001", ranked[0].Candidate.OfficialCode);
        Assert.True(ranked[0].Score > ranked[1].Score);
    }

    [Fact]
    public void Rank_ExactPositionCode_WinsOutright()
    {
        var candidates = new[]
        {
            new MatchCandidate(Guid.NewGuid(), "A", "35.415.1610",
                "Konvansiyonel harici tip yangın ihbar butonu", "Ad", null, null),
            new MatchCandidate(Guid.NewGuid(), "B", "35.200.4001",
                "1x40 mm2 NYY kablo çekilmesi", "m", null, null)
        };

        var ranked = PositionMatcher.Rank("35.415.1610", candidates);

        Assert.Equal("35.415.1610", ranked[0].Candidate.OfficialCode);
    }

    [Fact]
    public void Rank_UnrelatedQuery_ReturnsNothing()
    {
        // Alakasız poz önermek, hiç önermemekten kötüdür.
        var candidates = new[]
        {
            new MatchCandidate(Guid.NewGuid(), "A", "35.200.4001",
                "1x40 mm2 NYY kablo çekilmesi", "m", null, null)
        };

        Assert.Empty(PositionMatcher.Rank("beton dökülmesi", candidates));
    }

    // -----------------------------------------------------------------
    // Servis — model doğrulaması
    // -----------------------------------------------------------------

    [Fact]
    public async Task Suggest_ReturnsCandidatesWithPrices()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companyId = await SeedLibraryAsync(db, Guid.NewGuid().ToString("N")[..8]);

        var service = CreateService(db, new FakeLlm(null, configured: false));

        var result = await service.SuggestAsync(companyId, "40'lık NYY kablo çekilmesi");

        Assert.NotEmpty(result.Suggestions);
        Assert.Equal("35.200.4001", result.Suggestions[0].OfficialCode);
        Assert.Equal(412.50m, result.Suggestions[0].UnitPrice);
        Assert.False(result.AiUsed);
        Assert.Contains("yapılandırılmamış", result.AiSkippedReason);
    }

    [Fact]
    public async Task Suggest_AiOrdersCandidates()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companyId = await SeedLibraryAsync(db, Guid.NewGuid().ToString("N")[..8]);

        // Model, skor sıralamasından farklı bir sıra öneriyor.
        var llm = new FakeLlm("""
            {"sonuclar":[
              {"poz":"35.200.5001","gerekce":"NYA da kablo çekimidir"},
              {"poz":"35.200.4001","gerekce":"Kesit birebir uyuyor"}
            ]}
            """);

        var service = CreateService(db, llm);
        var result = await service.SuggestAsync(companyId, "40'lık NYY kablo çekilmesi");

        Assert.True(result.AiUsed);
        Assert.Equal("35.200.5001", result.Suggestions[0].OfficialCode);
        Assert.Equal(1, result.Suggestions[0].AiRank);
        Assert.Equal("NYA da kablo çekimidir", result.Suggestions[0].AiReason);
        Assert.Equal(2, result.Suggestions[1].AiRank);
    }

    [Fact]
    public async Task Suggest_AiInventedCode_IsRejected()
    {
        // ASIL GÜVENCE: model listede olmayan bir poz uydurursa elenmeli.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companyId = await SeedLibraryAsync(db, Guid.NewGuid().ToString("N")[..8]);

        var llm = new FakeLlm("""
            {"sonuclar":[
              {"poz":"99.999.9999","gerekce":"Bu poz uydurma"},
              {"poz":"35.200.4001","gerekce":"Gerçek aday"}
            ]}
            """);

        var service = CreateService(db, llm);
        var result = await service.SuggestAsync(companyId, "40'lık NYY kablo çekilmesi");

        Assert.DoesNotContain(result.Suggestions, x => x.OfficialCode == "99.999.9999");
        Assert.Equal("35.200.4001", result.Suggestions[0].OfficialCode);
        Assert.All(result.Suggestions, x => Assert.NotNull(x.OfficialCode));
    }

    [Fact]
    public async Task Suggest_AiOmitsSomeCandidates_TheyAreKeptAtTheEnd()
    {
        // Modelin sıralamadığı aday kaybolmamalı; kullanıcı görebilmeli.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companyId = await SeedLibraryAsync(db, Guid.NewGuid().ToString("N")[..8]);

        var llm = new FakeLlm("""
            {"sonuclar":[{"poz":"35.200.4001","gerekce":"Tek seçim"}]}
            """);

        var service = CreateService(db, llm);
        var result = await service.SuggestAsync(companyId, "NYY kablo çekilmesi");

        Assert.Equal(1, result.Suggestions[0].AiRank);
        Assert.Contains(result.Suggestions, x => x.AiRank is null);
    }

    [Fact]
    public async Task Suggest_AiReturnsGarbage_FallsBackToScoreOrder()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companyId = await SeedLibraryAsync(db, Guid.NewGuid().ToString("N")[..8]);

        var service = CreateService(db, new FakeLlm("bu bir JSON değil"));
        var result = await service.SuggestAsync(companyId, "40'lık NYY kablo çekilmesi");

        Assert.NotEmpty(result.Suggestions);
        Assert.Equal("35.200.4001", result.Suggestions[0].OfficialCode);
        Assert.All(result.Suggestions, x => Assert.Null(x.AiRank));
    }

    [Fact]
    public async Task Suggest_AiUnreachable_StillReturnsScoredCandidates()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companyId = await SeedLibraryAsync(db, Guid.NewGuid().ToString("N")[..8]);

        var service = CreateService(db, new ThrowingLlm());
        var result = await service.SuggestAsync(companyId, "40'lık NYY kablo çekilmesi");

        Assert.False(result.AiUsed);
        Assert.NotEmpty(result.Suggestions);
        Assert.Contains("ulaşılamadı", result.AiSkippedReason);
    }

    [Fact]
    public async Task Suggest_NoMatch_ReturnsEmptyWithGuidance()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companyId = await SeedLibraryAsync(db, Guid.NewGuid().ToString("N")[..8]);

        var llm = new FakeLlm("""{"sonuclar":[{"poz":"35.200.4001","gerekce":"x"}]}""");
        var service = CreateService(db, llm);

        var result = await service.SuggestAsync(companyId, "zirai gübreleme yapılması");

        Assert.Empty(result.Suggestions);
        Assert.Contains("özel poz", result.Explanation);

        // Aday yoksa modele hiç gidilmemeli.
        Assert.Equal(0, llm.CallCount);
    }

    [Fact]
    public async Task Suggest_EmptyQuery_IsRejectedWithoutCallingAi()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companyId = await SeedLibraryAsync(db, Guid.NewGuid().ToString("N")[..8]);

        var llm = new FakeLlm("""{"sonuclar":[]}""");
        var service = CreateService(db, llm);

        var result = await service.SuggestAsync(companyId, "   ");

        Assert.Empty(result.Suggestions);
        Assert.Equal(0, llm.CallCount);
    }
}
