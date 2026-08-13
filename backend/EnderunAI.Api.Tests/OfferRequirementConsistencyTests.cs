using EnderunAI.Api.Contracts.Purchasing;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Engineering;
using EnderunAI.Api.Services.Purchasing.Automation;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// AYRIŞMA TESTİ: teklif → satın alma talebi yolu ile ortak ihtiyaç
/// motoru AYNI sayıyı vermeli.
///
/// Bu hesap eskiden teklif yolunun içine gömülüydü. Proje malzeme
/// tedariki için ikinci bir kopya yazılsaydı, ikisi zamanla ayrışır ve
/// aynı iş için iki farklı miktar üretirdi — hangisinin doğru olduğu
/// da anlaşılamazdı. Motor ortaklaştırıldı; bu test ortaklığın
/// bozulduğu anda kırmızıya döner.
/// </summary>
[Collection("Integration")]
public sealed class OfferRequirementConsistencyTests(DatabaseFixture fixture)
{
    private sealed record Fixture(Guid OfferId, List<MaterialRequirementSource> Sources);

    /// <summary>
    /// Kazanılmış bir teklif kurar: iki poz, ikisinde de reçete, biri
    /// ortak malzeme; ayrıca reçetesiz bir satır.
    /// </summary>
    private async Task<Fixture> CreateWonOfferAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        EngineeringPosition NewPosition(string code, string name) => new()
        {
            CompanyId = project.CompanyId,
            Code = code,
            Name = name,
            Unit = "m",
            Source = EngineeringPositionSource.Enderun,
            Discipline = EngineeringPositionDiscipline.Electrical,
            Status = EngineeringPositionStatus.Active
        };

        var first = NewPosition($"POZ-A-{suffix}", "Kablo çekimi");
        var second = NewPosition($"POZ-B-{suffix}", "Buat montajı");
        var withoutRecipe = NewPosition($"POZ-C-{suffix}", "Reçetesiz iş");

        db.EngineeringPositions.AddRange(first, second, withoutRecipe);

        var cableItem = new InventoryItem
        {
            CompanyId = project.CompanyId,
            Code = $"KBL-{suffix}",
            Name = "NYA Kablo",
            Unit = "m",
            Type = InventoryItemType.Material
        };

        db.InventoryItems.Add(cableItem);

        // İki reçete: ortak malzeme (kablo) iki pozdan da geliyor.
        db.EngineeringRecipes.AddRange(
            new EngineeringRecipe
            {
                EngineeringPositionId = first.Id,
                Version = 1,
                IsDefault = true,
                Materials =
                {
                    new EngineeringRecipeMaterial
                    {
                        InventoryItemId = cableItem.Id,
                        MaterialCode = cableItem.Code,
                        MaterialName = cableItem.Name,
                        Quantity = 2m,
                        Unit = "m",
                        WastePercent = 5m
                    }
                }
            },
            new EngineeringRecipe
            {
                EngineeringPositionId = second.Id,
                Version = 1,
                IsDefault = true,
                Materials =
                {
                    new EngineeringRecipeMaterial
                    {
                        InventoryItemId = cableItem.Id,
                        MaterialCode = cableItem.Code,
                        MaterialName = cableItem.Name,
                        Quantity = 1.5m,
                        Unit = "m",
                        WastePercent = 0m
                    },
                    new EngineeringRecipeMaterial
                    {
                        InventoryItemId = null,
                        MaterialCode = $"BUAT-{suffix}",
                        MaterialName = "Buat",
                        Quantity = 1m,
                        Unit = "adet",
                        WastePercent = 10m
                    }
                }
            });

        var offer = new Offer
        {
            CompanyId = project.CompanyId,
            ProjectId = project.Id,
            OfferNumber = $"TKF-{suffix}",
            Title = "Ayrışma testi teklifi",
            Status = OfferStatus.Won,
            Currency = "TRY",
            ExchangeRate = 1m,
            Items =
            {
                new OfferItem
                {
                    LineNumber = 1,
                    EngineeringPositionId = first.Id,
                    PositionNumber = first.Code,
                    Description = first.Name,
                    Quantity = 100m,
                    Unit = "m"
                },
                new OfferItem
                {
                    LineNumber = 2,
                    EngineeringPositionId = second.Id,
                    PositionNumber = second.Code,
                    Description = second.Name,
                    Quantity = 40m,
                    Unit = "adet"
                },
                new OfferItem
                {
                    LineNumber = 3,
                    EngineeringPositionId = withoutRecipe.Id,
                    PositionNumber = withoutRecipe.Code,
                    Description = withoutRecipe.Name,
                    Quantity = 500m,
                    Unit = "m"
                }
            }
        };

        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        // Beklenen sonuç, teklif yolundan BAĞIMSIZ olarak doğrudan
        // motordan hesaplanıyor: karşılaştırma anlamlı olsun diye.
        var sources = new List<MaterialRequirementSource>
        {
            new(1, first.Code, first.Name, 100m,
            [
                new MaterialRequirementRecipeLine(
                    cableItem.Id, cableItem.Code, cableItem.Name, "m", 2m, 5m)
            ]),
            new(2, second.Code, second.Name, 40m,
            [
                new MaterialRequirementRecipeLine(
                    cableItem.Id, cableItem.Code, cableItem.Name, "m", 1.5m, 0m),
                new MaterialRequirementRecipeLine(
                    null, $"BUAT-{suffix}", "Buat", "adet", 1m, 10m)
            ]),
            new(3, withoutRecipe.Code, withoutRecipe.Name, 500m, null)
        };

        return new Fixture(offer.Id, sources);
    }

    [Fact]
    public async Task TeklifYolu_MotorlaAyniSayiyiUretir()
    {
        var data = await CreateWonOfferAsync();
        var expected = MaterialRequirementCalculator.Calculate(data.Sources);

        using var scope = fixture.Factory.Services.CreateScope();
        var generator = scope.ServiceProvider.GetRequiredService<IPurchaseRequestGenerator>();

        await generator.GenerateFromOfferAsync(
            data.OfferId,
            new GeneratePurchaseRequestFromOfferRequest(
                RequestedByName: "Test",
                NeededByDate: null,
                Priority: 1),
            CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var items = await db.PurchaseRequestItems
            .Where(x => x.PurchaseRequest.ProjectId != null)
            .Include(x => x.PurchaseRequest)
            .Where(x => x.PurchaseRequest.Description!.Contains("Ayrışma testi teklifi"))
            .OrderBy(x => x.LineNumber)
            .ToListAsync();

        Assert.Equal(expected.Materials.Count, items.Count);

        foreach (var (material, item) in expected.Materials.Zip(items))
        {
            Assert.Equal(material.Quantity, item.Quantity);
            Assert.Equal(material.Unit, item.Unit);
            Assert.Contains(material.MaterialName, item.MaterialDescription);

            // Stok kartı bağı da taşınıyor: talep kalemi hangi malzeme,
            // sonradan sorulabilsin (eksik hesabının ön şartı).
            Assert.Equal(material.InventoryItemId, item.InventoryItemId);
        }

        // Kablo iki pozdan geliyor: 100×2×1,05 + 40×1,5 = 270.
        var cable = items.Single(x => x.MaterialDescription.Contains("NYA Kablo"));
        Assert.Equal(270m, cable.Quantity);

        // Reçetesiz poz ihtiyaca SIFIR kattı; talebe kalem açılmadı.
        Assert.DoesNotContain(items, x => x.MaterialDescription.Contains("Reçetesiz"));
        Assert.Single(expected.MissingRecipes);
    }
}
