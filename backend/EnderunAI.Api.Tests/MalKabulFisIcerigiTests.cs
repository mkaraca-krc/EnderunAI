using System.Net;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.GoodsReceipt;
using EnderunAI.Api.Models.PurchaseOrder;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using PurchaseOrderEntity = EnderunAI.Api.Models.PurchaseOrder.PurchaseOrder;

namespace EnderunAI.Api.Tests;

/// <summary>
/// MAL KABUL FİŞİNİN İÇERİĞİ — 150/153 BORÇ, 379.01 ALACAK.
///
/// ═══ NEDEN VAR: ÖLÇÜLEN KAPSAM BOŞLUĞU (2026-09-13) ═══
///
/// `/api/goods-receipts/{id}/post` ucunu GERÇEKTEN çağıran testler VARDI
/// (`WarehouseIntegrationTests`, `PurchaseReturnTests`) — ama hiçbiri
/// üretilen MUHASEBE FİŞİNİ okumuyordu: yalnız stok, ortalama maliyet ve
/// hareket satırı sınanıyordu.
///
/// Sonuç: `accountingPoster.PostAsync` çağrısı silinse POST yine 200
/// döner ve TÜM SÜİT YEŞİL KALIRDI. Fişin varlığını koruyan tek şey
/// `GoodsReceiptAccountingTests`ti — o da mal kabulü hiç çağırmayan, KOD
/// METNİNDE dizge arayan bir testti:
///
///     code.IndexOf("accountingPoster.PostAsync")
///
/// Yani davranışı değil metni koruyordu ve hiçbir mutasyonda kırmızı
/// yanamazdı. Bu sonda o boşluğu davranışla kapatıyor.
///
/// ═══ NEDEN ÖNEMLİ ═══
///
/// Canlıda `accounting_voucher_lines` içinde 150/153/379.01 hesaplarına
/// ait TEK SATIR YOKTU — stok→muhasebe hattı üretimde hiç çalışmamıştı
/// ve 3237 testin hiçbiri bunu söylemiyordu.
/// </summary>
[Collection("Integration")]
public sealed class MalKabulFisIcerigiTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task MalKabulFislenince_150Borc_379AlacakYazilir()
    {
        var ek = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, branch, _) = await TestDataFactory.CreateCompanyStackAsync(db, ek);
        await TestDataFactory.EnsureStockAccountsAsync(db, company.Id);
        var proje = await TestDataFactory.CreateProjectAsync(db, ek + "P");

        var tedarikci = new CurrentAccount
        {
            CompanyId = company.Id,
            Code = $"TED-{ek}",
            Title = $"Test Tedarikçi {ek}",
            Roles = CurrentAccountRoles.Supplier,
            Status = CurrentAccountStatus.Approved,
        };
        db.CurrentAccounts.Add(tedarikci);

        var kalem = new InventoryItem
        {
            CompanyId = company.Id,
            Code = $"MLZ-{ek}",
            Name = $"Test Malzeme {ek}",
            Unit = "adet",
        };
        db.InventoryItems.Add(kalem);

        var depo = new Warehouse
        {
            CompanyId = company.Id,
            BranchId = branch.Id,
            Code = $"DEPO-{ek}",
            Name = $"Test Depo {ek}",
            Type = WarehouseType.Central,
        };
        db.Warehouses.Add(depo);

        var talep = new PurchaseRequest
        {
            CompanyId = company.Id,
            ProjectId = proje.Id,
            RequestNumber = $"PR-{ek}",
            RequestDate = DateTime.UtcNow.Date,
            RequestedByName = "Sonda",
            Priority = PurchaseRequestPriority.Normal,
            Status = PurchaseRequestStatus.Approved,
        };
        db.PurchaseRequests.Add(talep);
        await db.SaveChangesAsync();

        var rfq = new EnderunAI.Api.Models.Rfq.Rfq
        {
            CompanyId = company.Id,
            PurchaseRequestId = talep.Id,
            RfqNumber = $"RFQ-{ek}",
            Title = "Sonda RFQ",
            IssueDate = DateTime.UtcNow.Date,
            Currency = "TRY",
        };
        db.Rfqs.Add(rfq);
        await db.SaveChangesAsync();

        // 10 adet × 25 TRY = 250 TRY — fişin iki satırı da bu tutarı taşımalı.
        var siparis = new PurchaseOrderEntity
        {
            CompanyId = company.Id,
            ProjectId = proje.Id,
            RfqId = rfq.Id,
            SupplierCurrentAccountId = tedarikci.Id,
            OrderNumber = $"PO-{ek}",
            OrderDate = DateTime.UtcNow.Date,
            Status = PurchaseOrderStatus.Approved,
            Currency = "TRY",
            ExchangeRate = 1m,
        };
        var siparisKalemi = new PurchaseOrderItem
        {
            LineNumber = 1,
            MaterialDescription = kalem.Name,
            Quantity = 10,
            Unit = "adet",
            UnitPrice = 25m,
            NetUnitPrice = 25m,
            TotalPrice = 250m,
        };
        siparis.Items.Add(siparisKalemi);
        db.PurchaseOrders.Add(siparis);
        await db.SaveChangesAsync();

        var malKabul = new GoodsReceipt
        {
            CompanyId = company.Id,
            PurchaseOrderId = siparis.Id,
            WarehouseId = depo.Id,
            ReceiptNumber = $"GR-{ek}",
            ReceiptDate = DateTime.UtcNow.Date,
            Status = GoodsReceiptStatus.Draft,
            ReceivedByName = "Sonda",
        };
        malKabul.Items.Add(new GoodsReceiptItem
        {
            PurchaseOrderItemId = siparisKalemi.Id,
            InventoryItemId = kalem.Id,
            LineNumber = 1,
            MaterialDescription = kalem.Name,
            OrderedQuantity = 10,
            DeliveredQuantity = 10,
            AcceptedQuantity = 10,
            Unit = "adet",
        });
        db.GoodsReceipts.Add(malKabul);
        await db.SaveChangesAsync();

        var istemci = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var yanit = await istemci.PostAsync($"/api/goods-receipts/{malKabul.Id}/post", null);

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);

        using var dogrula = fixture.Factory.Services.CreateScope();
        var vt = dogrula.ServiceProvider.GetRequiredService<AppDbContext>();

        var kaydedilen = await vt.GoodsReceipts
            .SingleAsync(x => x.Id == malKabul.Id);

        /*
         * FİŞ GERÇEKTEN KESİLDİ Mİ — belge düzeyinde bağ.
         * KIRMIZIYA DÖNERSE: mal kabul stok yazar ama muhasebeye hiç
         * girmez; mutabakat raporu sapar ve kimse görmez.
         */
        Assert.NotNull(kaydedilen.AccountingVoucherId);

        var satirlar = await vt.AccountingVoucherLines
            .Where(x => x.AccountingVoucherId == kaydedilen.AccountingVoucherId!.Value)
            .Join(vt.AccountingAccounts, l => l.AccountingAccountId, a => a.Id,
                  (l, a) => new { a.Code, l.DebitAmount, l.CreditAmount })
            .ToListAsync();

        /*
         * ÖLÇÜM SAĞLIĞI (Kural 48): satır okunamadıysa aşağıdaki her
         * iddia BOŞ KÜME üzerinde koşar ve yeşil yanar.
         */
        Assert.True(satirlar.Count >= 2,
            $"Fişte yalnız {satirlar.Count} satır var; iddialar bu hâlde bir şey söylemez.");

        // STOK HESABI BORÇLANIR — 150 (sarf) ya da 153 (ticari mal).
        var stokSatiri = Assert.Single(
            satirlar.Where(x => x.Code is "150" or "153"));
        Assert.Equal(250m, stokSatiri.DebitAmount);
        Assert.Equal(0m, stokSatiri.CreditAmount);

        // GR-IR ALACAKLANIR — fatura henüz gelmedi.
        var grIr = Assert.Single(satirlar.Where(x => x.Code == "379.01"));
        Assert.Equal(0m, grIr.DebitAmount);
        Assert.Equal(250m, grIr.CreditAmount);

        // FİŞ DENK: borç toplamı = alacak toplamı.
        Assert.Equal(satirlar.Sum(x => x.DebitAmount), satirlar.Sum(x => x.CreditAmount));

        /*
         * KDV MAL KABULDE YAZILMAZ — fatura gelince yazılır.
         * KIRMIZIYA DÖNERSE: KDV iki kez kaydedilir.
         */
        Assert.DoesNotContain(satirlar, x => x.Code is "191" or "391");
    }
}
