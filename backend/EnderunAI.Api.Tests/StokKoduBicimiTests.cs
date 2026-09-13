using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Engineering;
using EnderunAI.Api.Services.Inventory;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// STOK KARTI KODU — TEK BİÇİM: `END` + 4 HANE.
///
/// ═══ ÖLÇÜLEN DURUM (2026-09-13) ═══
///
/// Canlıda 9 kart var ve ZATEN İKİ biçim taşıyorlar: END0001…END0009
/// (dolgulu, 7 kayıt) ve END004, END005 (dolgusuz, 2 kayıt — o tarihte
/// kod ELLE yazılıyordu, `request.Code.Trim().ToUpperInvariant()`;
/// dolgu garantisi yoktu).
///
/// Üretici sonradan otomatikleşti ama biçimi DEĞİŞTİRMİŞTİ: `100001`
/// üretiyordu — öneksiz, dolgusuz. İlk yeni kart açıldığı anda listede
/// ÜÇÜNCÜ biçim belirecekti ve her gün büyüyecekti. Mehmet Bey'in
/// kararı: tek biçim, `END` + 4 hane, mevcut en büyük numaradan devam.
///
/// ESKİ KAYITLAR DEĞİŞTİRİLMİYOR: kod alanı belge izinin parçası.
/// </summary>
[Collection("Integration")]
public sealed class StokKoduBicimiTests(DatabaseFixture fixture)
{
    private async Task<Guid> SirketKurAsync(params string[] mevcutKodlar)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await TestDataFactory.CreateProjectAsync(
            db, Guid.NewGuid().ToString("N")[..8]);

        foreach (var kod in mevcutKodlar)
        {
            db.InventoryItems.Add(new InventoryItem
            {
                CompanyId = proje.CompanyId,
                Code = kod,
                Name = $"Mevcut {kod}",
                Unit = "Adet",
                Type = InventoryItemType.Material
            });
        }

        await db.SaveChangesAsync();
        return proje.CompanyId;
    }

    private async Task<string> SiradakiAsync(Guid sirketId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<IInventoryCodeService>()
            .NextCodeAsync(sirketId, CancellationToken.None);
    }

    /// <summary>
    /// KIRMIZIYA DÖNERSE: üretici mevcut en büyük numarayı görmüyor.
    /// Sıfırdan başlarsa ürettiği ilk kod zaten VAR olan bir kodla
    /// çakışır ve `(CompanyId, Code)` tekil indeksinde çöker.
    /// </summary>
    [Fact]
    public async Task MevcutEnBuyukNumaradanDevamEder()
    {
        // Canlının bugünkü hâli: dolgulu ve dolgusuz kodlar bir arada.
        var sirket = await SirketKurAsync("END0001", "END004", "END005", "END0009");

        Assert.Equal("END0010", await SiradakiAsync(sirket));
        Assert.Equal("END0011", await SiradakiAsync(sirket));
        Assert.Equal("END0012", await SiradakiAsync(sirket));
    }

    /// <summary>
    /// KIRMIZIYA DÖNERSE: dolgu kaybolur ve liste yeniden iki biçimli
    /// olur — tam olarak END004/END005'i doğuran kusur.
    /// </summary>
    [Fact]
    public async Task DolguGarantiAltinda_UcHaneliUretilmez()
    {
        var sirket = await SirketKurAsync();

        var ilk = await SiradakiAsync(sirket);

        Assert.Equal("END0001", ilk);
        Assert.Matches("^END[0-9]{4,}$", ilk);
        // Dizge birleştirme değil, sabit genişlik biçimlendirme olduğunun
        // kanıtı: 1 sayısı "END1" değil "END0001" oluyor.
        Assert.DoesNotMatch("^END[0-9]{1,3}$", ilk);
    }

    /// <summary>
    /// KIRMIZIYA DÖNERSE: iki şirket aynı sırayı paylaşır; bir şirkette
    /// açılan kart öbürünün numarasını tüketir.
    /// </summary>
    [Fact]
    public async Task SiraSirketeOzel()
    {
        var a = await SirketKurAsync("END0050");
        var b = await SirketKurAsync();

        Assert.Equal("END0051", await SiradakiAsync(a));
        Assert.Equal("END0001", await SiradakiAsync(b));
        Assert.Equal("END0052", await SiradakiAsync(a));
    }

    /// <summary>
    /// KIRMIZIYA DÖNERSE: `END` ile başlamayan eski/yabancı kodlar sıra
    /// tohumuna karışır. Örn. `ABC9999` sayılırsa sıradaki kod END10000
    /// olurdu — kimsenin beklemediği bir sıçrama.
    /// </summary>
    [Fact]
    public async Task KalipDisiKodlarTohuma_Karismaz()
    {
        var sirket = await SirketKurAsync("ABC9999", "END0003", "100500");

        Assert.Equal("END0004", await SiradakiAsync(sirket));
    }
}

