using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Malzeme talebinin poz kütüphanesine bağlanması.
///
/// Talep bugüne kadar 9 kalemlik stok kartına bakıyordu; şirketin 23
/// binin üzerinde pozu ise hiç kullanılmıyordu. Poz bağı stok
/// kartından AYRI bir eksen: stok kartı "depoda hangi ürün", poz
/// "hangi imalat kalemi".
///
/// ÖZEL POZ KALICI: listede olmayan kalem için açılan poz şirket
/// kütüphanesine yazılıyor ve ikinci talepte aramada çıkıyor — aynı
/// kalem iki kez yazılmıyor.
/// </summary>
[Collection("Integration")]
public sealed class PurchaseRequestPositionTests(DatabaseFixture fixture)
{
    private sealed record Context(Guid CompanyId, Guid ProjectId);

    private Task<HttpClient> ClientAsync() =>
        AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        return new Context(project.CompanyId, project.Id);
    }

    private async Task<Guid> CreatePositionAsync(
        Context context,
        string code,
        string name,
        string unit = "M",
        EngineeringPositionSource source = EngineeringPositionSource.Official)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var position = new EngineeringPosition
        {
            CompanyId = context.CompanyId,
            Code = code,
            Name = name,
            Unit = unit,
            Source = source,
            Discipline = EngineeringPositionDiscipline.Electrical,
            Status = EngineeringPositionStatus.Active,
            OfficialInstitution = source == EngineeringPositionSource.Official
                ? "ÇŞB"
                : "Şirket",
            SearchKeywords = $"{code} {name}"
        };

        db.EngineeringPositions.Add(position);
        await db.SaveChangesAsync();

        return position.Id;
    }

    private static object RequestBody(
        Context context, object[] items) => new
    {
        companyId = context.CompanyId,
        projectId = context.ProjectId,
        requestType = 0,
        requestDate = DateTime.UtcNow.Date,
        neededByDate = (DateTime?)null,
        requestedByName = "Saha Şefi",
        description = (string?)null,
        priority = 1,
        items
    };

    private static object Line(
        string description,
        Guid? positionId = null,
        string unit = "M",
        decimal quantity = 10m) => new
    {
        materialDescription = description,
        quantity,
        unit,
        requestedDeliveryDate = (DateTime?)null,
        notes = (string?)null,
        engineeringPositionId = positionId
    };

    // ---------------- Poz bağı ----------------

    /// <summary>Poz seçilen talep kalemi pozu taşıyor.</summary>
    [Fact]
    public async Task RequestLine_CarriesTheSelectedPosition()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var positionId = await CreatePositionAsync(
            context, $"CSG-{suffix}", "NYY kablo 3x2,5 mm2");

        var response = await client.PostAsJsonAsync("/api/purchase-requests",
            RequestBody(context, [Line("NYY kablo 3x2,5", positionId)]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var requestId = JsonDocument
            .Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var line = await db.PurchaseRequestItems.AsNoTracking()
            .SingleAsync(x => x.PurchaseRequestId == requestId);

        Assert.Equal(positionId, line.EngineeringPositionId);

        // Stok kartı ekseni bağımsız: poz seçmek kartı doldurmuyor.
        Assert.Null(line.InventoryItemId);
    }

    /// <summary>
    /// POZSUZ TALEP HÂLÂ AÇILABİLİR: acil bir ihtiyaç, poz tanımlanana
    /// kadar bekleyemez.
    /// </summary>
    [Fact]
    public async Task RequestLine_WithoutPosition_IsStillAccepted()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync("/api/purchase-requests",
            RequestBody(context, [Line("Acil conta, ölçü sahada alınacak")]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Başka şirketin pozu talebe bağlanamıyor.</summary>
    [Fact]
    public async Task PositionOfAnotherCompany_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        var otherSuffix = Guid.NewGuid().ToString("N")[..8];
        var other = await CreateContextAsync(otherSuffix);

        var foreignPositionId = await CreatePositionAsync(
            other, $"CSG-{otherSuffix}", "Başka şirketin pozu");

        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync("/api/purchase-requests",
            RequestBody(context, [Line("Kablo", foreignPositionId)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("bu şirkete ait değil",
            await response.Content.ReadAsStringAsync());
    }

    // ---------------- Arama ----------------

    /// <summary>
    /// EŞİT İLGİDE ÖZEL POZ ÖNCE: aynı adı taşıyan iki pozdan şirketin
    /// kendi açtığı üstte çıkıyor ki bir kez açılan kalem ikinci
    /// talepte yeniden açılmasın. Sıralamanın kendisi ilgiye göre;
    /// bu yalnızca eşitlik bozucusu.
    /// </summary>
    [Fact]
    public async Task PositionSearch_PutsCustomPositionsFirstOnEqualRelevance()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        // Adlar birebir aynı: benzerlik eşit kalsın, ayrımı yalnız
        // kaynak yapsın. Kod sırası bilerek ters.
        await CreatePositionAsync(
            context, $"AAA-{suffix}", $"Galvaniz tava {suffix}");

        var custom = await CreatePositionAsync(
            context, $"ZZZ-{suffix}", $"Galvaniz tava {suffix}",
            source: EngineeringPositionSource.Enderun);

        var rows = await SearchAsync(client, context, $"galvaniz tava {suffix}");

        Assert.Equal(2, rows.Count);
        Assert.Equal(custom, rows[0].GetProperty("id").GetGuid());
        Assert.True(rows[0].GetProperty("isCustom").GetBoolean());
        Assert.Equal("Özel", rows[0].GetProperty("sourceName").GetString());
        Assert.False(rows[1].GetProperty("isCustom").GetBoolean());
    }

    /// <summary>
    /// Aramasız çağrı kütüphaneyi dökmüyor: 23 binin üzerinde poz var.
    /// </summary>
    [Fact]
    public async Task PositionSearch_WithoutTerm_ReturnsNothing()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        await CreatePositionAsync(context, $"CSG-{suffix}", "Kablo kanalı");

        var rows = await (await client.GetAsync(
            $"/api/purchase-requests/poz-ara?companyId={context.CompanyId}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Empty(rows.EnumerateArray());
    }

    /// <summary>Arama yalnız kimlik döndürüyor; fiyat ve saat dönmüyor.</summary>
    [Fact]
    public async Task PositionSearch_DoesNotLeakPriceOrLabourHours()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        await CreatePositionAsync(context, $"CSG-{suffix}", $"Pano montajı {suffix}");

        var raw = await (await client.GetAsync(
            $"/api/purchase-requests/poz-ara?companyId={context.CompanyId}" +
            $"&search=Pano montajı {suffix}"))
            .Content.ReadAsStringAsync();

        Assert.Contains("\"code\"", raw);
        Assert.DoesNotContain("LaborHours", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("price", raw, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------- Özel poz ----------------

    /// <summary>
    /// Özel poz KALICI: açılınca kütüphaneye yazılıyor, Enderun
    /// kaynağıyla işaretleniyor ve aramada bulunuyor. Tek talebe özel
    /// geçici kayıt üretilmiyor.
    /// </summary>
    [Fact]
    public async Task CustomPosition_IsPersistedAndFoundAgain()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var created = await client.PostAsJsonAsync(
            "/api/engineering-positions/custom", new
            {
                companyId = context.CompanyId,
                name = $"Özel askı aparatı {suffix}",
                unit = "AD",
                discipline = 99,
                notes = "Sahada imal edilecek",
                unitPrice = 450m
            });

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var payload = JsonDocument
            .Parse(await created.Content.ReadAsStringAsync()).RootElement;

        var positionId = payload.GetProperty("id").GetGuid();

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var position = await db.EngineeringPositions.AsNoTracking()
                .SingleAsync(x => x.Id == positionId);

            Assert.Equal(EngineeringPositionSource.Enderun, position.Source);
            Assert.Equal(EngineeringPositionStatus.Active, position.Status);
        }

        // İKİNCİ TALEP: aynı kalem aramada çıkıyor, yeniden açılmıyor.
        var rows = await (await client.GetAsync(
            $"/api/purchase-requests/poz-ara?companyId={context.CompanyId}" +
            $"&search=Özel askı aparatı {suffix}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var match = rows.EnumerateArray().Single();

        Assert.Equal(positionId, match.GetProperty("id").GetGuid());
        Assert.True(match.GetProperty("isCustom").GetBoolean());
    }

    /// <summary>
    /// Özel pozla açılan talep zinciri yürüyor: poz bağı talepten RFQ
    /// kalemine taşınıyor.
    /// </summary>
    [Fact]
    public async Task CustomPosition_FlowsIntoTheRfqChain()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var created = await client.PostAsJsonAsync(
            "/api/engineering-positions/custom", new
            {
                companyId = context.CompanyId,
                name = $"Özel kelepçe {suffix}",
                unit = "AD",
                discipline = 99
            });

        var positionId = JsonDocument
            .Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync("/api/purchase-requests",
            RequestBody(context,
                [Line($"Özel kelepçe {suffix}", positionId, unit: "AD")]));

        var requestId = JsonDocument
            .Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var line = await db.PurchaseRequestItems.AsNoTracking()
            .SingleAsync(x => x.PurchaseRequestId == requestId);

        Assert.Equal(positionId, line.EngineeringPositionId);
    }

    // ---------------- Benzerlik sıralaması ----------------

    /// <summary>
    /// KELİME SIRASI ÖNEMSİZ: "3x2,5 kablo" yazan kullanıcı "Kablo NYY
    /// 3x2,5" pozunu buluyor. Katı LIKE aramasında bu sorgu hiçbir şey
    /// döndürmüyordu.
    /// </summary>
    [Fact]
    public async Task Search_FindsPositionsRegardlessOfWordOrder()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var target = await CreatePositionAsync(
            context, $"KBL-{suffix}", $"Kablo NYY 3x2,5 mm2 {suffix}");

        var rows = await SearchAsync(client, context, $"3x2,5 kablo {suffix}");

        Assert.Contains(rows, x => x.GetProperty("id").GetGuid() == target);
    }

    /// <summary>
    /// TÜM KELİMELERİ İÇEREN ÖNCE: bir kısmını içerenler altta kalıyor.
    /// Sıralama alfabetik olsaydı kullanıcı aradığını listenin
    /// ortasında arardı.
    /// </summary>
    [Fact]
    public async Task Search_RanksFullMatchesAbovePartialOnes()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        // Kod sırası bilerek ters: alfabetik sıralamada "AAA" önce
        // gelirdi, oysa iki kelimeyi birden tutan "ZZZ".
        var partial = await CreatePositionAsync(
            context, $"AAA-{suffix}", $"Galvaniz tava {suffix}");

        var full = await CreatePositionAsync(
            context, $"ZZZ-{suffix}", $"Galvaniz kablo tavasi {suffix}");

        var rows = await SearchAsync(client, context, $"galvaniz kablo {suffix}");

        Assert.Equal(full, rows[0].GetProperty("id").GetGuid());
        Assert.Contains(rows, x => x.GetProperty("id").GetGuid() == partial);
    }

    /// <summary>
    /// TÜRKÇE DUYARLI: "ölçü" ile "olcu", "İ" ile "i" aynı sonucu
    /// veriyor. Türkçe klavyede en sık yapılan arama hatası bu.
    /// </summary>
    [Theory]
    [InlineData("olcu aleti")]
    [InlineData("ÖLÇÜ ALETİ")]
    [InlineData("Ölçü Aleti")]
    public async Task Search_IsTurkishInsensitive(string term)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var target = await CreatePositionAsync(
            context, $"OLC-{suffix}", $"Ölçü aleti kalibrasyonu {suffix}");

        var rows = await SearchAsync(client, context, $"{term} {suffix}");

        Assert.Contains(rows, x => x.GetProperty("id").GetGuid() == target);
    }

    /// <summary>
    /// KÜÇÜK YAZIM HATASINA TOLERANS: "kablo" yerine "kabbo" yazılsa
    /// da en yakın poz listeleniyor.
    /// </summary>
    [Fact]
    public async Task Search_ToleratesTypos()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        var target = await CreatePositionAsync(
            context, $"KBL-{suffix}", $"Kablo kanali {suffix}");

        var rows = await SearchAsync(client, context, $"kabbo kanali {suffix}");

        Assert.Contains(rows, x => x.GetProperty("id").GetGuid() == target);
    }

    /// <summary>
    /// BOŞ DÖNMÜYOR: hiçbir kelime tutmasa da en yakın pozlar geliyor.
    /// Boş liste "böyle bir poz yok" der ve gereksiz özel poz
    /// açtırırdı.
    /// </summary>
    [Fact]
    public async Task Search_FallsBackToNearestMatches()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await ClientAsync();

        await CreatePositionAsync(
            context, $"PNO-{suffix}", $"Pano montaji {suffix}");

        // Kelimelerin hiçbiri birebir tutmuyor.
        var rows = await SearchAsync(client, context, $"panoo montaj {suffix}");

        Assert.NotEmpty(rows);
    }

    private static async Task<List<JsonElement>> SearchAsync(
        HttpClient client, Context context, string term)
    {
        var response = await client.GetAsync(
            $"/api/purchase-requests/poz-ara?companyId={context.CompanyId}" +
            $"&search={Uri.EscapeDataString(term)}");

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        return payload.EnumerateArray().ToList();
    }
}
