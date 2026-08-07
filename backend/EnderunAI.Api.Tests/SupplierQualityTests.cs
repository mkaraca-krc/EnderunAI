using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.GoodsReceipt;
using EnderunAI.Api.Models.PurchaseOrder;
using EnderunAI.Api.Services.Purchasing;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using PurchaseOrderEntity = EnderunAI.Api.Models.PurchaseOrder.PurchaseOrder;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Tedarikçi kalite karnesi (S4).
///
/// Asıl güvenceler:
/// - Oran MİKTAR üzerinden hesaplanır: 1.000 adetten 5'i bozuk çıkan
///   tedarikçi ile 10 adetten 5'i bozuk çıkan aynı değildir.
/// - Yalnızca KESİNLEŞMİŞ mal kabul sayılır; taslak, depo sorumlusunun
///   girip vazgeçtiği bir kayıt olabilir ve düzeltilmiş bir hatayı
///   tedarikçinin karnesine yazmak haksızlık olurdu.
/// </summary>
[Collection("Integration")]
public sealed class SupplierQualityTests(DatabaseFixture fixture)
{
    private sealed record Context(
        Guid CompanyId, Guid ProjectId, Guid SupplierId, Guid WarehouseId);

    private async Task<Context> CreateContextAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var branchId = await db.Branches
            .Where(x => x.CompanyId == project.CompanyId)
            .Select(x => x.Id)
            .FirstAsync();

        var supplier = new CurrentAccount
        {
            CompanyId = project.CompanyId,
            Code = $"TED-{suffix}",
            Title = $"Test Tedarikçi {suffix}",
            Roles = CurrentAccountRoles.Supplier,
            Status = CurrentAccountStatus.Approved
        };
        var warehouse = new Warehouse
        {
            CompanyId = project.CompanyId,
            BranchId = branchId,
            Code = $"DEP-{suffix}",
            Name = "Test Depo"
        };

        db.CurrentAccounts.Add(supplier);
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();

