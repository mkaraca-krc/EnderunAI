using System.Net;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Inventory;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// DÖNEMSEL SAYIM (S7) sözleşmeleri.
///
/// Sayımın değeri farkı BULMASINDA değil, farkı gerekçeli ve onaylı
/// biçimde stoğa ve muhasebeye işlemesinde. Buradaki kurallar o
/// zincirin halkalarını koruyor: kilit, gerekçe, onay ayrımı,
/// sayılmayan satırın atlanması ve tek fiş.
/// </summary>
[Collection("Integration")]
public sealed class StockCountTests(DatabaseFixture fixture)
{
    private sealed record Scene(
        Guid CompanyId, Guid WarehouseId,
        Guid ZoneAId, Guid ZoneBId,
        Guid ItemInZoneA, Guid ItemInZoneB, Guid ItemWithoutZone);

    private static async Task<Scene> BuildAsync(
        AppDbContext db, string suffix, decimal onHand = 100m, decimal unitCost = 20m)
    {
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        var companyId = project.CompanyId;
        var branch = await db.Branches.FirstAsync(x => x.CompanyId == companyId);

        var warehouse = new Warehouse
        {
            CompanyId = companyId,
            BranchId = branch.Id,
            Code = $"DEPO-{suffix}",
            Name = $"Test Depo {suffix}",
            Type = WarehouseType.Central
        };
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();

        var zoneA = new WarehouseZone
        {
            WarehouseId = warehouse.Id,
            Code = $"A-{suffix}",
            Name = "Oda 1",
            Kind = WarehouseZoneKind.Open
        };
        var zoneB = new WarehouseZone
        {
            WarehouseId = warehouse.Id,
            Code = $"B-{suffix}",
            Name = "Oda 2",
            Kind = WarehouseZoneKind.Open
        };
        db.WarehouseZones.AddRange(zoneA, zoneB);

        var category = new InventoryCategory
        {
            Code = $"KAT-{suffix}",
            Name = $"Kategori {suffix}",
            AccountingKind = InventoryAccountingKind.Consumable
        };
        db.InventoryCategories.Add(category);
        await db.SaveChangesAsync();

        async Task<Guid> CardAsync(string tag, Guid? zoneId)
        {
            var item = new InventoryItem
            {
                CompanyId = companyId,
                InventoryCategoryId = category.Id,
                Code = $"MLZ-{tag}-{suffix}",
                Name = $"Malzeme {tag} {suffix}",
                Unit = "adet",
                AverageUnitCost = unitCost,
                WarehouseZoneId = zoneId
            };
            db.InventoryItems.Add(item);
            await db.SaveChangesAsync();

            db.WarehouseStocks.Add(new WarehouseStock
            {
                WarehouseId = warehouse.Id,
                InventoryItemId = item.Id,
                Quantity = onHand
            });
            await db.SaveChangesAsync();

            return item.Id;
        }

        var inA = await CardAsync("A", zoneA.Id);
        var inB = await CardAsync("B", zoneB.Id);
        var noZone = await CardAsync("N", null);

        await TestDataFactory.EnsureStockAccountsAsync(db, companyId);

        return new Scene(companyId, warehouse.Id, zoneA.Id, zoneB.Id, inA, inB, noZone);
    }

    private static async Task<HttpClient> CounterAsync(DatabaseFixture fixture, string suffix) =>
        await TestUserFactory.CreateClientWithRolesAsync(fixture, $"{suffix}c", ["Depo Sorumlusu"]);

    private static async Task<HttpClient> ApproverAsync(DatabaseFixture fixture, string suffix) =>
        await TestUserFactory.CreateClientWithRolesAsync(fixture, $"{suffix}a", ["Genel Müdür"]);

