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
/// İSTENEN MARKANIN ZİNCİRDE TAŞINMASI: talep → RFQ → sipariş.
///
/// İki marka birbirinden ayrıdır ve karıştırılmaz:
///   RequestedBrand — talep edenin İSTEDİĞİ
///   Brand          — tedarikçinin VERDİĞİ (teklifte)
/// Siparişte ikisi yan yana durur; tek alanda birleştirilseydi
/// "istenen mi geldi, muadil mi" sorusu bir daha cevaplanamazdı.
///
/// Test gerçek akışı sürüyor (talep → onay → RFQ → teklif → sipariş),
/// varlıkları elle kurmuyor: kopyalamanın yapıldığı kod yollarının
/// ta kendisi sınanıyor.
/// </summary>
[Collection("Integration")]
public sealed class PurchaseBrandChainTests(DatabaseFixture fixture)
{
    private sealed record Context(Guid CompanyId, Guid ProjectId, Guid SupplierId);

    private async Task<Context> CreateContextAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var supplier = new CurrentAccount
        {
            CompanyId = project.CompanyId,
            Code = $"TED-{suffix}",
            Title = $"Test Tedarikçi {suffix}",
            Roles = CurrentAccountRoles.Supplier,
            Status = CurrentAccountStatus.Approved
        };

        db.CurrentAccounts.Add(supplier);
        await db.SaveChangesAsync();

