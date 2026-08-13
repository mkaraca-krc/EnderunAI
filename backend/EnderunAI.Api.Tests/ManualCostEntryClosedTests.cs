using System.Net;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Expenses;
using EnderunAI.Api.Services.Expenses;
using EnderunAI.Api.Services.Projects;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// TEK KAYNAK: proje maliyet defterine ELLE yazan uç kapatıldı.
///
/// Aynı maliyeti iki ayrı yoldan sisteme sokabilen bir uç, tanımı gereği
/// ayrışma üretir. Maliyet defteri TÜRETİLMİŞ bir katman — otomatik
/// kaynaklar yazar; elle girilen maliyet artık gider kaydından geçiyor
/// ve proje maliyetine oradan yansıyor.
/// </summary>
[Collection("Integration")]
public sealed class ManualCostEntryClosedTests(DatabaseFixture fixture)
{
    private sealed record Context(Guid CompanyId, Guid ProjectId);

    private async Task<Context> CreateContextAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        await ExpenseCategoryProvisioner.EnsureAsync(
            db, project.CompanyId, CancellationToken.None);

        return new Context(project.CompanyId, project.Id);
    }

    private async Task<Guid> CategoryIdAsync(Guid companyId, string code)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.ExpenseCategories
            .Where(x => x.CompanyId == companyId && x.Code == code)
            .Select(x => x.Id)
            .SingleAsync();
    }

    /// <summary>
    /// Elle maliyet kaydı ucu ARTIK YOK. Açık kalsaydı aynı kira hem
    /// oradan hem gider kaydından girilebilir ve proje maliyetinde iki
    /// kez sayılırdı.
    /// </summary>
    [Fact]
    public async Task ElleMaliyetKaydiUcu_Kapali()
    {
        var context = await CreateContextAsync();
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var response = await client.PostAsJsonAsync(
            $"/api/projects/{context.ProjectId}/cost-transactions",
            new
            {
                projectSiteId = (Guid?)null,
                costType = 0,
                costDate = DateTime.UtcNow.Date,
                amount = 1_000m,
                description = "Elle maliyet"
            });

        // 405: yol hâlâ var ama YALNIZ OKUMA için (GET). POST kabul
        // edilmiyor — defter okunabilir, elle yazılamaz. 404 beklemek
        // yanlış olurdu: kaldırılan uç değil, kaldırılan METOT.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    /// <summary>
    /// Defterin kendisi kaldırılmadı: okuma uçları yerinde. Yalnız ona
    /// ELLE yazan kapı kapandı.
    /// </summary>
    [Fact]
    public async Task DefterOkumaUclari_Acik()
    {
        var context = await CreateContextAsync();
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var list = await client.GetAsync(
            $"/api/projects/{context.ProjectId}/cost-transactions");

        var breakdown = await client.GetAsync(
            $"/api/projects/{context.ProjectId}/cost-breakdown");

        var reconciliation = await client.GetAsync(
            $"/api/projects/{context.ProjectId}/cost-reconciliation");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, breakdown.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reconciliation.StatusCode);
    }

    /// <summary>
    /// Proje ekranından açılan kalem GİDER KAYDI olarak yazılır, merkezi
    /// projedir ve proje maliyetinde BİR KEZ görünür.
    /// </summary>
    [Fact]
    public async Task ProjeEkranindanGider_MaliyeteBirKezGirer()
    {
        var context = await CreateContextAsync();
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var categoryId = await CategoryIdAsync(
            context.CompanyId, ExpenseCategoryCatalog.Rent);

        var response = await client.PostAsJsonAsync("/api/expenses/kayitlar", new
        {
            companyId = context.CompanyId,
            centerType = (int)ExpenseCenterType.Project,
            centerId = context.ProjectId,
            expenseCategoryId = categoryId,
            expenseDate = DateTime.UtcNow.Date,
            amount = 7_500m,
            description = "Şantiye konteyner kirası",
            paymentMethod = (int)ExpensePaymentMethod.Bank,
            documentType = (int)ExpenseDocumentType.Invoice,
            documentNumber = (string?)null,
            supplierCurrentAccountId = (Guid?)null,
            partnerAccountId = (Guid?)null,
            creditCardId = (Guid?)null
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Gider kaydı olarak yazıldı, merkezi proje.
        var entry = await db.ExpenseEntries
            .SingleAsync(x => x.ProjectId == context.ProjectId);

        Assert.Equal(ExpenseCenterType.Project, entry.CenterType);
        Assert.Equal(7_500m, entry.Amount);

        // Maliyet defterine SATIR YAZILMADI: kopya yok.
        Assert.False(await db.ProjectCostTransactions
            .AnyAsync(x => x.ProjectId == context.ProjectId));

        // Proje maliyetinde BİR KEZ görünüyor.
        var analysis = await scope.ServiceProvider
            .GetRequiredService<IProjectCostAnalysisService>()
            .AnalyzeAsync(context.ProjectId, CancellationToken.None);

        Assert.Equal(7_500m, analysis!.TotalCost);
    }

    /// <summary>
    /// DAVRANIŞ DEĞİŞİKLİĞİNİN SINIRI: malzeme/işçilik/taşeron elle
    /// girilemez. Bu kalemler kaynağından (satın alma, puantaj, taşeron
    /// hakedişi) gelir; elle girilseydi aynı gider iki kez sayılırdı.
    /// </summary>
    [Fact]
    public async Task OtomatikKategori_ElleGirilemez()
    {
        var context = await CreateContextAsync();
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var categoryId = await CategoryIdAsync(
            context.CompanyId, ExpenseCategoryCatalog.Material);

        var response = await client.PostAsJsonAsync("/api/expenses/kayitlar", new
        {
            companyId = context.CompanyId,
            centerType = (int)ExpenseCenterType.Project,
            centerId = context.ProjectId,
            expenseCategoryId = categoryId,
            expenseDate = DateTime.UtcNow.Date,
            amount = 1_000m,
            description = "Elle malzeme",
            paymentMethod = (int)ExpensePaymentMethod.Bank,
            documentType = (int)ExpenseDocumentType.Invoice,
            documentNumber = (string?)null,
            supplierCurrentAccountId = (Guid?)null,
            partnerAccountId = (Guid?)null,
            creditCardId = (Guid?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
