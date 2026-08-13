using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Expenses;
using EnderunAI.Api.Services.Expenses;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// FİNANS PANOSU DÖNEM GİDERİ = PROJE + MERKEZ/ŞUBE.
///
/// Merkez/şube giderleri gider merkezi raporunda görünüyor ama panonun
/// dönem giderine katılmıyordu: dönem gideri olduğundan az görünüyordu
/// (sessiz dışlama). Merkez tutarı artık RAPORUN KENDİSİNDEN okunuyor,
/// panoda yeniden toplanmıyor — ikinci bir sorgu rapor ile panoyu
/// zamanla ayrıştırırdı.
///
/// İKİ KALEM BİLEREK DIŞARIDA:
/// - TAHMİNİ tekrarlayan dönemler: pano gerçekleşen rakamdır.
/// - KREDİ FAİZİ: gerçekleşen ama faaliyet gideri değil; ayrı satırda
///   gösteriliyor, yani gizlenmiyor.
/// </summary>
[Collection("Integration")]
public sealed class FinanceDashboardExpenseTests(DatabaseFixture fixture)
{
    private sealed record Context(Guid CompanyId, Guid ProjectId, Guid BranchId);

    private static readonly DateTime PeriodStart =
        DateTime.SpecifyKind(new DateTime(2026, 6, 1), DateTimeKind.Utc);

    private static readonly DateTime PeriodEnd =
        DateTime.SpecifyKind(new DateTime(2026, 6, 30), DateTimeKind.Utc);

    private static DateTime D(int day) =>
        DateTime.SpecifyKind(new DateTime(2026, 6, day), DateTimeKind.Utc);

    private async Task<Context> CreateContextAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        await ExpenseCategoryProvisioner.EnsureAsync(
            db, project.CompanyId, CancellationToken.None);

