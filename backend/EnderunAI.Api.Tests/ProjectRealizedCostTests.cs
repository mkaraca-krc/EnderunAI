using EnderunAI.Api.Security;
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
/// PROJENİN GERÇEKLEŞEN MALİYETİ — TEK OKUMA NOKTASI.
///
/// Elle girilen gider kayıtları (kira, faturalar, araç masrafı) bugüne
/// kadar proje maliyet analizinde HİÇ görünmüyordu: analiz yalnız
/// maliyet defterini okuyordu. İki defter AYRIK — gider modülü otomatik
/// kategorileri elle girişte reddediyor — bu yüzden toplamak çift sayım
/// değil, eksiği kapatmak.
/// </summary>
[Collection("Integration")]
public sealed class ProjectRealizedCostTests(DatabaseFixture fixture)
{
    private sealed record Context(Guid CompanyId, Guid ProjectId);

    private static DateTime D(int day) =>
        DateTime.SpecifyKind(new DateTime(2026, 5, day), DateTimeKind.Utc);

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

    /*
     * Bu testler OKUYUCUNUN kendisini ölçüyor, kapsam süzgecini değil:
     * kapsam sınaması FinanceScopeTests içinde. Burada global kapsam
     * veriliyor ki ölçülen şey maliyet toplamı olsun.
     */
    private static readonly CurrentDataScopeSnapshot TumKapsam = new(
        HasGlobalAccess: true,
        CompanyIds: new HashSet<Guid>(),
        BranchIds: new HashSet<Guid>(),
        ProjectIds: new HashSet<Guid>(),
        VisibleCompanyIds: new HashSet<Guid>(),
        VisibleBranchIds: new HashSet<Guid>(),
        SiteIds: new HashSet<Guid>());

