using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// EŞZAMANLI DEĞİŞİKLİK KORUMASI — ÇEKİN DURUMUNU DEĞİŞTİREN HER UÇTA.
///
/// Bir uçta eksik olması korumanın hiç olmaması demektir: iki kullanıcı
/// aynı çeke aynı anda işlem yaparsa biri diğerininkini görmeden
/// üzerine yazar ve çekte bu, aynı parayı iki kez işlemek anlamına
/// gelir.
///
/// BU TEST UÇ LİSTESİNİ SABİTLİYOR. Yeni bir durum değiştiren uç
/// eklenip damga istenmezse buraya bir satır eklenmediği sürece
/// fark edilmezdi; liste burada duruyor ki eksik göze batsın.
/// </summary>
[Collection("Integration")]
public sealed class ChequeConcurrencyGuardTests(DatabaseFixture fixture)
{
    private sealed record Scene(
        Guid CompanyId, Guid ProjectId, Guid CustomerId, Guid BankAccountId);

    private static async Task<Scene> BuildAsync(AppDbContext db, string suffix)
    {
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        foreach (var (code, name) in new[]
        {
            ("101", "Alınan Çekler"), ("101.01", "Portföy"),
            ("101.02", "Tahsildeki Çekler"), ("102", "Bankalar"),
            ("103", "Verilen Çekler"), ("120", "Alıcılar"), ("320", "Satıcılar")
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

        var bankAccountingId = await db.AccountingAccounts
            .Where(x => x.CompanyId == project.CompanyId && x.Code == "102")
            .Select(x => x.Id).SingleAsync();

        var bank = new CashAccount
        {
            CompanyId = project.CompanyId,
            Type = CashAccountType.Bank,
            Code = $"BNK-{suffix}",
            Name = $"Test Banka {suffix}",
            BankName = "Test Bankası",
            CurrencyCode = "TRY",
            OpeningBalance = 0m,
            AccountingAccountId = bankAccountingId
        };

        db.CashAccounts.Add(bank);
        await db.SaveChangesAsync();

        return new Scene(project.CompanyId, project.Id, customer.Id, bank.Id);
    }

    private Task<HttpClient> AdminAsync() =>
        AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    private static async Task<Guid> CreateAsync(
        HttpClient client, Scene scene, string number)
    {
        var response = await client.PostAsJsonAsync("/api/cheques", new
        {
            companyId = scene.CompanyId,
            direction = (int)ChequeDirection.Received,
            chequeNumber = number,
            bankName = "Test Bankası",
            bankBranch = "Kadıköy",
            drawer = "Keşideci",
            currentAccountId = scene.CustomerId,
            projectId = scene.ProjectId,
            amount = 10_000m,
            currencyCode = "TRY",
            issueDate = DateTime.UtcNow.Date,
            dueDate = DateTime.UtcNow.Date.AddMonths(1)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();
    }

    /// <summary>
    /// DAMGASIZ İSTEK HER UÇTA REDDEDİLİR.
    ///
    /// Damga opsiyonel olsaydı korumayı atlatmak için alanı hiç
    /// göndermemek yeterdi — yani koruma fiilen olmazdı.
    /// </summary>
    [Theory]
    [InlineData("status")]
    [InlineData("replace")]
    [InlineData("durum-geri-al")]
    [InlineData("iptal")]
    public async Task DamgasizIstek_Reddedilir(string yol)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var id = await CreateAsync(client, scene, $"EZ{suffix}");

        object body = yol switch
        {
            "status" => new
            {
                toStatus = (int)ChequeStatus.AtBank,
                movementDate = DateTime.UtcNow.Date,
                cashAccountId = scene.BankAccountId,
                description = "tahsile"
            },
            "replace" => new
            {
                chequeNumber = $"YN{suffix}",
                dueDate = DateTime.UtcNow.Date.AddMonths(3),
                movementDate = DateTime.UtcNow.Date
            },
            _ => new { reason = "gerekçe", reasonKind = (int)ChequeVoidReason.Other }
        };

        var response = await client.PostAsJsonAsync($"/api/cheques/{id}/{yol}", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var message = await response.Content.ReadAsStringAsync();
        Assert.Contains("Değişiklik damgası", message);
    }

    /// <summary>Dağılım ucu da damga istiyor (PUT).</summary>
    [Fact]
    public async Task Dagilim_DamgasizIstek_Reddedilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var id = await CreateAsync(client, scene, $"DG{suffix}");

        var response = await client.PutAsJsonAsync(
            $"/api/cheques/{id}/allocations",
            new { allocations = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Kırdırma ucu da damga istiyor.</summary>
    [Fact]
    public async Task Kirdirma_DamgasizIstek_Reddedilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var id = await CreateAsync(client, scene, $"KR{suffix}");

        var response = await client.PostAsJsonAsync("/api/factoring", new
        {
            chequeId = id,
            cashAccountId = scene.BankAccountId,
            factoringCurrentAccountId = (Guid?)null,
            projectId = scene.ProjectId,
            transactionDate = DateTime.UtcNow.Date,
            commissionRate = 2m,
            expenseAmount = 0m,
            description = "kırdırma"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// KAPANMIŞ DURUMDAN GERİ ALMA AYRI YETKİ İSTER.
    ///
    /// Tahsil edilmiş bir çeki geri almak, iptal etmek kadar ağır:
    /// gerçekleşmiş bir para hareketini storno ediyor. İptalde bu ayrım
    /// vardı, geri almada yoktu — yani aynı mali etki daha düşük bir
    /// yetkiyle üretilebiliyordu.
    ///
    /// ROL ELLE KURULUYOR: bugün `finance.approve` taşıyan tek rol
    /// (Finans Sorumlusu) `cheque.void-closed` de taşıyor. Yani ayrım
    /// hazır rollerde görünmüyor; özel rollerde görünüyor ve asıl
    /// koruma da orada. Hazır bir rolle sınansaydı test hiçbir şey
    /// kanıtlamazdı.
    /// </summary>
    [Fact]
    public async Task KapanmisDurumdanGeriAlma_YetkisizKullaniciyaKapali()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var admin = await AdminAsync();
        var id = await CreateAsync(admin, scene, $"YT{suffix}");

        // Çek TAHSİL EDİLİYOR: artık kapanmış bir durumda.
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostChequeAsync($"/api/cheques/{id}/status", id, new
            {
                toStatus = (int)ChequeStatus.Collected,
                movementDate = DateTime.UtcNow.Date,
                cashAccountId = scene.BankAccountId,
                description = "tahsil edildi"
            })).StatusCode);

        // finance.approve VAR, cheque.void-closed YOK.
        var roleName = $"Test Çek Onay {suffix}";

        var role = new AppRole
        {
            Name = roleName,
            Description = "Testte kurulan sınırlı çek onay rolü"
        };

        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var keys = new[]
        {
            PermissionCatalog.Keys.DashboardView,
            PermissionCatalog.Keys.FinanceView,
            PermissionCatalog.Keys.FinanceEdit,
            PermissionCatalog.Keys.FinanceApprove
        };

        var permissions = await db.Permissions
            .Where(x => keys.Contains(x.Key))
            .ToListAsync();

        db.RolePermissions.AddRange(permissions.Select(x => new RolePermission
        {
            RoleId = role.Id,
            PermissionId = x.Id
        }));

        await db.SaveChangesAsync();

        var limited = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, "cek-onay", [roleName]);

        var response = await limited.PostChequeAsync(
            $"/api/cheques/{id}/durum-geri-al", id,
            new { reason = "yanlış işaretlendi" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var message = await response.Content.ReadAsStringAsync();
        Assert.Contains("Kapanmış", message);

        // AYNI KULLANICI AÇIK DURUMDA GERİ ALABİLİR: kural "geri alma
        // yasak" değil, "kapanmış durumdan geri alma ayrı yetki".
        var open = await CreateAsync(admin, scene, $"AC{suffix}");

        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostChequeAsync($"/api/cheques/{open}/status", open, new
            {
                toStatus = (int)ChequeStatus.AtBank,
                movementDate = DateTime.UtcNow.Date,
                cashAccountId = scene.BankAccountId,
                description = "tahsile verildi"
            })).StatusCode);

        // AtBank da kapanmış sayılıyor; portföye dönmek için önce
        // yetkili kullanıcı geri alıyor, sonra sınırlı kullanıcı
        // portföydeki çekte deneniyor.
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostChequeAsync($"/api/cheques/{open}/durum-geri-al", open,
                new { reason = "yanlış banka" })).StatusCode);

        var onOpen = await limited.PostChequeAsync(
            $"/api/cheques/{open}/durum-geri-al", open,
            new { reason = "ilk kayıt geri alınamaz" });

        // Portföydeki çekte yetki engeli YOK; uç başka bir sebeple
        // (ilk kayıt geri alınamaz) reddediyor.
        Assert.NotEqual(HttpStatusCode.Forbidden, onOpen.StatusCode);
    }

    /// <summary>
    /// BAYAT DAMGA SESSİZCE ÜZERİNE YAZMAZ.
    ///
    /// Ekran çeki açtıktan sonra başkası işlem yaptıysa ikinci istek
    /// reddediliyor. İki kullanıcının aynı çeki arka arkaya işlemesi,
    /// çekte aynı parayı iki kez işlemek demek.
    /// </summary>
    [Fact]
    public async Task BayatDamga_IkinciDurumDegisikligiReddedilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var id = await CreateAsync(client, scene, $"BY{suffix}");

        // İki kullanıcı da ekranı AYNI anda açtı: ikisinin elinde de
        // bu damga var.
        var shared = await client.ChequeRowVersionAsync(id);

        var first = await client.PostAsJsonAsync($"/api/cheques/{id}/status", new
        {
            toStatus = (int)ChequeStatus.AtBank,
            movementDate = DateTime.UtcNow.Date,
            cashAccountId = scene.BankAccountId,
            description = "tahsile verildi",
            rowVersion = shared
        });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // İkinci kullanıcı aynı damgayla geliyor — arada durum değişti.
        var second = await client.PostAsJsonAsync($"/api/cheques/{id}/status", new
        {
            toStatus = (int)ChequeStatus.Collected,
            movementDate = DateTime.UtcNow.Date,
            cashAccountId = scene.BankAccountId,
            description = "tahsil edildi",
            rowVersion = shared
        });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains(
            "başka bir kullanıcı", await second.Content.ReadAsStringAsync());
    }
}