        return new Context(project.CompanyId, project.Id, project.BranchId);
    }

    private async Task AddExpenseAsync(
        Context context,
        decimal amount,
        bool central,
        string categoryCode = ExpenseCategoryCatalog.Rent,
        int day = 10)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var categoryId = await db.ExpenseCategories
            .Where(x => x.CompanyId == context.CompanyId && x.Code == categoryCode)
            .Select(x => x.Id)
            .SingleAsync();

        db.ExpenseEntries.Add(new ExpenseEntry
        {
            CompanyId = context.CompanyId,
            CenterType = central ? ExpenseCenterType.Branch : ExpenseCenterType.Project,
            BranchId = central ? context.BranchId : null,
            ProjectId = central ? null : context.ProjectId,
            ExpenseCategoryId = categoryId,
            ExpenseDate = D(day),
            Amount = amount,
            Description = central ? "Ofis kirası" : "Şantiye kirası",
            PaymentMethod = ExpensePaymentMethod.Bank,
            DocumentType = ExpenseDocumentType.Invoice
        });

        await db.SaveChangesAsync();
    }

    private async Task<JsonElement> ReadDashboardAsync(Context context)
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        return await client.GetFromJsonAsync<JsonElement>(
            $"/api/finance/financial-dashboard?companyId={context.CompanyId}" +
            $"&startDate={PeriodStart:yyyy-MM-dd}&endDate={PeriodEnd:yyyy-MM-dd}");
    }

    /// <summary>Rakamlar panonun "summary" düğümünde duruyor.</summary>
    private static decimal Money(JsonElement dashboard, string field) =>
        dashboard.GetProperty("summary").GetProperty(field).GetDecimal();

    /// <summary>
    /// ASIL DÜZELTME: dönem gideri artık merkez/şube giderini de içeriyor.
    /// </summary>
    [Fact]
    public async Task DonemGideri_ProjeVeMerkeziToplar()
    {
        var context = await CreateContextAsync();

        await AddExpenseAsync(context, 3_000m, central: false);
        await AddExpenseAsync(context, 9_000m, central: true, day: 12);

        var dashboard = await ReadDashboardAsync(context);

        Assert.Equal(3_000m, Money(dashboard, "projectExpense"));
        Assert.Equal(9_000m, Money(dashboard, "centralExpense"));
        Assert.Equal(12_000m, Money(dashboard, "periodExpense"));
    }

    /// <summary>
    /// Merkez/şube gideri AYRI SATIR olarak da dönüyor: toplam tam
    /// kapanıyor ve rakamın nereden geldiği görünüyor.
    /// </summary>
    [Fact]
    public async Task Kirilim_ToplamiKapatir()
    {
        var context = await CreateContextAsync();

        await AddExpenseAsync(context, 2_500m, central: false);
        await AddExpenseAsync(context, 4_500m, central: true, day: 15);

        var dashboard = await ReadDashboardAsync(context);

        var project = Money(dashboard, "projectExpense");
        var central = Money(dashboard, "centralExpense");
        var total = Money(dashboard, "periodExpense");

        Assert.Equal(total, project + central);
    }

    /// <summary>
    /// Aynı gider İKİ KEZ sayılmaz: gider kaydı merkez ya da proje
    /// olarak tekildir, ikisine birden yazılamaz.
    /// </summary>
    [Fact]
    public async Task AyniGider_IkiKezSayilmaz()
    {
        var context = await CreateContextAsync();

        await AddExpenseAsync(context, 6_000m, central: true);

        var dashboard = await ReadDashboardAsync(context);

        Assert.Equal(0m, Money(dashboard, "projectExpense"));
        Assert.Equal(6_000m, Money(dashboard, "centralExpense"));
        Assert.Equal(6_000m, Money(dashboard, "periodExpense"));
    }

    /// <summary>
    /// Merkez gideri olmayan dönemde toplam BOZULMAZ: eskisi gibi
    /// yalnız proje giderinden oluşur.
    /// </summary>
    [Fact]
    public async Task MerkezGideriYoksa_ToplamBozulmaz()
    {
        var context = await CreateContextAsync();

        await AddExpenseAsync(context, 7_000m, central: false);

        var dashboard = await ReadDashboardAsync(context);

        Assert.Equal(7_000m, Money(dashboard, "projectExpense"));
        Assert.Equal(0m, Money(dashboard, "centralExpense"));
        Assert.Equal(7_000m, Money(dashboard, "periodExpense"));
        Assert.Equal(0m, Money(dashboard, "financingExpense"));
    }

    /// <summary>
    /// Hiç gider olmayan dönemde bütün kalemler sıfır — boş dönem hata
    /// değil.
    /// </summary>
    [Fact]
    public async Task GiderYoksa_TumKalemlerSifir()
    {
        var context = await CreateContextAsync();

        var dashboard = await ReadDashboardAsync(context);

        Assert.Equal(0m, Money(dashboard, "periodExpense"));
        Assert.Equal(0m, Money(dashboard, "centralExpense"));
        Assert.Equal(0m, Money(dashboard, "financingExpense"));
    }

    /// <summary>
    /// PANO MERKEZ TUTARI = GİDER MERKEZİ RAPORUNUN merkez satırları
    /// (tahmini ve finansman hariç). İkisi ayrışırsa bu test kırmızıya
    /// döner — panonun kendi sorgusunu yazmamasının sebebi bu.
    /// </summary>
    [Fact]
    public async Task PanoMerkezTutari_RaporlaAyni()
    {
        var context = await CreateContextAsync();

        await AddExpenseAsync(context, 5_500m, central: true);
        await AddExpenseAsync(context, 1_250m, central: true,
            categoryCode: ExpenseCategoryCatalog.Utilities, day: 20);

        var dashboard = await ReadDashboardAsync(context);

        using var scope = fixture.Factory.Services.CreateScope();

        var report = await scope.ServiceProvider
            .GetRequiredService<ExpenseCenterReportService>()
            .BuildAsync(context.CompanyId, PeriodStart, PeriodEnd, CancellationToken.None);

        var reportCentral = report.Rows
            .Where(x =>
                x.CenterType == ExpenseCenterType.Branch &&
                !x.IsEstimated &&
                x.CategoryCode != ExpenseCategoryCatalog.Financing)
            .Sum(x => x.Amount);

        Assert.Equal(6_750m, reportCentral);
        Assert.Equal(reportCentral, Money(dashboard, "centralExpense"));
    }
}
