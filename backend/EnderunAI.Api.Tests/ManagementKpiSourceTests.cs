using System.Net;
using System.Net.Http.Headers;
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
/// YÖNETİM KPI'LARININ ANA KURALI: KPI kaynak servisi OKUR, yeniden
/// HESAPLAMAZ.
///
/// Bu testler kuralı birebir doğruluyor: KPI ucunun döndürdüğü sayı,
/// aynı anda kaynak ucun döndürdüğü sayıya EŞİT olmalı. Eşit değilse
/// bir yerde ikinci bir hesap var demektir ve iki sayı zamanla
/// ayrışır — yönetici iki ekranda iki farklı "açık sipariş" görür.
///
/// Kaynak uçlar ayrı ayrı çağrılıyor; KPI'nın onları çağırdığı
/// varsayılmıyor. Test, uygulamanın DIŞINDAN bakıyor.
/// </summary>
[Collection("Integration")]
public sealed class ManagementKpiSourceTests(DatabaseFixture fixture)
{

    /// <summary>
    /// Belirli bir rolle oturum açmış istemci. Yetki testleri için
    /// gerekli: varsayılan test kullanıcısı Admin ve her izne sahip.
    /// </summary>
    private async Task<HttpClient> CreateClientForRoleAsync(string roleName)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider
            .GetRequiredService<EnderunAI.Api.Security.PasswordService>();

        const string password = "KpiRole!2026Secure";
        var username = $"test-kpi-{Guid.NewGuid():N}"[..40];
        var hash = passwordService.Hash(password);

        var user = new AppUser
        {
            Username = username,
            FullName = $"Test {roleName}",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            IsActive = true,
            WorkHoursExempt = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var role = await db.Roles.SingleAsync(x => x.Name == roleName);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        db.UserDataScopes.Add(new UserDataScope
        {
            UserId = user.Id,
            ScopeType = DataScopeType.All
        });

        await db.SaveChangesAsync();

        var client = fixture.Factory.CreateClient();
        var token = await AuthHelper.LoginAsync(client, username, password);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private static async Task<Guid> EnsureCompanyAsync(DatabaseFixture fixture)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var company = await db.Companies.FirstOrDefaultAsync(x => x.IsActive);

        if (company is not null)
            return company.Id;

        company = new Company
        {
            Name = "KPI Test A.Ş.",
            Code = "KPITEST",
            IsActive = true
        };

        db.Companies.Add(company);
        await db.SaveChangesAsync();

        return company.Id;
    }

    private static JsonElement? FindKpi(JsonElement response, string key)
    {
        foreach (var kpi in response.GetProperty("kpis").EnumerateArray())
        {
            if (kpi.GetProperty("key").GetString() == key)
                return kpi;
        }

        return null;
    }

