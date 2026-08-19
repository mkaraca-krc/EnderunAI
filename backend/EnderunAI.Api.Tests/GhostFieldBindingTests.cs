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
/// Block 5 — okunup hiç yazılmayan alanların bağlanması.
///
/// A4 · Bakır katsayısı: bakır maruziyeti raporu YALNIZCA bu alandan
///      besleniyordu ama hiçbir uçtan girilemiyordu; emtia modülünün
///      proje ayağı her zaman boştu.
/// A5 · Kurumlar vergisi oranı: hesap sessizce koda gömülü %25'e
///      düşüyordu; alan ayarlanabilir görünüp ayarlanamıyordu.
/// A7 · Belge doğrulama: "aslı görüldü" işareti okunuyor ve
///      gösteriliyordu ama koyacak bir uç yoktu.
/// A9 · Gerçekleşen tarihler: hiç yazılmıyordu.
/// </summary>
[Collection("Integration")]
public sealed class GhostFieldBindingTests(DatabaseFixture fixture)
{
    private Task<HttpClient> ClientAsync() =>
        AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    // ---------------- A5 · Kurumlar vergisi oranı ----------------

    /// <summary>
    /// Oran yıl bazlı yazılıp geri okunuyor. Aynı yıl için ikinci
    /// kayıt açılmaz; mevcut oran güncellenir.
    /// </summary>
    [Fact]
    public async Task CorporateTaxRate_IsStoredPerYearAndUpdatedInPlace()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        Guid companyId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);
            companyId = company.Id;
        }

        var client = await ClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            "/api/kurumlar-vergisi-oranlari",
            new { companyId, year = 2026, rate = 25m, note = "7524 sayılı kanun" }))
            .StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            "/api/kurumlar-vergisi-oranlari",
            new { companyId, year = 2026, rate = 30m, note = "güncellendi" }))
            .StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            "/api/kurumlar-vergisi-oranlari",
            new { companyId, year = 2027, rate = 27m, note = (string?)null }))
            .StatusCode);

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rows = await verifyDb.CompanyCorporateTaxRates.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Year)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal(30m, rows[0].Rate);
        Assert.Equal(27m, rows[1].Rate);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task CorporateTaxRate_RejectsImpossibleRates(decimal rate)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        Guid companyId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);
            companyId = company.Id;
        }

        var client = await ClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/kurumlar-vergisi-oranlari",
            new { companyId, year = 2026, rate, note = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Oran tanımlı değilse vergi tahmini ÜRETİLMEZ ve bu açıkça
    /// söylenir. Eskiden sessizce %25 varsayılıp doğruymuş gibi rakam
    /// gösteriliyordu.
    /// </summary>
    [Fact]
    public async Task TaxOverview_WithoutRate_ProducesNoEstimateAndSaysSo()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        Guid companyId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);
            companyId = company.Id;
        }

        var client = await ClientAsync();

        var response = await client.GetAsync(
            $"/api/tax/overview?companyId={companyId}&year=2026");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        var payload = JsonDocument.Parse(raw).RootElement;

        Assert.Equal(
            JsonValueKind.Null,
            payload.GetProperty("corporateTaxRate").ValueKind);

        Assert.Equal(0m, payload.GetProperty("estimatedAnnualCorporateTax")
            .GetDecimal());

        Assert.Contains("tanımlanmadı", raw);
    }

    /// <summary>Oran girilince tahmin yeniden üretiliyor.</summary>
    [Fact]
    public async Task TaxOverview_WithRate_UsesIt()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        Guid companyId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);
            companyId = company.Id;
        }

        var client = await ClientAsync();

        await client.PutAsJsonAsync(
            "/api/kurumlar-vergisi-oranlari",
            new { companyId, year = 2026, rate = 23m, note = (string?)null });

        var raw = await (await client.GetAsync(
            $"/api/tax/overview?companyId={companyId}&year=2026")).Content
            .ReadAsStringAsync();

        var payload = JsonDocument.Parse(raw).RootElement;

        Assert.Equal(23m, payload.GetProperty("corporateTaxRate").GetDecimal());
        Assert.DoesNotContain("tanımlanmadı", raw);
    }

    // ---------------- A4 · Bakır katsayısı ----------------

    /// <summary>
    /// Malzeme kartına girilen bakır katsayısı saklanıyor ve detayda
    /// geri okunuyor. Geri okunmasaydı düzenleme ekranı onu boş
    /// gönderip ilk kayıtta siler, alan yine hayalet kalırdı.
    /// </summary>
    [Fact]
    public async Task CopperCoefficient_RoundTripsOnInventoryItem()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        Guid companyId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);
            companyId = company.Id;
        }

        var client = await ClientAsync();

        // KART AÇMA ARTIK KATEGORİ GÜDÜMLÜ (S2): kod otomatik, ad
        // özelliklerden üretiliyor. Bakır katsayısı bundan bağımsız
        // bir alan ve hâlâ karta yazılıp okunabilmeli.
        var categories = await client.GetFromJsonAsync<JsonElement>(
            "/api/inventory/categories");

        var kablo = categories.EnumerateArray()
            .Single(x => x.GetProperty("code").GetString() == "KABLO");

        Guid OptionOf(string attributeCode, string value) =>
            kablo.GetProperty("attributes").EnumerateArray()
                .Single(x => x.GetProperty("code").GetString() == attributeCode)
                .GetProperty("options").EnumerateArray()
                .Single(x => x.GetProperty("value").GetString() == value)
                .GetProperty("id").GetGuid();

        var created = await client.PostAsJsonAsync("/api/inventory/items", new
        {
            companyId,
            categoryId = kablo.GetProperty("id").GetGuid(),
            unit = "metre",
            optionIds = new[]
            {
                OptionOf("TIP", "NYY"),
                OptionOf("KESIT", "3x2.5"),
                OptionOf("ILETKEN", "Bakır")
            },
            minimumStock = 0m,
            type = 0,
            copperKgPerUnit = 0.0675m
        });

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        using var scope2 = fixture.Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();

        var item = await db2.InventoryItems.AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId);

        Assert.Equal(0.0675m, item.CopperKgPerUnit);

        var detail = await (await client.GetAsync(
            $"/api/inventory/items/{item.Id}")).Content.ReadAsStringAsync();

        Assert.Contains("copperKgPerUnit", detail);
        Assert.Contains("0.0675", detail);
    }

    // ---------------- A7 · Belge doğrulama ----------------

    /// <summary>
    /// "Aslı görüldü" işareti konabiliyor, kim ve ne zaman damgalanıyor.
    /// </summary>
    [Fact]
    public async Task DocumentVerification_StampsWhoAndWhen()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        Guid documentId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = await TestDataFactory.CreateProjectAsync(db, suffix);
            var personnel = await TestDataFactory.CreatePersonnelAsync(
                db, project.CompanyId, suffix);

            var document = new Models.HumanResources.PersonnelDocument
            {
                CompanyId = project.CompanyId,
                PersonnelId = personnel.Id,
                DocumentType = 0,
                DocumentName = "Kimlik Fotokopisi"
            };

            db.PersonnelDocuments.Add(document);
            await db.SaveChangesAsync();
            documentId = document.Id;
        }

        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/hr/personel-belgeleri/{documentId}/dogrula",
            new { isVerified = true, notes = "Aslı görüldü" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope3 = fixture.Factory.Services.CreateScope();
        var db3 = scope3.ServiceProvider.GetRequiredService<AppDbContext>();

        var stored = await db3.PersonnelDocuments.AsNoTracking()
            .SingleAsync(x => x.Id == documentId);

        Assert.True(stored.IsVerified);
        Assert.NotNull(stored.VerifiedAtUtc);
        Assert.NotNull(stored.VerifiedByUserId);
    }

    /// <summary>
    /// Doğrulama geri alınabiliyor ve damga temizleniyor: "aslı
    /// görüldü" yanlışlıkla konduysa iz bırakmadan kalkmalı.
    /// </summary>
    [Fact]
    public async Task DocumentVerification_CanBeRevoked()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        Guid documentId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = await TestDataFactory.CreateProjectAsync(db, suffix);
            var personnel = await TestDataFactory.CreatePersonnelAsync(
                db, project.CompanyId, suffix);

            var document = new Models.HumanResources.PersonnelDocument
            {
                CompanyId = project.CompanyId,
                PersonnelId = personnel.Id,
                DocumentType = 0,
                DocumentName = "Adli Sicil",
                IsVerified = true,
                VerifiedAtUtc = DateTime.UtcNow,
                VerifiedByUserId = Guid.NewGuid()
            };

            db.PersonnelDocuments.Add(document);
            await db.SaveChangesAsync();
            documentId = document.Id;
        }

        var client = await ClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/hr/personel-belgeleri/{documentId}/dogrula",
            new { isVerified = false, notes = (string?)null })).StatusCode);

        using var scope4 = fixture.Factory.Services.CreateScope();
        var db4 = scope4.ServiceProvider.GetRequiredService<AppDbContext>();

        var stored = await db4.PersonnelDocuments.AsNoTracking()
            .SingleAsync(x => x.Id == documentId);

        Assert.False(stored.IsVerified);
        Assert.Null(stored.VerifiedAtUtc);
        Assert.Null(stored.VerifiedByUserId);
    }

    // ---------------- A9 · Gerçekleşen tarihler ----------------

    /// <summary>
    /// Proje tamamlandı işaretlendiğinde gerçekleşen bitiş
    /// damgalanıyor. Daha önce hiç yazılmadığı için gecikme hesabı
    /// gerçekleşeni hiç görmüyordu.
    /// </summary>
    [Fact]
    public async Task CompletingProject_StampsActualEndDate()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        Guid projectId;
        Guid? employerId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = await TestDataFactory.CreateProjectAsync(db, suffix);
            projectId = project.Id;
            employerId = project.EmployerCurrentAccountId;

            project.ActualStartDate = null;
            project.ActualEndDate = null;
            project.Status = ProjectStatus.Active;
            await db.SaveChangesAsync();
        }

        var client = await ClientAsync();

        var response = await client.PutAsJsonAsync($"/api/projects/{projectId}", new
        {
            name = $"Test Proje {suffix}",
            employerCurrentAccountId = employerId,
            currencyCode = "TRY",
            vatRate = 20m,
            increaseRate = 0m,
            cashRetentionRate = 0m,
            withholdingTaxRate = 0m,
            materialDeductionRate = 0m,
            status = (int)ProjectStatus.Completed
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope5 = fixture.Factory.Services.CreateScope();
        var db5 = scope5.ServiceProvider.GetRequiredService<AppDbContext>();

        var stored = await db5.Projects.AsNoTracking()
            .SingleAsync(x => x.Id == projectId);

        Assert.NotNull(stored.ActualEndDate);
    }

    /// <summary>
    /// Elle girilen gerçekleşen tarih otomatik damgayı ezer: saha
    /// gerçeği kullanıcıdadır, sistemin tahmininde değil.
    /// </summary>
    [Fact]
    public async Task ExplicitActualDates_WinOverAutomaticStamp()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        Guid projectId;
        Guid? employerId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = await TestDataFactory.CreateProjectAsync(db, suffix);
            projectId = project.Id;
            employerId = project.EmployerCurrentAccountId;
            project.Status = ProjectStatus.Active;
            await db.SaveChangesAsync();
        }

        var actualStart = new DateTime(2026, 2, 3, 0, 0, 0, DateTimeKind.Utc);
        var actualEnd = new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc);

        var client = await ClientAsync();

        var response = await client.PutAsJsonAsync($"/api/projects/{projectId}", new
        {
            name = $"Test Proje {suffix}",
            employerCurrentAccountId = employerId,
            currencyCode = "TRY",
            vatRate = 20m,
            increaseRate = 0m,
            cashRetentionRate = 0m,
            withholdingTaxRate = 0m,
            materialDeductionRate = 0m,
            status = (int)ProjectStatus.Completed,
            actualStartDate = actualStart,
            actualEndDate = actualEnd
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope6 = fixture.Factory.Services.CreateScope();
        var db6 = scope6.ServiceProvider.GetRequiredService<AppDbContext>();

        var stored = await db6.Projects.AsNoTracking()
            .SingleAsync(x => x.Id == projectId);

        Assert.Equal(actualStart.Date, stored.ActualStartDate!.Value.Date);
        Assert.Equal(actualEnd.Date, stored.ActualEndDate!.Value.Date);
    }
}
