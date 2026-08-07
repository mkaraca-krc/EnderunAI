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
/// Demirbaş zimmet uçları ve uyarı özeti (D2 arayüzünün beslendiği
/// arka uç).
///
/// Asıl güvenceler:
/// - DEVİR eski zimmeti KAPATIR, üzerine yazmaz: hasar çıktığında
///   hangi dönemde kimde olduğu belli kalsın.
/// - Serviste ya da hurdadaki alet zimmetlenemez.
/// - İade alet kartını da depoya döndürür; iki yerde farklı gerçek
///   olmaz.
/// - Uyarı özeti dashboard ve brifingde AYNI sayıyı üretir.
/// </summary>
[Collection("Integration")]
public sealed class ToolAssetAssignmentTests(DatabaseFixture fixture)
{
    private sealed record Context(
        Guid CompanyId, Guid ProjectId, Guid AssetId, Guid FirstId, Guid SecondId);

    private async Task<Context> CreateContextAsync(
        DateTime? warrantyEnd = null,
        ToolAssetStatus status = ToolAssetStatus.InWarehouse)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var first = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, $"{suffix}a");

        var second = await TestDataFactory.CreatePersonnelAsync(
            db, project.CompanyId, $"{suffix}b");

        var asset = new ToolAsset
        {
            CompanyId = project.CompanyId,
            Code = $"ZMT-{suffix}",
            Name = "Hilti kırıcı",
            SerialNumber = $"SNZ-{suffix}",
            PurchaseCost = 12_000m,
            WarrantyEndDate = warrantyEnd,
            Status = status
        };

        db.ToolAssets.Add(asset);
        await db.SaveChangesAsync();

        return new Context(
            project.CompanyId, project.Id, asset.Id, first.Id, second.Id);
    }

    private async Task<HttpClient> ClientAsync() =>
        await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    private static async Task<HttpResponseMessage> AssignAsync(
        HttpClient client, Guid assetId, Guid personnelId, Guid? projectId = null) =>
        await client.PostAsJsonAsync(
            $"/api/tool-assets/{assetId}/assign",
            new
            {
                personnelId,
                projectId,
                assignmentDate = DateTime.UtcNow.Date,
                plannedReturnDate = (DateTime?)null,
                conditionAtAssignment = "Sağlam",
                notes = (string?)null
            });

    // ---------- Zimmet verme ----------

    /// <summary>
    /// Zimmet açıldığında hem zimmet kaydı oluşur hem de alet kartı
    /// "kullanımda" olur ve kimde olduğunu gösterir.
    /// </summary>
    [Fact]
    public async Task Assign_OpensAssignmentAndMarksAssetInUse()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var response = await AssignAsync(
            client, context.AssetId, context.FirstId, context.ProjectId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("transferred").GetBoolean());

        var assignmentId = body.GetProperty("assignmentId").GetGuid();

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var assignment = await db.HrAssetAssignments
            .SingleAsync(x => x.Id == assignmentId);

        Assert.Equal(context.AssetId, assignment.ToolAssetId);
        Assert.Equal(context.FirstId, assignment.PersonnelId);
        Assert.Equal(context.ProjectId, assignment.ProjectId);
        Assert.Equal(HrAssetAssignmentStatus.Assigned, assignment.Status);

        var asset = await db.ToolAssets.SingleAsync(x => x.Id == context.AssetId);

        Assert.Equal(ToolAssetStatus.InUse, asset.Status);
        Assert.Equal(context.FirstId, asset.AssignedPersonnelId);
    }

    /// <summary>
    /// DEVİR: aynı kaydın üzerine yazmak "bu alet kimdeydi" geçmişini
    /// silerdi. Eski zimmet iade olarak kapanır, yenisi açılır ve
    /// tarihte iki kayıt kalır.
    /// </summary>
    [Fact]
    public async Task Assign_ToAnotherPerson_ClosesPreviousAndKeepsHistory()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        Assert.Equal(
            HttpStatusCode.OK,
            (await AssignAsync(client, context.AssetId, context.FirstId)).StatusCode);

        var transfer = await AssignAsync(client, context.AssetId, context.SecondId);
        Assert.Equal(HttpStatusCode.OK, transfer.StatusCode);

        var body = await transfer.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("transferred").GetBoolean());

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rows = await db.HrAssetAssignments
            .Where(x => x.ToolAssetId == context.AssetId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync();

        Assert.Equal(2, rows.Count);

        Assert.Equal(context.FirstId, rows[0].PersonnelId);
        Assert.Equal(HrAssetAssignmentStatus.Returned, rows[0].Status);
        Assert.NotNull(rows[0].ActualReturnDate);

        Assert.Equal(context.SecondId, rows[1].PersonnelId);
        Assert.Equal(HrAssetAssignmentStatus.Assigned, rows[1].Status);

        var asset = await db.ToolAssets.SingleAsync(x => x.Id == context.AssetId);
        Assert.Equal(context.SecondId, asset.AssignedPersonnelId);
    }

    /// <summary>
    /// Aynı kişiye ikinci kez zimmetlemek çift kayıt üretirdi.
    /// </summary>
    [Fact]
    public async Task Assign_ToSamePerson_IsRejected()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await AssignAsync(client, context.AssetId, context.FirstId);

        var again = await AssignAsync(client, context.AssetId, context.FirstId);

        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    /// <summary>
    /// Serviste olan alet sahada yok; zimmetlenmesi kaydı gerçekten
    /// koparırdı.
    /// </summary>
    [Fact]
    public async Task Assign_WhileInService_IsRejected()
    {
        var context = await CreateContextAsync(status: ToolAssetStatus.InService);
        var client = await ClientAsync();

        var response = await AssignAsync(client, context.AssetId, context.FirstId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Hurdaya ayrılmış alet zimmetlenemez.</summary>
    [Fact]
    public async Task Assign_WhenScrapped_IsRejected()
    {
        var context = await CreateContextAsync(status: ToolAssetStatus.Scrapped);
        var client = await ClientAsync();

        var response = await AssignAsync(client, context.AssetId, context.FirstId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- İade ----------

    /// <summary>
    /// İade hem zimmeti kapatır hem aleti depoya döndürür; ikisinden
    /// biri geride kalırsa envanter ile zimmet listesi çelişir.
    /// </summary>
    [Fact]
    public async Task Return_ClosesAssignmentAndReturnsAssetToWarehouse()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await AssignAsync(client, context.AssetId, context.FirstId);

        var response = await client.PostAsJsonAsync(
            $"/api/tool-assets/{context.AssetId}/return",
            new { returnDate = DateTime.UtcNow.Date, conditionAtReturn = "Sağlam" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var assignment = await db.HrAssetAssignments
            .SingleAsync(x => x.ToolAssetId == context.AssetId);

        Assert.Equal(HrAssetAssignmentStatus.Returned, assignment.Status);
        Assert.Equal("Sağlam", assignment.ConditionAtReturn);

        var asset = await db.ToolAssets.SingleAsync(x => x.Id == context.AssetId);

        Assert.Equal(ToolAssetStatus.InWarehouse, asset.Status);
        Assert.Null(asset.AssignedPersonnelId);
    }

    /// <summary>Açık zimmeti olmayan alet iade edilemez.</summary>
    [Fact]
    public async Task Return_WithoutOpenAssignment_IsRejected()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/tool-assets/{context.AssetId}/return",
            new { returnDate = DateTime.UtcNow.Date, conditionAtReturn = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Kart ----------

    /// <summary>
    /// Kart açık zimmeti gösterir — tutanak yazdırma bu kimliğe
    /// dayanıyor.
    /// </summary>
    [Fact]
    public async Task Card_ExposesOpenAssignment()
    {
        var context = await CreateContextAsync();
        var client = await ClientAsync();

        await AssignAsync(client, context.AssetId, context.FirstId);

        var response = await client.GetAsync($"/api/tool-assets/{context.AssetId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var assignment = body.GetProperty("assignment");

        Assert.Equal(
            JsonValueKind.Object, assignment.ValueKind);

        Assert.Equal(
            context.FirstId, assignment.GetProperty("personnelId").GetGuid());

        Assert.False(string.IsNullOrWhiteSpace(
            assignment.GetProperty("personnelName").GetString()));

        // İade sonrası kart "zimmetli değil" der.
        await client.PostAsJsonAsync(
            $"/api/tool-assets/{context.AssetId}/return",
            new { returnDate = DateTime.UtcNow.Date, conditionAtReturn = (string?)null });

        var after = await (await client.GetAsync($"/api/tool-assets/{context.AssetId}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(JsonValueKind.Null, after.GetProperty("assignment").ValueKind);
    }

    // ---------- Uyarı özeti ----------

    /// <summary>
    /// Uyarı ucu, dashboard kartının beklediği dört sayıyı döndürür.
    /// Kart ve Hızır brifingi aynı servisten beslendiği için iki ekran
    /// farklı sayı gösteremez.
    /// </summary>
    [Fact]
    public async Task Alerts_CountsWarrantyServiceAndOverdueReturns()
    {
        var horizon = DateTime.UtcNow.Date.AddDays(
            ToolAssetAlertService.WarrantyHorizonDays - 5);

        var context = await CreateContextAsync(warrantyEnd: horizon);
        var client = await ClientAsync();

        await AssignAsync(client, context.AssetId, context.FirstId);

        // İade tarihi geçmiş zimmet.
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var assignment = await db.HrAssetAssignments
                .SingleAsync(x => x.ToolAssetId == context.AssetId);

            assignment.PlannedReturnDate = DateTime.UtcNow.Date.AddDays(-3);
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/tool-assets/alerts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Global paylaşılan tabloda başka testlerin kayıtları da var;
        // bu yüzden "en az" doğrulanıyor, eşitlik değil.
        Assert.True(summary.GetProperty("warrantyExpiringCount").GetInt32() >= 1);
        Assert.True(summary.GetProperty("overdueReturnCount").GetInt32() >= 1);

        // Aynı servis: doğrudan çağrıldığında da aynı sayıları verir.
        using var direct = fixture.Factory.Services.CreateScope();

        var alerts = direct.ServiceProvider
            .GetRequiredService<ToolAssetAlertService>();

        var scoped = await alerts.GetSummaryAsync(
            new HashSet<Guid> { context.CompanyId }, CancellationToken.None);

        Assert.Equal(1, scoped.WarrantyExpiringCount);
        Assert.Equal(1, scoped.OverdueReturnCount);
    }

    /// <summary>
    /// Şirket kapsamı daraltıldığında başka şirketin aleti sayılmaz;
    /// uyarı kartı kullanıcının göremediği veriyi sızdıramaz.
    /// </summary>
    [Fact]
    public async Task Alerts_RespectCompanyScope()
    {
        var context = await CreateContextAsync(
            warrantyEnd: DateTime.UtcNow.Date.AddDays(10));

        using var scope = fixture.Factory.Services.CreateScope();

        var alerts = scope.ServiceProvider
            .GetRequiredService<ToolAssetAlertService>();

        var other = await alerts.GetSummaryAsync(
            new HashSet<Guid> { Guid.NewGuid() }, CancellationToken.None);

        Assert.Equal(0, other.WarrantyExpiringCount);
        Assert.Equal(0, other.OverdueReturnCount);
        Assert.Equal(0, other.InServiceCount);
        Assert.Equal(0, other.FrequentFailureCount);
    }
}
