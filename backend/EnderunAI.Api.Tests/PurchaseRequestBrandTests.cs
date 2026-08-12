using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Malzeme talebinde İSTENEN MARKA.
///
/// Marka bilinçli bir seçim olmalı: ya bir marka istenir ya da
/// "muadil kabul" işaretlenir. Boş bırakılabilseydi tedarikçi neyin
/// kabul edileceğini bilemez, teklifler karşılaştırılamaz ve yanlış
/// malzeme geldiğinde kimsenin dayanacağı bir kayıt olmazdı.
///
/// ÜÇ GEÇERLİ DURUM:
///   marka dolu + muadil false → ZORUNLU marka
///   marka dolu + muadil true  → TERCİH, muadil kabul
///   marka boş  + muadil true  → farketmez
/// Marka boş + muadil false GEÇERSİZ.
///
/// Talep edenin istediği marka, tedarikçinin teklif ettiği markadan
/// (<c>RfqSupplierQuotation.Brand</c>) ayrıdır ve onunla karıştırılmaz.
/// </summary>
[Collection("Integration")]
public sealed class PurchaseRequestBrandTests(DatabaseFixture fixture)
{
    private sealed record Context(Guid CompanyId, Guid ProjectId);

    private async Task<Context> CreateContextAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        return new Context(project.CompanyId, project.Id);
    }

    private async Task<HttpClient> ClientAsync() =>
        await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    private static object Payload(
        Context context,
        string? requestedBrand,
        bool brandIrrelevant) =>
        new
        {
            companyId = context.CompanyId,
            projectId = context.ProjectId,
            requestDate = DateTime.UtcNow.Date,
            neededByDate = (DateTime?)null,
            requestedByName = "Şantiye Şefi",
            description = "Marka denemesi",
            priority = 1,
            items = new[]
            {
                new
                {
                    materialDescription = "Sigorta",
                    quantity = 5m,
                    unit = "adet",
                    requestedDeliveryDate = (DateTime?)null,
                    notes = (string?)null,
                    requestedBrand,
                    brandIrrelevant
                }
            }
        };

    private static async Task<JsonElement> FirstItemAsync(
        HttpClient client, Guid requestId)
    {
        var detail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/purchase-requests/{requestId}");

        return detail.GetProperty("items").EnumerateArray().First();
    }

    [Fact]
    public async Task MarkaBosVeMuadilIsaretsiz_Reddedilir()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/purchase-requests",
            Payload(context, requestedBrand: null, brandIrrelevant: false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("marka", body.GetProperty("message").GetString()!.ToLowerInvariant());
    }

    /// <summary>Yalnızca boşluk yazmak marka girmek sayılmaz.</summary>
    [Fact]
    public async Task MarkaSadeceBosluk_Reddedilir()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/purchase-requests",
            Payload(context, requestedBrand: "   ", brandIrrelevant: false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ZorunluMarka_Kaydedilir()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/purchase-requests",
            Payload(context, "Siemens", brandIrrelevant: false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = await FirstItemAsync(client, created.GetProperty("id").GetGuid());

        Assert.Equal("Siemens", item.GetProperty("requestedBrand").GetString());
        Assert.False(item.GetProperty("brandIrrelevant").GetBoolean());
    }

    /// <summary>
    /// TERCİH: muadil kabul ediliyor ama bir marka da yazılmış. Bilgi
    /// ATILMAZ — "muadil olur ama Siemens iyi olur" gerçek bir taleptir
    /// ve tedarikçi bunu bilmeli.
    /// </summary>
    [Fact]
    public async Task TercihVeMuadil_MarkaKorunur()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/purchase-requests",
            Payload(context, "Siemens", brandIrrelevant: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = await FirstItemAsync(client, created.GetProperty("id").GetGuid());

        Assert.Equal("Siemens", item.GetProperty("requestedBrand").GetString());
        Assert.True(item.GetProperty("brandIrrelevant").GetBoolean());
    }

    [Fact]
    public async Task Farketmez_MarkasizKabulEdilir()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/purchase-requests",
            Payload(context, requestedBrand: null, brandIrrelevant: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = await FirstItemAsync(client, created.GetProperty("id").GetGuid());

        Assert.Equal(JsonValueKind.Null, item.GetProperty("requestedBrand").ValueKind);
        Assert.True(item.GetProperty("brandIrrelevant").GetBoolean());
    }

    /// <summary>
    /// Kural GÜNCELLEMEDE de işler: create ve update ayrı yollar ama
    /// aynı doğrulamayı çağırıyor. Yalnız create korunsaydı, geçerli
    /// bir talep güncellemeyle geçersiz hale getirilebilirdi.
    /// </summary>
    [Fact]
    public async Task Guncellemede_MarkaKuraliIsler()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var created = await (await client.PostAsJsonAsync(
            "/api/purchase-requests",
            Payload(context, "Siemens", brandIrrelevant: false)))
            .Content.ReadFromJsonAsync<JsonElement>();

        var id = created.GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/purchase-requests/{id}",
            new
            {
                requestDate = DateTime.UtcNow.Date,
                neededByDate = (DateTime?)null,
                requestedByName = "Şantiye Şefi",
                description = "Marka silindi",
                priority = 1,
                items = new[]
                {
                    new
                    {
                        materialDescription = "Sigorta",
                        quantity = 5m,
                        unit = "adet",
                        requestedDeliveryDate = (DateTime?)null,
                        notes = (string?)null,
                        requestedBrand = (string?)null,
                        brandIrrelevant = false
                    }
                }
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// MARKA ALANINI HİÇ GÖNDERMEYEN çağıran bozulmaz: sözleşmede
    /// varsayılan "muadil kabul". Varsayılan false olsaydı, alanı
    /// bilmeyen her eski çağıranın talebi bir anda geçersiz olurdu —
    /// aynı gerekçeyle mevcut veritabanı satırları da true.
    /// </summary>
    [Fact]
    public async Task MarkaAlaniHicGonderilmezse_TalepGecerlidir()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/purchase-requests",
            new
            {
                companyId = context.CompanyId,
                projectId = context.ProjectId,
                requestDate = DateTime.UtcNow.Date,
                neededByDate = (DateTime?)null,
                requestedByName = "Şantiye Şefi",
                description = "Eski çağıran",
                priority = 1,
                items = new[]
                {
                    new
                    {
                        materialDescription = "Kablo",
                        quantity = 3m,
                        unit = "metre",
                        requestedDeliveryDate = (DateTime?)null,
                        notes = (string?)null
                    }
                }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = await FirstItemAsync(client, created.GetProperty("id").GetGuid());

        Assert.True(item.GetProperty("brandIrrelevant").GetBoolean());
    }
}
