using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Expenses;
using EnderunAI.Api.Services.Expenses;
using Microsoft.EntityFrameworkCore;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// PARA UÇLARI — KAPSAM SIZINTISI KAPALI MI.
///
/// PANODA SIZINTI SATIR DEĞİL, RAKAM OLARAK OLUR.
///
/// Liste ucunda sızıntıyı kullanıcı görür: tanımadığı bir kayıt
/// ekranda durur. Panoda öyle bir kayıt yok — yalnız TOPLAM var.
/// Başka şirketin cirosu o toplamın içine karışırsa hiç kimse
/// fark etmez; rakam sadece "beklediğimden büyük" görünür.
///
/// BU YÜZDEN BURADAKİ TESTLER SATIR KÜMESİNİ DEĞİL RAKAMI SINIYOR
/// ve sınama şu biçimde:
///
///   1. A şirketinin panosu okunur, rakamlar not edilir.
///   2. B şirketine veri EKLENİR.
///   3. A'nın panosu yeniden okunur — RAKAMLARIN HİÇBİRİ DEĞİŞMEMELİ.
///
/// "Toplam şu sayıya eşit" demek yetmezdi: veritabanında başka
/// testlerden kalan kayıtlar da var, sabit bir beklenen değer
/// kırılgan olurdu. DEĞİŞMEZLİK ise kesin: B'nin verisi süzgeçten
/// kaçarsa fark tam olarak eklenen tutar kadar olur.
///
/// HER RAKAM AYRI DOĞRULANIYOR — ciro, hakediş, kesinti, net ödenecek,
/// sözleşme, maliyet, kâr. Tek bir "toplam eşit mi" kontrolü
/// yapılsaydı, süzgeçten kaçan tek bir alan diğerleri doğru olduğu
/// için gözden kaçardı.
/// </summary>
[Collection("Integration")]
public sealed class FinanceScopeTests(DatabaseFixture fixture)
{
    private static readonly DateTime Gun =
        DateTime.SpecifyKind(new DateTime(2026, 3, 15), DateTimeKind.Utc);

    private sealed record Sahne(Project ProjeA, Project ProjeB);

    /*
     * ROL SEÇİMİ: "Admin" rol ADI tek başına global erişim veriyor
     * (CurrentDataScopeService). Kapsam testi o rolle koşarsa süzgeç
     * hiç çalışmaz ve test hiçbir şey kanıtlamaz. "Muhasebe Müdürü"
     * finans görme iznine sahip, global erişimi yok.
     */
    private const string ParaRolu = "Finans Sorumlusu";

    private static async Task<Sahne> KurAsync(AppDbContext db, string suffix)
    {
        var a = await TestDataFactory.CreateProjectAsync(db, $"FA{suffix}");
        var b = await TestDataFactory.CreateProjectAsync(db, $"FB{suffix}");
        return new Sahne(a, b);
    }

    private static async Task<ProgressPayment> HakedisEkleAsync(
        AppDbContext db, Project proje, string no, decimal tutar)
    {
        var hakedis = new ProgressPayment
        {
            CompanyId = proje.CompanyId,
            ProjectId = proje.Id,
            ProgressPaymentNumber = no,
            PeriodNumber = 1,
            ProgressPaymentDate = Gun,
            Status = ProgressPaymentStatus.Approved,
            ContractAmount = tutar * 4,
            CurrentAmount = tutar,
            CumulativeAmount = tutar,
            PriceDifferenceAmount = tutar / 10m,
            TotalDeductionAmount = tutar / 5m,
            NetPayableAmount = tutar - (tutar / 5m)
        };

        db.ProgressPayments.Add(hakedis);
        await db.SaveChangesAsync();
        return hakedis;
    }

