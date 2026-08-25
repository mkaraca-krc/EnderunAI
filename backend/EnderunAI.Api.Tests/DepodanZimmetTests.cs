using System.Net;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// DEPODAN ZİMMET — stok düşer, şirket varlığı DEĞİŞMEZ.
///
/// Testler kendi şirketini, deposunu, stok kartını ve personelini
/// kuruyor. Ortak veriye yaslanmıyor: paylaşılan test veritabanında
/// "varsa kullan" deseni, aynı satırı ezen başka bir teste karşı
/// savunmasız.
/// </summary>
[Collection("Integration")]
public sealed class DepodanZimmetTests(DatabaseFixture fixture)
{
    private sealed record Ortam(
        Guid CompanyId, Guid WarehouseId, Guid ItemId, Guid PersonnelId, HttpClient Client);

    private async Task<Ortam> KurAsync(string ek, InventoryItemType tur, decimal stok)
    {
        Guid companyId, warehouseId, itemId, personnelId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (company, branch, _) = await TestDataFactory.CreateCompanyStackAsync(db, ek);
            await TestDataFactory.EnsureStockAccountsAsync(db, company.Id);

            var depo = new Warehouse
            {
                CompanyId = company.Id,
                BranchId = branch.Id,
                Code = $"DP-{ek}",
                Name = "Zimmet Test Deposu",
                Type = WarehouseType.Central
            };
            db.Warehouses.Add(depo);

            var kalem = new InventoryItem
            {
                CompanyId = company.Id,
                Code = $"KLM-{ek}",
                Name = "Zimmet Test Kalemi",
                Unit = "adet",
                Type = tur,
                AverageUnitCost = 100m
            };
            db.InventoryItems.Add(kalem);

            db.WarehouseStocks.Add(new WarehouseStock
            {
                WarehouseId = depo.Id,
                InventoryItemId = kalem.Id,
                Quantity = stok
            });

            var personel = await TestDataFactory.CreatePersonnelAsync(db, company.Id, ek);

            await db.SaveChangesAsync();

            companyId = company.Id;
            warehouseId = depo.Id;
            itemId = kalem.Id;
            personnelId = personel.Id;
        }

        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, ek, ["Satın Alma Sorumlusu"], companyId);

        return new Ortam(companyId, warehouseId, itemId, personnelId, client);
    }

    private static object Istek(Ortam o, decimal miktar) => new
    {
        companyId = o.CompanyId,
        personnelId = o.PersonnelId,
        warehouseId = o.WarehouseId,
        inventoryItemId = o.ItemId,
        assignmentDate = DateTime.UtcNow,
        miktar
    };

    private async Task<T> OlcAsync<T>(Func<AppDbContext, Task<T>> olc)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        return await olc(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>
    /// SARF ZİMMETE VERİLİNCE GİDER YAZILIR.
    ///
    /// Fişin varlığı stok hareketi üzerinden ölçülüyor: hareketin
    /// `AccountingVoucherId` alanı dolu olmalı.
    /// </summary>
    [Fact]
    public async Task SarfZimmet_GiderYazar()
    {
        var o = await KurAsync($"zs{DateTime.UtcNow:ffffff}", InventoryItemType.Consumable, 10m);

        var cevap = await o.Client.PostAsJsonAsync("/api/hr/assets/from-inventory", Istek(o, 3m));
        Assert.Equal(HttpStatusCode.OK, cevap.StatusCode);

        var fisId = await OlcAsync(db => db.StockMovements
            .Where(x => x.InventoryItemId == o.ItemId && x.Type == StockMovementType.Issue)
            .Select(x => x.AccountingVoucherId)
            .FirstOrDefaultAsync());

        Assert.NotNull(fisId);
    }

    /// <summary>
    /// DAYANIKLI TAŞINIRDA GİDER YAZILMAZ.
    ///
    /// Stok yine düşüyor — malzeme depodan çıktı — ama gider kaydı
    /// oluşmuyor: kişide duran ekipman hâlâ şirketin varlığı.
    /// </summary>
    [Fact]
    public async Task DayanikliZimmet_GiderYazmaz()
    {
        var o = await KurAsync($"zd{DateTime.UtcNow:ffffff}", InventoryItemType.Equipment, 5m);

        var cevap = await o.Client.PostAsJsonAsync("/api/hr/assets/from-inventory", Istek(o, 1m));
        Assert.Equal(HttpStatusCode.OK, cevap.StatusCode);

        var hareket = await OlcAsync(db => db.StockMovements
            .Where(x => x.InventoryItemId == o.ItemId && x.Type == StockMovementType.Issue)
            .SingleAsync());

        Assert.Null(hareket.AccountingVoucherId);
        Assert.Equal(1m, hareket.Quantity);
    }

    /// <summary>
    /// DEPO DÜŞER, ZİMMET ARTAR, TOPLAM DEĞİŞMEZ.
    ///
    /// Testin asıl iddiası son satırda: şirketin elindeki toplam
    /// miktar zimmet öncesi ve sonrası AYNI. Yalnız "stok düştü"
    /// demek yetmezdi — malzemenin kaybolmadığını göstermiyor.
    /// </summary>
    [Fact]
    public async Task Zimmet_ToplamVarligiDegistirmez()
    {
        var o = await KurAsync($"zt{DateTime.UtcNow:ffffff}", InventoryItemType.Consumable, 10m);

        var oncesi = await OlcAsync(db => db.WarehouseStocks
            .Where(x => x.WarehouseId == o.WarehouseId && x.InventoryItemId == o.ItemId)
            .Select(x => x.Quantity).SingleAsync());

        await o.Client.PostAsJsonAsync("/api/hr/assets/from-inventory", Istek(o, 4m));

        var sonrasi = await OlcAsync(db => db.WarehouseStocks
            .Where(x => x.WarehouseId == o.WarehouseId && x.InventoryItemId == o.ItemId)
            .Select(x => x.Quantity).SingleAsync());

        var zimmette = await OlcAsync(db => db.HrAssetAssignments
            .Where(x => x.CompanyId == o.CompanyId
                        && x.Status == HrAssetAssignmentStatus.Assigned
                        && x.InventoryItemId == o.ItemId)
            .Join(db.StockMovements, z => z.IssueStockMovementId, m => m.Id, (z, m) => m.Quantity)
            .SumAsync());

        Assert.Equal(10m, oncesi);
        Assert.Equal(6m, sonrasi);
        Assert.Equal(4m, zimmette);

        // ASIL İDDİA: toplam varlık değişmedi.
        Assert.Equal(oncesi, sonrasi + zimmette);
    }

    /// <summary>
    /// İADE — stok geri döner, çıkışta gider yazıldıysa TERS KAYIT atılır.
    ///
    /// Ters kayıt atılmazsa malzeme hem gider yazılmış hem stokta
    /// durur sayılır; stok-muhasebe mutabakatı fark verir.
    /// </summary>
    [Fact]
    public async Task Iade_StoguGeriGetirirVeTersKayitAtar()
    {
        var o = await KurAsync($"zi{DateTime.UtcNow:ffffff}", InventoryItemType.Consumable, 8m);

        var olustur = await o.Client.PostAsJsonAsync("/api/hr/assets/from-inventory", Istek(o, 3m));
        var olusan = await olustur.Content.ReadFromJsonAsync<ZimmetCevap>();

        var surum = await OlcAsync(db => db.HrAssetAssignments
            .Where(x => x.Id == olusan!.Id)
            .Select(x => x.UpdatedAtUtc ?? x.CreatedAtUtc)
            .SingleAsync());

        var iade = await o.Client.PostAsJsonAsync(
            $"/api/hr/assets/{olusan!.Id}/return-to-warehouse",
            new { rowVersion = surum });

        Assert.Equal(HttpStatusCode.OK, iade.StatusCode);

        var stok = await OlcAsync(db => db.WarehouseStocks
            .Where(x => x.WarehouseId == o.WarehouseId && x.InventoryItemId == o.ItemId)
            .Select(x => x.Quantity).SingleAsync());

        Assert.Equal(8m, stok);

        var giris = await OlcAsync(db => db.StockMovements
            .Where(x => x.InventoryItemId == o.ItemId && x.Type == StockMovementType.Receipt)
            .SingleAsync());

        Assert.NotNull(giris.AccountingVoucherId);

        var durum = await OlcAsync(db => db.HrAssetAssignments
            .Where(x => x.Id == olusan.Id).Select(x => x.Status).SingleAsync());

        Assert.Equal(HrAssetAssignmentStatus.Returned, durum);
    }

    /// <summary>
    /// İPTAL GEREKÇESİZ YAPILAMAZ.
    ///
    /// İptal bu akıştaki en çok suistimal edilebilecek eylem:
    /// malzeme kişide kalırken kayıt kapatılmış görünebilir.
    /// Gerekçesiz iptale izin verilseydi denetim kaydı "birisi iptal
    /// etti" düzeyinde kalırdı.
    /// </summary>
    [Fact]
    public async Task Iptal_GerekcesizReddedilir()
    {
        var o = await KurAsync($"zp{DateTime.UtcNow:ffffff}", InventoryItemType.Consumable, 5m);

        var olustur = await o.Client.PostAsJsonAsync("/api/hr/assets/from-inventory", Istek(o, 2m));
        var olusan = await olustur.Content.ReadFromJsonAsync<ZimmetCevap>();

        var surum = await OlcAsync(db => db.HrAssetAssignments
            .Where(x => x.Id == olusan!.Id)
            .Select(x => x.UpdatedAtUtc ?? x.CreatedAtUtc).SingleAsync());

        var iptal = await o.Client.PostAsJsonAsync(
            $"/api/hr/assets/{olusan!.Id}/cancel-assignment",
            new { gerekce = "   ", rowVersion = surum });

        Assert.Equal(HttpStatusCode.BadRequest, iptal.StatusCode);
    }

    /// <summary>
    /// ZİMMET, İADE VE İPTAL DENETİM KAYDINA YAZILIR — KİM YAPTIĞIYLA.
    /// </summary>
    [Fact]
    public async Task ZimmetVeIptal_DenetimKaydinaYazilir()
    {
        var o = await KurAsync($"zk{DateTime.UtcNow:ffffff}", InventoryItemType.Consumable, 5m);

        var olustur = await o.Client.PostAsJsonAsync("/api/hr/assets/from-inventory", Istek(o, 2m));
        var olusan = await olustur.Content.ReadFromJsonAsync<ZimmetCevap>();

        var surum = await OlcAsync(db => db.HrAssetAssignments
            .Where(x => x.Id == olusan!.Id)
            .Select(x => x.UpdatedAtUtc ?? x.CreatedAtUtc).SingleAsync());

        await o.Client.PostAsJsonAsync(
            $"/api/hr/assets/{olusan!.Id}/cancel-assignment",
            new { gerekce = "Yanlış kişiye verilmiş", rowVersion = surum });

        var kayitlar = await OlcAsync(db => db.SecurityAuditEvents
            .Where(x => x.EntityId == olusan.Id)
            .Select(x => new { x.Action, x.ActorUserId })
            .ToListAsync());

        Assert.Contains(kayitlar, x => x.Action == "DepodanZimmetVerildi");
        Assert.Contains(kayitlar, x => x.Action == "DepodanZimmetIptalEdildi");

        // KİM YAPTIĞI GÖRÜNMELİ: eylem adı tek başına yetmez.
        Assert.All(kayitlar, x => Assert.NotNull(x.ActorUserId));
    }

    /// <summary>
    /// KAPSAM DIŞI DEPODAN ZİMMET VERİLEMEZ.
    ///
    /// Kullanıcı A şirketine kapsamlı; B şirketinin deposunu
    /// deniyor. Kapsam süzgeci olmasaydı istek geçerdi.
    /// </summary>
    [Fact]
    public async Task KapsamDisiDepo_Reddedilir()
    {
        var ek = $"zx{DateTime.UtcNow:ffffff}";
        var a = await KurAsync(ek, InventoryItemType.Consumable, 5m);
        var b = await KurAsync($"{ek}b", InventoryItemType.Consumable, 5m);

        // A'nın istemcisiyle B'nin deposu ve kalemi isteniyor.
        var cevap = await a.Client.PostAsJsonAsync("/api/hr/assets/from-inventory", new
        {
            companyId = b.CompanyId,
            personnelId = b.PersonnelId,
            warehouseId = b.WarehouseId,
            inventoryItemId = b.ItemId,
            assignmentDate = DateTime.UtcNow,
            miktar = 1m
        });

        Assert.NotEqual(HttpStatusCode.OK, cevap.StatusCode);

        var zimmetVar = await OlcAsync(db => db.HrAssetAssignments
            .AnyAsync(x => x.CompanyId == b.CompanyId && x.InventoryItemId == b.ItemId));

        Assert.False(zimmetVar);
    }

    /// <summary>
    /// EŞZAMANLI ZİMMET STOĞU EKSİYE DÜŞÜRMEZ.
    ///
    /// Depoda 1 adet var, iki istek aynı anda 1'er adet istiyor.
    /// `warehouse_stocks` üzerinde eşzamanlılık jetonu YOK ve çıkış
    /// oku-değiştir-yaz yapıyor; satır kilidi olmasaydı ikisi de
    /// "1 adet var" okuyup düşer, stok -1 olurdu.
    ///
    /// İDDİA ÜÇ PARÇALI: biri geçer, diğeri TEMİZ hata alır
    /// (çökme değil), stok asla eksiye düşmez.
    /// </summary>
    [Fact]
    public async Task EszamanliZimmet_StokEksiyeDusmez()
    {
        var o = await KurAsync($"ze{DateTime.UtcNow:ffffff}", InventoryItemType.Consumable, 1m);

        var ikinci = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, $"ze2{DateTime.UtcNow:ffffff}", ["Satın Alma Sorumlusu"], o.CompanyId);

        var birinciGorev = o.Client.PostAsJsonAsync("/api/hr/assets/from-inventory", Istek(o, 1m));
        var ikinciGorev = ikinci.PostAsJsonAsync("/api/hr/assets/from-inventory", Istek(o, 1m));

        var cevaplar = await Task.WhenAll(birinciGorev, ikinciGorev);

        var basarili = cevaplar.Count(x => x.StatusCode == HttpStatusCode.OK);
        var basarisiz = cevaplar.Count(x => x.StatusCode == HttpStatusCode.BadRequest);

        Assert.Equal(1, basarili);
        Assert.Equal(1, basarisiz);

        var stok = await OlcAsync(db => db.WarehouseStocks
            .Where(x => x.WarehouseId == o.WarehouseId && x.InventoryItemId == o.ItemId)
            .Select(x => x.Quantity).SingleAsync());

        Assert.Equal(0m, stok);
        Assert.True(stok >= 0m, "Stok eksiye düştü.");
    }

    private sealed record ZimmetCevap(Guid AssetAssignmentId)
    {
        public Guid Id => AssetAssignmentId;
    }
}
