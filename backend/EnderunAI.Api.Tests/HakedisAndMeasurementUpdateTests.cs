using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// TASLAK HAKEDİŞ VE METRAJ GÜNCELLEME — ikisi de test edilmemiş,
/// ikisi de RFQ teklif kaydetmedeki kalıbın aynısını taşıyordu:
///
///   1) Yeni alt satır, izlenen üst kaydın koleksiyonuna ekleniyordu.
///      BaseEntity kurulumda Id'yi doldurduğu için EF bunları VAR OLAN
///      satır sanıp Modified işaretliyor, olmayan satıra UPDATE atıp
///      "beklenen 1 satır, etkilenen 0" ile patlıyordu.
///   2) Güncelleme eski satırları YUMUŞAK siliyor (silinen satır tabloda
///      kalıyor) ama satır numarası benzersiz kısıtı koşulsuzdu; aynı
///      satır numarası yeniden yazılınca kısıt ihlal ediliyordu.
///
/// Bu yüzden testler İKİ KEZ güncelliyor: birinci güncelleme (1)'i,
/// aynı satırın ikinci kez güncellenmesi (2)'yi yakalar.
/// </summary>
[Collection("Integration")]
public sealed class HakedisAndMeasurementUpdateTests(DatabaseFixture fixture)
{
    private async Task<HttpClient> ClientAsync() =>
        await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    private static async Task AssertOkAsync(HttpResponseMessage response, string step)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync();

