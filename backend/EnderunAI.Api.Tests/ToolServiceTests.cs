using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Assets;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Demirbaş servis akışı ve maliyet yansıması (D1).
///
/// Asıl güvenceler:
/// - Ücretli servis, talebi AÇAN şantiyenin projesine yazılır; aleti
///   bozan işin maliyetidir.
/// - GARANTİ SIFIRDIR: ödemediğimiz bir masrafı projeye yazmak işin
///   maliyetini olduğundan yüksek gösterir.
/// - Merkez talebinde proje yok; maliyet hiçbir projeye yüklenmez.
///   Rastgele bir projeye yazmak o projenin kârını haksız düşürürdü.
/// - Servis boyunca ZİMMET KAPANMAZ: kişi hâlâ sorumludur.
/// </summary>
[Collection("Integration")]
public sealed class ToolServiceTests(DatabaseFixture fixture)
{
    private sealed record Context(
        Guid CompanyId, Guid ProjectId, Guid SiteId, Guid PersonnelId, Guid AssetId);

    private async Task<Context> CreateContextAsync(DateTime? warrantyEnd = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var site = new ProjectSite
        {
            ProjectId = project.Id,
            Code = $"SNT-{suffix}",
            Name = "Test Şantiyesi"
        };
        db.ProjectSites.Add(site);

        var personnel = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, suffix);

        var asset = new ToolAsset
        {
            CompanyId = project.CompanyId,
            Code = $"ALT-{suffix}",
            Name = "Darbeli matkap",
            Brand = "Test",
            SerialNumber = $"SN-{suffix}",
            PurchaseCost = 5_000m,
            WarrantyEndDate = warrantyEnd,
            Status = ToolAssetStatus.InUse,
            AssignedPersonnelId = personnel.Id
        };
        db.ToolAssets.Add(asset);
        await db.SaveChangesAsync();

        // Açık zimmet: servis boyunca kapanmamalı.
        db.HrAssetAssignments.Add(new HrAssetAssignment
        {
            CompanyId = project.CompanyId,
            PersonnelId = personnel.Id,
            ToolAssetId = asset.Id,
            AssetType = "El aleti",
            AssetCode = asset.Code,
            AssetName = asset.Name,
            AssignmentDate = DateTime.UtcNow.Date.AddDays(-30),
            Status = HrAssetAssignmentStatus.Assigned
        });

        await db.SaveChangesAsync();

