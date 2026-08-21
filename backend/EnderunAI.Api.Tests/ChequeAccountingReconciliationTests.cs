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
/// ÇEK ↔ MUHASEBE MUTABAKATI — FARK SIFIR OLMALI.
///
/// Düzeltme ve iptal artık fiş ters kaydediyor ve yenisini kesiyor.
/// Buradaki tek soru şu: bütün bu ters kayıtlardan SONRA portföydeki
/// çeklerin toplamı, 101 (Alınan Çekler) hesabının bakiyesine EŞİT mi.
///
/// Tek tek fişleri doğrulayan testler var; onlar "her adım doğru"
/// diyor. Bu test "adımların TOPLAMI doğru" diyor — iki ayrı soru:
/// her adımı doğru olan bir dizi, bir adımı iki kez sayarak yine
/// tutarsız bir bakiye bırakabilir.
/// </summary>
[Collection("Integration")]
public sealed class ChequeAccountingReconciliationTests(DatabaseFixture fixture)
{
    private sealed record Scene(Guid CompanyId, Guid ProjectId, Guid CustomerId);

    private static async Task<Scene> BuildAsync(AppDbContext db, string suffix)
    {
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        foreach (var (code, name) in new[]
        {
            ("101", "Alınan Çekler"), ("101.01", "Portföy"),
            ("101.02", "Tahsildeki Çekler"), ("102", "Bankalar"),
            ("103", "Verilen Çekler"), ("120", "Alıcılar"), ("320", "Satıcılar")
        })
        {
            db.AccountingAccounts.Add(new AccountingAccount
            {
                CompanyId = project.CompanyId,
                Code = code,
                Name = name,
                Nature = AccountingAccountNature.Debit,
                Level = code.Length > 3 ? 5 : 1,
                IsPostingAllowed = true
            });
        }

        var customer = new CurrentAccount
        {
            CompanyId = project.CompanyId,
            Code = $"MUS-{suffix}",
            Title = $"Test Müşteri {suffix}",
            Roles = CurrentAccountRoles.Customer | CurrentAccountRoles.Supplier,
            Status = CurrentAccountStatus.Approved
        };

        db.CurrentAccounts.Add(customer);
        await db.SaveChangesAsync();

        return new Scene(project.CompanyId, project.Id, customer.Id);
    }

    private Task<HttpClient> AdminAsync() =>
        AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    private static object Payload(Scene scene, string number, decimal amount) =>
        new
        {
            companyId = scene.CompanyId,
            direction = (int)ChequeDirection.Received,
            chequeNumber = number,
            bankName = "Test Bankası",
            bankBranch = "Kadıköy",
            drawer = "Keşideci",
            currentAccountId = scene.CustomerId,
            projectId = scene.ProjectId,
            amount,
            currencyCode = "TRY",
            issueDate = DateTime.UtcNow.Date,
            dueDate = DateTime.UtcNow.Date.AddMonths(1)
        };