    private static async Task<Guid> StartAsync(
        HttpClient client, Scene scene, Guid? zoneId, string name = "2026 1. Yarıyıl")
    {
        var response = await client.PostAsJsonAsync("/api/stock-counts", new
        {
            companyId = scene.CompanyId,
            warehouseId = scene.WarehouseId,
            warehouseZoneId = zoneId,
            name,
            countDate = DateTime.UtcNow.Date
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<StartResult>();
        Assert.NotNull(body);
        return body!.Id;
    }

    private sealed record StartResult(Guid Id, string DocumentNumber, int LineCount);

    /// <summary>
    /// OTURUM SİSTEM MİKTARLARINI DONDURUR ve bölge filtresi uygular.
    ///
    /// Bölge sayımında yalnız o bölgenin kartları listelenmeli;
    /// tamamı listelenseydi sayan kişi elindeki bölgede olmayan
    /// malzemeyi "bulamadım" diye eksik yazardı.
    /// </summary>
    [Fact]
    public async Task Oturum_BolgeninKartlariniDondurur()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix, onHand: 40m, unitCost: 20m);

        var counter = await CounterAsync(fixture, suffix);
        var sessionId = await StartAsync(counter, scene, scene.ZoneAId);

        var lines = await db.StockCountLines.AsNoTracking()
            .Where(x => x.StockCountSessionId == sessionId)
            .ToListAsync();

        Assert.Single(lines);
        Assert.Equal(scene.ItemInZoneA, lines[0].InventoryItemId);
        Assert.Equal(40m, lines[0].SystemQuantity);
        Assert.Equal(20m, lines[0].UnitCostAtCount);
        Assert.Null(lines[0].CountedQuantity);
    }

