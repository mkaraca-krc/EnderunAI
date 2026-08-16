using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using EnderunAI.Api.Services.Engineering;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Reçete aktarımının karar mantığı: satır hangi poza, hangi stok
/// kartına bağlanacak, hangi satır neden atlanacak.
///
/// STOK KARTI BAĞI ZORUNLU. Proje malzeme ihtiyacında depo mevcudu ve
/// açık talep yalnız stok kartı üzerinden düşülebiliyor; kartsız
/// malzeme "eksik" hesabına hiç giremez. Bu yüzden kartı olmayan
/// satır ya kart açar ya da atlanır — serbest metin olarak sessizce
/// yazılmaz.
/// </summary>
[Collection("Integration")]
public sealed class RecipeImportServiceTests(DatabaseFixture fixture)
{
    private sealed record Context(Guid CompanyId, string PositionCode);

    private async Task<Context> CreateContextAsync(
        params (string Code, string Name, string Unit)[] inventoryItems)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var position = new EngineeringPosition
        {
            CompanyId = project.CompanyId,
            Code = $"POZ-{suffix}",
            Name = "Kablo çekimi",
            Unit = "m",
            Source = EngineeringPositionSource.Enderun,
            Discipline = EngineeringPositionDiscipline.Electrical,
            Status = EngineeringPositionStatus.Active
        };

        db.EngineeringPositions.Add(position);

        foreach (var item in inventoryItems)
        {
            db.InventoryItems.Add(new InventoryItem
            {
                CompanyId = project.CompanyId,
                Code = item.Code,
                Name = item.Name,
                Unit = item.Unit,
                Type = InventoryItemType.Material
            });
        }

        await db.SaveChangesAsync();

