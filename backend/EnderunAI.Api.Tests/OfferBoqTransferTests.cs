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
/// Teklifin keşif icmaline aktarılması ve pozdan kalem ekleme (K5).
///
/// Aktarım TEK YÖNLÜdür ve bilinçli olarak AYRI bir kayıt üretir:
/// teklif satış belgesi, icmal ise hakedişin referansıdır. Aynı kaydı
/// paylaşsalardı teklifte yapılan bir düzeltme sözleşme metrajını
/// sessizce değiştirirdi.
///
/// Asıl güvence fiyat bileşenlerinin (malzeme/montaj/GG) bire bir
/// taşınması: icmalden hakedişe geçerken bu ayrım korunmazsa hakediş
/// kalemi bileşensiz kalır ve kâr analizi çöker.
/// </summary>
[Collection("Integration")]
public sealed class OfferBoqTransferTests(DatabaseFixture fixture)
{
    private async Task<(Guid CompanyId, Guid ProjectId, Guid PositionId)>
        CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var position = new EngineeringPosition
        {
            CompanyId = project.CompanyId,
            Code = $"POZ-{suffix}",
            Name = "NYY kablo çekilmesi",
            Unit = "MTR",
            Source = EngineeringPositionSource.Official,
            Discipline = EngineeringPositionDiscipline.Electrical,
            Status = EngineeringPositionStatus.Active,
            OfficialInstitution = "ÇŞB"
        };

        db.EngineeringPositions.Add(position);
        await db.SaveChangesAsync();

