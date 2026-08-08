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
/// İş zincirinin izlenebilirliği: teklif → proje → icmal → hakediş
/// (T3).
///
/// Bu bağ bugüne kadar yazılıyor ama HİÇ OKUNMUYORDU
/// (ProjectBoq.SourceOfferId). Bir kalemin fiyatı tartışıldığında
/// hangi teklife dayandığını, bir projenin hangi işten doğduğunu
/// göstermek zincirin iki yönünün de sorgulanabilmesine bağlı.
/// </summary>
[Collection("Integration")]
public sealed class OfferChainTests(DatabaseFixture fixture)
{
    private sealed record Context(
        Guid CompanyId, Guid BranchId, Guid AccountId, string Suffix);

    private async Task<Context> CreateContextAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (company, branch, account) =
            await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        return new Context(company.Id, branch.Id, account.Id, suffix);
    }

    private async Task<HttpClient> ClientAsync() =>
        await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    private async Task<Guid> WonOfferAsync(HttpClient client, Context context)
    {
        var created = await client.PostAsJsonAsync("/api/offers", new
        {
            companyId = context.CompanyId,
            title = "Zincir testi işi",
            offerDate = new DateTime(2026, 6, 1),
            currency = "TRY",
            exchangeRate = 1m,
            counterpartyCurrentAccountId = context.AccountId,
            counterpartyRole = (int)OfferCounterpartyRole.Employer,
            kind = (int)OfferKind.LumpSum,
            items = new[]
            {
                new
                {
                    description = "Komple imalat",
                    quantity = 1m,
                    unit = "AD",
                    listPrice = 750_000m,
                    discountRate = 0m,
                    freightRate = 0m,
                    wasteRate = 0m,
                    financeRate = 0m,
                    generalExpenseRate = 0m,
                    profitRate = 0m
                }
            }
        });

        var offerId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        foreach (var status in new[] { OfferStatus.Submitted, OfferStatus.Won })
        {
            await client.PostAsJsonAsync(
                $"/api/offers/{offerId}/durum",
                new { status = (int)status, lostReason = 0 });
        }

        return offerId;
    }

    private async Task<(Guid OfferId, Guid ProjectId)> WonWithContractAsync(
        HttpClient client, Context context, string codeSuffix)
    {
        var offerId = await WonOfferAsync(client, context);

        var response = await client.PostAsJsonAsync(
            $"/api/offers/{offerId}/sozlesme",
            new
            {
                branchId = context.BranchId,
                code = $"ZNC-{codeSuffix}",
                name = "Zincir Projesi",
                contractNumber = $"SZL-{codeSuffix}",
                contractDate = new DateTime(2026, 6, 20),
                progressPaymentPeriod = (int)ProjectProgressPaymentPeriod.Monthly,
                paymentTerms = "Hakedişten 45 gün sonra.",
                transferToBoq = true
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var projectId = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("projectId").GetGuid();

        return (offerId, projectId);
    }

    /// <summary>
    /// Zincir ucu tekliften başlayıp projeyi, icmalleri ve hakedişleri
    /// sırayla veriyor.
    /// </summary>
    [Fact]
    public async Task Chain_WalksFromOfferToProgressPayments()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var (offerId, projectId) =
            await WonWithContractAsync(client, context, context.Suffix);

        // Projeye bir hakediş ekle.
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.ProgressPayments.Add(new ProgressPayment
            {
                CompanyId = context.CompanyId,
                ProjectId = projectId,
                ProgressPaymentNumber = $"HKD-{context.Suffix}-1",
                PeriodNumber = 1,
                ProgressPaymentDate = DateTime.SpecifyKind(
                    new DateTime(2026, 8, 1), DateTimeKind.Utc),
                CurrencyCode = "TRY",
                CurrentAmount = 250_000m,
                CumulativeAmount = 250_000m,
                Status = ProgressPaymentStatus.Draft
            });

            await db.SaveChangesAsync();
        }

        var chain = await client.GetFromJsonAsync<JsonElement>(
            $"/api/offers/{offerId}/zincir");

        var offer = chain.GetProperty("offer");
        Assert.Equal((int)OfferStatus.Won, offer.GetProperty("status").GetInt32());
        Assert.Equal("Kazanıldı", offer.GetProperty("statusName").GetString());
        Assert.Equal(
            "Anahtar teslim götürü", offer.GetProperty("kindName").GetString());
        Assert.Equal("İşveren", offer.GetProperty("counterpartyRoleName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            offer.GetProperty("counterpartyName").GetString()));

        var project = chain.GetProperty("project");
        Assert.Equal(projectId, project.GetProperty("id").GetGuid());
        Assert.Equal($"SZL-{context.Suffix}", project.GetProperty("contractNumber").GetString());
        Assert.True(project.GetProperty("bornFromThisOffer").GetBoolean());
        Assert.Equal(
            (int)ProjectProgressPaymentPeriod.Monthly,
            project.GetProperty("progressPaymentPeriod").GetInt32());

        var boqs = chain.GetProperty("boqs").EnumerateArray().ToList();
        Assert.Single(boqs);
        Assert.True(boqs[0].GetProperty("fromThisOffer").GetBoolean());
        Assert.Equal(750_000m, boqs[0].GetProperty("totalAmount").GetDecimal());
        Assert.Equal(1, boqs[0].GetProperty("itemCount").GetInt32());

        var payments = chain.GetProperty("progressPayments")
            .EnumerateArray().ToList();

        Assert.Single(payments);
        Assert.Equal(250_000m, payments[0].GetProperty("currentAmount").GetDecimal());
    }

    /// <summary>
    /// Ek iş zincirinde proje "bu tekliften doğmadı" der ama icmal
    /// "bu teklifin kalemlerinden üretildi" der. Bu ayrım olmadan ek
    /// işin icmali asıl sözleşmeymiş gibi okunurdu.
    /// </summary>
    [Fact]
    public async Task Chain_DistinguishesExtraWorkFromOriginalContract()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var (firstOffer, projectId) =
            await WonWithContractAsync(client, context, context.Suffix);

        var extraOffer = await WonOfferAsync(client, context);

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync(
                $"/api/offers/{extraOffer}/sozlesme",
                new { projectId, transferToBoq = true })).StatusCode);

        var chain = await client.GetFromJsonAsync<JsonElement>(
            $"/api/offers/{extraOffer}/zincir");

        var project = chain.GetProperty("project");
        Assert.Equal(projectId, project.GetProperty("id").GetGuid());

        // Proje asıl teklifden doğdu, ek işten değil.
        Assert.False(project.GetProperty("bornFromThisOffer").GetBoolean());

        var boqs = chain.GetProperty("boqs").EnumerateArray().ToList();
        Assert.Equal(2, boqs.Count);

        var mine = boqs.Single(x => x.GetProperty("fromThisOffer").GetBoolean());
        Assert.Equal(extraOffer, mine.GetProperty("sourceOfferId").GetGuid());

        var original = boqs.Single(x => !x.GetProperty("fromThisOffer").GetBoolean());
        Assert.Equal(firstOffer, original.GetProperty("sourceOfferId").GetGuid());
    }

    /// <summary>
    /// Projeye bağlanmamış teklifin zinciri boş döner — hata değil.
    /// Kaybedilen teklifin de zinciri sorulabilmeli.
    /// </summary>
    [Fact]
    public async Task Chain_ForOfferWithoutProject_IsEmptyNotAnError()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var offerId = await WonOfferAsync(client, context);

        var chain = await client.GetFromJsonAsync<JsonElement>(
            $"/api/offers/{offerId}/zincir");

        Assert.Equal(JsonValueKind.Null, chain.GetProperty("project").ValueKind);
        Assert.Empty(chain.GetProperty("boqs").EnumerateArray());
        Assert.Empty(chain.GetProperty("progressPayments").EnumerateArray());
    }

    /// <summary>Olmayan teklifin zinciri 404.</summary>
    [Fact]
    public async Task Chain_ForMissingOffer_Returns404()
    {
        var client = await ClientAsync();

        var response = await client.GetAsync($"/api/offers/{Guid.NewGuid()}/zincir");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Zincirin geri yönü: proje kartı hangi tekliften geldiğini
    /// numarasıyla gösterir.
    /// </summary>
    [Fact]
    public async Task ProjectDetail_ShowsSourceOffer()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var (offerId, projectId) =
            await WonWithContractAsync(client, context, context.Suffix);

        var project = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{projectId}");

        Assert.Equal(offerId, project.GetProperty("sourceOfferId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(
            project.GetProperty("sourceOfferNumber").GetString()));
        Assert.Equal(
            "Zincir testi işi", project.GetProperty("sourceOfferTitle").GetString());

        // Sözleşme künyesinin yeni alanları da kartta.
        Assert.Equal(
            (int)ProjectProgressPaymentPeriod.Monthly,
            project.GetProperty("progressPaymentPeriod").GetInt32());
        Assert.Contains("45 gün", project.GetProperty("paymentTerms").GetString()!);
    }

    /// <summary>
    /// Doğrudan açılan projede kaynak teklif boştur; teklif modülü
    /// öncesi projeler bu bağı taşımıyor ve bu bir hata değil.
    /// </summary>
    [Fact]
    public async Task ProjectDetail_WithoutOffer_HasNullSource()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var client = await ClientAsync();

        var detail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{project.Id}");

        Assert.Equal(
            JsonValueKind.Null, detail.GetProperty("sourceOfferId").ValueKind);
    }
}