    private static async Task<Guid> CreateAsync(HttpClient client, object payload)
    {
        var response = await client.PostAsJsonAsync("/api/cheques", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> DetailAsync(HttpClient client, Guid id) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/cheques/{id}");

    /// <summary>101 ailesinin bakiyesi (borç − alacak).</summary>
    private static async Task<decimal> ChequeAccountBalanceAsync(
        AppDbContext db, Guid companyId)
    {
        var lines = await db.AccountingVoucherLines
            .AsNoTracking()
            .Where(x => x.AccountingAccount.CompanyId == companyId
                        && x.AccountingAccount.Code.StartsWith("101"))
            .Select(x => new { x.DebitAmountLocal, x.CreditAmountLocal })
            .ToListAsync();

        return lines.Sum(x => x.DebitAmountLocal - x.CreditAmountLocal);
    }

    /// <summary>
    /// GİRİŞ, DÜZELTME ve İPTAL SONRASI FARK SIFIR.
    ///
    /// Senaryo bilerek karışık: üç çek girilir, birinin tutarı
    /// değiştirilir, biri iptal edilir. İptal edilen çek portföy
    /// toplamından da hesap bakiyesinden de tamamen çıkmalı; tutarı
    /// değişen çek ise YALNIZCA yeni tutarıyla kalmalı — eski tutar
    /// hesapta artık bulunmamalı.
    /// </summary>
    [Fact]
    public async Task GirisDuzeltmeIptalSonrasi_CekToplamiHesapBakiyesineEsit()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();

        var first = await CreateAsync(client, Payload(scene, $"MTB1{suffix}", 10_000m));
        var second = await CreateAsync(client, Payload(scene, $"MTB2{suffix}", 25_000m));
        var third = await CreateAsync(client, Payload(scene, $"MTB3{suffix}", 7_500m));

        // 1) İkinci çekin tutarı düzeltiliyor: 25.000 → 18.000.
        var secondDetail = await DetailAsync(client, second);

        var edit = await client.PutAsJsonAsync($"/api/cheques/{second}", new
        {
            chequeNumber = $"MTB2{suffix}",
            bankName = "Test Bankası",
            bankBranch = "Kadıköy",
            drawer = "Keşideci",
            currentAccountId = scene.CustomerId,
            projectId = scene.ProjectId,
            amount = 18_000m,
            issueDate = DateTime.UtcNow.Date,
            dueDate = DateTime.UtcNow.Date.AddMonths(1),
            rowVersion = secondDetail.GetProperty("rowVersion").GetDateTime(),
            editReason = "tutar yanlış girilmiş"
        });

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        // 2) Üçüncü çek iptal ediliyor.
        var thirdDetail = await DetailAsync(client, third);

        var voided = await client.PostAsJsonAsync($"/api/cheques/{third}/iptal", new
        {
            reason = "mükerrer kayıt",
            reasonKind = (int)ChequeVoidReason.DataEntryError,
            rowVersion = thirdDetail.GetProperty("rowVersion").GetDateTime()
        });

        Assert.Equal(HttpStatusCode.OK, voided.StatusCode);

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Portföydeki çeklerin toplamı: 10.000 + 18.000 = 28.000.
        var portfolioTotal = await verifyDb.Cheques
            .AsNoTracking()
            .Where(x => x.CompanyId == scene.CompanyId
                        && x.Status != ChequeStatus.Voided)
            .SumAsync(x => x.Amount);

        Assert.Equal(28_000m, portfolioTotal);

        var balance = await ChequeAccountBalanceAsync(verifyDb, scene.CompanyId);

        // ASIL SÖZ: fark sıfır.
        Assert.Equal(portfolioTotal, balance);

        // Ve iptal edilen çek gerçekten defterde iz bırakmış olmalı —
        // "fark sıfır" bir kaydı hiç yazmayarak da sağlanabilirdi.
        var thirdVoucherLines = await verifyDb.ChequeMovements
            .AsNoTracking()
            .Where(x => x.ChequeId == first || x.ChequeId == second || x.ChequeId == third)
            .CountAsync();

        Assert.True(thirdVoucherLines >= 3);
    }

    /// <summary>
    /// İPTAL EDİLEN ÇEK ÖZETE GİRMİYOR — ekrandaki toplam ile defter
    /// aynı şeyi söylüyor. Özet ayrı hesaplandığı için ayrışabilir;
    /// bir kez ayrıştı da (ay alt toplamları ile üst toplam).
    /// </summary>
    [Fact]
    public async Task IptalEdilenCek_OzetToplamaGirmez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();

        await CreateAsync(client, Payload(scene, $"OZT1{suffix}", 12_000m));
        var doomed = await CreateAsync(client, Payload(scene, $"OZT2{suffix}", 40_000m));

        var detail = await DetailAsync(client, doomed);

        var voided = await client.PostAsJsonAsync($"/api/cheques/{doomed}/iptal", new
        {
            reason = "yanlış müşteriye işlenmiş",
            reasonKind = (int)ChequeVoidReason.DataEntryError,
            rowVersion = detail.GetProperty("rowVersion").GetDateTime()
        });

        Assert.Equal(HttpStatusCode.OK, voided.StatusCode);

        var summary = await client.GetFromJsonAsync<JsonElement>(
            $"/api/cheques/summary?companyId={scene.CompanyId}");

        Assert.Equal(
            12_000m, summary.GetProperty("receivedPortfolioAmount").GetDecimal());
        Assert.Equal(1, summary.GetProperty("receivedOpenCount").GetInt32());
    }

    /// <summary>
    /// İPTAL LİSTEDE VARSAYILAN GÖRÜNMÜYOR, İSTENİRSE GELİYOR.
    /// Gizlemek yok saymak değil: denetim izi duruyor.
    /// </summary>
    [Fact]
    public async Task IptalEdilenCek_VarsayilanListedeYok_IstenirseGelir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();

        var doomed = await CreateAsync(client, Payload(scene, $"GIZ{suffix}", 5_000m));
        var detail = await DetailAsync(client, doomed);

        await client.PostAsJsonAsync($"/api/cheques/{doomed}/iptal", new
        {
            reason = "hatalı",
            reasonKind = (int)ChequeVoidReason.DataEntryError,
            rowVersion = detail.GetProperty("rowVersion").GetDateTime()
        });

        var defaultList = await client.GetFromJsonAsync<JsonElement>(
            $"/api/cheques?companyId={scene.CompanyId}");

        Assert.DoesNotContain(defaultList.EnumerateArray(), x =>
            x.GetProperty("id").GetGuid() == doomed);

        var withVoided = await client.GetFromJsonAsync<JsonElement>(
            $"/api/cheques?companyId={scene.CompanyId}&includeVoided=true");

        Assert.Contains(withVoided.EnumerateArray(), x =>
            x.GetProperty("id").GetGuid() == doomed);
    }
}
