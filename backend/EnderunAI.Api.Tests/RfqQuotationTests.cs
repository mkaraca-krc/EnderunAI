using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Rfq;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// TEKLİF KAYDETME — satın almanın kritik yolu.
///
/// Bu uç hiç test edilmemişti ve canlıda 500 dönüyordu: yeni teklif,
/// izlenen tedarikçinin Quotations koleksiyonuna ekleniyordu. BaseEntity
/// kurulumda Id'yi Guid.NewGuid() ile doldurduğu için EF anahtarı dolu
/// gelen bu varlığı VAR OLAN satır sanıp Added yerine Modified işaretledi;
/// olmayan satıra UPDATE atınca "beklenen 1 satır, etkilenen 0" hatası
/// verdi. Düzeltme: teklif DbSet üzerinden ekleniyor.
///
/// Testler ucun kendisini sürüyor — servis içi kısayol yok, çünkü
/// kırılan şey tam olarak uçtan geçen yoldu.
/// </summary>
[Collection("Integration")]
public sealed class RfqQuotationTests(DatabaseFixture fixture)
{
    private sealed record Chain(Guid RfqId, Guid RfqSupplierId, Guid RfqItemId);

    private async Task<HttpClient> ClientAsync() =>
        await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    private static async Task AssertOkAsync(HttpResponseMessage response, string step)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync();