/// <summary>
/// REÇETE AKTARIMI BİÇİM KAPISINI DELMİYOR.
///
/// Kart açma ekranı kodu üreticiden alıyor, ama reçete aktarımı kodu
/// DOSYADAN aynen alıp yeni kart açıyordu (`Code = key.ToUpperInvariant()`,
/// kalıp denetimi yok). Biçim birliğini delen tek kapı burasıydı.
///
/// NEDEN REDDEDİLİYOR, NEDEN ÜRETİCİDEN KOD VERİLMİYOR: dosyadaki kod
/// bir EŞLEŞTİRME ANAHTARI. Kart başka bir kodla açılsaydı AYNI dosyanın
/// bir sonraki aktarımı o kartı bulamaz ve her aktarımda bir mükerrer
/// kart daha açardı.
/// </summary>
[Collection("Integration")]
public sealed class ReceteAktarimiKodBicimiTests(DatabaseFixture fixture)
{
    private async Task<(Guid CompanyId, string PositionCode)> ZeminAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ek = Guid.NewGuid().ToString("N")[..8];
        var proje = await TestDataFactory.CreateProjectAsync(db, ek);

        var poz = new EngineeringPosition
        {
            CompanyId = proje.CompanyId,
            Code = $"POZ-{ek}",
            Name = "Kablo çekimi",
            Unit = "m",
            Source = EngineeringPositionSource.Enderun,
            Discipline = EngineeringPositionDiscipline.Electrical,
            Status = EngineeringPositionStatus.Active
        };

        db.EngineeringPositions.Add(poz);
        await db.SaveChangesAsync();

        return (proje.CompanyId, poz.Code);
    }

    private async Task<RecipeImportPreview> OnizleAsync(
        Guid sirketId, params RecipeImportRow[] satirlar)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<IRecipeImportService>()
            .PreviewAsync(
                new RecipeImportParseResult(satirlar, []),
                new RecipeImportOptions(sirketId, CreateMissingInventoryItems: true),
                CancellationToken.None);
    }

    private static RecipeImportRow Satir(string poz, string kod, string ad) =>
        new(2, poz, kod, ad, 10m, "m", 0m, null, null, false);

    /// <summary>
    /// KIRMIZIYA DÖNERSE: içe aktarma kalıp dışı kodla kart açar ve
    /// listede yeni bir biçim daha belirir — sessizce.
    /// </summary>
    [Fact]
    public async Task KalipDisiKod_KartAcmaz()
    {
        var (sirket, poz) = await ZeminAsync();

        // "end0010" BİLEREK YOK: kod karşılaştırmadan önce büyük harfe
        // çevriliyor, yani küçük harfli ama kalıba uyan bir kod GEÇERLİ.
        // Onu da reddetmesini beklemek, olmayan bir kural sınamak olurdu
        // — ilk koşuda tam bu yüzden kırmızı yandı ve sonda düzeltildi.
        foreach (var kod in new[] { "ABC-99", "END007", "100500", "END-0010" })
        {
            var onizleme = await OnizleAsync(sirket, Satir(poz, kod, $"Yeni {kod}"));
            var satir = Assert.Single(onizleme.Rows);

            Assert.Equal(RecipeImportAction.Skip, satir.Action);
            Assert.Contains("biçime uymuyor", satir.Error ?? string.Empty);
            Assert.Equal(0, onizleme.NewInventoryItemCount);
        }
    }

    /// <summary>
    /// KIRMIZIYA DÖNERSE: kalıp denetimi büyük/küçük harfe takılır ve
    /// kullanıcının küçük harf yazdığı GEÇERLİ kod reddedilir. Kod
    /// zaten büyük harfe çevrilerek yazılıyor; denetim de öyle bakmalı.
    /// </summary>
    [Fact]
    public async Task KucukHarfliKalibaUyanKod_Kabul()
    {
        var (sirket, poz) = await ZeminAsync();

        var onizleme = await OnizleAsync(sirket, Satir(poz, "end0888", "Küçük Harfli Kod"));
        var satir = Assert.Single(onizleme.Rows);

        Assert.Equal(RecipeImportAction.CreateItem, satir.Action);
        Assert.Null(satir.Error);
    }

    /// <summary>
    /// POZİTİF KONTROL (Kural 48). Bu olmadan yukarıdaki "hepsi atlandı"
    /// sonucu, kuralın çalıştığının değil ÖNİZLEMENİN HİÇ KART AÇMADIĞININ
    /// kanıtı olabilirdi.
    /// </summary>
    [Fact]
    public async Task KalibaUyanKod_KartAcar()
    {
        var (sirket, poz) = await ZeminAsync();

        var onizleme = await OnizleAsync(sirket, Satir(poz, "END0777", "Uygun Kodlu Malzeme"));
        var satir = Assert.Single(onizleme.Rows);

        Assert.Equal(RecipeImportAction.CreateItem, satir.Action);
        Assert.Null(satir.Error);
        Assert.Equal(1, onizleme.NewInventoryItemCount);
    }
}
