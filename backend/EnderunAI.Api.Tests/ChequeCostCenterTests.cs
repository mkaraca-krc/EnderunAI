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
/// MERKEZ — MASRAF MERKEZİ OLARAK.
///
/// Şikâyet somuttu: "çekte proje seçerken Merkez görünmüyor, merkeze
/// çek işleyemiyorum." Sebep, seçilen şeyin aslında MASRAF MERKEZİ
/// olması ama ekranın yalnız PROJE sorması; merkez proje listesinde
/// olmadığı için hiç seçilemiyordu.
///
/// Buradaki sözler:
///  - merkez seçilebilir ve kaydedilir,
///  - muhasebe fişine MERKEZİN KODU gider (projeninki ya da şirket
///    kodu değil),
///  - liste merkeze göre süzülebilir — "merkezin çekleri" sorusu
///    cevaplanabilir olmalı,
///  - kapalı proje listede yok ama mevcut kayıttaki kapalı proje
///    geliyor, yoksa eski kayıt açılınca merkezini kaybederdi.
/// </summary>
[Collection("Integration")]
public sealed class ChequeCostCenterTests(DatabaseFixture fixture)
{
    private sealed record Scene(
        Guid CompanyId, Guid ProjectId, string ProjectCode,
        Guid CustomerId, string HeadOfficeCode);

    private static async Task<Scene> BuildAsync(AppDbContext db, string suffix)
    {
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        foreach (var (code, name) in new[]
        {
            ("101", "Alınan Çekler"), ("101.01", "Portföy"),
            ("102", "Bankalar"), ("103", "Verilen Çekler"),
            ("120", "Alıcılar"), ("320", "Satıcılar")
        })
        {
            db.AccountingAccounts.Add(new AccountingAccount
            {
                CompanyId = project.CompanyId,
                Code = code,
                Name = name,
                Nature = AccountingAccountNature.Debit,
                Level = code.Length > 3 ? 5 : 1,
                IsPostingAllowed = true
            });
        }

        var customer = new CurrentAccount
        {
            CompanyId = project.CompanyId,
            Code = $"MUS-{suffix}",
            Title = $"Test Müşteri {suffix}",
            Roles = CurrentAccountRoles.Customer | CurrentAccountRoles.Supplier,
            Status = CurrentAccountStatus.Approved
        };

        db.CurrentAccounts.Add(customer);
        await db.SaveChangesAsync();

        var branch = await db.Branches
            .SingleAsync(x => x.CompanyId == project.CompanyId && x.IsHeadOffice);

        return new Scene(
            project.CompanyId, project.Id, project.Code, customer.Id,
            branch.CostCenterCode ?? branch.Code);
    }

    private Task<HttpClient> AdminAsync() =>
        AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    private static object Payload(
        Scene scene, string number, Guid? projectId, string? costCenterCode) =>
        new
        {
            companyId = scene.CompanyId,
            direction = (int)ChequeDirection.Received,
            chequeNumber = number,
            bankName = "Test Bankası",
            bankBranch = "Kadıköy",
            drawer = "Keşideci",
            currentAccountId = scene.CustomerId,
            projectId,
            costCenterCode,
            amount = 25_000m,
            currencyCode = "TRY",
            issueDate = DateTime.UtcNow.Date,
            dueDate = DateTime.UtcNow.Date.AddMonths(1)
        };

    private static async Task<Guid> CreateAsync(HttpClient client, object payload)
    {
        var response = await client.PostAsJsonAsync("/api/cheques", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();
    }

    // ---------------------------------------------------------------
    // SEÇENEK LİSTESİ
    // ---------------------------------------------------------------

    /// <summary>
    /// MERKEZ LİSTEDE VE EN ÜSTTE. Sorunun kökü buydu: seçenek hiç
    /// yoktu. Sıra da önemli — listenin ortasında kaybolsaydı kullanıcı
    /// yine bulamazdı.
    /// </summary>
    [Fact]
    public async Task MasrafMerkezleri_MerkeziIlkSiradaDondurur()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();

        var options = await client.GetFromJsonAsync<JsonElement>(
            $"/api/masraf-merkezleri?companyId={scene.CompanyId}");

        var rows = options.EnumerateArray().ToList();

        Assert.NotEmpty(rows);
        Assert.Equal(0, rows[0].GetProperty("kind").GetInt32());
        Assert.Equal(scene.HeadOfficeCode, rows[0].GetProperty("code").GetString());

        // Proje de listede: merkez projelerin yerine geçmiyor, yanına
        // ekleniyor.
        Assert.Contains(rows, x =>
            x.GetProperty("kind").GetInt32() == 1 &&
            x.GetProperty("code").GetString() == scene.ProjectCode);
    }