        Assert.Fail($"{step}: {(int)response.StatusCode} {response.StatusCode}. Gövde: {body}");
    }

    /// <summary>Talepten RFQ'ya kadar gerçek akışı kurar.</summary>
    private async Task<Chain> BuildSentRfqAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Guid companyId, projectId, supplierId;

        using (var seedScope = fixture.Factory.Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = await TestDataFactory.CreateProjectAsync(seedDb, suffix);

            companyId = project.CompanyId;
            projectId = project.Id;

            var supplier = new CurrentAccount
            {
                CompanyId = companyId,
                Code = $"TED-{suffix}",
                Title = $"Test Tedarikçi {suffix}",
                Roles = CurrentAccountRoles.Supplier,
                Status = CurrentAccountStatus.Approved
            };

            seedDb.CurrentAccounts.Add(supplier);
            await seedDb.SaveChangesAsync();
            supplierId = supplier.Id;
        }

        var created = await (await client.PostAsJsonAsync("/api/purchase-requests", new
        {
            companyId,
            projectId,
            requestDate = DateTime.UtcNow.Date,
            neededByDate = (DateTime?)null,
            requestedByName = "Şantiye Şefi",
            description = "Teklif akışı",
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
                    requestedBrand = "Schneider",
                    brandIrrelevant = false
                }
            }
        })).Content.ReadFromJsonAsync<JsonElement>();

        var requestId = created.GetProperty("id").GetGuid();

        await AssertOkAsync(
            await client.PostAsJsonAsync($"/api/purchase-requests/{requestId}/submit", new { }),
            "talep gönderme");

        await AssertOkAsync(
            await client.PostAsJsonAsync($"/api/purchase-requests/{requestId}/approve", new { }),
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
                supplierCurrentAccountIds = new[] { supplierId }
            });

        await AssertOkAsync(rfqResponse, "RFQ oluşturma");

        var rfqId = (await rfqResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await AssertOkAsync(
            await client.PostAsJsonAsync($"/api/rfq/{rfqId}/send", new { }),
            "RFQ gönderme");

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/rfq/{rfqId}");

        var rfqSupplierId = detail
            .GetProperty("suppliers").EnumerateArray().Single()
            .GetProperty("id").GetGuid();

        var rfqItemId = detail
            .GetProperty("items").EnumerateArray().Single()
            .GetProperty("id").GetGuid();

        return new Chain(rfqId, rfqSupplierId, rfqItemId);
    }

    private static object QuotationPayload(
        Guid rfqItemId,
        decimal unitPrice,
        string brand,
        string quotationNumber) =>
        new
        {
            supplierQuotationNumber = quotationNumber,
            quotationDate = DateTime.UtcNow.Date,
            validUntil = (DateTime?)null,
            currency = "TRY",
            exchangeRate = 1m,
            deliveryDays = 5,
            paymentTerm = "30 gün",
            notes = (string?)null,
            items = new[]
            {
                new
                {
                    rfqItemId,
                    quantity = 4m,
                    unitPrice,
                    discountRate = 0m,
                    brand,
                    model = "LC1D",
                    deliveryDays = 5,
                    notes = (string?)null
                }
            }
        };

    /// <summary>
    /// ASIL GÜVENCE: teklif kaydedilebiliyor. Kırıkken bu çağrı 500
    /// dönüyordu ve RFQ→sipariş yolu tamamen kapalıydı.
    /// </summary>
    [Fact]
    public async Task Teklif_Kaydedilir()
    {
        var client = await ClientAsync();
        var chain = await BuildSentRfqAsync(client);

        await AssertOkAsync(
            await client.PostAsJsonAsync(
                $"/api/rfq/{chain.RfqId}/suppliers/{chain.RfqSupplierId}/quotation",
                QuotationPayload(chain.RfqItemId, 100m, "ABB", "TKF-1")),
            "teklif kaydetme");

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var quotation = await db.RfqSupplierQuotations
            .Include(x => x.Items)
            .SingleAsync(x => x.RfqSupplierId == chain.RfqSupplierId);

        Assert.Equal("TKF-1", quotation.SupplierQuotationNumber);
        Assert.Equal(400m, quotation.GrandTotal);

        var item = Assert.Single(quotation.Items);
        Assert.Equal("ABB", item.Brand);
        Assert.Equal(100m, item.NetUnitPrice);
        Assert.Equal(400m, item.TotalPrice);

        // Teklif gelince tedarikçi ve RFQ durumu ilerler; ekran
        // karşılaştırmaya bu durumlara bakarak geçiyor.
        var supplier = await db.RfqSuppliers.SingleAsync(x => x.Id == chain.RfqSupplierId);
        Assert.Equal(RfqSupplierStatus.Responded, supplier.Status);
        Assert.NotNull(supplier.RespondedAtUtc);

        var rfq = await db.Rfqs.SingleAsync(x => x.Id == chain.RfqId);
        Assert.Equal(RfqStatus.ResponsesReceived, rfq.Status);
    }

    /// <summary>
    /// YENİDEN KAYDETME: tedarikçi teklifini düzeltirse eski teklif
    /// yumuşak silinir, yerine yenisi geçer. Eski teklif ORTADAN
    /// KALKMAZ — denetim izi olarak tabloda kalır ama listelerde ve
    /// karşılaştırmada görünmez; yoksa hangi fiyata karar verildiği
    /// sonradan doğrulanamazdı.
    /// </summary>
    [Fact]
    public async Task Teklif_YenidenKaydedilince_EskisiYerineGecer()
    {
        var client = await ClientAsync();
        var chain = await BuildSentRfqAsync(client);

        await AssertOkAsync(
            await client.PostAsJsonAsync(
                $"/api/rfq/{chain.RfqId}/suppliers/{chain.RfqSupplierId}/quotation",
                QuotationPayload(chain.RfqItemId, 100m, "ABB", "TKF-1")),
            "ilk teklif");

        await AssertOkAsync(
            await client.PostAsJsonAsync(
                $"/api/rfq/{chain.RfqId}/suppliers/{chain.RfqSupplierId}/quotation",
                QuotationPayload(chain.RfqItemId, 90m, "Siemens", "TKF-2")),
            "düzeltilmiş teklif");

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var active = await db.RfqSupplierQuotations
            .Include(x => x.Items)
            .Where(x => x.RfqSupplierId == chain.RfqSupplierId)
            .ToListAsync();

        var current = Assert.Single(active);
        Assert.Equal("TKF-2", current.SupplierQuotationNumber);
        Assert.Equal(360m, current.GrandTotal);
        Assert.Equal("Siemens", Assert.Single(current.Items).Brand);

        // Sorgu süzgeci yok sayılınca eski teklif hâlâ orada.
        var all = await db.RfqSupplierQuotations
            .IgnoreQueryFilters()
            .Where(x => x.RfqSupplierId == chain.RfqSupplierId)
            .ToListAsync();

        Assert.Equal(2, all.Count);
        Assert.Single(all.Where(x => x.IsDeleted));

        // Karşılaştırma ekranı da tek ve güncel teklifi görür.
        var comparison = await client.GetFromJsonAsync<JsonElement>(
            $"/api/rfq/{chain.RfqId}/comparison");

        var candidate = comparison.GetProperty("suppliers").EnumerateArray().Single();

        Assert.True(candidate.GetProperty("hasQuotation").GetBoolean());
        Assert.Equal(360m, candidate.GetProperty("grandTotal").GetDecimal());
    }

    /// <summary>
    /// EŞ ZAMANLI KAYIT: iki teklif aynı anda gelirse uç sunucu hatası
    /// vermemeli ve geriye TEK geçerli teklif kalmalı. Kırık hâlde bu
    /// yol zaten ilk kayıtta patlıyordu; burada ikinci kaydın da
    /// birinciyi düzgün devraldığı sınanıyor.
    /// </summary>
    [Fact]
    public async Task Teklif_EsZamanliKayitta_TekGecerliTeklifKalir()
    {
        var client = await ClientAsync();
        var chain = await BuildSentRfqAsync(client);

        var first = client.PostAsJsonAsync(
            $"/api/rfq/{chain.RfqId}/suppliers/{chain.RfqSupplierId}/quotation",
            QuotationPayload(chain.RfqItemId, 100m, "ABB", "TKF-A"));

        var second = client.PostAsJsonAsync(
            $"/api/rfq/{chain.RfqId}/suppliers/{chain.RfqSupplierId}/quotation",
            QuotationPayload(chain.RfqItemId, 90m, "Siemens", "TKF-B"));

        var responses = await Task.WhenAll(first, second);

        // Hiçbir koşulda 5xx olmamalı: eş zamanlılık kullanıcıya
        // "beklenmeyen hata" olarak yansımaz.
        foreach (var response in responses)
        {
            Assert.True(
                (int)response.StatusCode < 500,
                $"eş zamanlı kayıt sunucu hatası verdi: {(int)response.StatusCode}");
        }

        // En az biri kabul edilmiş olmalı.
        Assert.Contains(responses, x => x.StatusCode == HttpStatusCode.OK);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var active = await db.RfqSupplierQuotations
            .Where(x => x.RfqSupplierId == chain.RfqSupplierId)
            .ToListAsync();

        Assert.Single(active);
    }

    /// <summary>
    /// Sonuçlanmış RFQ'ya teklif girilemez — kural düzeltmeden sonra da
    /// yerinde: 500 yerine anlaşılır bir hata dönmeli.
    /// </summary>
    [Fact]
    public async Task SonuclanmisRfqya_TeklifKaydedilemez()
    {
        var client = await ClientAsync();
        var chain = await BuildSentRfqAsync(client);

        await AssertOkAsync(
            await client.PostAsJsonAsync(
                $"/api/rfq/{chain.RfqId}/suppliers/{chain.RfqSupplierId}/quotation",
                QuotationPayload(chain.RfqItemId, 100m, "ABB", "TKF-1")),
            "teklif kaydetme");

        await AssertOkAsync(
            await client.PostAsJsonAsync(
                $"/api/rfq/{chain.RfqId}/award/{chain.RfqSupplierId}", new { }),
            "RFQ sonuçlandırma");

        var response = await client.PostAsJsonAsync(
            $"/api/rfq/{chain.RfqId}/suppliers/{chain.RfqSupplierId}/quotation",
            QuotationPayload(chain.RfqItemId, 80m, "ABB", "TKF-3"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