        return (project.CompanyId, project.Id, position.Id);
    }

    /// <summary>
    /// Bileşenleriyle birlikte tek kalemli bir teklif oluşturur.
    /// </summary>
    private static object BuildOfferPayload(
        Guid companyId,
        Guid projectId,
        decimal? material = null,
        decimal? labor = null,
        decimal? overhead = null) =>
        new
        {
            companyId,
            projectId,
            title = "Test teklifi",
            offerDate = new DateTime(2026, 5, 1),
            currency = "TRY",
            exchangeRate = 1m,
            items = new[]
            {
                new
                {
                    description = "NYY 3x2,5 kablo",
                    quantity = 100m,
                    unit = "MTR",
                    listPrice = 100m,
                    discountRate = 0m,
                    freightRate = 0m,
                    wasteRate = 0m,
                    financeRate = 0m,
                    generalExpenseRate = 0m,
                    profitRate = 0m,
                    materialUnitPrice = material,
                    laborUnitPrice = labor,
                    overheadUnitPrice = overhead
                }
            }
        };

    /// <summary>
    /// Bileşenler girilmişse icmale birebir taşınmalı.
    /// </summary>
    [Fact]
    public async Task Transfer_CarriesPriceComponents()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId, _) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var created = await client.PostAsJsonAsync(
            "/api/offers",
            BuildOfferPayload(companyId, projectId,
                material: 60m, labor: 30m, overhead: 10m));

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var offerId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var transferred = await client.PostAsJsonAsync(
            $"/api/offers/{offerId}/icmale-aktar",
            new { projectId, name = (string?)null });

        Assert.Equal(HttpStatusCode.OK, transferred.StatusCode);

        var result = await transferred.Content.ReadFromJsonAsync<JsonElement>();
        var boqId = result.GetProperty("projectBoqId").GetGuid();

        Assert.Equal(1, result.GetProperty("itemCount").GetInt32());
        // 100 birim × (60 + 30 + 10)
        Assert.Equal(10_000m, result.GetProperty("totalAmount").GetDecimal());

        using var verify = fixture.Factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        var item = await db.ProjectBoqItems
            .AsNoTracking()
            .SingleAsync(x => x.ProjectBoqId == boqId);

        Assert.Equal(60m, item.MaterialUnitPrice);
        Assert.Equal(30m, item.LaborUnitPrice);
        Assert.Equal(10m, item.OverheadUnitPrice);
        Assert.Equal(100m, item.UnitPrice);
        Assert.Equal(100m, item.ContractQuantity);

        var boq = await db.ProjectBoqs.AsNoTracking().SingleAsync(x => x.Id == boqId);
        Assert.Equal(offerId, boq.SourceOfferId);
        Assert.Equal(ProjectBoqStatus.Draft, boq.Status);
    }

    /// <summary>
    /// Bileşen girilmemişse tutarın tamamı malzemeye yazılmalı ve
    /// TOPLAM DEĞİŞMEMELİ; kullanıcı da bu varsayımdan haberdar
    /// edilmeli.
    /// </summary>
    [Fact]
    public async Task Transfer_WithoutComponents_PutsEverythingInMaterial()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId, _) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var created = await client.PostAsJsonAsync(
            "/api/offers", BuildOfferPayload(companyId, projectId));

        var offerId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var transferred = await client.PostAsJsonAsync(
            $"/api/offers/{offerId}/icmale-aktar", new { projectId });

        var result = await transferred.Content.ReadFromJsonAsync<JsonElement>();
        var boqId = result.GetProperty("projectBoqId").GetGuid();

        using var verify = fixture.Factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        var item = await db.ProjectBoqItems
            .AsNoTracking()
            .SingleAsync(x => x.ProjectBoqId == boqId);

        // Liste fiyatı 100, oran yok → satış fiyatı 100
        Assert.Equal(100m, item.MaterialUnitPrice);
        Assert.Equal(0m, item.LaborUnitPrice);
        Assert.Equal(0m, item.OverheadUnitPrice);
        Assert.Equal(10_000m, item.TotalAmount);
    }

    /// <summary>
    /// Kalemsiz teklif aktarılamamalı — boş bir icmal, hakedişin
    /// referansı olarak duran anlamsız bir kayıt olurdu.
    /// </summary>
    [Fact]
    public async Task Transfer_EmptyOffer_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId, _) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        Guid offerId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var offer = new Offer
            {
                CompanyId = companyId,
                ProjectId = projectId,
                OfferNumber = $"TKL-{suffix}",
                Title = "Boş teklif",
                Currency = "TRY"
            };

            db.Offers.Add(offer);
            await db.SaveChangesAsync();
            offerId = offer.Id;
        }

        var response = await client.PostAsJsonAsync(
            $"/api/offers/{offerId}/icmale-aktar", new { projectId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Aynı teklif ikinci kez aktarılırsa engellenmez (revizyon meşru)
    /// ama sessiz de geçilmez: hangisinin sözleşme olduğu belirsizleşir.
    /// </summary>
    [Fact]
    public async Task Transfer_Twice_WarnsAboutDuplicate()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId, _) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var created = await client.PostAsJsonAsync(
            "/api/offers", BuildOfferPayload(companyId, projectId, 100m, 0m, 0m));

        var offerId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await client.PostAsJsonAsync(
            $"/api/offers/{offerId}/icmale-aktar", new { projectId });

        var second = await client.PostAsJsonAsync(
            $"/api/offers/{offerId}/icmale-aktar", new { projectId });

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var payload = await second.Content.ReadFromJsonAsync<JsonElement>();
        var warnings = payload.GetProperty("warnings").EnumerateArray()
            .Select(x => x.GetString() ?? string.Empty)
            .ToList();

        Assert.Contains(warnings, x => x.Contains("daha önce icmale aktarılmış"));
    }

    /// <summary>
    /// Resmî yıl fiyatı olan pozdan kalem eklenebilmeli ve malzeme/
    /// montaj ayrımı fiyattan gelmeli.
    /// </summary>
    [Fact]
    public async Task AddItemFromPosition_UsesOfficialYearPrice()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId, positionId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.PositionUnitPrices.AddRange(
                new PositionUnitPrice
                {
                    EngineeringPositionId = positionId,
                    Year = 2026,
                    Institution = PositionPriceInstitution.Csb,
                    Component = PositionPriceComponent.Material,
                    UnitPrice = 70m,
                    CurrencyCode = "TRY"
                },
                new PositionUnitPrice
                {
                    EngineeringPositionId = positionId,
                    Year = 2026,
                    Institution = PositionPriceInstitution.Csb,
                    Component = PositionPriceComponent.Labor,
                    UnitPrice = 30m,
                    CurrencyCode = "TRY"
                });

            await db.SaveChangesAsync();
        }

        var created = await client.PostAsJsonAsync(
            "/api/offers", BuildOfferPayload(companyId, projectId, 100m, 0m, 0m));

        var offerId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/offers/{offerId}/items/from-position",
            new
            {
                engineeringPositionId = positionId,
                quantity = 50m,
                source = 0,
                year = 2026,
                institution = (int?)null,
                profitRate = 0m,
                laborHourRate = 0m,
                machineHourRate = 0m
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(100m, payload.GetProperty("unitSalesPrice").GetDecimal());
        Assert.Equal(70m, payload.GetProperty("materialUnitPrice").GetDecimal());
        Assert.Equal(30m, payload.GetProperty("laborUnitPrice").GetDecimal());
        Assert.Equal(5_000m, payload.GetProperty("salesTotal").GetDecimal());
    }

    /// <summary>
    /// Fiyatı olmayan pozdan kalem eklenmemeli: sıfır fiyatlı bir
    /// keşif satırı toplamı sessizce düşürür ve fark edilmesi zordur.
    /// </summary>
    [Fact]
    public async Task AddItemFromPosition_WithoutPrice_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId, positionId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var created = await client.PostAsJsonAsync(
            "/api/offers", BuildOfferPayload(companyId, projectId, 100m, 0m, 0m));

        var offerId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/offers/{offerId}/items/from-position",
            new
            {
                engineeringPositionId = positionId,
                quantity = 50m,
                source = 0,
                year = 2026,
                institution = (int?)null,
                profitRate = 0m,
                laborHourRate = 0m,
                machineHourRate = 0m
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Yazdırma ucu antet bilgisini ve kalemleri birlikte döndürmeli;
    /// iki ayrı istekle gelseydi yazdırma sırasında yarım görünürdü.
    /// </summary>
    [Fact]
    public async Task PrintData_IncludesCompanyAndItems()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId, _) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var created = await client.PostAsJsonAsync(
            "/api/offers", BuildOfferPayload(companyId, projectId, 60m, 40m, 0m));

        var offerId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var print = await client.GetFromJsonAsync<JsonElement>(
            $"/api/offers/{offerId}/print");

        Assert.False(string.IsNullOrWhiteSpace(
            print.GetProperty("company").GetProperty("name").GetString()));

        var items = print.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal(60m, items[0].GetProperty("materialUnitPrice").GetDecimal());
        Assert.Equal(10_000m, print.GetProperty("grandTotal").GetDecimal());
    }
}