    /// <summary>
    /// KAPALI PROJE LİSTEDE YOK — AMA MEVCUT KAYITTAKİ GELİYOR.
    /// İkisi birlikte olmalı: yalnız süzmek eski kaydı açan kullanıcıya
    /// boş masraf merkezi gösterirdi ve kaydeden kişi onu sessizce
    /// kaybederdi.
    /// </summary>
    [Fact]
    public async Task KapaliProje_ListedeYokAmaMevcutSecimKoruniyor()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var project = await db.Projects.SingleAsync(x => x.Id == scene.ProjectId);
        project.Status = ProjectStatus.Completed;
        await db.SaveChangesAsync();

        var client = await AdminAsync();

        var without = await client.GetFromJsonAsync<JsonElement>(
            $"/api/masraf-merkezleri?companyId={scene.CompanyId}");

        Assert.DoesNotContain(without.EnumerateArray(), x =>
            x.GetProperty("code").GetString() == scene.ProjectCode);

        var with = await client.GetFromJsonAsync<JsonElement>(
            $"/api/masraf-merkezleri?companyId={scene.CompanyId}" +
            $"&includeProjectId={scene.ProjectId}");

        var row = Assert.Single(
            with.EnumerateArray().Where(x =>
                x.GetProperty("code").GetString() == scene.ProjectCode));