    [Fact]
    public async Task AcikSiparisKpisi_SatinAlmaDashboardIleAyniSayiyiVerir()
    {
        var companyId = await EnsureCompanyAsync(fixture);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var kpiResponse = await client.GetFromJsonAsync<JsonElement>(
            $"/api/yonetim/kpi?companyId={companyId}");

        var dashboard = await client.GetFromJsonAsync<JsonElement>(
            $"/api/procurement/dashboard?companyId={companyId}");

        var kpi = FindKpi(kpiResponse, "purchasing.open");
        Assert.NotNull(kpi);

        var expected = dashboard
            .GetProperty("purchaseOrders")
            .GetProperty("open")
            .GetInt32();

        Assert.Equal(expected, kpi!.Value.GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task CekKpisi_CekOzetiIleAyniTutariVerir()
    {
        var companyId = await EnsureCompanyAsync(fixture);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var kpiResponse = await client.GetFromJsonAsync<JsonElement>(
            $"/api/yonetim/kpi?companyId={companyId}");

        var summary = await client.GetFromJsonAsync<JsonElement>(
            $"/api/cheques/summary?companyId={companyId}");

        var kpi = FindKpi(kpiResponse, "cheque.open");
        Assert.NotNull(kpi);

        Assert.Equal(
            summary.GetProperty("issuedOpenAmount").GetDecimal(),
            kpi!.Value.GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task GiderKpisi_GiderRaporuIleAyniToplamiVerir()
    {
        var companyId = await EnsureCompanyAsync(fixture);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var now = DateTime.UtcNow;
        var start = new DateTime(now.Year, now.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);

        var kpiResponse = await client.GetFromJsonAsync<JsonElement>(
            $"/api/yonetim/kpi?companyId={companyId}&year={now.Year}&month={now.Month}");

        var report = await client.GetFromJsonAsync<JsonElement>(
            $"/api/expenses/rapor?companyId={companyId}" +
            $"&from={start:yyyy-MM-dd}&to={end:yyyy-MM-dd}");

        var kpi = FindKpi(kpiResponse, "expense.total");
        Assert.NotNull(kpi);

        Assert.Equal(
            report.GetProperty("total").GetDecimal(),
            kpi!.Value.GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task BordroKpisi_BordroOzetiIleAyniTutariVerir()
    {
        var companyId = await EnsureCompanyAsync(fixture);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var now = DateTime.UtcNow;

        var kpiResponse = await client.GetFromJsonAsync<JsonElement>(
            $"/api/yonetim/kpi?companyId={companyId}&year={now.Year}&month={now.Month}");

        var summary = await client.GetFromJsonAsync<JsonElement>(
            $"/api/hr/payroll/summary?companyId={companyId}" +
            $"&year={now.Year}&month={now.Month}");

        var kpi = FindKpi(kpiResponse, "payroll.cost");
        Assert.NotNull(kpi);

        Assert.Equal(
            summary.GetProperty("totalGrossSalary").GetDecimal(),
            kpi!.Value.GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task NakitKpisi_ProjeksiyonIleAyniKapanisBakiyesiniVerir()
    {
        var companyId = await EnsureCompanyAsync(fixture);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var kpiResponse = await client.GetFromJsonAsync<JsonElement>(
            $"/api/yonetim/kpi?companyId={companyId}");

        var projection = await client.GetFromJsonAsync<JsonElement>(
            $"/api/cash-flow/projeksiyon?companyId={companyId}&months=6");

        var kpi = FindKpi(kpiResponse, "cash.closing");
        Assert.NotNull(kpi);

        Assert.Equal(
            projection.GetProperty("closingBalance").GetDecimal(),
            kpi!.Value.GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task KarMarjiKpisi_KarlilikOzetindekiEnDusukMarjiVerir()
    {
        var companyId = await EnsureCompanyAsync(fixture);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var kpiResponse = await client.GetFromJsonAsync<JsonElement>(
            $"/api/yonetim/kpi?companyId={companyId}");

        var summary = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/profitability-summary?companyId={companyId}");

        var kpi = FindKpi(kpiResponse, "project.margin");
        Assert.NotNull(kpi);

        // Cirosu olmayan proje 0 marj döndürüyor ve "en kötü"
        // sıralamasına girerse gerçek sorunlu projeyi gizler; KPI
        // onları eliyor, test de aynı elemeyi uyguluyor.
        var margins = summary.EnumerateArray()
            .Where(x => x.GetProperty("revenue").GetDecimal() > 0m)
            .Select(x => x.GetProperty("profitMargin").GetDecimal())
            .ToList();

        var expected = margins.Count == 0 ? 0m : margins.Min();

        Assert.Equal(expected, kpi!.Value.GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task FinansalAracKpisi_AracOzetiIleAyniTutariVerir()
    {
        var companyId = await EnsureCompanyAsync(fixture);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var kpiResponse = await client.GetFromJsonAsync<JsonElement>(
            $"/api/yonetim/kpi?companyId={companyId}");

        var summary = await client.GetFromJsonAsync<JsonElement>(
            $"/api/finansal-araclar/ozet?companyId={companyId}");

        var kpi = FindKpi(kpiResponse, "instrument.outflow");
        Assert.NotNull(kpi);

        Assert.Equal(
            summary.GetProperty("totalCashOutflow").GetDecimal(),
            kpi!.Value.GetProperty("value").GetDecimal());
    }

    /// <summary>
    /// Barter NAKİT DEĞİLDİR: mal/hizmetle kapanır, kasaya para
    /// girmez. Nakit çıkış toplamına karışsaydı likidite olduğundan
    /// iyi ya da kötü görünürdü.
    /// </summary>
    [Fact]
    public async Task AracOzeti_BarterAlacaginiNakitToplamaKatmaz()
    {
        var companyId = await EnsureCompanyAsync(fixture);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var summary = await client.GetFromJsonAsync<JsonElement>(
            $"/api/finansal-araclar/ozet?companyId={companyId}");

        var installments = summary.GetProperty("loanInstallmentOutflow").GetDecimal();
        var statements = summary.GetProperty("cardStatementOutflow").GetDecimal();
        var total = summary.GetProperty("totalCashOutflow").GetDecimal();

        // Toplam yalnızca taksit + ekstredir; barter ayrı alanda durur.
        Assert.Equal(installments + statements, total);
    }

    /// <summary>
    /// Yetkisi olmayan KPI yanıta HİÇ girmemeli — "unavailable"
    /// listesine bile. Kilitli bir kart, o göstergenin var olduğunu
    /// ve mertebesini ele verirdi.
    /// </summary>
    [Fact]
    public async Task YetkisizKullanici_KpiyiHicGormez()
    {
        var companyId = await EnsureCompanyAsync(fixture);

        // Şantiye Şefi'nde ne cashflow.view ne salary.view var.
        var client = await CreateClientForRoleAsync("Şantiye Şefi");

        var response = await client.GetAsync(
            $"/api/yonetim/kpi?companyId={companyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Null(FindKpi(payload, "cash.closing"));
        Assert.Null(FindKpi(payload, "payroll.cost"));

        foreach (var item in payload.GetProperty("unavailable").EnumerateArray())
        {
            var key = item.GetProperty("key").GetString();
            Assert.NotEqual("cash.closing", key);
            Assert.NotEqual("payroll.cost", key);
        }
    }
}
