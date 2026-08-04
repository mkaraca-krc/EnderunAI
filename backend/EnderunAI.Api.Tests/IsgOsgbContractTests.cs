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
/// OSGB sözleşmesi ve hakedişe önerilen İSG kesintisi — uçtan uca.
/// </summary>
[Collection("Integration")]
public sealed class IsgOsgbContractTests(DatabaseFixture fixture)
{
    private async Task<(Guid CompanyId, Guid ProjectId, Guid OsgbAccountId)>
        CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var osgb = new CurrentAccount
        {
            CompanyId = project.CompanyId,
            Code = $"OSGB-{suffix}",
            Title = $"Test OSGB {suffix}",
            TaxNumber = "1112223334",
            Roles = CurrentAccountRoles.ServiceCompany,
            Status = CurrentAccountStatus.Approved
        };
        db.CurrentAccounts.Add(osgb);
        await db.SaveChangesAsync();

        return (project.CompanyId, project.Id, osgb.Id);
    }

    /// <summary>Projenin bir şantiyesine verilen sayıda personel atar.</summary>
    private async Task AssignSitePersonnelAsync(
        Guid companyId, Guid projectId, string suffix, int count)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var site = new ProjectSite
        {
            ProjectId = projectId,
            Code = $"SNT-{suffix}",
            Name = $"Test Şantiye {suffix}"
        };
        db.ProjectSites.Add(site);
        await db.SaveChangesAsync();

        for (var index = 0; index < count; index++)
        {
            var personnel = await TestDataFactory.CreatePersonnelAsync(
                db, companyId, $"{suffix}{index}");

            db.ProjectSiteAssignments.Add(new ProjectSiteAssignment
            {
                PersonnelId = personnel.Id,
                ProjectSiteId = site.Id,
                StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        }

        await db.SaveChangesAsync();
    }

    private static object BuildPayload(
        Guid companyId, Guid osgbAccountId, string contractNumber,
        int billingType = 0, decimal monthlyFee = 12_000m,
        decimal perPersonFee = 0m,
        string startDate = "2026-01-01", string? endDate = null) => new
        {
            companyId,
            currentAccountId = osgbAccountId,
            contractNumber,
            startDate,
            endDate,
            billingType,
            monthlyFee,
            perPersonFee,
            currencyCode = "TRY",
            notes = (string?)null,
            experts = new[]
            {
                new
                {
                    expertType = 0,
                    fullName = "Test Uzman",
                    certificateNumber = "IGU-12345",
                    expertClass = "b",
                    phone = "5550000000",
                    email = "uzman@test.local",
                    startDate,
                    endDate = (string?)null
                },
                new
                {
                    expertType = 1,
                    fullName = "Test Hekim",
                    certificateNumber = "IYH-99887",
                    expertClass = (string?)null,
                    phone = (string?)null,
                    email = (string?)null,
                    startDate,
                    endDate = (string?)null
                }
            }
        };

    [Fact]
    public async Task Create_StoresContractWithExperts()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, _, osgbId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/isg/osgb-sozlesmeleri",
            BuildPayload(companyId, osgbId, $"OSGB-{suffix}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Aylık sabit", payload.GetProperty("billingTypeName").GetString());
        Assert.Equal(12_000m, payload.GetProperty("monthlyFee").GetDecimal());
        Assert.Equal(2, payload.GetProperty("experts").GetArrayLength());

        var expert = payload.GetProperty("experts").EnumerateArray().First();
        Assert.Equal("İş güvenliği uzmanı", expert.GetProperty("expertTypeName").GetString());
        // Sınıf harfi büyütülerek saklanır.
        Assert.Equal("B", expert.GetProperty("expertClass").GetString());
        Assert.True(expert.GetProperty("isCurrentlyAssigned").GetBoolean());
    }

    [Fact]
    public async Task Create_DuplicateContractNumber_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, _, osgbId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var number = $"OSGB-{suffix}";

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/isg/osgb-sozlesmeleri",
                BuildPayload(companyId, osgbId, number))).StatusCode);

        var second = await client.PostAsJsonAsync("/api/isg/osgb-sozlesmeleri",
            BuildPayload(companyId, osgbId, number));

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Create_EndDateBeforeStart_IsRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, _, osgbId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync("/api/isg/osgb-sozlesmeleri",
            BuildPayload(companyId, osgbId, $"OSGB-{suffix}",
                startDate: "2026-06-01", endDate: "2026-01-01"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Detail_ListsOsgbSupplierInvoices()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId, osgbId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var create = await client.PostAsJsonAsync("/api/isg/osgb-sozlesmeleri",
            BuildPayload(companyId, osgbId, $"OSGB-{suffix}"));
        var contractId = (await create.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        // OSGB faturası ayrı bir tabloya değil, normal tedarikçi
        // faturası olarak girilir; sözleşme ekranı onu cari üzerinden bulur.
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.SupplierInvoices.Add(new SupplierInvoice
            {
                CompanyId = companyId,
                SupplierCurrentAccountId = osgbId,
                ProjectId = projectId,
                InternalNumber = $"SFT-{suffix}",
                InvoiceNumber = $"OSGBFTR-{suffix}",
                InvoiceDate = DateTime.UtcNow.Date,
                CurrencyCode = "TRY",
                ExchangeRate = 1m,
                Subtotal = 12_000m,
                VatTotal = 2_400m,
                GrandTotal = 14_400m,
                Status = SupplierInvoiceStatus.Draft
            });
            await db.SaveChangesAsync();
        }

        var detail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/isg/osgb-sozlesmeleri/{contractId}");

        var invoice = detail.GetProperty("invoices").EnumerateArray().Single();
        Assert.Equal($"OSGBFTR-{suffix}", invoice.GetProperty("invoiceNumber").GetString());
        Assert.Equal(14_400m, invoice.GetProperty("grandTotal").GetDecimal());
    }

    [Fact]
    public async Task DeductionSuggestion_MonthlyContract_ReturnsOhsContribution()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId, osgbId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/isg/osgb-sozlesmeleri",
                BuildPayload(companyId, osgbId, $"OSGB-{suffix}",
                    monthlyFee: 9_500m))).StatusCode);

        var suggestion = await client.GetFromJsonAsync<JsonElement>(
            $"/api/isg/hakedis-kesinti-onerisi?companyId={companyId}" +
            $"&projectId={projectId}&donem=2026-06-15");

        Assert.True(suggestion.GetProperty("hasSuggestion").GetBoolean());
        // 8 = HakedisDeductionType.OhsContribution — mevcut tür kullanılıyor.
        Assert.Equal(8, suggestion.GetProperty("deductionType").GetInt32());
        Assert.Equal(9_500m, suggestion.GetProperty("manualAmount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, suggestion.GetProperty("personCount").ValueKind);
    }

    [Fact]
    public async Task DeductionSuggestion_PerPersonContract_CountsSitePersonnel()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId, osgbId) = await CreateContextAsync(suffix);
        await AssignSitePersonnelAsync(companyId, projectId, suffix, count: 4);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/isg/osgb-sozlesmeleri",
                BuildPayload(companyId, osgbId, $"OSGB-{suffix}",
                    billingType: 1, monthlyFee: 0m, perPersonFee: 250m))).StatusCode);

        var suggestion = await client.GetFromJsonAsync<JsonElement>(
            $"/api/isg/hakedis-kesinti-onerisi?companyId={companyId}" +
            $"&projectId={projectId}&donem=2026-06-15");

        Assert.True(suggestion.GetProperty("hasSuggestion").GetBoolean());
        Assert.Equal(4, suggestion.GetProperty("personCount").GetInt32());
        Assert.Equal(1_000m, suggestion.GetProperty("manualAmount").GetDecimal());
    }

    [Fact]
    public async Task DeductionSuggestion_WithoutContract_ExplainsWhyInsteadOfGuessing()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId, _) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var suggestion = await client.GetFromJsonAsync<JsonElement>(
            $"/api/isg/hakedis-kesinti-onerisi?companyId={companyId}" +
            $"&projectId={projectId}&donem=2026-06-15");

        Assert.False(suggestion.GetProperty("hasSuggestion").GetBoolean());
        Assert.Equal(0m, suggestion.GetProperty("manualAmount").GetDecimal());
        Assert.Contains("sözleşme",
            suggestion.GetProperty("reason").GetString()!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeductionSuggestion_PeriodOutsideContract_ProducesNoSuggestion()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, projectId, osgbId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/isg/osgb-sozlesmeleri",
                BuildPayload(companyId, osgbId, $"OSGB-{suffix}",
                    startDate: "2026-01-01", endDate: "2026-03-31"))).StatusCode);

        var suggestion = await client.GetFromJsonAsync<JsonElement>(
            $"/api/isg/hakedis-kesinti-onerisi?companyId={companyId}" +
            $"&projectId={projectId}&donem=2026-06-15");

        Assert.False(suggestion.GetProperty("hasSuggestion").GetBoolean());
    }

    [Fact]
    public async Task ExpiringContract_IsFlaggedInList()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (companyId, _, osgbId) = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var endDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/isg/osgb-sozlesmeleri",
                BuildPayload(companyId, osgbId, $"OSGB-{suffix}",
                    startDate: "2026-01-01",
                    endDate: endDate.ToString("yyyy-MM-dd")))).StatusCode);

        var list = await client.GetFromJsonAsync<JsonElement>(
            $"/api/isg/osgb-sozlesmeleri?companyId={companyId}");

        var item = list.EnumerateArray().Single();
        Assert.Equal("Süresi doluyor", item.GetProperty("statusName").GetString());
        Assert.Equal(10, item.GetProperty("daysUntilExpiry").GetInt32());
    }
}
