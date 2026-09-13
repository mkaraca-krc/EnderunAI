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
/// PASİF KARTTA STOK GİRİŞİ YASAK, ÇIKIŞ İZİNLİ — İKİ YÖNLÜ KURAL.
///
/// ═══ ÖLÇÜLEN AÇIK (2026-09-13) ═══
///
/// Pasif kart yalnız seçicide gizleniyordu. İki ayaklı canlı-benzeri
/// ölçüm: aynı kart AKTİF → 200, PASİF → **yine 200**, ikisinde de
/// muhasebe fişi üretildi.
///
/// ═══ POZİTİF KONTROLLER ATLANMAZ ═══
///
/// "Hepsini engelledim" demek kolay; gizli bir kilit kurmak da öyle.
/// Bu yüzden üç ayak birlikte koşuyor:
///   1. pasif kart + stok ARTIRAN → 4xx  (asıl kapı)
///   2. AKTİF kart + stok ARTIRAN → 200  (pozitif kontrol)
///   3. pasif kart + stok AZALTAN → 200  (pozitif kontrol — arşiv
///      kartının kalan stoğu boşaltılabilmeli)
/// Üçüncüsü olmadan kural, stoğu üstünde kalmış kartı sonsuza
/// kilitlerdi.
/// </summary>
[Collection("Integration")]
public sealed class PasifKartHareketiTests(DatabaseFixture fixture)
{
    private sealed record Zemin(Guid ItemId, Guid WarehouseId);

    private async Task<Zemin> ZeminKurAsync(AppDbContext db, bool aktif, decimal baslangicStok)
    {
        var ek = Guid.NewGuid().ToString("N")[..8];
        var (company, branch, _) = await TestDataFactory.CreateCompanyStackAsync(db, ek);
        await TestDataFactory.EnsureStockAccountsAsync(db, company.Id);

        var kalem = new InventoryItem
        {
            CompanyId = company.Id,
            Code = $"MLZ-{ek}",
            Name = $"Pasif kart sondası {ek}",
            Unit = "adet",
            IsActive = aktif,
            AverageUnitCost = 10m,
        };
        db.InventoryItems.Add(kalem);

        var depo = new Warehouse
        {
            CompanyId = company.Id,
            BranchId = branch.Id,
            Code = $"DEPO-{ek}",
            Name = $"Sonda deposu {ek}",
            Type = WarehouseType.Central,
        };
        db.Warehouses.Add(depo);
        await db.SaveChangesAsync();

        if (baslangicStok > 0)
        {
            // Zemin doğrudan kuruluyor: kartı PASİFKEN stok girmek zaten
            // yasak; sondanın ölçtüğü şey o yasağın kendisi.
            db.WarehouseStocks.Add(new WarehouseStock
            {
                WarehouseId = depo.Id,
                InventoryItemId = kalem.Id,
                Quantity = baslangicStok,
            });
            await db.SaveChangesAsync();
        }

        return new Zemin(kalem.Id, depo.Id);
    }

    private static object Duzeltme(Zemin z, decimal sayilan) => new
    {
        warehouseId = z.WarehouseId,
        inventoryItemId = z.ItemId,
        countedQuantity = sayilan,
        movementDate = DateTime.UtcNow,
        description = "pasif kart sondası",
    };

    private static object Cikis(Zemin z, decimal miktar) => new
    {
        warehouseId = z.WarehouseId,
        inventoryItemId = z.ItemId,
        quantity = miktar,
        movementDate = DateTime.UtcNow,
        description = "pasif kart sondası",
    };

    [Fact]
    public async Task PasifKart_StokArtiranHareket_Reddedilir()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var zemin = await ZeminKurAsync(db, aktif: false, baslangicStok: 5m);

        var istemci = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        // Sayılan (9) > mevcut (5) → sayım FAZLASI → stok ARTAR.
        var yanit = await istemci.PostAsJsonAsync(
            "/api/inventory/adjustments", Duzeltme(zemin, 9m));

        var govde = await yanit.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Contains("Bu malzeme kartı pasif", govde);
        // Mesaj NE YAPILACAĞINI söylemeli.
        Assert.Contains("aktif edin", govde);
    }

    [Fact]
    public async Task AktifKart_StokArtiranHareket_POZITIF_KONTROL_Gecer()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var zemin = await ZeminKurAsync(db, aktif: true, baslangicStok: 5m);

        var istemci = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var yanit = await istemci.PostAsJsonAsync(
            "/api/inventory/adjustments", Duzeltme(zemin, 9m));

        Assert.True(yanit.IsSuccessStatusCode,
            $"Aktif kartta artıran hareket engellendi — gizli kilit: " +
            $"{yanit.StatusCode} {await yanit.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task PasifKart_StokAzaltanHareket_POZITIF_KONTROL_Izinli()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var zemin = await ZeminKurAsync(db, aktif: false, baslangicStok: 5m);

        var istemci = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var yanit = await istemci.PostAsJsonAsync(
            "/api/inventory/issues", Cikis(zemin, 2m));

        Assert.True(yanit.IsSuccessStatusCode,
            $"Arşiv kartının kalan stoğu boşaltılamıyor — kural sonsuza kilitledi: " +
            $"{yanit.StatusCode} {await yanit.Content.ReadAsStringAsync()}");

        var kalan = await db.WarehouseStocks
            .Where(x => x.InventoryItemId == zemin.ItemId)
            .Select(x => x.Quantity)
            .SingleAsync();
        Assert.Equal(3m, kalan);
    }

    [Fact]
    public async Task PasifKart_SayimNoksani_Izinli()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var zemin = await ZeminKurAsync(db, aktif: false, baslangicStok: 5m);

        var istemci = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        // Sayılan (2) < mevcut (5) → sayım NOKSANI → stok AZALIR.
        var yanit = await istemci.PostAsJsonAsync(
            "/api/inventory/adjustments", Duzeltme(zemin, 2m));

        Assert.True(yanit.IsSuccessStatusCode,
            $"Pasif kartta sayım noksanı engellendi: {yanit.StatusCode} " +
            await yanit.Content.ReadAsStringAsync());
    }
}
