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
/// Onay politikası yapılandırılmamış şirkette varsayılan politika.
///
/// SORUN: sipariş onaya gönderilirken politika aranıyordu; hiçbir
/// şirkete politika seed edilmediği için yeni kurulan şirkette
/// sipariş süreci HİÇ başlamıyordu ("politika yapılandırılmadan
/// sipariş onaya gönderilemez").
///
/// ÇÖZÜM seed değil, okuma anında varsayılan: politika denetim
/// defterine yazılan bir İNSAN KARARIdır. Kurulumda sahte bir
/// "politika yapılandırıldı" olayı yazmak, kimsenin vermediği bir
/// kararı deftere koymak olurdu.
///
/// Varsayılan limitler şirketin FATURA tarafında zaten seçtiği GM
/// onay eşiğinden türetilir — uydurma sabit yerine şirketin kendi
/// kararı.
/// </summary>
[Collection("Integration")]
public sealed class ProcurementDefaultPolicyTests(DatabaseFixture fixture)
{
    /// <summary>
    /// Şirket ve (istenirse) finans ayarı kurar.
    /// </summary>
    private async Task<Guid> CreateCompanyAsync(decimal? gmThreshold)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        if (gmThreshold is decimal threshold)
        {
            var existing = await db.CompanyFinanceSettings
                .SingleOrDefaultAsync(x => x.CompanyId == company.Id);

            if (existing is null)
            {
                db.CompanyFinanceSettings.Add(new CompanyFinanceSettings
                {
                    CompanyId = company.Id,
                    GmApprovalThresholdTry = threshold
                });
            }
            else
            {
                existing.GmApprovalThresholdTry = threshold;
            }

            await db.SaveChangesAsync();
        }

        return company.Id;
    }

    /// <summary>
    /// Politika yokken de bir politika dönmeli; aksi hâlde sipariş
    /// süreci hiç başlamaz.
    /// </summary>
    [Fact]
    public async Task WithoutConfiguredPolicy_DefaultIsReturned()
    {
        var companyId = await CreateCompanyAsync(gmThreshold: 250_000m);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var dashboard = await client.GetFromJsonAsync<JsonElement>(
            $"/api/procurement/approval-control/dashboard?companyId={companyId}");

        var policy = dashboard.GetProperty("policy");

        Assert.NotEqual(JsonValueKind.Null, policy.ValueKind);

        // Finans kademesi = GM eşiği; üstü GM'ye gider.
        Assert.Equal(
            250_000m, policy.GetProperty("financeApprovalLimitTry").GetDecimal());

        // Satın alma kademesi eşiğin beşte biri.
        Assert.Equal(
            50_000m, policy.GetProperty("purchasingApprovalLimitTry").GetDecimal());

        // Bütçe zorunluluğu varsayılanda kapalı: açık olsaydı bütçesi
        // olmayan şirkette hiçbir sipariş onaya gidemezdi.
        Assert.False(policy.GetProperty("requireBudget").GetBoolean());

        // Kullanıcı bunun varsayılan olduğunu görmeli.
        Assert.Contains(
            "yapılandırılmadı", policy.GetProperty("note").GetString()!);
    }

    /// <summary>
    /// Şirketin GM eşiği tanımlı değilse son çare sabit kullanılır ama
    /// yine de politika döner.
    /// </summary>
    [Fact]
    public async Task WithoutFinanceSettings_FallsBackToConstant()
    {
        var companyId = await CreateCompanyAsync(gmThreshold: null);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var dashboard = await client.GetFromJsonAsync<JsonElement>(
            $"/api/procurement/approval-control/dashboard?companyId={companyId}");

        var policy = dashboard.GetProperty("policy");

        Assert.NotEqual(JsonValueKind.Null, policy.ValueKind);

        var financeLimit =
            policy.GetProperty("financeApprovalLimitTry").GetDecimal();
        var purchasingLimit =
            policy.GetProperty("purchasingApprovalLimitTry").GetDecimal();

        Assert.True(financeLimit > 0m);
        // ValidatePolicy elle girişte de aynı kuralı uyguluyor:
        // satın alma limiti finans limitinden küçük olmalı.
        Assert.True(purchasingLimit < financeLimit);
    }

    /// <summary>
    /// KRİTİK: kullanıcı politikayı gerçekten yapılandırdığında
    /// varsayılan DEVREDEN ÇIKMALI. Aksi hâlde şirketin kendi kararı
    /// sessizce yok sayılırdı.
    /// </summary>
    [Fact]
    public async Task ConfiguredPolicy_OverridesDefault()
    {
        var companyId = await CreateCompanyAsync(gmThreshold: 250_000m);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var configured = await client.PutAsJsonAsync(
            $"/api/procurement/approval-control/companies/{companyId}/policy",
            new
            {
                purchasingApprovalLimitTry = 10_000m,
                financeApprovalLimitTry = 60_000m,
                requireBudget = false,
                note = "Elle yapılandırıldı"
            });

        Assert.True(
            configured.IsSuccessStatusCode,
            $"Politika kaydedilemedi: {configured.StatusCode}");

        var dashboard = await client.GetFromJsonAsync<JsonElement>(
            $"/api/procurement/approval-control/dashboard?companyId={companyId}");

        var policy = dashboard.GetProperty("policy");

        Assert.Equal(
            60_000m, policy.GetProperty("financeApprovalLimitTry").GetDecimal());
        Assert.Equal(
            10_000m, policy.GetProperty("purchasingApprovalLimitTry").GetDecimal());
        Assert.Equal(
            "Elle yapılandırıldı", policy.GetProperty("note").GetString());
    }
}