    /// <summary>
    /// SAYIM SIRASINDA BÖLGEYE HAREKET GİRMEZ.
    ///
    /// Sayan kişi 40 saymışken araya 5 çıkış girerse sistem 35 gösterir
    /// ve 5 adet fire gibi görünür — gerçekte var olmayan bir kayıp.
    /// Kilit SERT: uyarı değil, engel.
    /// </summary>
    [Fact]
    public async Task SayilanBolge_HareketeKapali_DigerBolgeAcik()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix, onHand: 40m);

        var counter = await CounterAsync(fixture, suffix);
        await StartAsync(counter, scene, scene.ZoneAId);

        var mover = await ApproverAsync(fixture, suffix);

        // Sayılan bölgedeki kart: ENGELLİ.
        var blocked = await mover.PostAsJsonAsync("/api/inventory/issues", new
        {
            warehouseId = scene.WarehouseId,
            inventoryItemId = scene.ItemInZoneA,
            quantity = 1m,
            movementDate = DateTime.UtcNow.Date
        });
        Assert.NotEqual(HttpStatusCode.OK, blocked.StatusCode);

        var stockA = await db.WarehouseStocks.AsNoTracking()
            .SingleAsync(x => x.InventoryItemId == scene.ItemInZoneA);
        Assert.Equal(40m, stockA.Quantity);

        // BAŞKA bölgedeki kart: SERBEST. Kilit bölge bazlı olmasaydı
        // tek bir bölge sayımı tüm depoyu durdururdu.
        var allowed = await mover.PostAsJsonAsync("/api/inventory/issues", new
        {
            warehouseId = scene.WarehouseId,
            inventoryItemId = scene.ItemInZoneB,
            quantity = 1m,
            movementDate = DateTime.UtcNow.Date
        });
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    /// <summary>
    /// TÜM DEPO SAYIMINDA bölgesiz kart da kilitli — o kart listeye
    /// girdiği için hareketi de durmalı.
    /// </summary>
    [Fact]
    public async Task TumDepoSayimi_BolgesizKartiDaKilitler()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix, onHand: 10m);

        var counter = await CounterAsync(fixture, suffix);
        await StartAsync(counter, scene, null);

        var mover = await ApproverAsync(fixture, suffix);

        var blocked = await mover.PostAsJsonAsync("/api/inventory/issues", new
        {
            warehouseId = scene.WarehouseId,
            inventoryItemId = scene.ItemWithoutZone,
            quantity = 1m,
            movementDate = DateTime.UtcNow.Date
        });

        Assert.NotEqual(HttpStatusCode.OK, blocked.StatusCode);
    }

    /// <summary>
    /// GEREKÇESİZ FARK ONAYA GİDEMEZ.
    ///
    /// Gerekçesiz fark "bir şey oldu ama kimse yazmadı" demektir;
    /// fire oranı ölçülemez ve tekrar eden kayıp fark edilmez.
    /// </summary>
    [Fact]
    public async Task GerekcesizFark_OnayaGonderilemez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix, onHand: 40m);

        var counter = await CounterAsync(fixture, suffix);
        var sessionId = await StartAsync(counter, scene, scene.ZoneAId);

        var lineId = await db.StockCountLines.AsNoTracking()
            .Where(x => x.StockCountSessionId == sessionId)
            .Select(x => x.Id).SingleAsync();

        // Fark var, gerekçe YOK.
        var saved = await counter.PutAsJsonAsync($"/api/stock-counts/{sessionId}/miktarlar", new
        {
            lines = new[] { new { lineId, countedQuantity = 35m, varianceReason = (int?)null, note = (string?)null } }
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var submitted = await counter.PostAsync($"/api/stock-counts/{sessionId}/onaya-gonder", null);
        Assert.Equal(HttpStatusCode.BadRequest, submitted.StatusCode);

        // Gerekçe girilince geçer.
        await counter.PutAsJsonAsync($"/api/stock-counts/{sessionId}/miktarlar", new
        {
            lines = new[] { new { lineId, countedQuantity = 35m, varianceReason = (int?)0, note = "Fire" } }
        });

        var again = await counter.PostAsync($"/api/stock-counts/{sessionId}/onaya-gonder", null);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    /// <summary>
    /// SAYMAK ile ONAYLAMAK AYRI İZİNDE.
    ///
    /// Aynı kişi hem sayıp hem onaylayabilseydi fark, gerekçesi hiç
    /// sorgulanmadan stoğa ve gidere işlenirdi.
    /// </summary>
    [Fact]
    public async Task DepoSorumlusu_KendiSayiminiOnaylayamaz()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix, onHand: 40m);

        var counter = await CounterAsync(fixture, suffix);
        var sessionId = await StartAsync(counter, scene, scene.ZoneAId);

        var lineId = await db.StockCountLines.AsNoTracking()
            .Where(x => x.StockCountSessionId == sessionId)
            .Select(x => x.Id).SingleAsync();

        await counter.PutAsJsonAsync($"/api/stock-counts/{sessionId}/miktarlar", new
        {
            lines = new[] { new { lineId, countedQuantity = 35m, varianceReason = (int?)0, note = "Fire" } }
        });
        await counter.PostAsync($"/api/stock-counts/{sessionId}/onaya-gonder", null);

        var denied = await counter.PostAsync($"/api/stock-counts/{sessionId}/onayla", null);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var approver = await ApproverAsync(fixture, suffix);
        var allowed = await approver.PostAsync($"/api/stock-counts/{sessionId}/onayla", null);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    /// <summary>
    /// ONAY: stok düzeltilir, TEK fiş kesilir, noksan ve fazla AYRI
    /// satırlarda durur ve SAYILMAYAN satır atlanır.
    ///
    /// Noksan ile fazla netleştirilseydi "ne kadar fire var" sorusu
    /// cevapsız kalırdı: net 100 TL fark, 500 kayıp ve 400 fazlanın
    /// toplamı da olabilir.
    /// </summary>
    [Fact]
    public async Task Onay_StoguDuzeltir_TekFisKeser_SayilmayaniAtlar()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix, onHand: 100m, unitCost: 20m);

        var counter = await CounterAsync(fixture, suffix);
        var sessionId = await StartAsync(counter, scene, null);

        var lines = await db.StockCountLines.AsNoTracking()
            .Where(x => x.StockCountSessionId == sessionId)
            .ToListAsync();

        var lineA = lines.Single(x => x.InventoryItemId == scene.ItemInZoneA);
        var lineB = lines.Single(x => x.InventoryItemId == scene.ItemInZoneB);
        // ItemWithoutZone SAYILMIYOR — atlanmalı.

        await counter.PutAsJsonAsync($"/api/stock-counts/{sessionId}/miktarlar", new
        {
            lines = new object[]
            {
                // NOKSAN: 100 -> 90, 10 × 20 = 200 TL
                new { lineId = lineA.Id, countedQuantity = 90m, varianceReason = (int?)0, note = "Fire" },
                // FAZLA: 100 -> 105, 5 × 20 = 100 TL
                new { lineId = lineB.Id, countedQuantity = 105m, varianceReason = (int?)2, note = "Kayıt hatası" }
            }
        });

        await counter.PostAsync($"/api/stock-counts/{sessionId}/onaya-gonder", null);

        var approver = await ApproverAsync(fixture, suffix);
        Assert.Equal(HttpStatusCode.OK,
            (await approver.PostAsync($"/api/stock-counts/{sessionId}/onayla", null)).StatusCode);

        var stocks = await db.WarehouseStocks.AsNoTracking()
            .Where(x => x.WarehouseId == scene.WarehouseId)
            .ToDictionaryAsync(x => x.InventoryItemId, x => x.Quantity);

        Assert.Equal(90m, stocks[scene.ItemInZoneA]);
        Assert.Equal(105m, stocks[scene.ItemInZoneB]);
        // SAYILMAYAN SATIR DOKUNULMADAN KALDI.
        Assert.Equal(100m, stocks[scene.ItemWithoutZone]);

        var session = await db.StockCountSessions.AsNoTracking()
            .SingleAsync(x => x.Id == sessionId);

        Assert.Equal(StockCountStatus.Approved, session.Status);
        Assert.NotNull(session.AccountingVoucherId);

        // OTURUM BAŞINA TEK FİŞ.
        var voucherCount = await db.AccountingVouchers.AsNoTracking()
            .CountAsync(x => x.SourceModule == "StockCount" && x.SourceEntityId == sessionId);
        Assert.Equal(1, voucherCount);

        var voucherLines = await db.AccountingVoucherLines.AsNoTracking()
            .Where(x => x.AccountingVoucherId == session.AccountingVoucherId)
            .ToListAsync();

        async Task<Guid> AccountAsync(string code) =>
            await db.AccountingAccounts.AsNoTracking()
                .Where(x => x.CompanyId == scene.CompanyId && x.Code == code)
                .Select(x => x.Id).SingleAsync();

        var shortageId = await AccountAsync(InventoryAccountResolver.InventoryShortageCode);
        var surplusId = await AccountAsync(InventoryAccountResolver.InventorySurplusCode);
        var stockId = await AccountAsync(InventoryAccountResolver.ConsumableStockCode);

        // NETLEŞTİRİLMİYOR: 200 noksan ve 100 fazla ayrı duruyor.
        Assert.Equal(200m, voucherLines.Single(x => x.AccountingAccountId == shortageId).DebitAmount);
        Assert.Equal(100m, voucherLines.Single(x => x.AccountingAccountId == surplusId).CreditAmount);

        var stockLines = voucherLines.Where(x => x.AccountingAccountId == stockId).ToList();
        Assert.Equal(200m, stockLines.Sum(x => x.CreditAmount));
        Assert.Equal(100m, stockLines.Sum(x => x.DebitAmount));
    }

    /// <summary>
    /// FARK HESABI FİNANS AYARINDAN GELİR (kullanıcı isteği); boşsa
    /// S6c'de açılan 689.02 / 649.03 kullanılır.
    /// </summary>
    [Fact]
    public async Task FarkHesabi_FinansAyarindanOkunur()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix, onHand: 50m, unitCost: 10m);

        // Mali müşavir noksanı BAŞKA bir hesaba yönlendiriyor.
        var custom = new AccountingAccount
        {
            CompanyId = scene.CompanyId,
            Code = $"157.{suffix}"[..12],
            Name = "Stok Değer Düşüklüğü",
            Nature = AccountingAccountNature.Debit,
            Level = 2,
            IsPostingAllowed = true
        };
        db.AccountingAccounts.Add(custom);
        await db.SaveChangesAsync();

        var settings = await db.CompanyFinanceSettings
            .SingleOrDefaultAsync(x => x.CompanyId == scene.CompanyId);

        if (settings is null)
        {
            settings = new CompanyFinanceSettings { CompanyId = scene.CompanyId };
            db.CompanyFinanceSettings.Add(settings);
        }

        settings.StockCountShortageAccountId = custom.Id;
        await db.SaveChangesAsync();

        var counter = await CounterAsync(fixture, suffix);
        var sessionId = await StartAsync(counter, scene, scene.ZoneAId);

        var lineId = await db.StockCountLines.AsNoTracking()
            .Where(x => x.StockCountSessionId == sessionId)
            .Select(x => x.Id).SingleAsync();

        await counter.PutAsJsonAsync($"/api/stock-counts/{sessionId}/miktarlar", new
        {
            lines = new[] { new { lineId, countedQuantity = 48m, varianceReason = (int?)1, note = "Kayıp" } }
        });
        await counter.PostAsync($"/api/stock-counts/{sessionId}/onaya-gonder", null);

        var approver = await ApproverAsync(fixture, suffix);
        await approver.PostAsync($"/api/stock-counts/{sessionId}/onayla", null);

        var session = await db.StockCountSessions.AsNoTracking().SingleAsync(x => x.Id == sessionId);

        var voucherLines = await db.AccountingVoucherLines.AsNoTracking()
            .Where(x => x.AccountingVoucherId == session.AccountingVoucherId)
            .ToListAsync();

        // 2 × 10 = 20 TL, AYARDAKİ hesaba.
        Assert.Equal(20m, voucherLines.Single(x => x.AccountingAccountId == custom.Id).DebitAmount);

        var defaultShortage = await db.AccountingAccounts.AsNoTracking()
            .Where(x => x.CompanyId == scene.CompanyId
                && x.Code == InventoryAccountResolver.InventoryShortageCode)
            .Select(x => x.Id).SingleAsync();

        Assert.DoesNotContain(voucherLines, x => x.AccountingAccountId == defaultShortage);
    }

    /// <summary>
    /// ONAYDAN SONRA KİLİT KALKAR — depo yeniden çalışır hale gelmeli.
    /// İptalde de kalkar.
    /// </summary>
    [Fact]
    public async Task Onay_VeIptal_KilidiKaldirir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix, onHand: 30m);

        var counter = await CounterAsync(fixture, suffix);
        var mover = await ApproverAsync(fixture, suffix);

        // 1) İPTAL kilidi kaldırır.
        var cancelled = await StartAsync(counter, scene, scene.ZoneAId);

        Assert.NotEqual(HttpStatusCode.OK, (await mover.PostAsJsonAsync(
            "/api/inventory/issues", new
            {
                warehouseId = scene.WarehouseId,
                inventoryItemId = scene.ItemInZoneA,
                quantity = 1m,
                movementDate = DateTime.UtcNow.Date
            })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await counter.PostAsJsonAsync(
            $"/api/stock-counts/{cancelled}/iptal",
            new { reason = "Yanlış bölge seçildi" })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await mover.PostAsJsonAsync(
            "/api/inventory/issues", new
            {
                warehouseId = scene.WarehouseId,
                inventoryItemId = scene.ItemInZoneA,
                quantity = 1m,
                movementDate = DateTime.UtcNow.Date
            })).StatusCode);

        // 2) ONAY da kaldırır.
        var approved = await StartAsync(counter, scene, scene.ZoneAId, "İkinci tur");

        var lineId = await db.StockCountLines.AsNoTracking()
            .Where(x => x.StockCountSessionId == approved)
            .Select(x => x.Id).SingleAsync();

        await counter.PutAsJsonAsync($"/api/stock-counts/{approved}/miktarlar", new
        {
            lines = new[] { new { lineId, countedQuantity = 29m, varianceReason = (int?)3, note = "Kırılma" } }
        });
        await counter.PostAsync($"/api/stock-counts/{approved}/onaya-gonder", null);
        await mover.PostAsync($"/api/stock-counts/{approved}/onayla", null);

        Assert.Equal(HttpStatusCode.OK, (await mover.PostAsJsonAsync(
            "/api/inventory/issues", new
            {
                warehouseId = scene.WarehouseId,
                inventoryItemId = scene.ItemInZoneA,
                quantity = 1m,
                movementDate = DateTime.UtcNow.Date
            })).StatusCode);
    }

    /// <summary>
    /// AYNI DEPO/BÖLGEDE İKİNCİ OTURUM AÇILMAZ.
    ///
    /// İki oturum, iki farklı dondurulmuş sistem miktarı demektir;
    /// ikisi de onaylanırsa aynı fark stoğa iki kez uygulanırdı.
    /// </summary>
    [Fact]
    public async Task AyniBolgede_IkinciOturumAcilmaz()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var counter = await CounterAsync(fixture, suffix);
        await StartAsync(counter, scene, scene.ZoneAId);

        var second = await counter.PostAsJsonAsync("/api/stock-counts", new
        {
            companyId = scene.CompanyId,
            warehouseId = scene.WarehouseId,
            warehouseZoneId = scene.ZoneAId,
            name = "Aynı bölge tekrar",
            countDate = DateTime.UtcNow.Date
        });

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);

        // TÜM DEPO sayımı da açılamaz: bölge sayımını kapsardı.
        var whole = await counter.PostAsJsonAsync("/api/stock-counts", new
        {
            companyId = scene.CompanyId,
            warehouseId = scene.WarehouseId,
            warehouseZoneId = (Guid?)null,
            name = "Tüm depo",
            countDate = DateTime.UtcNow.Date
        });

        Assert.Equal(HttpStatusCode.BadRequest, whole.StatusCode);
    }

    /// <summary>
    /// SAYIM SONRASI MUTABAKAT SIFIR FARK VERİR — düzeltme fişi stok
    /// değerindeki değişimi birebir karşılamalı.
    /// </summary>
    [Fact]
    public async Task SayimSonrasi_MutabakatSifirFarkVerir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var report = scope.ServiceProvider
            .GetRequiredService<IStockAccountingConsistencyService>();

        var scene = await BuildAsync(db, suffix, onHand: 10m, unitCost: 50m);

        // Depoyu muhasebeyle EŞİT başlat: 3 kart × 10 × 50 = 1500.
        var stockAccountId = await db.AccountingAccounts
            .Where(x => x.CompanyId == scene.CompanyId
                && x.Code == InventoryAccountResolver.ConsumableStockCode)
            .Select(x => x.Id).SingleAsync();
        var grirId = await db.AccountingAccounts
            .Where(x => x.CompanyId == scene.CompanyId
                && x.Code == InventoryAccountResolver.GoodsReceivedNotInvoicedCode)
            .Select(x => x.Id).SingleAsync();

        var entry = new AccountingVoucher
        {
            CompanyId = scene.CompanyId,
            VoucherNumber = $"GIRIS-{suffix}",
            Status = AccountingVoucherStatus.Posted,
            VoucherDate = DateTime.UtcNow.Date,
            TotalDebit = 1500m,
            TotalCredit = 1500m
        };
        db.AccountingVouchers.Add(entry);
        await db.SaveChangesAsync();

        db.AccountingVoucherLines.AddRange(
            new AccountingVoucherLine
            {
                AccountingVoucherId = entry.Id,
                AccountingAccountId = stockAccountId,
                LineNumber = 1,
                DebitAmount = 1500m,
                CreditAmount = 0m,
                CurrencyCode = "TRY",
                ExchangeRate = 1m
            },
            new AccountingVoucherLine
            {
                AccountingVoucherId = entry.Id,
                AccountingAccountId = grirId,
                LineNumber = 2,
                DebitAmount = 0m,
                CreditAmount = 1500m,
                CurrencyCode = "TRY",
                ExchangeRate = 1m
            });
        await db.SaveChangesAsync();

        Assert.True((await report.BuildAsync(scene.CompanyId, default)).IsConsistent,
            "Sayım öncesi zaten tutmalıydı.");

        var counter = await CounterAsync(fixture, suffix);
        var sessionId = await StartAsync(counter, scene, null);

        var lines = await db.StockCountLines.AsNoTracking()
            .Where(x => x.StockCountSessionId == sessionId).ToListAsync();

        var a = lines.Single(x => x.InventoryItemId == scene.ItemInZoneA);
        var b = lines.Single(x => x.InventoryItemId == scene.ItemInZoneB);

        await counter.PutAsJsonAsync($"/api/stock-counts/{sessionId}/miktarlar", new
        {
            lines = new object[]
            {
                new { lineId = a.Id, countedQuantity = 8m, varianceReason = (int?)0, note = "Fire" },
                new { lineId = b.Id, countedQuantity = 13m, varianceReason = (int?)2, note = "Kayıt hatası" }
            }
        });
        await counter.PostAsync($"/api/stock-counts/{sessionId}/onaya-gonder", null);

        var approver = await ApproverAsync(fixture, suffix);
        await approver.PostAsync($"/api/stock-counts/{sessionId}/onayla", null);

        var after = await report.BuildAsync(scene.CompanyId, default);
        var line = after.Lines.Single(x =>
            x.StockAccountCode == InventoryAccountResolver.ConsumableStockCode);

        // 8 + 13 + 10 = 31 adet × 50 = 1550.
        Assert.Equal(1550m, line.StockValue);
        Assert.Equal(1550m, line.AccountBalance);
        Assert.Equal(0m, line.Difference);
        Assert.True(after.IsConsistent);
    }
}