        Assert.True(row.GetProperty("isClosed").GetBoolean());
    }

    // ---------------------------------------------------------------
    // MERKEZE ÇEK
    // ---------------------------------------------------------------

    /// <summary>
    /// MERKEZE ÇEK İŞLENİYOR ve fişe MERKEZİN KODU gidiyor.
    ///
    /// Kod kontrolü şart: masraf merkezi boş bırakılsaydı fiş şirket
    /// koduna düşerdi ve merkez gideri hiçbir kırılımda görünmezdi.
    /// </summary>
    [Fact]
    public async Task MerkezeCek_FisteMerkezKoduylaGorunur()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();

        var id = await CreateAsync(
            client, Payload(scene, $"MRK{suffix}", null, scene.HeadOfficeCode));

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/cheques/{id}");

        Assert.Equal(
            scene.HeadOfficeCode, detail.GetProperty("costCenterCode").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("projectId").ValueKind);

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var voucherId = detail.GetProperty("movements").EnumerateArray()
            .Select(x => x.GetProperty("accountingVoucherId"))
            .First(x => x.ValueKind != JsonValueKind.Null)
            .GetGuid();

        var lines = await verifyDb.AccountingVoucherLines
            .Where(x => x.AccountingVoucherId == voucherId)
            .ToListAsync();

        Assert.NotEmpty(lines);
        Assert.All(lines, line =>
            Assert.Equal(scene.HeadOfficeCode, line.CostCenterCode));
    }

    /// <summary>
    /// PROJEYE ÇEK: fişe PROJE KODU gidiyor. Merkez eklendi diye proje
    /// yolu bozulmasın — aynı alanın iki değeri karışabilirdi.
    /// </summary>
    [Fact]
    public async Task ProjeyeCek_FisteProjeKoduylaGorunur()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();

        var id = await CreateAsync(
            client, Payload(scene, $"PRJ{suffix}", scene.ProjectId, null));

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/cheques/{id}");

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var voucherId = detail.GetProperty("movements").EnumerateArray()
            .Select(x => x.GetProperty("accountingVoucherId"))
            .First(x => x.ValueKind != JsonValueKind.Null)
            .GetGuid();

        var lines = await verifyDb.AccountingVoucherLines
            .Where(x => x.AccountingVoucherId == voucherId)
            .ToListAsync();

        Assert.NotEmpty(lines);
        Assert.All(lines, line =>
            Assert.Equal(scene.ProjectCode, line.CostCenterCode));
    }

    /// <summary>
    /// MASRAF MERKEZİ ZORUNLU: ne proje ne merkez seçilmeden çek
    /// kaydedilemiyor. Boş geçilebilseydi gider hiçbir kırılıma
    /// düşmezdi ve rapor sessizce eksik kalırdı.
    /// </summary>
    [Fact]
    public async Task MasrafMerkezsizCek_Reddedilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();

        var response = await client.PostAsJsonAsync(
            "/api/cheques", Payload(scene, $"BOS{suffix}", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// MASRAF MERKEZİ DEĞİŞİNCE FİŞ YENİLENİR (kullanıcı kararı).
    ///
    /// Fişin masraf merkezi kırılımı çekin alanlarından çözülüyor.
    /// Yenilenmeseydi çek yeni merkezi gösterirken DEFTER eskisinde
    /// kalırdı — ve fark hiçbir raporda görünmezdi. Bu test, düzeltme
    /// sonrası defterde AKTİF (ters kaydedilmemiş) fişin YENİ kodu
    /// taşıdığını söylüyor.
    /// </summary>
    [Fact]
    public async Task MasrafMerkeziDegisince_FisYenilenirVeYeniKoduTasir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();

        // Önce MERKEZE işleniyor.
        var id = await CreateAsync(
            client, Payload(scene, $"TAS{suffix}", null, scene.HeadOfficeCode));

        var before = await client.GetFromJsonAsync<JsonElement>($"/api/cheques/{id}");

        // Sonra PROJEYE taşınıyor.
        var response = await client.PutAsJsonAsync($"/api/cheques/{id}", new
        {
            chequeNumber = $"TAS{suffix}",
            bankName = "Test Bankası",
            bankBranch = "Kadıköy",
            drawer = "Keşideci",
            currentAccountId = scene.CustomerId,
            projectId = scene.ProjectId,
            costCenterCode = (string?)null,
            amount = 25_000m,
            issueDate = DateTime.UtcNow.Date,
            dueDate = DateTime.UtcNow.Date.AddMonths(1),
            rowVersion = before.GetProperty("rowVersion").GetDateTime(),
            editReason = "yanlış masraf merkezine işlenmiş"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Giriş hareketi ters kaydedilmiş, yerine yenisi yazılmış olmalı.
        var entries = await verifyDb.ChequeMovements
            .AsNoTracking()
            .Where(x => x.ChequeId == id && x.FromStatus == null)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync();

        Assert.Equal(2, entries.Count);
        Assert.NotNull(entries[0].ReversedAtUtc);
        Assert.Null(entries[1].ReversedAtUtc);

        // AKTİF fişin satırları YENİ kodu taşıyor.
        var activeVoucherId = entries[1].AccountingVoucherId!.Value;

        var lines = await verifyDb.AccountingVoucherLines
            .AsNoTracking()
            .Where(x => x.AccountingVoucherId == activeVoucherId)
            .ToListAsync();

        Assert.NotEmpty(lines);
        Assert.All(lines, line =>
            Assert.Equal(scene.ProjectCode, line.CostCenterCode));

        // Denetim kaydı: masraf merkezi değişikliği "muhasebeyi
        // etkiler" diye işaretli — rapor bununla süzülüyor.
        var log = await verifyDb.ChequeChangeLogs
            .AsNoTracking()
            .Where(x => x.ChequeId == id && x.FieldName == "CostCenterCode")
            .SingleAsync();

        Assert.True(log.AffectsAccounting);
        Assert.Equal(scene.HeadOfficeCode, log.OldValue);
    }

    /// <summary>
    /// MASRAF MERKEZİ AYNIYSA FİŞ YENİLENMEZ. Aksi hâlde her açıklama
    /// düzeltmesi defteri iki fişle şişirirdi.
    /// </summary>
    [Fact]
    public async Task MasrafMerkeziAyniKalirsa_FisYenilenmez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();

        var id = await CreateAsync(
            client, Payload(scene, $"SBT{suffix}", null, scene.HeadOfficeCode));

        var before = await client.GetFromJsonAsync<JsonElement>($"/api/cheques/{id}");

        var response = await client.PutAsJsonAsync($"/api/cheques/{id}", new
        {
            chequeNumber = $"SBT{suffix}",
            bankName = "Test Bankası",
            bankBranch = "Kadıköy",
            drawer = "Keşideci",
            currentAccountId = scene.CustomerId,
            projectId = (Guid?)null,
            costCenterCode = scene.HeadOfficeCode,
            amount = 25_000m,
            issueDate = DateTime.UtcNow.Date,
            dueDate = DateTime.UtcNow.Date.AddMonths(1),
            description = "yalnız açıklama değişti",
            rowVersion = before.GetProperty("rowVersion").GetDateTime()
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var entries = await verifyDb.ChequeMovements
            .AsNoTracking()
            .Where(x => x.ChequeId == id && x.FromStatus == null)
            .ToListAsync();

        Assert.Single(entries);
        Assert.Null(entries[0].ReversedAtUtc);
    }

    // ---------------------------------------------------------------
    // LİSTE SÜZGECİ
    // ---------------------------------------------------------------

    /// <summary>
    /// "MERKEZİN ÇEKLERİ" CEVAPLANABİLİR. Liste yalnız projeye göre
    /// süzülüyordu; merkeze işlenen çekler hiçbir süzgeçle
    /// ayrılamıyordu — raporda "—" olarak görünüp atanmamış sanılıyordu.
    /// </summary>
    [Fact]
    public async Task MerkezSuzgeci_YalnizMerkezCeklerini_Getirir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();

        var centerId = await CreateAsync(
            client, Payload(scene, $"MRK{suffix}", null, scene.HeadOfficeCode));

        var projectId = await CreateAsync(
            client, Payload(scene, $"PRJ{suffix}", scene.ProjectId, null));

        var center = await client.GetFromJsonAsync<JsonElement>(
            $"/api/cheques?companyId={scene.CompanyId}" +
            $"&costCenterCode={scene.HeadOfficeCode}");

        var centerIds = center.EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();

        Assert.Contains(centerId, centerIds);
        Assert.DoesNotContain(projectId, centerIds);

        var byProject = await client.GetFromJsonAsync<JsonElement>(
            $"/api/cheques?companyId={scene.CompanyId}&projectId={scene.ProjectId}");

        var projectIds = byProject.EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();

        Assert.Contains(projectId, projectIds);
        Assert.DoesNotContain(centerId, projectIds);
    }
}
