using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Hizir;
using EnderunAI.Api.Services.Hizir.Briefing;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Şantiye maliyet analizi: icmal öngörüsü ↔ gerçekleşen ↔ kâr.
///
/// Kritik nokta karşılaştırmanın tabanı: sözleşmenin tamamıyla
/// kıyaslanırsa proje bitene kadar her bileşen "tasarruf" görünür ve
/// aşım çok geç fark edilir. Bu yüzden öngörü hakediş ilerlemesine göre
/// düzeltilir.
/// </summary>
[Collection("Integration")]
public sealed class ProjectCostAnalysisTests(DatabaseFixture fixture)
{
    private sealed record TestContext(
        Guid CompanyId,
        Guid ProjectId,
        Guid SectionId,
        Guid PersonnelId);

    /// <summary>
    /// İcmal: 100.000 malzeme + 60.000 işçilik + 40.000 GG&amp;K = 200.000.
    /// Hakediş: 100.000 (yani ilerleme %50).
    /// </summary>
    private async Task<TestContext> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var section = new ProjectHakedisSection
        {
            ProjectId = project.Id,
            Order = 1,
            Name = "Kolon Kablo",
            IsActive = true
        };

        db.ProjectHakedisSections.Add(section);

        var boq = new ProjectBoq
        {
            CompanyId = project.CompanyId,
            ProjectId = project.Id,
            BoqNumber = $"KSF-{suffix}",
            Name = "Sözleşme icmali",
            Status = ProjectBoqStatus.Approved,
            IsCurrentRevision = true,
            IsContractBaseline = true,
            CurrencyCode = "TRY",
            TotalAmount = 200_000m
        };

        db.ProjectBoqs.Add(boq);
        await db.SaveChangesAsync();

        db.ProjectBoqItems.Add(new ProjectBoqItem
        {
            ProjectBoqId = boq.Id,
            ProjectHakedisSectionId = section.Id,
            LineNumber = 1,
            PositionCode = "1.1",
            Description = "Kolon kablo tesisi",
            Unit = "mt",
            ContractQuantity = 1_000m,
            MaterialUnitPrice = 100m,
            LaborUnitPrice = 60m,
            OverheadUnitPrice = 40m,
            UnitPrice = 200m,
            TotalAmount = 200_000m
        });

        // Kesinleşmiş hakediş: kümülatif 100.000 → ilerleme %50.
        db.ProgressPayments.Add(new ProgressPayment
        {
            CompanyId = project.CompanyId,
            ProjectId = project.Id,
            ProgressPaymentNumber = $"HK-{suffix}",
            ProgressPaymentDate = DateTime.SpecifyKind(
                new DateTime(2026, 3, 31), DateTimeKind.Utc),
            Status = ProgressPaymentStatus.Approved,
            ContractAmount = 200_000m,
            CurrentAmount = 100_000m,
            CumulativeAmount = 100_000m
        });

        var personnel = new Personnel
        {
            CompanyId = project.CompanyId,
            FirstName = "Test",
            LastName = $"Usta {suffix}",
            EmployeeNumber = $"PRS-{suffix}",
            IsActive = true
        };

        db.Personnel.Add(personnel);

        // İşveren yükü çarpanı bordro ayarlarından okunur; ayar yoksa
        // servis 1 döndürüp varsayımı yazar (uydurma oran kullanmaz).
        var hasSettings = await db.CompanyPayrollSettings
            .AnyAsync(x => x.CompanyId == project.CompanyId);

        if (!hasSettings)
        {
            db.CompanyPayrollSettings.Add(new CompanyPayrollSettings
            {
                CompanyId = project.CompanyId,
                Year = 2026,
                MinimumWageGross = 26_005.50m,
                MinimumWageNet = 22_104.67m,
                SgkBaseFloor = 26_005.50m,
                SgkBaseCeiling = 195_041.40m,
                SgkEmployerRate = 20.75m,
                UnemploymentEmployerRate = 2m
            });
        }