        return new Context(project.CompanyId, position.Code);
    }

    private static RecipeImportParseResult Parsed(params RecipeImportRow[] rows) =>
        new(rows, []);

    private static RecipeImportRow Row(
        string? positionCode,
        string? materialCode,
        string? materialName,
        decimal quantity = 10m,
        string unit = "m",
        decimal waste = 0m,
        int rowNumber = 2) =>
        new(rowNumber, positionCode, materialCode, materialName,
            quantity, unit, waste, null, null, false);

    private async Task<T> WithServiceAsync<T>(
        Func<IRecipeImportService, AppDbContext, Task<T>> action)
    {
        using var scope = fixture.Factory.Services.CreateScope();

        return await action(
            scope.ServiceProvider.GetRequiredService<IRecipeImportService>(),
            scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task MevcutStokKarti_Baglanir()
    {
        var context = await CreateContextAsync(("KBL-01", "NYA Kablo", "m"));

        var preview = await WithServiceAsync((service, _) => service.PreviewAsync(
            Parsed(Row(context.PositionCode, "KBL-01", "NYA Kablo")),
            new RecipeImportOptions(context.CompanyId, CreateMissingInventoryItems: true),
            CancellationToken.None));

        var row = Assert.Single(preview.Rows);

        Assert.Equal(RecipeImportAction.UseExistingItem, row.Action);
        Assert.Equal(0, preview.NewInventoryItemCount);
        Assert.Equal(1, preview.ValidRows);
    }

    /// <summary>Kod tutmasa da ad tutuyorsa aynı malzemedir; kart ikinci kez açılmaz.</summary>
    [Fact]
    public async Task KodTutmazAdTutarsa_MevcutKartaBaglanir()
    {
        var context = await CreateContextAsync(("KBL-99", "NYA Kablo", "m"));

        var preview = await WithServiceAsync((service, _) => service.PreviewAsync(
            Parsed(Row(context.PositionCode, "BASKA-KOD", "NYA Kablo")),
            new RecipeImportOptions(context.CompanyId, CreateMissingInventoryItems: true),
            CancellationToken.None));

        var row = Assert.Single(preview.Rows);

        Assert.Equal(RecipeImportAction.UseExistingItem, row.Action);
        Assert.Equal("KBL-99", row.MaterialCode);
    }

    /// <summary>
    /// Birim uyuşmazlığı SESSİZ GEÇMEZ: "kg" reçeteyi "ton" kartıyla
    /// çarpmak ihtiyacı bin kat şişirir ve o miktar satın almaya gider.
    /// </summary>
    [Fact]
    public async Task BirimUyusmazsa_SatirAtlanir()
    {
        var context = await CreateContextAsync(("DMR-01", "Nervürlü demir", "ton"));

        var preview = await WithServiceAsync((service, _) => service.PreviewAsync(
            Parsed(Row(context.PositionCode, "DMR-01", "Nervürlü demir", unit: "kg")),
            new RecipeImportOptions(context.CompanyId, CreateMissingInventoryItems: true),
            CancellationToken.None));

        var row = Assert.Single(preview.Rows);

        Assert.Equal(RecipeImportAction.Skip, row.Action);
        Assert.Contains("Birim uyuşmuyor", row.Error);
        Assert.Equal("ton", row.ExistingItemUnit);
    }

    [Fact]
    public async Task KartYokVeAcmaKapali_SatirAtlanir()
    {
        var context = await CreateContextAsync();

        var preview = await WithServiceAsync((service, _) => service.PreviewAsync(
            Parsed(Row(context.PositionCode, "YENI-01", "Yeni malzeme")),
            new RecipeImportOptions(context.CompanyId, CreateMissingInventoryItems: false),
            CancellationToken.None));

        var row = Assert.Single(preview.Rows);

        Assert.Equal(RecipeImportAction.Skip, row.Action);
        Assert.Contains("Stok kartı bulunamadı", row.Error);
    }

    [Fact]
    public async Task PozBulunamazsa_SatirAtlanir()
    {
        var context = await CreateContextAsync();

        var preview = await WithServiceAsync((service, _) => service.PreviewAsync(
            Parsed(Row("OLMAYAN-POZ", "KBL-01", "NYA Kablo")),
            new RecipeImportOptions(context.CompanyId, CreateMissingInventoryItems: true),
            CancellationToken.None));

        var row = Assert.Single(preview.Rows);

        Assert.Equal(RecipeImportAction.Skip, row.Action);
        Assert.Contains("Poz bulunamadı", row.Error);
        Assert.Equal(1, preview.MissingPositionCount);
    }

    /// <summary>
    /// ÖNİZLEME VE AKTARIM AYNI KARARI VERİR. Ayrı hesaplansaydı
    /// önizlemede "aktarılacak" görünen satır sessizce düşebilirdi.
    /// </summary>
    [Fact]
    public async Task Aktarim_ReceteyiVeKartiKurar()
    {
        var context = await CreateContextAsync(("KBL-01", "NYA Kablo", "m"));

        var parsed = Parsed(
            Row(context.PositionCode, "KBL-01", "NYA Kablo", quantity: 12m, waste: 5m),
            Row(context.PositionCode, "BUAT-01", "Buat", quantity: 2m, unit: "adet", rowNumber: 3));

        var options = new RecipeImportOptions(
            context.CompanyId, CreateMissingInventoryItems: true);

        var preview = await WithServiceAsync((service, _) =>
            service.PreviewAsync(parsed, options, CancellationToken.None));

        Assert.Equal(2, preview.ValidRows);
        Assert.Equal(1, preview.NewInventoryItemCount);

        var result = await WithServiceAsync((service, _) =>
            service.CommitAsync(parsed, options, CancellationToken.None));

        Assert.Equal(1, result.CreatedRecipes);
        Assert.Equal(1, result.CreatedInventoryItems);
        Assert.Equal(2, result.ImportedMaterials);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var recipe = await db.EngineeringRecipes
            .Include(x => x.Materials)
            .SingleAsync(x => x.EngineeringPosition.Code == context.PositionCode);

        Assert.True(recipe.IsDefault);
        Assert.Equal(1, recipe.Version);
        Assert.Equal(2, recipe.Materials.Count);

        // HER MALZEME STOK KARTINA BAĞLI — eksik hesabının ön şartı.
        Assert.All(recipe.Materials, x => Assert.NotNull(x.InventoryItemId));

        var cable = recipe.Materials.Single(x => x.MaterialCode == "KBL-01");
        Assert.Equal(12m, cable.Quantity);
        Assert.Equal(5m, cable.WastePercent);
    }

    /// <summary>
    /// Aynı poz ikinci kez aktarılırsa YENİ SÜRÜM açılır ve eskisi
    /// varsayılan olmaktan çıkar — silinmez. Hangi reçeteyle hesap
    /// yapıldığı geriye dönük sorulabilmeli.
    /// </summary>
    [Fact]
    public async Task IkinciAktarim_YeniSurumAcar()
    {
        var context = await CreateContextAsync(("KBL-01", "NYA Kablo", "m"));

        var options = new RecipeImportOptions(
            context.CompanyId, CreateMissingInventoryItems: true);

        await WithServiceAsync((service, _) => service.CommitAsync(
            Parsed(Row(context.PositionCode, "KBL-01", "NYA Kablo", quantity: 10m)),
            options, CancellationToken.None));

        await WithServiceAsync((service, _) => service.CommitAsync(
            Parsed(Row(context.PositionCode, "KBL-01", "NYA Kablo", quantity: 14m)),
            options, CancellationToken.None));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var recipes = await db.EngineeringRecipes
            .Include(x => x.Materials)
            .Where(x => x.EngineeringPosition.Code == context.PositionCode)
            .OrderBy(x => x.Version)
            .ToListAsync();

        Assert.Equal(2, recipes.Count);
        Assert.False(recipes[0].IsDefault);
        Assert.True(recipes[1].IsDefault);
        Assert.Equal(14m, recipes[1].Materials.Single().Quantity);
    }

    /// <summary>
    /// Aynı malzeme dosyada iki pozda geçerse kart BİR KEZ açılır.
    /// Açılmasaydı ikinci satır benzersiz kod kısıtına takılıp tüm
    /// aktarımı düşürürdü.
    /// </summary>
    [Fact]
    public async Task AyniMalzemeIkiPozda_KartBirKezAcilir()
    {
        var context = await CreateContextAsync();

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.EngineeringPositions.Add(new EngineeringPosition
            {
                CompanyId = context.CompanyId,
                Code = context.PositionCode + "-B",
                Name = "İkinci poz",
                Unit = "m",
                Source = EngineeringPositionSource.Enderun,
                Discipline = EngineeringPositionDiscipline.Electrical,
                Status = EngineeringPositionStatus.Active
            });

            await db.SaveChangesAsync();
        }

        var result = await WithServiceAsync((service, _) => service.CommitAsync(
            Parsed(
                Row(context.PositionCode, "ORTAK-01", "Ortak malzeme"),
                Row(context.PositionCode + "-B", "ORTAK-01", "Ortak malzeme", rowNumber: 3)),
            new RecipeImportOptions(context.CompanyId, CreateMissingInventoryItems: true),
            CancellationToken.None));

        Assert.Equal(2, result.CreatedRecipes);
        Assert.Equal(1, result.CreatedInventoryItems);
    }
    /// <summary>
    /// EŞDEĞER YAZIM ARTIK UYUŞMAZLIK SAYILMIYOR.
    ///
    /// Poz kütüphanesi adet birimini "Ad" yazıyor, Enderun stok
    /// kartları "Adet". Eskiden bu satırların HEPSİ atlanıyordu —
    /// canlıda 14.628 poz "Ad"/"AD" yazımıyla duruyor, yani bir reçete
    /// dosyasının neredeyse tamamı düşerdi.
    /// </summary>
    [Fact]
    public async Task EsdegerBirimYazimi_KartaBaglanir()
    {
        var context = await CreateContextAsync(("BUAT-01", "Buat", "Adet"));

        var preview = await WithServiceAsync((service, _) => service.PreviewAsync(
            Parsed(Row(context.PositionCode, "BUAT-01", "Buat", unit: "Ad")),
            new RecipeImportOptions(context.CompanyId, CreateMissingInventoryItems: false),
            CancellationToken.None));

        var row = Assert.Single(preview.Rows);

        Assert.Equal(RecipeImportAction.UseExistingItem, row.Action);
        Assert.Null(row.Error);
    }

    /// <summary>
    /// GERÇEK UYUŞMAZLIK HÂLÂ ATLANIYOR. Normalizasyon kontrolü
    /// gevşetmiyor; "m" ile "Adet" farklı fiziksel birimler ve sessizce
    /// bağlanmaları reçeteye yanlış miktar yazmak olurdu.
    /// </summary>
    [Fact]
    public async Task GercekBirimUyusmazligi_HalaAtlanir()
    {
        var context = await CreateContextAsync(("KBL-77", "NYA Kablo", "Adet"));

        var preview = await WithServiceAsync((service, _) => service.PreviewAsync(
            Parsed(Row(context.PositionCode, "KBL-77", "NYA Kablo", unit: "m")),
            new RecipeImportOptions(context.CompanyId, CreateMissingInventoryItems: false),
            CancellationToken.None));

        var row = Assert.Single(preview.Rows);

        Assert.Equal(RecipeImportAction.Skip, row.Action);
        Assert.Contains("Birim uyuşmuyor", row.Error);
    }

    /// <summary>
    /// KART AÇMA KAPALIYKEN kartsız malzeme OLUŞTURULMUYOR ama
    /// raporda malzeme bazında görünüyor — "kaç malzeme, hangileri"
    /// sorusu kart açma kararını verecek olan şey.
    /// </summary>
    [Fact]
    public async Task KartAcmaKapali_KartsizMalzemeler_RaporlaniyorAmaAcilmiyor()
    {
        var context = await CreateContextAsync();

        var preview = await WithServiceAsync((service, _) => service.PreviewAsync(
            Parsed(
                Row(context.PositionCode, "YOK-01", "Tanımsız Malzeme", unit: "Ad"),
                Row(context.PositionCode, "YOK-01", "Tanımsız Malzeme", unit: "Ad"),
                Row(context.PositionCode, "YOK-02", "Başka Malzeme", unit: "m")),
            new RecipeImportOptions(context.CompanyId, CreateMissingInventoryItems: false),
            CancellationToken.None));

        Assert.All(preview.Rows, row => Assert.Equal(RecipeImportAction.Skip, row.Action));
        Assert.Equal(0, preview.NewInventoryItemCount);

        // Malzeme bazında toplanmış: iki satırda geçen malzeme TEK kayıt.
        Assert.Equal(2, preview.MissingInventoryItems.Count);

        var mostCommon = preview.MissingInventoryItems.First();
        Assert.Equal("YOK-01", mostCommon.MaterialCode);
        Assert.Equal(2, mostCommon.RowCount);
    }

}