        return new Context(
            project.CompanyId, project.Id, supplier.Id, warehouse.Id);
    }

    /// <summary>
    /// Sipariş + mal kabul yazar.
    /// </summary>
    private async Task AddReceiptAsync(
        Context context,
        decimal delivered,
        decimal accepted,
        decimal rejected,
        decimal damaged,
        GoodsReceiptStatus status = GoodsReceiptStatus.Posted,
        DateTime? receiptDate = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Sipariş her zaman bir RFQ'dan doğuyor (RfqId zorunlu FK);
        // RFQ da bir talebe bağlı. Zinciri testte de kuruyoruz.
        var request = new PurchaseRequest
        {
            CompanyId = context.CompanyId,
            ProjectId = context.ProjectId,
            RequestNumber = $"TLP-{Guid.NewGuid():N}"[..14],
            RequestDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            RequestedByName = "Test Talep",
            Status = PurchaseRequestStatus.Approved
        };
        db.PurchaseRequests.Add(request);
        await db.SaveChangesAsync();

        var rfq = new Models.Rfq.Rfq
        {
            CompanyId = context.CompanyId,
            PurchaseRequestId = request.Id,
            RfqNumber = $"RFQ-{Guid.NewGuid():N}"[..14],
            Title = "Test teklif talebi",
            IssueDate = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc),
            Currency = "TRY"
        };
        db.Rfqs.Add(rfq);
        await db.SaveChangesAsync();

        var order = new PurchaseOrderEntity
        {
            RfqId = rfq.Id,
            CompanyId = context.CompanyId,
            ProjectId = context.ProjectId,
            SupplierCurrentAccountId = context.SupplierId,
            OrderNumber = $"SIP-{Guid.NewGuid():N}"[..14],
            OrderDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = PurchaseOrderStatus.Completed,
            Currency = "TRY",
            ExchangeRate = 1m,
            Subtotal = 1_000m,
            GrandTotal = 1_000m
        };

        var orderItem = new PurchaseOrderItem
        {
            LineNumber = 1,
            MaterialDescription = "Test malzeme",
            Quantity = delivered,
            Unit = "AD"
        };

        order.Items.Add(orderItem);
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync();

        db.GoodsReceipts.Add(new GoodsReceipt
        {
            CompanyId = context.CompanyId,
            PurchaseOrderId = order.Id,
            WarehouseId = context.WarehouseId,
            ReceiptNumber = $"MK-{Guid.NewGuid():N}"[..14],
            ReceiptDate = receiptDate ?? DateTime.UtcNow.Date.AddDays(-10),
            Status = status,
            ReceivedByName = "Test Depocu",
            Items =
            [
                new GoodsReceiptItem
                {
                    PurchaseOrderItemId = orderItem.Id,
                    LineNumber = 1,
                    MaterialDescription = "Test malzeme",
                    OrderedQuantity = delivered,
                    DeliveredQuantity = delivered,
                    AcceptedQuantity = accepted,
                    RejectedQuantity = rejected,
                    DamagedQuantity = damaged,
                    Unit = "AD"
                }
            ]
        });

        await db.SaveChangesAsync();
    }

    private async Task<SupplierQualityReport> GetReportAsync(Context context)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var service = scope.ServiceProvider
            .GetRequiredService<SupplierQualityService>();

        return await service.GetReportAsync(context.CompanyId, months: 12, default);
    }

    /// <summary>
    /// Red ve hasar birlikte orana girmeli; ikisi de tedarikçinin
    /// gönderdiği kusurlu maldır.
    /// </summary>
    [Fact]
    public async Task RejectionRate_CountsRejectedAndDamaged()
    {
        var context = await CreateContextAsync();

        // 100 geldi: 90 kabul, 6 red, 4 hasar → %10
        await AddReceiptAsync(context, 100m, 90m, 6m, 4m);

        var report = await GetReportAsync(context);

        var row = Assert.Single(report.Rows);

        Assert.Equal(100m, row.DeliveredQuantity);
        Assert.Equal(90m, row.AcceptedQuantity);
        Assert.Equal(10m, row.RejectionRatePercent);
        Assert.Equal(1, row.ProblemReceiptCount);
        Assert.Equal(1, report.ProblemSupplierCount);
    }

    /// <summary>
    /// BU PAKETİN ASIL GÜVENCESİ: oran miktar üzerinden hesaplanmalı,
    /// teslimat sayısı üzerinden değil. Büyük hacimde birkaç kusurlu
    /// kalem, küçük hacimde aynı sayıda kusurla aynı sayılmamalı.
    /// </summary>
    [Fact]
    public async Task RejectionRate_IsQuantityWeightedNotReceiptCount()
    {
        var context = await CreateContextAsync();

        // İki teslimat: biri büyük ve temiz, biri küçük ve kusurlu
        await AddReceiptAsync(context, 1_000m, 1_000m, 0m, 0m);
        await AddReceiptAsync(context, 10m, 5m, 5m, 0m);

        var report = await GetReportAsync(context);
        var row = Assert.Single(report.Rows);

        // 5 / 1.010 = %0,5 — teslimat sayısına göre olsaydı %50 çıkardı
        Assert.Equal(0.5m, row.RejectionRatePercent);
        Assert.Equal(2, row.ReceiptCount);
        Assert.Equal(1, row.ProblemReceiptCount);

        // Eşiğin altında kaldığı için sorunlu sayılmamalı
        Assert.Equal(0, report.ProblemSupplierCount);
    }

    /// <summary>
    /// Taslak mal kabul karneye girmemeli: henüz bir teslimat değil,
    /// depo sorumlusu girip vazgeçmiş olabilir.
    /// </summary>
    [Fact]
    public async Task DraftReceipt_IsExcluded()
    {
        var context = await CreateContextAsync();

        await AddReceiptAsync(context, 100m, 100m, 0m, 0m);
        await AddReceiptAsync(
            context, 100m, 0m, 100m, 0m, status: GoodsReceiptStatus.Draft);

        var report = await GetReportAsync(context);
        var row = Assert.Single(report.Rows);

        // Yalnızca kesinleşmiş, temiz teslimat sayıldı
        Assert.Equal(100m, row.DeliveredQuantity);
        Assert.Equal(0m, row.RejectionRatePercent);
        Assert.Equal(0, row.ProblemReceiptCount);
    }

    /// <summary>
    /// Pencere dışındaki teslimat sayılmamalı; iki yıl önceki bir sorun
    /// bugünkü tedarikçi kalitesini temsil etmez.
    /// </summary>
    [Fact]
    public async Task ReceiptsOutsideWindow_AreExcluded()
    {
        var context = await CreateContextAsync();

        await AddReceiptAsync(context, 50m, 50m, 0m, 0m);
        await AddReceiptAsync(
            context, 100m, 0m, 100m, 0m,
            receiptDate: DateTime.UtcNow.Date.AddMonths(-30));

        var report = await GetReportAsync(context);
        var row = Assert.Single(report.Rows);

        Assert.Equal(50m, row.DeliveredQuantity);
        Assert.Equal(0m, row.RejectionRatePercent);
    }

    /// <summary>
    /// Sorunsuz tedarikçi de listede görünmeli — karne yalnızca kötüyü
    /// değil, karşılaştırmayı sağlar.
    /// </summary>
    [Fact]
    public async Task CleanSupplier_AppearsWithZeroRate()
    {
        var context = await CreateContextAsync();

        await AddReceiptAsync(context, 200m, 200m, 0m, 0m);

        var report = await GetReportAsync(context);
        var row = Assert.Single(report.Rows);

        Assert.Equal(0m, row.RejectionRatePercent);
        Assert.Null(row.LastProblemDate);
        Assert.Equal(0, report.ProblemSupplierCount);
    }

    /// <summary>
    /// Uç, yetkili kullanıcıya karneyi döndürmeli.
    /// </summary>
    [Fact]
    public async Task Endpoint_ReturnsReport()
    {
        var context = await CreateContextAsync();
        await AddReceiptAsync(context, 100m, 80m, 20m, 0m);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var report = await client.GetFromJsonAsync<JsonElement>(
            $"/api/purchasing/supplier-quality?companyId={context.CompanyId}&months=12");

        var row = report.GetProperty("rows").EnumerateArray()
            .Single(x => x.GetProperty("supplierCurrentAccountId").GetGuid()
                == context.SupplierId);

        Assert.Equal(20m, row.GetProperty("rejectionRatePercent").GetDecimal());
        Assert.Equal(1, report.GetProperty("problemSupplierCount").GetInt32());
    }
}