        return new Context(project.CompanyId, project.Id, supplier.Id);
    }


    /// <summary>
    /// Yanıtı doğrular; başarısızsa GÖVDEYİ de mesaja koyar. Çıplak
    /// "Expected OK, actual InternalServerError" hata ayıklamada
    /// hiçbir şey söylemiyor.
    /// </summary>
    private static async Task AssertOkAsync(HttpResponseMessage response, string step)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync();

        Assert.Fail($"{step}: {(int)response.StatusCode} {response.StatusCode}. Gövde: {body}");
    }


    /// <summary>
    /// Teklifi DOĞRUDAN veritabanına kurar.
    ///
    /// NEDEN UÇ ÜZERİNDEN DEĞİL: teklif kaydetme ucu
    /// (POST rfq/{id}/suppliers/{id}/quotation) bu akışta 500 veriyor
    /// — DbUpdateConcurrencyException, "beklenen 1 satır, etkilenen 0".
    /// Bu MARKA PAKETİNDEN ÖNCE de vardı: uç hiç test edilmemiş ve
    /// marka alanları teklif kaydetme kodunu hiç tutmuyor. Ayrı bir
    /// kusur olarak raporlandı; burada körlemesine düzeltilmiyor.
    ///
    /// Bu testin doğrulaması sipariş tarafındaki KOPYALAMA olduğu için
    /// teklif veriyle kuruluyor ve zincirin geri kalanı gerçek
    /// uçlardan yürüyor.
    /// </summary>
    private async Task SeedQuotationAsync(
        Guid rfqSupplierId, Guid rfqItemId, string suppliedBrand)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var quotation = new EnderunAI.Api.Models.Rfq.RfqSupplierQuotation
        {
            RfqSupplierId = rfqSupplierId,
            SupplierQuotationNumber = "TKF-SEED",
            QuotationDate = DateTime.UtcNow.Date,
            Currency = "TRY",
            ExchangeRate = 1m,
            DeliveryDays = 5,
            Subtotal = 400m,
            DiscountTotal = 0m,
            GrandTotal = 400m,
            Items =
            {
                new EnderunAI.Api.Models.Rfq.RfqSupplierQuotationItem
                {
                    RfqItemId = rfqItemId,
                    Quantity = 4m,
                    UnitPrice = 100m,
                    DiscountRate = 0m,
                    NetUnitPrice = 100m,
                    TotalPrice = 400m,
                    Brand = suppliedBrand,
                    DeliveryDays = 5
                }
            }
        };

        db.Set<EnderunAI.Api.Models.Rfq.RfqSupplierQuotation>().Add(quotation);
        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> ClientAsync() =>
        await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    /// <summary>
    /// Talebi oluşturur, onaylar ve RFQ'ya çevirir; RFQ kimliğini ve
    /// tedarikçi satırını döner.
    /// </summary>
    private async Task<(Guid RfqId, Guid RfqSupplierId, Guid RfqItemId)> BuildRfqAsync(
        HttpClient client,
        Context context,
        string? requestedBrand,
        bool brandIrrelevant)
    {
        var created = await (await client.PostAsJsonAsync(
            "/api/purchase-requests",
            new
            {
                companyId = context.CompanyId,
                projectId = context.ProjectId,
                requestDate = DateTime.UtcNow.Date,
                neededByDate = (DateTime?)null,
                requestedByName = "Şantiye Şefi",
                description = "Zincir denemesi",
                priority = 1,
                items = new[]
                {
                    new
                    {
                        materialDescription = "Kontaktör",
                        quantity = 4m,
                        unit = "adet",
                        requestedDeliveryDate = (DateTime?)null,
                        notes = (string?)null,
                        requestedBrand,
                        brandIrrelevant
                    }
                }
            })).Content.ReadFromJsonAsync<JsonElement>();

        var requestId = created.GetProperty("id").GetGuid();

        await AssertOkAsync(
            await client.PostAsJsonAsync(
                $"/api/purchase-requests/{requestId}/submit", new { }),
            "talep gönderme");

        await AssertOkAsync(
            await client.PostAsJsonAsync(
                $"/api/purchase-requests/{requestId}/approve", new { }),
            "talep onayı");

        var rfqResponse = await client.PostAsJsonAsync(
            $"/api/rfq/create-from-purchase-request/{requestId}",
            new
            {
                title = "Kontaktör teklifi",
                responseDeadline = DateTime.UtcNow.Date.AddDays(7),
                currency = "TRY",
                description = (string?)null,
                notes = (string?)null,
                supplierCurrentAccountIds = new[] { context.SupplierId }
            });

        await AssertOkAsync(rfqResponse, "RFQ oluşturma");

        var rfqId = (await rfqResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        // RFQ tedarikçiye GÖNDERİLMEDEN teklif kaydedilemez; taslak
        // bir talebe teklif girmek akışın dışında.
        await AssertOkAsync(
            await client.PostAsJsonAsync($"/api/rfq/{rfqId}/send", new { }),
            "RFQ gönderme");

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rfqSupplierId = await db.RfqSuppliers
            .Where(x => x.RfqId == rfqId)
            .Select(x => x.Id)
            .SingleAsync();

        var rfqItemId = await db.RfqItems
            .Where(x => x.RfqId == rfqId)
            .Select(x => x.Id)
            .SingleAsync();

        return (rfqId, rfqSupplierId, rfqItemId);
    }

    [Fact]
    public async Task IstenenMarka_TalepdenRfqyaTasinir()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var (rfqId, _, _) = await BuildRfqAsync(
            client, context, "Schneider", brandIrrelevant: false);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var item = await db.RfqItems.SingleAsync(x => x.RfqId == rfqId);

        Assert.Equal("Schneider", item.RequestedBrand);
        Assert.False(item.BrandIrrelevant);
    }

    /// <summary>
    /// Asıl güvence: siparişte İSTENEN ve VERİLEN marka ayrı alanlarda
    /// ve birbirine karışmıyor. Tedarikçi başka marka teklif etti;
    /// ikisi de kayıtta duruyor.
    /// </summary>
    [Fact]
    public async Task Siparişte_IstenenVeVerilenMarkaAyriDurur()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var (rfqId, rfqSupplierId, rfqItemId) = await BuildRfqAsync(
            client, context, "Schneider", brandIrrelevant: false);

        await SeedQuotationAsync(rfqSupplierId, rfqItemId, "ABB");

        await AssertOkAsync(
            await client.PostAsJsonAsync(
                $"/api/rfq/{rfqId}/award/{rfqSupplierId}", new { }),
            "RFQ sonuçlandırma");

        var orderResponse = await client.PostAsJsonAsync(
            $"/api/purchase-orders/create-from-rfq/{rfqId}", new { });

        await AssertOkAsync(orderResponse, "sipariş oluşturma");

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var orderItem = await db.PurchaseOrderItems
            .Include(x => x.PurchaseOrder)
            .SingleAsync(x => x.RfqItemId == rfqItemId);

        // İki marka, iki alan, iki farklı değer.
        Assert.Equal("Schneider", orderItem.RequestedBrand);
        Assert.Equal("ABB", orderItem.Brand);
        Assert.False(orderItem.BrandIrrelevant);
    }

    /// <summary>
    /// FARKETMEZ'de tedarikçi serbest: istenen marka boş kalır, verdiği
    /// marka kayda geçer ve bu bir uyumsuzluk değildir.
    /// </summary>
    [Fact]
    public async Task Farketmezde_TedarikciSerbesttir()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var (rfqId, rfqSupplierId, rfqItemId) = await BuildRfqAsync(
            client, context, requestedBrand: null, brandIrrelevant: true);

        await SeedQuotationAsync(rfqSupplierId, rfqItemId, "Herhangi");

        await AssertOkAsync(
            await client.PostAsJsonAsync(
                $"/api/rfq/{rfqId}/award/{rfqSupplierId}", new { }),
            "RFQ sonuçlandırma");

        await AssertOkAsync(
            await client.PostAsJsonAsync(
                $"/api/purchase-orders/create-from-rfq/{rfqId}", new { }),
            "sipariş oluşturma");

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var orderItem = await db.PurchaseOrderItems
            .SingleAsync(x => x.RfqItemId == rfqItemId);

        Assert.Null(orderItem.RequestedBrand);
        Assert.True(orderItem.BrandIrrelevant);
        Assert.Equal("Herhangi", orderItem.Brand);
    }

    /// <summary>
    /// TERCİH: muadil kabul ama marka da yazılmış. Marka zincirde
    /// TAŞINIR — tedarikçi serbest kalsa bile "şu tercih edilir"
    /// bilgisi kaybolmaz.
    /// </summary>
    [Fact]
    public async Task TercihVeMuadil_MarkaZincirdeKorunur()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var (rfqId, _, _) = await BuildRfqAsync(
            client, context, "Siemens", brandIrrelevant: true);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var item = await db.RfqItems.SingleAsync(x => x.RfqId == rfqId);

        Assert.Equal("Siemens", item.RequestedBrand);
        Assert.True(item.BrandIrrelevant);
    }

    /// <summary>
    /// EKRANLARIN GÖRDÜĞÜ VERİ: marka veritabanında durmakla kalmaz,
    /// RFQ ve sipariş uçlarından da döner. Yalnız modeli sınasaydık
    /// alan dolu ama ekran boş olabilirdi — "uç var, ekran yok"un
    /// tersi: veri var, uç göstermiyor.
    /// </summary>
    [Fact]
    public async Task Uclar_IstenenMarkayiDondurur()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var (rfqId, rfqSupplierId, rfqItemId) = await BuildRfqAsync(
            client, context, "Schneider", brandIrrelevant: false);

        var rfqDetail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/rfq/{rfqId}");

        var rfqItem = rfqDetail.GetProperty("items").EnumerateArray().Single();

        Assert.Equal("Schneider", rfqItem.GetProperty("requestedBrand").GetString());
        Assert.False(rfqItem.GetProperty("brandIrrelevant").GetBoolean());

        await SeedQuotationAsync(rfqSupplierId, rfqItemId, "ABB");

        await AssertOkAsync(
            await client.PostAsJsonAsync(
                $"/api/rfq/{rfqId}/award/{rfqSupplierId}", new { }),
            "RFQ sonuçlandırma");

        var orderResponse = await client.PostAsJsonAsync(
            $"/api/purchase-orders/create-from-rfq/{rfqId}", new { });

        await AssertOkAsync(orderResponse, "sipariş oluşturma");

        var orderId = (await orderResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var orderDetail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/purchase-orders/{orderId}");

        var orderItem = orderDetail.GetProperty("items").EnumerateArray().Single();

        // İKİ MARKA AYNI ANDA UÇTAN DÖNER — ekran ikisini yan yana
        // gösterebilsin diye.
        Assert.Equal("Schneider", orderItem.GetProperty("requestedBrand").GetString());
        Assert.Equal("ABB", orderItem.GetProperty("brand").GetString());
    }

    /// <summary>
    /// Teklif karşılaştırma ucu da istenen markayı taşır: ekranın
    /// sorduğu asıl soru "teklif edilen marka istenenle uyuyor mu".
    /// </summary>
    [Fact]
    public async Task Karsilastirma_IstenenMarkayiTasir()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var (rfqId, rfqSupplierId, rfqItemId) = await BuildRfqAsync(
            client, context, "Schneider", brandIrrelevant: false);

        await SeedQuotationAsync(rfqSupplierId, rfqItemId, "ABB");

        var comparison = await client.GetFromJsonAsync<JsonElement>(
            $"/api/rfq/{rfqId}/comparison");

        var item = comparison
            .GetProperty("suppliers").EnumerateArray().First()
            .GetProperty("items").EnumerateArray().Single();

        Assert.Equal("Schneider", item.GetProperty("requestedBrand").GetString());
        Assert.Equal("ABB", item.GetProperty("brand").GetString());
        Assert.False(item.GetProperty("brandIrrelevant").GetBoolean());
    }
}