        Assert.Fail($"{step}: {(int)response.StatusCode} {response.StatusCode}. Gövde: {body}");
    }

    private async Task<(Guid CompanyId, Guid ProjectId)> CreateProjectAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(
            db, Guid.NewGuid().ToString("N")[..8]);

        return (project.CompanyId, project.Id);
    }

    // ------------------------------------------------------------------
    // HAKEDİŞ
    // ------------------------------------------------------------------

    private static object HakedisItem(
        string positionCode, decimal quantity, decimal unitPrice) => new
        {
            engineeringPositionId = (Guid?)null,
            positionCode,
            description = "Test imalat",
            unit = "m2",
            contractQuantity = quantity,
            currentQuantity = quantity,
            unitPrice,
            measurementReference = (string?)null,
            notes = (string?)null
        };

    private static object HakedisUpdatePayload(params object[] items) => new
    {
        periodStartDate = (DateOnly?)null,
        periodEndDate = (DateOnly?)null,
        progressPaymentDate = DateOnly.FromDateTime(DateTime.UtcNow),
        priceDifferenceAmount = 0m,
        vatRate = 20m,
        withholdingNumerator = 0,
        withholdingDenominator = 0,
        description = "Güncellendi",
        notes = (string?)null,
        items,
        deductions = Array.Empty<object>(),
        advanceMaterials = Array.Empty<object>(),
        advanceOffsets = Array.Empty<object>(),
        incomeTaxWithholdingRate = 0m,
        paymentPlans = Array.Empty<object>()
    };

    private async Task<Guid> CreateDraftHakedisAsync(HttpClient client)
    {
        var (companyId, projectId) = await CreateProjectAsync();

        var response = await client.PostAsJsonAsync("/api/progress-payments", new
        {
            companyId,
            projectId,
            projectMeasurementId = (Guid?)null,
            progressPaymentNumber = $"HK-{Guid.NewGuid():N}"[..12],
            periodNumber = 1,
            periodStartDate = (DateOnly?)null,
            periodEndDate = (DateOnly?)null,
            progressPaymentDate = DateOnly.FromDateTime(DateTime.UtcNow),
            priceDifferenceAmount = 0m,
            vatRate = 20m,
            withholdingNumerator = 0,
            withholdingDenominator = 0,
            description = "Güncelleme testi",
            notes = (string?)null,
            items = new[] { HakedisItem("POZ-1", 10m, 100m) },
            deductions = Array.Empty<object>(),
            incomeTaxWithholdingRate = 0m,
            advanceMaterials = Array.Empty<object>(),
            paymentPlans = Array.Empty<object>()
        });

        await AssertOkAsync(response, "hakediş oluşturma");

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();
    }

    /// <summary>
    /// KRİTİK YOL: taslak hakediş güncellenebilmeli. Kırıkken bu çağrı
    /// 500 dönüyordu — hakediş bir kez kurulduktan sonra düzeltilemezdi.
    /// </summary>
    [Fact]
    public async Task Hakedis_Guncellenir()
    {
        var client = await ClientAsync();
        var id = await CreateDraftHakedisAsync(client);

        await AssertOkAsync(
            await client.PutAsJsonAsync(
                $"/api/progress-payments/{id}",
                HakedisUpdatePayload(HakedisItem("POZ-1", 12m, 100m))),
            "hakediş güncelleme");

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var item = Assert.Single(await db.ProgressPaymentItems
            .Where(x => x.ProgressPaymentId == id)
            .ToListAsync());

        Assert.Equal(12m, item.CurrentQuantity);
        Assert.Equal(1_200m, item.CurrentAmount);
    }

    /// <summary>
    /// İKİNCİ GÜNCELLEME: aynı satır numarası yeniden yazılıyor. Kısmi
    /// olmayan benzersiz kısıtla bu çağrı, yumuşak silinen eski satır
    /// tabloda durduğu için kısıt ihlaliyle patlıyordu.
    /// </summary>
    [Fact]
    public async Task Hakedis_IkinciKezGuncellenir()
    {
        var client = await ClientAsync();
        var id = await CreateDraftHakedisAsync(client);

        await AssertOkAsync(
            await client.PutAsJsonAsync(
                $"/api/progress-payments/{id}",
                HakedisUpdatePayload(HakedisItem("POZ-1", 12m, 100m))),
            "birinci güncelleme");

        await AssertOkAsync(
            await client.PutAsJsonAsync(
                $"/api/progress-payments/{id}",
                HakedisUpdatePayload(
                    HakedisItem("POZ-1", 15m, 100m),
                    HakedisItem("POZ-2", 5m, 200m))),
            "ikinci güncelleme");

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var items = await db.ProgressPaymentItems
            .Where(x => x.ProgressPaymentId == id)
            .OrderBy(x => x.LineNumber)
            .ToListAsync();

        // Geçerli satırlar YALNIZ son güncellemenin satırları.
        Assert.Equal(2, items.Count);
        Assert.Equal(15m, items[0].CurrentQuantity);
        Assert.Equal("POZ-2", items[1].PositionCode);

        // Eski satırlar silinmedi, denetim izi olarak duruyor.
        var all = await db.ProgressPaymentItems
            .IgnoreQueryFilters()
            .Where(x => x.ProgressPaymentId == id)
            .ToListAsync();

        Assert.True(
            all.Count > items.Count,
            "yumuşak silinen eski satırlar kayıtta kalmalı");
    }

    // ------------------------------------------------------------------
    // METRAJ
    // ------------------------------------------------------------------

    private async Task<(Guid MeasurementId, Guid BoqItemId)> CreateDraftMeasurementAsync(
        HttpClient client)
    {
        var (companyId, projectId) = await CreateProjectAsync();

        var boqResponse = await client.PostAsJsonAsync("/api/project-boqs", new
        {
            companyId,
            projectId,
            boqNumber = $"KSF-{Guid.NewGuid():N}"[..12],
            name = "Test keşfi",
            revisionNumber = 1,
            currencyCode = "TRY",
            description = (string?)null,
            notes = (string?)null,
            items = new[]
            {
                new
                {
                    engineeringPositionId = (Guid?)null,
                    positionCode = "POZ-1",
                    description = "Test imalat",
                    unit = "m2",
                    contractQuantity = 100m,
                    unitPrice = 50m,
                    itemType = 0,
                    category = (string?)null,
                    notes = (string?)null
                }
            }
        });

        await AssertOkAsync(boqResponse, "keşif oluşturma");

        var boqId = (await boqResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var boqDetail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/project-boqs/{boqId}");

        var boqItemId = boqDetail
            .GetProperty("items").EnumerateArray().First()
            .GetProperty("id").GetGuid();

        var measurementResponse = await client.PostAsJsonAsync("/api/project-measurements", new
        {
            companyId,
            projectId,
            projectBoqId = boqId,
            measurementNumber = $"MTR-{Guid.NewGuid():N}"[..12],
            measurementDate = DateTime.UtcNow.Date,
            description = (string?)null,
            notes = (string?)null,
            items = new[]
            {
                new
                {
                    projectBoqItemId = boqItemId,
                    currentQuantity = 10m,
                    measurementReference = (string?)null,
                    location = (string?)null,
                    block = (string?)null,
                    floor = (string?)null,
                    room = (string?)null,
                    notes = (string?)null
                }
            }
        });

        await AssertOkAsync(measurementResponse, "metraj oluşturma");

        var measurementId = (await measurementResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        return (measurementId, boqItemId);
    }

    private static object MeasurementUpdatePayload(Guid boqItemId, decimal quantity) => new
    {
        measurementDate = DateTime.UtcNow.Date,
        description = "Güncellendi",
        notes = (string?)null,
        items = new[]
        {
            new
            {
                projectBoqItemId = boqItemId,
                currentQuantity = quantity,
                measurementReference = (string?)null,
                location = (string?)null,
                block = (string?)null,
                floor = (string?)null,
                room = (string?)null,
                notes = (string?)null
            }
        }
    };

    [Fact]
    public async Task Metraj_Guncellenir()
    {
        var client = await ClientAsync();
        var (measurementId, boqItemId) = await CreateDraftMeasurementAsync(client);

        await AssertOkAsync(
            await client.PutAsJsonAsync(
                $"/api/project-measurements/{measurementId}",
                MeasurementUpdatePayload(boqItemId, 20m)),
            "metraj güncelleme");

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var item = Assert.Single(await db.ProjectMeasurementItems
            .Where(x => x.ProjectMeasurementId == measurementId)
            .ToListAsync());

        Assert.Equal(20m, item.CurrentQuantity);
    }

    /// <summary>
    /// Metrajda benzersiz kısıt İKİ tane: (metraj, satır no) ve
    /// (metraj, keşif kalemi). İkinci güncelleme ikisini birden yeniden
    /// yazdığı için kısmi olmayan kısıtta iki kez patlardı.
    /// </summary>
    [Fact]
    public async Task Metraj_IkinciKezGuncellenir()
    {
        var client = await ClientAsync();
        var (measurementId, boqItemId) = await CreateDraftMeasurementAsync(client);

        await AssertOkAsync(
            await client.PutAsJsonAsync(
                $"/api/project-measurements/{measurementId}",
                MeasurementUpdatePayload(boqItemId, 20m)),
            "birinci güncelleme");

        await AssertOkAsync(
            await client.PutAsJsonAsync(
                $"/api/project-measurements/{measurementId}",
                MeasurementUpdatePayload(boqItemId, 30m)),
            "ikinci güncelleme");

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var item = Assert.Single(await db.ProjectMeasurementItems
            .Where(x => x.ProjectMeasurementId == measurementId)
            .ToListAsync());

        Assert.Equal(30m, item.CurrentQuantity);
    }
}