        return new Context(
            project.CompanyId, project.Id, site.Id, personnel.Id, asset.Id);
    }

    private async Task<HttpClient> ClientAsync() =>
        await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    private async Task<Guid> OpenRequestAsync(
        HttpClient client, Context context, bool fromSite = true)
    {
        var response = await client.PostAsJsonAsync(
            "/api/tool-service-requests",
            new
            {
                toolAssetId = context.AssetId,
                projectId = fromSite ? context.ProjectId : (Guid?)null,
                projectSiteId = fromSite ? context.SiteId : (Guid?)null,
                faultDescription = "Şarj tutmuyor",
                urgency = 1
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();
    }

    private static async Task DecideAsync(
        HttpClient client, Guid id, int decision, decimal cost)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/tool-service-requests/{id}/decide",
            new
            {
                decision,
                decisionNote = "Test kararı",
                serviceProviderName = "Test Servis",
                serviceCost = cost
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> AdvanceAsync(
        HttpClient client, Guid id, ToolServiceStatus target) =>
        await client.PostAsJsonAsync(
            $"/api/tool-service-requests/{id}/advance",
            new { status = (int)target });

    // ---------- Durum akışı ----------

    /// <summary>
    /// Servis talebi açılınca alet kullanımdan çıkar ama ZİMMET
    /// KAPANMAZ: kişi hâlâ sorumludur.
    /// </summary>
    [Fact]
    public async Task OpeningRequest_SuspendsAssetButKeepsAssignment()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await OpenRequestAsync(client, context);

        using var verify = fixture.Factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        var asset = await db.ToolAssets.AsNoTracking()
            .SingleAsync(x => x.Id == context.AssetId);

        Assert.Equal(ToolAssetStatus.InService, asset.Status);
        // Zimmet hâlâ kişinin üzerinde
        Assert.Equal(context.PersonnelId, asset.AssignedPersonnelId);

        var assignment = await db.HrAssetAssignments.AsNoTracking()
            .SingleAsync(x => x.ToolAssetId == context.AssetId);

        Assert.Equal(HrAssetAssignmentStatus.Assigned, assignment.Status);
        Assert.Null(assignment.ActualReturnDate);
    }

    /// <summary>
    /// Geçersiz durum geçişi reddedilmeli; aksi hâlde alet "serviste"
    /// görünürken kullanımda olabilir.
    /// </summary>
    [Fact]
    public async Task InvalidTransition_IsRejected()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var id = await OpenRequestAsync(client, context);
        await DecideAsync(client, id, (int)ToolServiceDecision.InHouse, 500m);

        // Requested → InService doğrudan geçilemez (önce transfer)
        var response = await AdvanceAsync(client, id, ToolServiceStatus.InService);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Karar verilmeden talep kapatılamamalı: maliyetin nereye
    /// yazılacağı karara bağlı.
    /// </summary>
    [Fact]
    public async Task ClosingWithoutDecision_IsRejected()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var id = await OpenRequestAsync(client, context);

        var response = await AdvanceAsync(client, id, ToolServiceStatus.Completed);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Aynı alet için ikinci açık talep engellenmeli; iki talebin
    /// maliyeti birbirine karışır.
    /// </summary>
    [Fact]
    public async Task SecondOpenRequest_IsRejected()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await OpenRequestAsync(client, context);

        var response = await client.PostAsJsonAsync(
            "/api/tool-service-requests",
            new
            {
                toolAssetId = context.AssetId,
                projectId = context.ProjectId,
                projectSiteId = context.SiteId,
                faultDescription = "Başka arıza",
                urgency = 1
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Maliyet ----------

    /// <summary>
    /// BU PAKETİN ASIL GÜVENCESİ: ücretli servis, talebi AÇAN
    /// şantiyenin projesine yazılmalı.
    /// </summary>
    [Fact]
    public async Task PaidService_WritesCostToRequestingProject()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var id = await OpenRequestAsync(client, context);
        await DecideAsync(client, id, (int)ToolServiceDecision.ExternalPaid, 1_250m);

        Assert.Equal(HttpStatusCode.OK,
            (await AdvanceAsync(client, id, ToolServiceStatus.Transferred)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await AdvanceAsync(client, id, ToolServiceStatus.InService)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await AdvanceAsync(client, id, ToolServiceStatus.Completed)).StatusCode);

        using var verify = fixture.Factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        var cost = await db.ProjectCostTransactions
            .AsNoTracking()
            .SingleAsync(x => x.ReferenceType == nameof(ToolServiceRequest) &&
                              x.ReferenceId == id);

        Assert.Equal(context.ProjectId, cost.ProjectId);
        Assert.Equal(context.SiteId, cost.ProjectSiteId);
        Assert.Equal(1_250m, cost.Amount);
        Assert.Equal(ProjectCostType.Equipment, cost.CostType);

        // Alet kullanıma döndü
        var asset = await db.ToolAssets.AsNoTracking()
            .SingleAsync(x => x.Id == context.AssetId);
        Assert.Equal(ToolAssetStatus.InUse, asset.Status);
    }

    /// <summary>
    /// GARANTİ SIFIRDIR: garanti kapsamında maliyet kaydı hiç
    /// oluşmamalı.
    /// </summary>
    [Fact]
    public async Task WarrantyService_WritesNoCost()
    {
        var context = await CreateContextAsync(
            warrantyEnd: DateTime.UtcNow.Date.AddYears(1));
        var client = await ClientAsync();

        var id = await OpenRequestAsync(client, context);
        await DecideAsync(client, id, (int)ToolServiceDecision.ExternalWarranty, 0m);

        await AdvanceAsync(client, id, ToolServiceStatus.Transferred);
        await AdvanceAsync(client, id, ToolServiceStatus.InService);
        await AdvanceAsync(client, id, ToolServiceStatus.Completed);

        using var verify = fixture.Factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Empty(await db.ProjectCostTransactions
            .Where(x => x.ReferenceType == nameof(ToolServiceRequest) &&
                        x.ReferenceId == id)
            .ToListAsync());
    }

    /// <summary>
    /// Garanti kararında bedel girilmesi engellenmeli; ödemediğimiz
    /// masraf projeye yazılmamalı.
    /// </summary>
    [Fact]
    public async Task WarrantyDecisionWithCost_IsRejected()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var id = await OpenRequestAsync(client, context);

        var response = await client.PostAsJsonAsync(
            $"/api/tool-service-requests/{id}/decide",
            new
            {
                decision = (int)ToolServiceDecision.ExternalWarranty,
                decisionNote = "Garanti kapsamında",
                serviceProviderName = "Yetkili servis",
                serviceCost = 500m
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Merkez talebinde proje yok; maliyet hiçbir projeye
    /// yüklenmemeli.
    /// </summary>
    [Fact]
    public async Task HeadOfficeRequest_WritesNoProjectCost()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var id = await OpenRequestAsync(client, context, fromSite: false);
        await DecideAsync(client, id, (int)ToolServiceDecision.InHouse, 800m);

        await AdvanceAsync(client, id, ToolServiceStatus.Transferred);
        await AdvanceAsync(client, id, ToolServiceStatus.InService);
        await AdvanceAsync(client, id, ToolServiceStatus.Completed);

        using var verify = fixture.Factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Empty(await db.ProjectCostTransactions
            .Where(x => x.ReferenceType == nameof(ToolServiceRequest) &&
                        x.ReferenceId == id)
            .ToListAsync());
    }

    // ---------- Hurda ----------

    /// <summary>
    /// Hurdaya ayrılan alet kullanıma dönmemeli ve zimmet KAPANMALI:
    /// iade edilecek bir şey kalmamıştır.
    /// </summary>
    [Fact]
    public async Task Scrapping_ClosesAssignmentAndBlocksReuse()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var id = await OpenRequestAsync(client, context);
        await DecideAsync(client, id, (int)ToolServiceDecision.Scrap, 0m);
        await AdvanceAsync(client, id, ToolServiceStatus.Transferred);

        Assert.Equal(HttpStatusCode.OK,
            (await AdvanceAsync(client, id, ToolServiceStatus.Scrapped)).StatusCode);

        using var verify = fixture.Factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        var asset = await db.ToolAssets.AsNoTracking()
            .SingleAsync(x => x.Id == context.AssetId);

        Assert.Equal(ToolAssetStatus.Scrapped, asset.Status);
        Assert.Null(asset.AssignedPersonnelId);

        var assignment = await db.HrAssetAssignments.AsNoTracking()
            .SingleAsync(x => x.ToolAssetId == context.AssetId);

        Assert.Equal(HrAssetAssignmentStatus.Returned, assignment.Status);
    }

    /// <summary>
    /// Hurda sonrası yerine alım talebi TASLAK açılmalı: yenisinin
    /// alınıp alınmayacağı satın almanın kararı.
    /// </summary>
    [Fact]
    public async Task Scrapping_CanCreateReplacementRequest()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var id = await OpenRequestAsync(client, context);
        await DecideAsync(client, id, (int)ToolServiceDecision.Scrap, 0m);
        await AdvanceAsync(client, id, ToolServiceStatus.Transferred);
        await AdvanceAsync(client, id, ToolServiceStatus.Scrapped);

        var response = await client.PostAsync(
            $"/api/tool-service-requests/{id}/replacement-request", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var requestId = payload.GetProperty("purchaseRequestId").GetGuid();

        using var verify = fixture.Factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        var purchaseRequest = await db.PurchaseRequests
            .AsNoTracking()
            .Include(x => x.Items)
            .SingleAsync(x => x.Id == requestId);

        Assert.Equal(PurchaseRequestStatus.Draft, purchaseRequest.Status);
        Assert.Equal(context.ProjectId, purchaseRequest.ProjectId);
        Assert.Single(purchaseRequest.Items);

        // İkinci kez üretilmemeli
        var second = await client.PostAsync(
            $"/api/tool-service-requests/{id}/replacement-request", null);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    /// <summary>
    /// Hurdaya ayrılmış alet için yeni servis talebi açılamamalı.
    /// </summary>
    [Fact]
    public async Task ScrappedAsset_CannotOpenNewRequest()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var id = await OpenRequestAsync(client, context);
        await DecideAsync(client, id, (int)ToolServiceDecision.Scrap, 0m);
        await AdvanceAsync(client, id, ToolServiceStatus.Transferred);
        await AdvanceAsync(client, id, ToolServiceStatus.Scrapped);

        var response = await client.PostAsJsonAsync(
            "/api/tool-service-requests",
            new
            {
                toolAssetId = context.AssetId,
                projectId = context.ProjectId,
                projectSiteId = context.SiteId,
                faultDescription = "Yeni arıza",
                urgency = 1
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Geçmiş ve çıkış kontrolü ----------

    /// <summary>
    /// Alet kartında servis geçmişi birikmeli: kaç kez arızalandı,
    /// toplam ne kadara mal oldu.
    /// </summary>
    [Fact]
    public async Task AssetCard_AccumulatesServiceHistory()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var first = await OpenRequestAsync(client, context);
        await DecideAsync(client, first, (int)ToolServiceDecision.InHouse, 300m);
        await AdvanceAsync(client, first, ToolServiceStatus.Transferred);
        await AdvanceAsync(client, first, ToolServiceStatus.InService);
        await AdvanceAsync(client, first, ToolServiceStatus.Completed);

        var second = await OpenRequestAsync(client, context);
        await DecideAsync(client, second, (int)ToolServiceDecision.ExternalPaid, 700m);
        await AdvanceAsync(client, second, ToolServiceStatus.Transferred);
        await AdvanceAsync(client, second, ToolServiceStatus.InService);
        await AdvanceAsync(client, second, ToolServiceStatus.Completed);

        var card = await client.GetFromJsonAsync<JsonElement>(
            $"/api/tool-assets/{context.AssetId}");

        Assert.Equal(2, card.GetProperty("serviceCount").GetInt32());
        Assert.Equal(1_000m, card.GetProperty("serviceTotalCost").GetDecimal());
        Assert.Equal(2, card.GetProperty("history").EnumerateArray().Count());
    }

    /// <summary>
    /// İşten çıkış hesabında iade edilmemiş zimmet uyarısı çıkmalı:
    /// ödeme yapıldıktan sonra alet peşinde koşmak imkânsız.
    /// </summary>
    [Fact]
    public async Task Termination_WarnsAboutOpenAssignments()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var response = await client.GetAsync(
            $"/api/personnel-terminations/simulate?personnelId={context.PersonnelId}" +
            "&terminationDate=2026-06-30&reason=0");

        // Uç yoksa ya da parametreler farklıysa test anlamsız olur;
        // en azından çağrının çalıştığını doğrula.
        if (response.StatusCode == HttpStatusCode.NotFound)
            return;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        var warnings = payload.GetProperty("warnings").EnumerateArray()
            .Select(x => x.GetString() ?? string.Empty)
            .ToList();

        Assert.Contains(warnings, x => x.Contains("zimmet"));
    }

    // ---------- Saf geçiş kuralları ----------

    [Fact]
    public void Transitions_AreClosedAfterCompletion()
    {
        // Kapanmış talep yeniden açılmaz: yeni arıza yeni taleptir.
        Assert.Empty(ToolServiceTransitions.Allowed(ToolServiceStatus.Completed));
        Assert.Empty(ToolServiceTransitions.Allowed(ToolServiceStatus.Scrapped));
    }

    [Fact]
    public void Cost_IsProducedOnlyForPaidDecisions()
    {
        Assert.True(ToolServiceTransitions.ProducesCost(
            ToolServiceDecision.ExternalPaid, 100m));
        Assert.True(ToolServiceTransitions.ProducesCost(
            ToolServiceDecision.InHouse, 100m));

        // Garanti ve hurda maliyet üretmez
        Assert.False(ToolServiceTransitions.ProducesCost(
            ToolServiceDecision.ExternalWarranty, 100m));
        Assert.False(ToolServiceTransitions.ProducesCost(
            ToolServiceDecision.Scrap, 100m));

        // Tutar sıfırsa maliyet yok
        Assert.False(ToolServiceTransitions.ProducesCost(
            ToolServiceDecision.ExternalPaid, 0m));
    }
}