        await db.SaveChangesAsync();

        return new TestContext(project.CompanyId, project.Id, section.Id, personnel.Id);
    }

    private async Task AddCostAsync(
        TestContext context,
        ProjectCostClass costClass,
        decimal amount,
        Guid? sectionId = null,
        DateTime? date = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.ProjectCostTransactions.Add(new ProjectCostTransaction
        {
            ProjectId = context.ProjectId,
            CostClass = costClass,
            CostType = ProjectCostType.Other,
            ProjectHakedisSectionId = sectionId,
            CostDate = DateTime.SpecifyKind(
                date ?? new DateTime(2026, 3, 15), DateTimeKind.Utc),
            Amount = amount,
            Description = "Test maliyeti"
        });

        await db.SaveChangesAsync();
    }

    private async Task AddLaborCostAsync(
        TestContext context, decimal amount, Guid? sectionId = null)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.HrProjectLaborCosts.Add(new HrProjectLaborCost
        {
            CompanyId = context.CompanyId,
            ProjectId = context.ProjectId,
            PersonnelId = context.PersonnelId,
            ProjectHakedisSectionId = sectionId,
            WorkDate = DateTime.SpecifyKind(new DateTime(2026, 3, 10), DateTimeKind.Utc),
            NormalHours = 8m,
            NormalCost = amount,
            TotalLaborCost = amount
        });

        await db.SaveChangesAsync();
    }

    private static decimal Component(JsonElement analysis, ProjectCostClass costClass, string field)
    {
        return analysis.GetProperty("components").EnumerateArray()
            .Single(x => x.GetProperty("costClass").GetInt32() == (int)costClass)
            .GetProperty(field)
            .GetDecimal();
    }

    /// <summary>
    /// Öngörü hakediş ilerlemesine göre düzeltilir: %50 ilerlemede
    /// 100.000'lik malzeme öngörüsünün karşılığı 50.000'dir.
    /// </summary>
    [Fact]
    public async Task Analysis_ScalesForecastByProgressRatio()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await AddCostAsync(context, ProjectCostClass.Material, 55_000m);

        var analysis = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{context.ProjectId}/cost-analysis");

        Assert.True(analysis.GetProperty("hasContractBaseline").GetBoolean());
        Assert.Equal(0.5m, analysis.GetProperty("progressRatio").GetDecimal());
        Assert.Equal(100_000m, analysis.GetProperty("revenueAmount").GetDecimal());

        Assert.Equal(100_000m,
            Component(analysis, ProjectCostClass.Material, "forecastContract"));
        Assert.Equal(50_000m,
            Component(analysis, ProjectCostClass.Material, "forecastEarned"));
        Assert.Equal(55_000m,
            Component(analysis, ProjectCostClass.Material, "actual"));

        // 55.000 − 50.000 = 5.000 aşım, %10.
        Assert.Equal(5_000m, Component(analysis, ProjectCostClass.Material, "variance"));
        Assert.Equal(10m, Component(analysis, ProjectCostClass.Material, "variancePercent"));
    }

    /// <summary>
    /// İşçilik brüt kazanca işveren yükü çarpanı uygulanarak hesaplanır;
    /// yalnız brüt alınsaydı maliyet yaklaşık beşte bir eksik çıkardı.
    /// </summary>
    [Fact]
    public async Task Analysis_AppliesEmployerCostFactorToLabor()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await AddLaborCostAsync(context, 10_000m);

        var analysis = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{context.ProjectId}/cost-analysis");

        var factor = analysis.GetProperty("employerCostFactor").GetDecimal();

        Assert.True(factor > 1m, "İşveren yükü çarpanı 1'den büyük olmalı.");

        Assert.Equal(
            decimal.Round(10_000m * factor, 2),
            Component(analysis, ProjectCostClass.Labor, "actual"));

        // Varsayım ekranda görünmeli.
        Assert.Contains(
            analysis.GetProperty("assumptions").EnumerateArray()
                .Select(x => x.GetString()),
            x => x is not null && x.Contains("teşvik"));
    }

    /// <summary>
    /// Taşeron işçiliği hem kendi satırında hem işçilik bileşeninin
    /// içinde görünür: icmalde ayrı bir taşeron bileşeni yoktur, işçilik
    /// öngörüsü ikisinin toplamını karşılar.
    /// </summary>
    [Fact]
    public async Task Analysis_CountsSubcontractorInsideLaborComponent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await AddCostAsync(context, ProjectCostClass.SubcontractorLabor, 20_000m);
        await AddLaborCostAsync(context, 10_000m);

        var analysis = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{context.ProjectId}/cost-analysis");

        var factor = analysis.GetProperty("employerCostFactor").GetDecimal();

        Assert.Equal(20_000m,
            Component(analysis, ProjectCostClass.SubcontractorLabor, "actual"));

        Assert.Equal(
            decimal.Round(10_000m * factor, 2) + 20_000m,
            Component(analysis, ProjectCostClass.Labor, "actual"));

        // Toplam maliyette taşeron İKİ KEZ sayılmamalı.
        Assert.Equal(
            decimal.Round(10_000m * factor, 2) + 20_000m,
            analysis.GetProperty("totalCost").GetDecimal());
    }

    /// <summary>
    /// Kısımlı maliyetler kendi satırında, kısımsızlar "Genel" satırında
    /// toplanır.
    /// </summary>
    [Fact]
    public async Task Analysis_BreaksDownBySection()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await AddCostAsync(context, ProjectCostClass.Material, 30_000m, context.SectionId);
        await AddCostAsync(context, ProjectCostClass.Material, 12_000m);

        var analysis = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{context.ProjectId}/cost-analysis");

        var sections = analysis.GetProperty("sections").EnumerateArray().ToList();

        Assert.Equal(2, sections.Count);

        var named = sections.Single(x =>
            x.GetProperty("sectionName").GetString() == "Kolon Kablo");
        var general = sections.Single(x =>
            x.GetProperty("sectionName").GetString()!.StartsWith("Genel"));

        Assert.Equal(30_000m, named.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(12_000m, general.GetProperty("totalAmount").GetDecimal());
    }

    /// <summary>Aylık trend maliyeti ve geliri aynı eksende verir.</summary>
    [Fact]
    public async Task Analysis_BuildsMonthlyTrend()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await AddCostAsync(context, ProjectCostClass.Material, 10_000m,
            date: new DateTime(2026, 2, 10));
        await AddCostAsync(context, ProjectCostClass.Overhead, 4_000m,
            date: new DateTime(2026, 3, 5));

        var analysis = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{context.ProjectId}/cost-analysis");

        var monthly = analysis.GetProperty("monthly").EnumerateArray().ToList();

        Assert.Equal(2, monthly.Count);
        Assert.Equal("02.2026", monthly[0].GetProperty("label").GetString());
        Assert.Equal(10_000m, monthly[0].GetProperty("totalAmount").GetDecimal());
        Assert.Equal(0m, monthly[0].GetProperty("revenueAmount").GetDecimal());

        Assert.Equal("03.2026", monthly[1].GetProperty("label").GetString());
        Assert.Equal(4_000m, monthly[1].GetProperty("totalAmount").GetDecimal());
        Assert.Equal(100_000m, monthly[1].GetProperty("revenueAmount").GetDecimal());
    }

    /// <summary>
    /// Kâr = gelir − sınıflı maliyet toplamı.
    /// </summary>
    [Fact]
    public async Task Analysis_ComputesProfitFromRevenueAndCost()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await AddCostAsync(context, ProjectCostClass.Material, 40_000m);
        await AddCostAsync(context, ProjectCostClass.Overhead, 20_000m);

        var analysis = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{context.ProjectId}/cost-analysis");

        Assert.Equal(60_000m, analysis.GetProperty("totalCost").GetDecimal());
        Assert.Equal(40_000m, analysis.GetProperty("profit").GetDecimal());
        Assert.Equal(40m, analysis.GetProperty("profitMarginPercent").GetDecimal());
    }

    /// <summary>
    /// Kârlılık ucu arayüzde çağrılıyordu ama backend'de yoktu; artık
    /// analizle AYNI kaynaktan besleniyor ki iki farklı maliyet rakamı
    /// oluşmasın.
    /// </summary>
    [Fact]
    public async Task ProfitabilityEndpoint_MatchesAnalysis()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await AddCostAsync(context, ProjectCostClass.Material, 25_000m);
        await AddCostAsync(context, ProjectCostClass.SubcontractorLabor, 15_000m);

        var profitability = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{context.ProjectId}/profitability");

        Assert.Equal(100_000m, profitability.GetProperty("revenue").GetDecimal());
        Assert.Equal(25_000m, profitability.GetProperty("materialCost").GetDecimal());
        Assert.Equal(15_000m, profitability.GetProperty("subcontractorCost").GetDecimal());
        // Taşeron işçilik satırına ayrıldığı için işçilik sıfır kalmalı.
        Assert.Equal(0m, profitability.GetProperty("laborCost").GetDecimal());
        Assert.Equal(40_000m, profitability.GetProperty("totalCost").GetDecimal());
        Assert.Equal(60_000m, profitability.GetProperty("profit").GetDecimal());
    }

    /// <summary>
    /// İcmali olmayan projede öngörü uydurulmaz: sütunlar sıfır kalır ve
    /// eksik olduğu varsayım listesinde yazar.
    /// </summary>
    [Fact]
    public async Task Analysis_WithoutContractBaseline_LeavesForecastEmpty()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        Guid projectId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = await TestDataFactory.CreateProjectAsync(db, suffix);
            projectId = project.Id;
        }

        var analysis = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{projectId}/cost-analysis");

        Assert.False(analysis.GetProperty("hasContractBaseline").GetBoolean());
        Assert.Equal(0m, analysis.GetProperty("contractForecastTotal").GetDecimal());
        Assert.Contains(
            analysis.GetProperty("assumptions").EnumerateArray().Select(x => x.GetString()),
            x => x is not null && x.Contains("sözleşme referansı icmal yok"));
    }

    /// <summary>
    /// EK ÖDEME İZOLASYONU: yetkisi olmayan kullanıcı yalnız resmi
    /// işçiliği görür, elden ödeme payı alanı null döner.
    /// </summary>
    [Fact]
    public async Task Analysis_HidesExtraPaymentShareFromUnauthorisedUser()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.PersonnelExtraPayments.Add(new PersonnelExtraPayment
            {
                CompanyId = context.CompanyId,
                PersonnelId = context.PersonnelId,
                MonthlyAmount = 9_000m,
                EffectiveStartDate = DateTime.SpecifyKind(
                    new DateTime(2026, 1, 1), DateTimeKind.Utc)
            });

            db.AttendanceRecords.Add(new AttendanceRecord
            {
                CompanyId = context.CompanyId,
                PersonnelId = context.PersonnelId,
                ProjectId = context.ProjectId,
                WorkDate = DateTime.SpecifyKind(new DateTime(2026, 3, 10), DateTimeKind.Utc),
                Status = (int)AttendanceStatus.Worked,
                NormalHours = 8m,
                TotalHours = 8m,
                IsApproved = true
            });

            await db.SaveChangesAsync();
        }

        await AddLaborCostAsync(context, 10_000m);

        var authorized = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        // Teknik Ofis maliyeti ve kârı görür ama elden ödemeyi GÖRMEZ —
        // izolasyonun sınavı tam olarak bu rol.
        var restricted = await CreateClientForRoleAsync("Teknik Ofis");

        var authorizedAnalysis = await authorized.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{context.ProjectId}/cost-analysis");

        var restrictedResponse = await restricted.GetAsync(
            $"/api/projects/{context.ProjectId}/cost-analysis");

        Assert.Equal(HttpStatusCode.OK, restrictedResponse.StatusCode);

        var restrictedAnalysis =
            await restrictedResponse.Content.ReadFromJsonAsync<JsonElement>();

        // Yetkili: elden ödeme payı dolu ve toplama dahil.
        Assert.True(authorizedAnalysis.GetProperty("includesExtraPayments").GetBoolean());
        Assert.Equal(9_000m,
            authorizedAnalysis.GetProperty("extraPaymentLaborCost").GetDecimal());

        // Yetkisiz: alan null, işçilik yalnız resmi kısım.
        Assert.False(restrictedAnalysis.GetProperty("includesExtraPayments").GetBoolean());
        Assert.Equal(JsonValueKind.Null,
            restrictedAnalysis.GetProperty("extraPaymentLaborCost").ValueKind);

        Assert.True(
            Component(authorizedAnalysis, ProjectCostClass.Labor, "actual") >
            Component(restrictedAnalysis, ProjectCostClass.Labor, "actual"),
            "Yetkilinin gördüğü işçilik, elden ödeme kadar daha yüksek olmalı.");
    }

    /// <summary>
    /// Elden ödeme, personelin o ayki proje gün oranına göre dağıtılır:
    /// iki projede yarı yarıya çalışan personelin ödemesi tamamen tek
    /// projeye yüklenirse toplam maliyet gerçekte ödenenin katı çıkar.
    /// </summary>
    [Fact]
    public async Task ExtraPaymentShare_IsProratedAcrossProjects()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var otherProject = await TestDataFactory.CreateProjectAsync(db, $"{suffix}o");

            db.PersonnelExtraPayments.Add(new PersonnelExtraPayment
            {
                CompanyId = context.CompanyId,
                PersonnelId = context.PersonnelId,
                MonthlyAmount = 10_000m,
                EffectiveStartDate = DateTime.SpecifyKind(
                    new DateTime(2026, 1, 1), DateTimeKind.Utc)
            });

            // Mart: 2 gün bu projede, 2 gün başka projede.
            for (var day = 1; day <= 4; day++)
            {
                db.AttendanceRecords.Add(new AttendanceRecord
                {
                    CompanyId = context.CompanyId,
                    PersonnelId = context.PersonnelId,
                    ProjectId = day <= 2 ? context.ProjectId : otherProject.Id,
                    WorkDate = DateTime.SpecifyKind(
                        new DateTime(2026, 3, day), DateTimeKind.Utc),
                    Status = (int)AttendanceStatus.Worked,
                    NormalHours = 8m,
                    TotalHours = 8m,
                    IsApproved = true
                });
            }

            await db.SaveChangesAsync();
        }

        var analysis = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{context.ProjectId}/cost-analysis");

        Assert.Equal(5_000m, analysis.GetProperty("extraPaymentLaborCost").GetDecimal());
    }

    /// <summary>
    /// Kâr görünümüne vergi katmanı: vergi öncesi kâr → tahmini vergi →
    /// net kâr. Zararda vergi hesaplanmaz; eksi vergi üretmek yanıltıcı
    /// olurdu.
    /// </summary>
    [Fact]
    public async Task Analysis_AddsEstimatedTaxLayer()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        // Kurumlar vergisi oranı yıl bazlı ve varsayılanı yok; tahmin
        // katmanının çalışması için önce oran tanımlanır.
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.CompanyCorporateTaxRates.Add(new CompanyCorporateTaxRate
            {
                CompanyId = context.CompanyId,
                Year = DateTime.UtcNow.Year,
                Rate = 25m
            });

            await db.SaveChangesAsync();
        }

        // Gelir 100.000, maliyet 60.000 → vergi öncesi kâr 40.000.
        await AddCostAsync(context, ProjectCostClass.Material, 60_000m);

        var analysis = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{context.ProjectId}/cost-analysis");

        var rate = analysis.GetProperty("taxRate").GetDecimal();

        Assert.Equal(40_000m, analysis.GetProperty("profit").GetDecimal());
        Assert.Equal(
            decimal.Round(40_000m * rate / 100m, 2),
            analysis.GetProperty("estimatedTax").GetDecimal());
        Assert.Equal(
            40_000m - decimal.Round(40_000m * rate / 100m, 2),
            analysis.GetProperty("netProfitAfterTax").GetDecimal());

        Assert.Contains(
            analysis.GetProperty("assumptions").EnumerateArray().Select(x => x.GetString()),
            x => x is not null && x.Contains("Vergi yükü TAHMİNİDİR"));
    }

    /// <summary>Zararda tahmini vergi sıfırdır.</summary>
    [Fact]
    public async Task Analysis_WhenLoss_EstimatedTaxIsZero()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await AddCostAsync(context, ProjectCostClass.Material, 150_000m);

        var analysis = await client.GetFromJsonAsync<JsonElement>(
            $"/api/projects/{context.ProjectId}/cost-analysis");

        Assert.Equal(-50_000m, analysis.GetProperty("profit").GetDecimal());
        Assert.Equal(0m, analysis.GetProperty("estimatedTax").GetDecimal());
        Assert.Equal(-50_000m, analysis.GetProperty("netProfitAfterTax").GetDecimal());
    }

    /// <summary>
    /// Hızır brifingi maliyet aşımını bildirir ve eşiğin altındaki
    /// sapmada susar — her küçük sapmada uyarı verilse brifing gürültüye
    /// dönüşür ve gerçek aşım gözden kaçardı.
    /// </summary>
    [Fact]
    public async Task Briefing_ReportsOverrunAboveThresholdOnly()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        // %50 ilerlemede malzeme öngörüsü 50.000; 60.000 → %20 aşım.
        await AddCostAsync(context, ProjectCostClass.Material, 60_000m);
        // GG&K öngörüsü 20.000; 20.400 → %2 sapma, eşiğin altında.
        await AddCostAsync(context, ProjectCostClass.Overhead, 20_400m);

        using var scope = fixture.Factory.Services.CreateScope();

        var source = scope.ServiceProvider
            .GetServices<IHizirBriefingSource>()
            .Single(x => x.Key == "maliyet_asimi");

        var emptyIds = new HashSet<Guid>();

        var toolContext = new HizirToolContext(
            Guid.NewGuid(),
            "Test Kullanıcı",
            null,
            [],
            new HashSet<string> { PermissionCatalog.Keys.ProjectsView },
            new CurrentDataScopeSnapshot(
                true, emptyIds, emptyIds, emptyIds, emptyIds, emptyIds, emptyIds));

        var items = await source.BuildAsync(toolContext, CancellationToken.None);

        var costItems = items
            .Where(x => x.Title.Contains(context.ProjectId.ToString()) ||
                        x.TargetPath?.Contains(context.ProjectId.ToString()) == true)
            .ToList();

        Assert.Single(costItems);
        Assert.Contains("malzeme", costItems[0].Title);
        Assert.Contains("%20", costItems[0].Title);
    }

    /// <summary>
    /// Elden ödemeyi göremeyen bir rol için oturum açar. Maskelemenin
    /// role değil izne bağlı olduğunu doğrular.
    /// </summary>
    private async Task<HttpClient> CreateClientForRoleAsync(string roleName)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();

        const string password = "CostAnalysis!2026";
        var username = $"test-cost-{Guid.NewGuid():N}"[..40];
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
}