    private static async Task MaliyetEkleAsync(
        AppDbContext db, Project proje, decimal tutar)
    {
        db.ProjectCostTransactions.Add(new ProjectCostTransaction
        {
            ProjectId = proje.Id,
            CostClass = ProjectCostClass.Material,
            CostType = ProjectCostType.Other,
            CostDate = Gun,
            Amount = tutar,
            Description = "Kapsam testi maliyeti"
        });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Gider kaydı maliyetin İKİNCİ yolu: proje maliyet defteri ve
    /// elle gider kaydı ayrık kümeler, ikisi de panonun gideri.
    /// İkisinden yalnız biri süzülse test yeşil kalırdı — bu yüzden
    /// her iki yol da ayrı ayrı besleniyor.
    /// </summary>
    private static async Task GiderEkleAsync(
        AppDbContext db, Project proje, decimal tutar)
    {
        await ExpenseCategoryProvisioner.EnsureAsync(
            db, proje.CompanyId, CancellationToken.None);

        var kategoriId = await db.ExpenseCategories
            .Where(x => x.CompanyId == proje.CompanyId &&
                        x.Code == ExpenseCategoryCatalog.Rent)
            .Select(x => x.Id)
            .SingleAsync();

        db.ExpenseEntries.Add(new ExpenseEntry
        {
            CompanyId = proje.CompanyId,
            CenterType = ExpenseCenterType.Project,
            ProjectId = proje.Id,
            ExpenseCategoryId = kategoriId,
            ExpenseDate = Gun,
            Amount = tutar,
            Description = "Kapsam testi gideri",
            PaymentMethod = ExpensePaymentMethod.Bank,
            DocumentType = ExpenseDocumentType.Invoice
        });

        await db.SaveChangesAsync();
    }

    private static decimal Sayi(JsonElement govde, string alan) =>
        govde.GetProperty(alan).GetDecimal();

    // ---------------------------------------------------------------
    // 1) /dashboard — sözleşme, hakediş, fiyat farkı, kesinti, net
    // ---------------------------------------------------------------

    [Fact]
    public async Task Pano_BSirketininRakamlariniToplamaKatmaz()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sahne = await KurAsync(db, suffix);

        await HakedisEkleAsync(db, sahne.ProjeA, $"HKA{suffix}", 100_000m);

        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, "finans-a", [ParaRolu], sahne.ProjeA.CompanyId);

        var once = await client.GetFromJsonAsync<JsonElement>(
            "/api/finance/dashboard");

        // B ŞİRKETİNE VERİ EKLENİYOR — A'nın panosu bunu görmemeli.
        await HakedisEkleAsync(db, sahne.ProjeB, $"HKB{suffix}", 777_000m);

        var sonra = await client.GetFromJsonAsync<JsonElement>(
            "/api/finance/dashboard");

        // HER RAKAM AYRI: biri süzgeçten kaçarsa diğerleri doğru
        // olduğu için gözden kaçardı.
        Assert.Equal(Sayi(once, "totalContractAmount"),
            Sayi(sonra, "totalContractAmount"));
        Assert.Equal(Sayi(once, "totalProgressPaymentAmount"),
            Sayi(sonra, "totalProgressPaymentAmount"));
        Assert.Equal(Sayi(once, "totalPriceDifferenceAmount"),
            Sayi(sonra, "totalPriceDifferenceAmount"));
        Assert.Equal(Sayi(once, "totalDeductionAmount"),
            Sayi(sonra, "totalDeductionAmount"));
        Assert.Equal(Sayi(once, "totalNetPayableAmount"),
            Sayi(sonra, "totalNetPayableAmount"));
        Assert.Equal(Sayi(once, "progressPaymentCount"),
            Sayi(sonra, "progressPaymentCount"));
        Assert.Equal(Sayi(once, "activeProjectCount"),
            Sayi(sonra, "activeProjectCount"));