    private async Task<Guid> CategoryIdAsync(Guid companyId, string code)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.ExpenseCategories
            .Where(x => x.CompanyId == companyId && x.Code == code)
            .Select(x => x.Id)
            .SingleAsync();
    }

    private async Task AddExpenseAsync(
        Context context,
        decimal amount,
        string categoryCode = ExpenseCategoryCatalog.Rent,
        ExpensePaymentMethod method = ExpensePaymentMethod.Bank,
        ExpenseDocumentType document = ExpenseDocumentType.Invoice,
        int day = 10)
    {
        var categoryId = await CategoryIdAsync(context.CompanyId, categoryCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.ExpenseEntries.Add(new ExpenseEntry
        {
            CompanyId = context.CompanyId,
            CenterType = ExpenseCenterType.Project,
            ProjectId = context.ProjectId,
            ExpenseCategoryId = categoryId,
            ExpenseDate = D(day),
            Amount = amount,
            Description = "Test gideri",
            PaymentMethod = method,
            DocumentType = document
        });

        await db.SaveChangesAsync();
    }

    private async Task AddLedgerRowAsync(
        Context context,
        decimal amount,
        ProjectCostClass costClass = ProjectCostClass.Material,
        int day = 12)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.ProjectCostTransactions.Add(new ProjectCostTransaction
        {
            ProjectId = context.ProjectId,
            CostType = ProjectCostType.Material,
            CostClass = costClass,
            CostDate = D(day),
            Amount = amount,
            Description = "Defter satırı",
            ReferenceType = "SupplierInvoice"
        });

        await db.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<RealizedCostRow>> ReadAsync(
        Guid projectId,
        bool includeMasked = true,
        DateTime? from = null,
        DateTime? toExclusive = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();

        var reader = scope.ServiceProvider
            .GetRequiredService<IProjectRealizedCostReader>();

        return await reader.ReadAsync(
            projectId, from, toExclusive, includeMasked, CancellationToken.None);
    }

    /// <summary>
    /// ASIL EKSİK: elle girilen gider artık projenin gerçekleşen
    /// maliyetinde görünüyor.
    /// </summary>
    [Fact]
    public async Task ElleGider_MaliyeteGirer()
    {
        var context = await CreateContextAsync();

        await AddExpenseAsync(context, 12_000m);

        var rows = await ReadAsync(context.ProjectId);

        var row = Assert.Single(rows);

        Assert.Equal(12_000m, row.Amount);
        Assert.Equal(RealizedCostSource.ManualExpense, row.Source);
    }

    /// <summary>
    /// Elle girilebilen kategorilerin hepsi GENEL GİDER: hiçbiri
    /// imalata doğrudan girmez. Kira malzemeye sayılsaydı bileşen
    /// karşılaştırması sessizce yanılırdı.
    /// </summary>
    [Theory]
    [InlineData(ExpenseCategoryCatalog.Rent)]
    [InlineData(ExpenseCategoryCatalog.Utilities)]
    [InlineData(ExpenseCategoryCatalog.Vehicle)]
    [InlineData(ExpenseCategoryCatalog.Maintenance)]
    public async Task ElleGirilebilenKategoriler_GenelGidereDuser(string categoryCode)
    {
        var context = await CreateContextAsync();

        await AddExpenseAsync(context, 1_000m, categoryCode);

        var row = Assert.Single(await ReadAsync(context.ProjectId));

        Assert.Equal(ProjectCostClass.Overhead, row.CostClass);
    }

    /// <summary>
    /// İki defter birlikte okunuyor ve TOPLAM ikisinin toplamı —
    /// ne eksik ne fazla.
    /// </summary>
    [Fact]
    public async Task IkiDefter_BirlikteOkunur_CiftSayimYok()
    {
        var context = await CreateContextAsync();

        await AddLedgerRowAsync(context, 5_000m);
        await AddExpenseAsync(context, 3_000m);

        var rows = await ReadAsync(context.ProjectId);

        Assert.Equal(2, rows.Count);
        Assert.Equal(8_000m, rows.Sum(x => x.Amount));
        Assert.Single(rows, x => x.Source == RealizedCostSource.CostLedger);
        Assert.Single(rows, x => x.Source == RealizedCostSource.ManualExpense);
    }

    /// <summary>
    /// ELDEN İZOLASYONU: yetkisiz okumada maskeli kalem HİÇ gelmez;
    /// yetkili okumada gelir. Maske gider modülünün kendi yükleminden
    /// geçiyor, burada yeniden yazılmıyor.
    /// </summary>
    [Fact]
    public async Task EldenGider_YetkisizOkumada_Gelmez()
    {
        var context = await CreateContextAsync();

        await AddExpenseAsync(context, 4_000m);
        await AddExpenseAsync(
            context, 1_500m,
            method: ExpensePaymentMethod.Cash,
            document: ExpenseDocumentType.None,
            day: 11);

        var masked = await ReadAsync(context.ProjectId, includeMasked: false);
        var full = await ReadAsync(context.ProjectId, includeMasked: true);

        Assert.Equal(4_000m, masked.Sum(x => x.Amount));
        Assert.Equal(5_500m, full.Sum(x => x.Amount));
    }

    /// <summary>
    /// Poz bağı yalnız defter satırlarında olabilir; gider kaydında poz
    /// alanı yok. Poz kâr analizi bu yüzden gider kayıtlarını göremez —
    /// ve bunu bilerek yapıyor.
    /// </summary>
    [Fact]
    public async Task ElleGider_PozBagiTasimaz()
    {
        var context = await CreateContextAsync();

        await AddExpenseAsync(context, 2_000m);

        var row = Assert.Single(await ReadAsync(context.ProjectId));

        Assert.Null(row.BoqItemId);
        Assert.Null(row.SectionId);
    }

    /// <summary>Tarih aralığı iki defteri de süzer (hakediş kârı için).</summary>
    [Fact]
    public async Task TarihAraligi_IkiDefteriDeSuzer()
    {
        var context = await CreateContextAsync();

        await AddLedgerRowAsync(context, 5_000m, day: 3);
        await AddExpenseAsync(context, 3_000m, day: 20);

        var rows = await ReadAsync(
            context.ProjectId, from: D(1), toExclusive: D(10));

        var row = Assert.Single(rows);

        Assert.Equal(5_000m, row.Amount);
    }

    /// <summary>
    /// Uçtan uca: maliyet analizi ekranının toplamı artık elle gideri
    /// içeriyor ve genel gider bileşeninde görünüyor.
    /// </summary>
    [Fact]
    public async Task MaliyetAnalizi_ElleGideriIcerir()
    {
        var context = await CreateContextAsync();

        await AddLedgerRowAsync(context, 5_000m);
        await AddExpenseAsync(context, 3_000m);

        using var scope = fixture.Factory.Services.CreateScope();

        var analysis = await scope.ServiceProvider
            .GetRequiredService<IProjectCostAnalysisService>()
            .AnalyzeAsync(context.ProjectId, CancellationToken.None);

        Assert.NotNull(analysis);
        Assert.Equal(8_000m, analysis!.TotalCost);

        var overhead = analysis.Components
            .Single(x => x.CostClass == (int)ProjectCostClass.Overhead);

        Assert.Equal(3_000m, overhead.Actual);

        // Varsayım listesinde gerekçe yazıyor: kullanıcı toplamın neden
        // arttığını ekrandan okuyabilmeli.
        Assert.Contains(
            analysis.Assumptions,
            x => x.Contains("Elle girilen gider"));
    }

    /// <summary>
    /// Kısım kırılımında gider kaydı "Genel" satırına düşer — şantiye
    /// bilgisi kısım demek değildir (biri lokasyon, diğeri imalat
    /// kırılımı).
    /// </summary>
    [Fact]
    public async Task MaliyetAnalizi_ElleGider_GenelKisimdaGorunur()
    {
        var context = await CreateContextAsync();

        await AddExpenseAsync(context, 2_500m);

        using var scope = fixture.Factory.Services.CreateScope();

        var analysis = await scope.ServiceProvider
            .GetRequiredService<IProjectCostAnalysisService>()
            .AnalyzeAsync(context.ProjectId, CancellationToken.None);

        var section = Assert.Single(analysis!.Sections);

        Assert.Null(section.SectionId);
        Assert.Equal(2_500m, section.OverheadAmount);
    }

    /// <summary>
    /// M2'NİN ASIL GÜVENCESİ: proje maliyet analizi ile finans panosu
    /// AYNI dönem için AYNI gideri sayıyor.
    ///
    /// Pano kendi sorgusunu tutsaydı, analiz kirayı sayarken pano
    /// saymaz ve iki ekran aynı proje için farklı rakam gösterirdi —
    /// hangisinin doğru olduğu da anlaşılamazdı.
    /// </summary>
    [Fact]
    public async Task Analiz_VePano_AyniToplamiVerir()
    {
        var context = await CreateContextAsync();

        await AddLedgerRowAsync(context, 5_000m, day: 12);
        await AddExpenseAsync(context, 3_000m, day: 10);

        using var scope = fixture.Factory.Services.CreateScope();

        var analysis = await scope.ServiceProvider
            .GetRequiredService<IProjectCostAnalysisService>()
            .AnalyzeAsync(context.ProjectId, CancellationToken.None);

        var dashboardTotal = await scope.ServiceProvider
            .GetRequiredService<IProjectRealizedCostReader>()
            .ReadProjectCostTotalAsync(
                context.CompanyId, TumKapsam, D(1), D(28), true, CancellationToken.None);

        Assert.Equal(8_000m, analysis!.TotalCost);
        Assert.Equal(analysis.TotalCost, dashboardTotal);
    }

    /// <summary>
    /// Şirket geneli toplam MERKEZ/ŞUBE giderini saymaz: o rakam
    /// "proje maliyeti" anlamına geliyor ve ofis kirasını katmak
    /// panonun anlamını sessizce değiştirirdi.
    /// </summary>
    [Fact]
    public async Task SirketGeneli_MerkezGiderisniSaymaz()
    {
        var context = await CreateContextAsync();

        await AddExpenseAsync(context, 3_000m);

        var categoryId = await CategoryIdAsync(
            context.CompanyId, ExpenseCategoryCatalog.Rent);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var branchId = await db.Projects
                .Where(x => x.Id == context.ProjectId)
                .Select(x => x.BranchId)
                .SingleAsync();

            db.ExpenseEntries.Add(new ExpenseEntry
            {
                CompanyId = context.CompanyId,
                CenterType = ExpenseCenterType.Branch,
                BranchId = branchId,
                ExpenseCategoryId = categoryId,
                ExpenseDate = D(10),
                Amount = 9_000m,
                Description = "Ofis kirası",
                PaymentMethod = ExpensePaymentMethod.Bank,
                DocumentType = ExpenseDocumentType.Invoice
            });

            await db.SaveChangesAsync();
        }

        using var readScope = fixture.Factory.Services.CreateScope();

        var total = await readScope.ServiceProvider
            .GetRequiredService<IProjectRealizedCostReader>()
            .ReadProjectCostTotalAsync(
                context.CompanyId, TumKapsam, D(1), D(28), true, CancellationToken.None);

        Assert.Equal(3_000m, total);
    }

    /// <summary>
    /// Şirket geneli toplamda da elden maskesi işler: yetkisiz okumada
    /// maskeli kalem toplama girmez.
    /// </summary>
    [Fact]
    public async Task SirketGeneli_EldenMaskesiIsler()
    {
        var context = await CreateContextAsync();

        await AddExpenseAsync(context, 4_000m);
        await AddExpenseAsync(
            context, 1_500m,
            method: ExpensePaymentMethod.Cash,
            document: ExpenseDocumentType.None,
            day: 11);

        using var scope = fixture.Factory.Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IProjectRealizedCostReader>();

        var masked = await reader.ReadProjectCostTotalAsync(
            context.CompanyId, TumKapsam, D(1), D(28), false, CancellationToken.None);

        var full = await reader.ReadProjectCostTotalAsync(
            context.CompanyId, TumKapsam, D(1), D(28), true, CancellationToken.None);

        Assert.Equal(4_000m, masked);
        Assert.Equal(5_500m, full);
    }

    /// <summary>
    /// KAYNAK KIRILIMININ TOPLAMI = GERÇEKLEŞEN MALİYET.
    ///
    /// Ekranda "bu rakam nereden geldi" tablosu duruyor; toplamı
    /// tutmasaydı kullanıcı tabloya değil, tabloya olan güvenini
    /// kaybederdi.
    /// </summary>
    [Fact]
    public async Task KaynakKirilimi_ToplamiMaliyeteEsit()
    {
        var context = await CreateContextAsync();

        await AddLedgerRowAsync(context, 5_000m);
        await AddExpenseAsync(context, 3_000m);

        using var scope = fixture.Factory.Services.CreateScope();

        var analysis = await scope.ServiceProvider
            .GetRequiredService<IProjectCostAnalysisService>()
            .AnalyzeAsync(context.ProjectId, CancellationToken.None);

        Assert.Equal(analysis!.TotalCost, analysis.CostSources.Sum(x => x.Amount));

        Assert.Equal(
            5_000m,
            analysis.CostSources.Single(x => x.Source == "CostLedger").Amount);

        Assert.Equal(
            3_000m,
            analysis.CostSources.Single(x => x.Source == "ManualExpense").Amount);
    }

    /// <summary>
    /// POZA BAĞLANMAMIŞ TUTAR AYRICA GÖSTERİLİR: poz kâr analizi bunu
    /// ölçülmüş maliyet olarak göremiyor, fark sessiz kalmamalı.
    /// Elle gider kaydı ne poza ne kısma bağlı olduğu için iki sayıya
    /// da girer.
    /// </summary>
    [Fact]
    public async Task PozaBaglanmamisTutar_AyricaBildirilir()
    {
        var context = await CreateContextAsync();

        await AddExpenseAsync(context, 2_500m);

        using var scope = fixture.Factory.Services.CreateScope();

        var analysis = await scope.ServiceProvider
            .GetRequiredService<IProjectCostAnalysisService>()
            .AnalyzeAsync(context.ProjectId, CancellationToken.None);

        Assert.Equal(2_500m, analysis!.UnlinkedToBoqItemAmount);
        Assert.Equal(2_500m, analysis.UnlinkedToSectionAmount);
    }

    /// <summary>
    /// Maliyeti hiç olmayan projede kaynak tablosu BOŞ döner — sıfır
    /// satırlarla dolu bir tablo, veri varmış gibi görünürdü.
    /// </summary>
    [Fact]
    public async Task MaliyetYoksa_KaynakTablosuBos()
    {
        var context = await CreateContextAsync();

        using var scope = fixture.Factory.Services.CreateScope();

        var analysis = await scope.ServiceProvider
            .GetRequiredService<IProjectCostAnalysisService>()
            .AnalyzeAsync(context.ProjectId, CancellationToken.None);

        Assert.Empty(analysis!.CostSources);
        Assert.Equal(0m, analysis.UnlinkedToBoqItemAmount);
    }
}