        // SÜZGEÇ HER ŞEYİ SİLMİYOR: A'nın kendi hakedişi rakamda VAR.
        // Bu kontrol olmasaydı, sorgunun her zaman 0 döndürmesi de
        // testi geçerdi.
        Assert.True(Sayi(sonra, "totalProgressPaymentAmount") >= 100_000m);
    }

    // ---------------------------------------------------------------
    // 2) /financial-dashboard — ciro, gider, kâr
    // ---------------------------------------------------------------

    [Fact]
    public async Task FinansalPano_CiroGiderVeKarBSirketindenEtkilenmez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sahne = await KurAsync(db, suffix);

        await HakedisEkleAsync(db, sahne.ProjeA, $"FHA{suffix}", 50_000m);
        await MaliyetEkleAsync(db, sahne.ProjeA, 20_000m);
        await GiderEkleAsync(db, sahne.ProjeA, 5_000m);

        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, "finans-b", [ParaRolu], sahne.ProjeA.CompanyId);

        const string yol =
            "/api/finance/financial-dashboard?startDate=2026-01-01&endDate=2026-12-31";

        var once = (await client.GetFromJsonAsync<JsonElement>(yol))
            .GetProperty("summary");

        // B'ye hem CİRO hem MALİYET hem GİDER ekleniyor: üç ayrı
        // sorgu yolu var, üçü de ayrı ayrı sızabilir.
        await HakedisEkleAsync(db, sahne.ProjeB, $"FHB{suffix}", 900_000m);
        await MaliyetEkleAsync(db, sahne.ProjeB, 400_000m);
        await GiderEkleAsync(db, sahne.ProjeB, 60_000m);

        var sonra = (await client.GetFromJsonAsync<JsonElement>(yol))
            .GetProperty("summary");

        Assert.Equal(Sayi(once, "periodRevenue"), Sayi(sonra, "periodRevenue"));
        Assert.Equal(Sayi(once, "periodExpense"), Sayi(sonra, "periodExpense"));
        Assert.Equal(Sayi(once, "projectExpense"), Sayi(sonra, "projectExpense"));
        Assert.Equal(Sayi(once, "centralExpense"), Sayi(sonra, "centralExpense"));
        Assert.Equal(Sayi(once, "financingExpense"), Sayi(sonra, "financingExpense"));
        Assert.Equal(Sayi(once, "netProfit"), Sayi(sonra, "netProfit"));
        Assert.Equal(Sayi(once, "netLoss"), Sayi(sonra, "netLoss"));

        // Sorgu boş dönmüyor: A'nın kendi cirosu ve gideri sayılıyor.
        Assert.True(Sayi(sonra, "periodRevenue") >= 50_000m);
        Assert.True(Sayi(sonra, "projectExpense") >= 25_000m);
    }

    // ---------------------------------------------------------------
    // 3) /projects-summary ve /cari-summary
    // ---------------------------------------------------------------

    [Fact]
    public async Task ProjeOzeti_BSirketininProjesiniIcermez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sahne = await KurAsync(db, suffix);

        await HakedisEkleAsync(db, sahne.ProjeB, $"POB{suffix}", 123_000m);

        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, "finans-c", [ParaRolu], sahne.ProjeA.CompanyId);

        var yanit = await client.GetFromJsonAsync<JsonElement>(
            "/api/finance/projects-summary");

        var govde = yanit.GetRawText();

        Assert.Contains(sahne.ProjeA.Code, govde);
        Assert.DoesNotContain(sahne.ProjeB.Code, govde);
    }

    [Fact]
    public async Task CariOzeti_BSirketininCarileriniSaymaz()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sahne = await KurAsync(db, suffix);

        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, "finans-d", [ParaRolu], sahne.ProjeA.CompanyId);

        var once = await client.GetFromJsonAsync<JsonElement>(
            "/api/finance/cari-summary");

        // B şirketine yeni bir cari açılıyor.
        await TestDataFactory.CreateCompanyStackAsync(db, $"FC{suffix}");

        var sonra = await client.GetFromJsonAsync<JsonElement>(
            "/api/finance/cari-summary");

        Assert.Equal(Sayi(once, "accountCount"), Sayi(sonra, "accountCount"));
    }

    // ---------------------------------------------------------------
    // 4) DIŞA AKTARIM — liste ucundan AYRI kod, ayrı test
    // ---------------------------------------------------------------

    /// <summary>
    /// Excel ucu kendi sorgusunu kuruyor ve hakedişi doğrudan
    /// KİMLİKLE çekiyor. Liste ucunun süzülmesi burayı süzmez —
    /// bu yüzden ayrı test.
    /// </summary>
    [Fact]
    public async Task HakedisExcel_BSirketininHakedisiniIndirtmez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sahne = await KurAsync(db, suffix);

        var kendi = await HakedisEkleAsync(db, sahne.ProjeA, $"XA{suffix}", 10_000m);
        var yabanci = await HakedisEkleAsync(db, sahne.ProjeB, $"XB{suffix}", 10_000m);

        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, "finans-e", [ParaRolu], sahne.ProjeA.CompanyId);

        // Meşru erişim kapanmamalı.
        var kendiYanit = await client.GetAsync($"/api/hakedis-export/{kendi.Id}/excel");
        Assert.Equal(HttpStatusCode.OK, kendiYanit.StatusCode);

        var yabanciYanit = await client.GetAsync($"/api/hakedis-export/{yabanci.Id}/excel");
        Assert.Equal(HttpStatusCode.NotFound, yabanciYanit.StatusCode);

        // DOSYA GÖVDESİ SIZMIYOR: 404 yanıtı Excel içeriği taşımamalı.
        Assert.NotEqual(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            yabanciYanit.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// Liste ucu ile dışa aktarım ucu AYRI AYRI kontrol ediliyor:
    /// hakediş listesinde de B'nin kaydı görünmemeli.
    /// </summary>
    [Fact]
    public async Task HakedisListesi_BSirketininKaydiniGostermez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sahne = await KurAsync(db, suffix);

        await HakedisEkleAsync(db, sahne.ProjeA, $"LA{suffix}", 10_000m);
        await HakedisEkleAsync(db, sahne.ProjeB, $"LB{suffix}", 10_000m);

        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, "finans-f", [ParaRolu], sahne.ProjeA.CompanyId);

        var govde = await (await client.GetAsync("/api/progress-payments"))
            .Content.ReadAsStringAsync();

        Assert.Contains($"LA{suffix}", govde);
        Assert.DoesNotContain($"LB{suffix}", govde);
    }

    // ---------------------------------------------------------------
    // 5) Proje maliyet uçları — kimlik elle yazılırsa
    // ---------------------------------------------------------------

    [Fact]
    public async Task ProjeMaliyeti_BProjesininKimligiyleVeriDonmez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sahne = await KurAsync(db, suffix);

        await MaliyetEkleAsync(db, sahne.ProjeA, 1_000m);
        await MaliyetEkleAsync(db, sahne.ProjeB, 999_000m);

        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, "finans-g", [ParaRolu], sahne.ProjeA.CompanyId);

        var kendi = await client.GetAsync(
            $"/api/projects/{sahne.ProjeA.Id}/cost-transactions");
        Assert.Equal(HttpStatusCode.OK, kendi.StatusCode);

        var yabanci = await client.GetAsync(
            $"/api/projects/{sahne.ProjeB.Id}/cost-transactions");
        Assert.Equal(HttpStatusCode.NotFound, yabanci.StatusCode);

        var yabanciKirilim = await client.GetAsync(
            $"/api/projects/{sahne.ProjeB.Id}/cost-breakdown");
        Assert.Equal(HttpStatusCode.NotFound, yabanciKirilim.StatusCode);

        // Tutar sızmıyor.
        Assert.DoesNotContain("999000",
            await yabanci.Content.ReadAsStringAsync());
    }

    // ---------------------------------------------------------------
    // 6) Perakende — ciro ve fiyat listesi
    // ---------------------------------------------------------------

    [Fact]
    public async Task PerakendeKaynaklari_BSirketininKasaVeDeposunuVermez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sahne = await KurAsync(db, suffix);

        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, "finans-h", [ParaRolu], sahne.ProjeA.CompanyId);

        var yanit = await client.GetAsync("/api/perakende/kaynaklar");

        // İzin yoksa test yanlış şeyi ölçer; açıkça ayır.
        Assert.NotEqual(HttpStatusCode.Forbidden, yanit.StatusCode);

        var govde = await yanit.Content.ReadAsStringAsync();

        Assert.DoesNotContain(sahne.ProjeB.CompanyId.ToString(), govde);
    }

    // ---------------------------------------------------------------
    // 7) Global erişim kapanmamalı
    // ---------------------------------------------------------------

    [Fact]
    public async Task GlobalErisimliKullanici_HerIkiSirketiDeSayar()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sahne = await KurAsync(db, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var once = await client.GetFromJsonAsync<JsonElement>(
            "/api/finance/dashboard");

        await HakedisEkleAsync(db, sahne.ProjeB, $"GB{suffix}", 250_000m);

        var sonra = await client.GetFromJsonAsync<JsonElement>(
            "/api/finance/dashboard");

        // Koruma meşru erişimi kapatmamalı: global kullanıcıda rakam
        // ARTMALI. Bu test olmasaydı "her şeyi süz" de yeşil verirdi.
        Assert.Equal(
            Sayi(once, "totalProgressPaymentAmount") + 250_000m,
            Sayi(sonra, "totalProgressPaymentAmount"));
    }

    // ---------------------------------------------------------------
    // 8) PERAKENDE RAPOR UÇLARI — liste ucundan AYRI kod
    // ---------------------------------------------------------------

    /// <summary>
    /// Rapor uçları `companyId`'yi ZORUNLU parametre olarak alıyor ve
    /// her sorguda `x.CompanyId == companyId` var. Bu bir kapsam
    /// süzgeci DEĞİL: değeri kullanıcı yazıyor. Kapsam olmadan A
    /// şirketinin kullanıcısı adres çubuğuna B'nin kimliğini yazarak
    /// B'nin gün sonu kasasını, personel satış performansını ve açık
    /// alacaklarını okuyabiliyordu.
    ///
    /// BU UÇLAR ÖN YÜZDE DÜĞMESİ OLMADIĞI İÇİN İLK TARAMADA GÖZDEN
    /// KAÇTI. "Arayüzden erişilebiliyor mu" yanlış ölçüt; doğru ölçüt
    /// "kimlik doğrulamış bir kullanıcı çağırabiliyor mu".
    /// </summary>
    /// <summary>
    /// Perakende satış kaydı — rapor rakamlarını beslemek için.
    /// TAMAMLANMIŞ ve peşin: gün sonu raporu yalnız bunları sayıyor.
    /// </summary>
    private static async Task<Warehouse> SatisEkleAsync(
        AppDbContext db, Project proje, string no, decimal tutar)
    {
        var depo = await db.Warehouses
            .FirstOrDefaultAsync(x => x.CompanyId == proje.CompanyId);

        if (depo is null)
        {
            depo = new Warehouse
            {
                CompanyId = proje.CompanyId,
                BranchId = proje.BranchId,
                Code = $"DEP-{no}",
                Name = $"Depo {no}",
                Type = WarehouseType.Central
            };
            db.Warehouses.Add(depo);
            await db.SaveChangesAsync();
        }

        db.RetailSales.Add(new RetailSale
        {
            CompanyId = proje.CompanyId,
            WarehouseId = depo.Id,
            DocumentNumber = no,
            SaleDate = Gun,
            PaymentMethod = RetailPaymentMethod.Cash,
            Status = RetailSaleStatus.Completed,
            Subtotal = tutar,
            GrandTotal = tutar,
            RecordedAmount = tutar,
            IsReturn = false
        });

        await db.SaveChangesAsync();
        return depo;
    }

    /// <summary>
    /// Rapor uçları `companyId`'yi ZORUNLU parametre olarak alıyor ve
    /// her sorguda `x.CompanyId == companyId` var. Bu bir kapsam
    /// süzgeci DEĞİL: değeri kullanıcı yazıyor. Kapsam olmadan A
    /// şirketinin kullanıcısı adres çubuğuna B'nin kimliğini yazarak
    /// B'nin GÜN SONU KASASINI ve satış performansını okuyabiliyordu.
    ///
    /// BU UÇLAR ÖN YÜZDE DÜĞMESİ OLMADIĞI İÇİN İLK TARAMADA GÖZDEN
    /// KAÇTI. "Arayüzden erişilebiliyor mu" yanlış ölçüt; doğru ölçüt
    /// "kimlik doğrulamış bir kullanıcı çağırabiliyor mu".
    ///
    /// TESTİN İLK HALİ HİÇBİR ŞEY KANITLAMIYORDU: B şirketine veri
    /// eklenmediği için rapor zaten boş dönüyordu ve süzgeç
    /// kaldırıldığında da yeşil kalıyordu (sonda ile yakalandı).
    /// Şimdi B'ye GERÇEK satış yazılıyor ve RAKAMIN sıfır kaldığı
    /// doğrulanıyor.
    /// </summary>
    [Fact]
    public async Task PerakendeGunSonu_BSirketininKasasiniGostermez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sahne = await KurAsync(db, suffix);

        await SatisEkleAsync(db, sahne.ProjeA, $"SA{suffix}", 1_000m);
        await SatisEkleAsync(db, sahne.ProjeB, $"SB{suffix}", 500_000m);

        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, "perakende-gunsonu", [ParaRolu], sahne.ProjeA.CompanyId);

        var tarih = Gun.ToString("yyyy-MM-dd");

        // Kendi şirketi: koruma meşru erişimi kapatmamalı — SAYIM 1.
        var kendi = await client.GetFromJsonAsync<JsonElement>(
            $"/api/perakende/raporlar/gun-sonu" +
            $"?companyId={sahne.ProjeA.CompanyId}&date={tarih}");

        Assert.Equal(1, kendi.GetProperty("saleCount").GetInt32());

        // B'nin kimliği ELLE yazılıyor: B'de 1 satış VAR ama A'nın
        // kullanıcısı için sayım SIFIR olmalı.
        var yabanci = await client.GetFromJsonAsync<JsonElement>(
            $"/api/perakende/raporlar/gun-sonu" +
            $"?companyId={sahne.ProjeB.CompanyId}&date={tarih}");

        var teshis =
            $"A={sahne.ProjeA.CompanyId} B={sahne.ProjeB.CompanyId} " +
            $"ADetayi={await db.RetailSales.CountAsync(x => x.CompanyId == sahne.ProjeA.CompanyId && x.DocumentNumber == $"SA{suffix}")} " +
            $"BDetayi={await db.RetailSales.CountAsync(x => x.CompanyId == sahne.ProjeB.CompanyId && x.DocumentNumber == $"SB{suffix}")} " +
            $"gövde={yabanci.GetRawText()}";

        Assert.True(0 == yabanci.GetProperty("saleCount").GetInt32(), teshis);
        Assert.Equal(0m, yabanci.GetProperty("recordedTotal").GetDecimal());
    }

    [Fact]
    public async Task PerakendePersonelRaporu_BSirketininCirosunuGostermez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sahne = await KurAsync(db, suffix);

        await SatisEkleAsync(db, sahne.ProjeB, $"PB{suffix}", 500_000m);

        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, "perakende-personel", [ParaRolu], sahne.ProjeA.CompanyId);

        /*
         * TARİH ARALIĞI AÇIKÇA VERİLİYOR.
         *
         * İlk sürüm vermiyordu ve uç varsayılan olarak SON 30 GÜNE
         * bakıyordu; test verisi mart ayında olduğu için sorgu her
         * koşuda boş dönüyordu. Süzgeç kaldırıldığında da yeşil
         * kalıyordu — yani test hiçbir şey ölçmüyordu. Sonda yakaladı.
         */
        var yanit = await client.GetFromJsonAsync<JsonElement>(
            "/api/perakende/raporlar/personel" +
            $"?companyId={sahne.ProjeB.CompanyId}" +
            $"&from={Gun.AddDays(-1):yyyy-MM-dd}" +
            $"&to={Gun.AddDays(1):yyyy-MM-dd}");

        // Satır kümesi değil RAKAM: B'nin 500.000'i toplama girmemeli.
        var toplam = yanit.EnumerateArray()
            .Sum(x => x.GetProperty("total").GetDecimal());

        Assert.Equal(0m, toplam);

        // KARŞI KONTROL: aynı uç, B'nin KENDİ kullanıcısıyla çağrılınca
        // rakamı GÖRMELİ. Bu olmasaydı "uç her zaman boş dönüyor"
        // ihtimali testi geçirirdi — ilk sürümde tam olarak bu oldu.
        var bClient = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, "perakende-personel-b", [ParaRolu], sahne.ProjeB.CompanyId);

        var bYanit = await bClient.GetFromJsonAsync<JsonElement>(
            "/api/perakende/raporlar/personel" +
            $"?companyId={sahne.ProjeB.CompanyId}" +
            $"&from={Gun.AddDays(-1):yyyy-MM-dd}" +
            $"&to={Gun.AddDays(1):yyyy-MM-dd}");

        Assert.Equal(
            500_000m,
            bYanit.EnumerateArray().Sum(x => x.GetProperty("total").GetDecimal()));
    }
}