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
/// ALAN SINIFININ UÇTAKİ DAVRANIŞI (ÇEK/2 · K1/K2/K3/K5).
///
/// Saf kural testleri (`ChequeAlanSinifiTests`) sınıflandırmanın
/// eksiksiz olduğunu gösteriyor; burası o sınıflandırmanın API'de
/// gerçekten uygulandığını gösteriyor. İkisi ayrı: kural doğru olup
/// servise bağlanmamış olabilir.
/// </summary>
[Collection("Integration")]
public sealed class ChequeAlanSinifiDavranisTests(DatabaseFixture fixture)
{
    private sealed record Scene(
        Guid CompanyId, Guid ProjectId, Guid CustomerId, Guid BankAccountId, Guid CashAccountId);

    private static async Task<Scene> BuildAsync(AppDbContext db, string suffix)
    {
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        foreach (var (code, name) in new[]
        {
            ("100", "Kasa"), ("101", "Alınan Çekler"), ("101.01", "Portföy"),
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

        async Task<Guid> HesapAsync(string kod) => await db.AccountingAccounts
            .Where(x => x.CompanyId == project.CompanyId && x.Code == kod)
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
            AccountingAccountId = await HesapAsync("102")
        };

        var kasa = new CashAccount
        {
            CompanyId = project.CompanyId,
            Type = CashAccountType.Cash,
            Code = $"KSA-{suffix}",
            Name = $"Test Kasa {suffix}",
            CurrencyCode = "TRY",
            OpeningBalance = 0m,
            AccountingAccountId = await HesapAsync("100")
        };

        db.CashAccounts.AddRange(bank, kasa);
        await db.SaveChangesAsync();

        return new Scene(project.CompanyId, project.Id, customer.Id, bank.Id, kasa.Id);
    }

    private Task<HttpClient> AdminAsync() =>
        AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    private static object Payload(
        Scene scene, string number, ChequeDirection direction = ChequeDirection.Received) =>
        new
        {
            companyId = scene.CompanyId,
            direction = (int)direction,
            chequeNumber = number,
            bankName = "Test Bankası",
            bankBranch = "Merkez",
            drawer = "Keşideci",
            currentAccountId = scene.CustomerId,
            projectId = scene.ProjectId,
            amount = 10_000m,
            currencyCode = "TRY",
            issueDate = DateTime.UtcNow.Date,
            dueDate = DateTime.UtcNow.Date.AddMonths(2)
        };

    /// <summary>Kayıtla BİREBİR aynı mali alanlar; yalnız tanımlayıcılar değişebilir.</summary>
    private static object EditPayload(
        Scene scene, string number, DateTime rowVersion,
        decimal amount = 10_000m,
        string? drawer = "Keşideci",
        string? bankBranch = "Merkez",
        string? description = null,
        string? editReason = "yazım hatası") =>
        new
        {
            chequeNumber = number,
            bankName = "Test Bankası",
            bankBranch,
            drawer,
            currentAccountId = scene.CustomerId,
            projectId = scene.ProjectId,
            amount,
            issueDate = DateTime.UtcNow.Date,
            dueDate = DateTime.UtcNow.Date.AddMonths(2),
            description,
            rowVersion,
            editReason
        };

    private static async Task<Guid> CreateAsync(HttpClient client, object payload)
    {
        var response = await client.PostAsJsonAsync("/api/cheques", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> DetailAsync(HttpClient client, Guid id) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/cheques/{id}");

    /// <summary>Çeki kapanmış duruma getirir (tahsil edildi).</summary>
    private static async Task KapatAsync(HttpClient client, Guid id, Guid bankAccountId)
    {
        var response = await client.PostChequeAsync($"/api/cheques/{id}/status", id, new
        {
            toStatus = (int)ChequeStatus.Collected,
            movementDate = DateTime.UtcNow.Date,
            cashAccountId = bankAccountId,
            description = "tahsil edildi"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════
    // K2 — TANIMLAYICI ALANLAR KAPANMIŞ ÇEKTE DE DÜZELTİLEBİLİR
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// PAKETİN ASIL SEBEBİ: kapanmış çekte keşideci yazım hatası
    /// düzeltilebiliyor ve bunun için mali kaydı iptal etmek
    /// gerekmiyor.
    /// </summary>
    [Fact]
    public async Task KapanmisCekte_KesideciDuzeltilebilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var id = await CreateAsync(client, Payload(scene, $"CK{suffix}"));
        await KapatAsync(client, id, scene.BankAccountId);

        var detail = await DetailAsync(client, id);
        Assert.False(detail.GetProperty("canEdit").GetBoolean());
        Assert.True(detail.GetProperty("canEditDescriptive").GetBoolean());

        var response = await client.PutAsJsonAsync(
            $"/api/cheques/{id}",
            EditPayload(scene, $"CK{suffix}",
                detail.GetProperty("rowVersion").GetDateTime(),
                drawer: "Doğru Keşideci",
                bankBranch: "Kızılay",
                description: "yazım hatası düzeltildi"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var sonra = await DetailAsync(client, id);
        Assert.Equal("Doğru Keşideci", sonra.GetProperty("drawer").GetString());
        Assert.Equal("Kızılay", sonra.GetProperty("bankBranch").GetString());
        Assert.Equal("yazım hatası düzeltildi", sonra.GetProperty("description").GetString());

        // DURUM DEĞİŞMEDİ: düzeltme çeki açmadı.
        Assert.Equal((int)ChequeStatus.Collected, sonra.GetProperty("status").GetInt32());
    }

    // ═══════════════════════════════════════════════════════════════
    // K1 — MALİ VE KİMLİK ALANLARI KİLİTLİ KALIR
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// SONDA HEDEFİ: kilit gerçekten çalışıyor mu. Kapanmış çekte
    /// tutar değiştirilemez ve red mesajı HANGİ alanı söyler.
    /// </summary>
    [Fact]
    public async Task KapanmisCekte_TutarDegistirilemez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var id = await CreateAsync(client, Payload(scene, $"CK{suffix}"));
        await KapatAsync(client, id, scene.BankAccountId);

        var detail = await DetailAsync(client, id);

        var response = await client.PutAsJsonAsync(
            $"/api/cheques/{id}",
            EditPayload(scene, $"CK{suffix}",
                detail.GetProperty("rowVersion").GetDateTime(), amount: 25_000m));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Tutar", await response.Content.ReadAsStringAsync());

        // TUTAR GERÇEKTEN DEĞİŞMEDİ — red mesajı yetmez.
        var sonra = await DetailAsync(client, id);
        Assert.Equal(10_000m, sonra.GetProperty("amount").GetDecimal());
    }

    /// <summary>
    /// ÇEK NUMARASI DA KİLİTLİ — kimlik alanı. Tanımlayıcı alanla
    /// birlikte gönderilse bile istek TÜMÜYLE reddediliyor: yarısı
    /// uygulanan bir düzeltme, kullanıcının gördüğüyle kaydın
    /// tutmadığı bir hâl bırakırdı.
    /// </summary>
    [Fact]
    public async Task KapanmisCekte_CekNumarasiDegistirilemez_VeTanimlayiciDaUygulanmaz()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var id = await CreateAsync(client, Payload(scene, $"CK{suffix}"));
        await KapatAsync(client, id, scene.BankAccountId);

        var detail = await DetailAsync(client, id);

        var response = await client.PutAsJsonAsync(
            $"/api/cheques/{id}",
            EditPayload(scene, $"BASKA{suffix}",
                detail.GetProperty("rowVersion").GetDateTime(),
                drawer: "Doğru Keşideci"));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);

        var sonra = await DetailAsync(client, id);
        Assert.Equal($"CK{suffix}", sonra.GetProperty("chequeNumber").GetString());
        Assert.Equal("Keşideci", sonra.GetProperty("drawer").GetString());
    }

    /// <summary>
    /// AÇIK ÇEKTE HER ŞEY ESKİSİ GİBİ: paket kapanmış çeki
    /// gevşetirken açık çekte bir şey kısmamalı.
    /// </summary>
    [Fact]
    public async Task AcikCekte_MaliAlanlarHalaDuzenlenebilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var id = await CreateAsync(client, Payload(scene, $"CK{suffix}"));

        var detail = await DetailAsync(client, id);
        Assert.True(detail.GetProperty("canEdit").GetBoolean());

        var response = await client.PutAsJsonAsync(
            $"/api/cheques/{id}",
            EditPayload(scene, $"CK{suffix}",
                detail.GetProperty("rowVersion").GetDateTime(), amount: 12_500m));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var sonra = await DetailAsync(client, id);
        Assert.Equal(12_500m, sonra.GetProperty("amount").GetDecimal());
    }

    // ═══════════════════════════════════════════════════════════════
    // K3 — HER TANIMLAYICI DÜZELTME AYRI DENETİM KAYDI BIRAKIR
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// KİM, NE ZAMAN, ESKİ DEĞER, YENİ DEĞER — üç alan için üç ayrı
    /// satır. Tek satırda toplanmış bir "düzeltildi" kaydı, hangi
    /// alanın ne olduğunu cevapsız bırakırdı.
    /// </summary>
    [Fact]
    public async Task TanimlayiciDuzeltme_AlanBazindaDenetimKaydiBirakir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var id = await CreateAsync(client, Payload(scene, $"CK{suffix}"));
        await KapatAsync(client, id, scene.BankAccountId);

        var detail = await DetailAsync(client, id);

        var response = await client.PutAsJsonAsync(
            $"/api/cheques/{id}",
            EditPayload(scene, $"CK{suffix}",
                detail.GetProperty("rowVersion").GetDateTime(),
                drawer: "Doğru Keşideci",
                bankBranch: "Kızılay",
                description: "not eklendi",
                editReason: "keşideci adı yanlış yazılmış"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var kayitlar = await db.ChequeChangeLogs
            .AsNoTracking()
            .Where(x => x.ChequeId == id)
            .ToListAsync();

        var alanlar = kayitlar.Select(x => x.FieldName).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "BankBranch", "Description", "Drawer" }, alanlar);

        var kesideci = kayitlar.Single(x => x.FieldName == "Drawer");
        Assert.Equal("Keşideci", kesideci.OldValue);
        Assert.Equal("Doğru Keşideci", kesideci.NewValue);
        Assert.Equal("Keşideci", kesideci.FieldLabel);
        Assert.Equal("keşideci adı yanlış yazılmış", kesideci.Reason);
        Assert.NotNull(kesideci.ChangedByUserId);
        Assert.False(kesideci.AffectsAccounting);
    }

    // ═══════════════════════════════════════════════════════════════
    // K5 — ROWVERSION TANIMLAYICI DÜZENLEMEDE DE ÇALIŞIR
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// YENİ AÇILAN YOL KORUMASIZ KALMASIN: bayat damgayla gelen
    /// tanımlayıcı düzeltme de reddediliyor.
    /// </summary>
    [Fact]
    public async Task TanimlayiciDuzenlemede_BayatDamgaReddedilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var id = await CreateAsync(client, Payload(scene, $"CK{suffix}"));
        await KapatAsync(client, id, scene.BankAccountId);

        var detail = await DetailAsync(client, id);
        var bayat = detail.GetProperty("rowVersion").GetDateTime();

        var ilk = await client.PutAsJsonAsync(
            $"/api/cheques/{id}",
            EditPayload(scene, $"CK{suffix}", bayat, drawer: "Birinci"));
        Assert.Equal(HttpStatusCode.OK, ilk.StatusCode);

        // AYNI damga ikinci kez: arada kayıt değişti.
        var ikinci = await client.PutAsJsonAsync(
            $"/api/cheques/{id}",
            EditPayload(scene, $"CK{suffix}", bayat, drawer: "İkinci"));

        Assert.NotEqual(HttpStatusCode.OK, ikinci.StatusCode);

        var sonra = await DetailAsync(client, id);
        Assert.Equal("Birinci", sonra.GetProperty("drawer").GetString());
    }

    /// <summary>
    /// DAMGA HİÇ YOLLANMAZSA da reddediliyor — tanımlayıcı yolda da
    /// korumayı atlatmanın yolu alanı göndermemek olmasın.
    /// </summary>
    [Fact]
    public async Task TanimlayiciDuzenlemede_DamgaZorunlu()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var id = await CreateAsync(client, Payload(scene, $"CK{suffix}"));
        await KapatAsync(client, id, scene.BankAccountId);

        var response = await client.PutAsJsonAsync($"/api/cheques/{id}", new
        {
            chequeNumber = $"CK{suffix}",
            bankName = "Test Bankası",
            bankBranch = "Merkez",
            drawer = "Damgasız",
            currentAccountId = scene.CustomerId,
            projectId = scene.ProjectId,
            amount = 10_000m,
            issueDate = DateTime.UtcNow.Date,
            dueDate = DateTime.UtcNow.Date.AddMonths(2)
        });

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════
    // İPTAL EDİLMİŞ ÇEK — MÜKERRER KONTROLÜNÜN TUZAĞI
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// İPTAL NUMARAYI SERBEST BIRAKIYOR; aynı numara yeniden
    /// kullanıldıktan sonra ESKİ (iptal) kaydın açıklaması hâlâ
    /// düzeltilebilmeli.
    ///
    /// Bu yol ÇEK/2'den önce hiç yürünemiyordu (kapanmış çekte her
    /// şey kapalıydı) ve açıldığında mükerrer kontrolüne takılıyordu:
    /// kontrol iptalleri eliyor, dolayısıyla iptal kaydın kendi
    /// numarası "başkasında" görünüyordu.
    /// </summary>
    [Fact]
    public async Task IptalEdilmisCekte_NumaraYenidenKullanilsaBile_AciklamaDuzeltilebilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var numara = $"CK{suffix}";

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var iptalEdilecek = await CreateAsync(client, Payload(scene, numara));

        var detay = await DetailAsync(client, iptalEdilecek);
        var iptal = await client.PostChequeAsync(
            $"/api/cheques/{iptalEdilecek}/iptal", iptalEdilecek, new
            {
                reason = "yanlış girildi",
                rowVersion = detay.GetProperty("rowVersion").GetDateTime(),
                reasonKind = (int)ChequeVoidReason.DataEntryError
            });
        Assert.Equal(HttpStatusCode.OK, iptal.StatusCode);

        // AYNI NUMARA yeniden kullanılıyor.
        await CreateAsync(client, Payload(scene, numara));

        var iptalDetay = await DetailAsync(client, iptalEdilecek);
        Assert.True(iptalDetay.GetProperty("canEditDescriptive").GetBoolean());

        var duzeltme = await client.PutAsJsonAsync(
            $"/api/cheques/{iptalEdilecek}",
            EditPayload(scene, numara,
                iptalDetay.GetProperty("rowVersion").GetDateTime(),
                description: "iptal notu düzeltildi"));

        Assert.Equal(HttpStatusCode.OK, duzeltme.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════
    // VERİLEN ÇEK KASADAN ÖDENMEZ
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// CANLIDAKİ İKİ YANLIŞ KAYDIN (VCK-2026-000020, 000022) tam
    /// olarak imkânsız kılındığı test.
    /// </summary>
    [Fact]
    public async Task VerilenCek_KasadanOdenemez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var id = await CreateAsync(
            client, Payload(scene, $"VC{suffix}", ChequeDirection.Issued));

        var response = await client.PostChequeAsync($"/api/cheques/{id}/status", id, new
        {
            toStatus = (int)ChequeStatus.Paid,
            movementDate = DateTime.UtcNow.Date,
            cashAccountId = scene.CashAccountId,
            description = "kasadan ödendi"
        });

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("kasadan ödenemez", await response.Content.ReadAsStringAsync());

        // ÇEK AÇIK KALDI — red gerçekten uygulandı.
        var sonra = await DetailAsync(client, id);
        Assert.Equal((int)ChequeStatus.Issued, sonra.GetProperty("status").GetInt32());
    }

    /// <summary>Banka hesabıyla aynı ödeme geçiyor — kural fazla geniş değil.</summary>
    [Fact]
    public async Task VerilenCek_BankadanOdenebilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var id = await CreateAsync(
            client, Payload(scene, $"VC{suffix}", ChequeDirection.Issued));

        var response = await client.PostChequeAsync($"/api/cheques/{id}/status", id, new
        {
            toStatus = (int)ChequeStatus.Paid,
            movementDate = DateTime.UtcNow.Date,
            cashAccountId = scene.BankAccountId,
            description = "bankadan ödendi"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// ALINAN ÇEK KASAYA TAHSİL EDİLEBİLİR — kural verilen çekle
    /// sınırlı; elden tahsil gerçek bir akış.
    /// </summary>
    [Fact]
    public async Task AlinanCek_KasayaTahsilEdilebilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var id = await CreateAsync(client, Payload(scene, $"CK{suffix}"));

        var response = await client.PostChequeAsync($"/api/cheques/{id}/status", id, new
        {
            toStatus = (int)ChequeStatus.Collected,
            movementDate = DateTime.UtcNow.Date,
            cashAccountId = scene.CashAccountId,
            description = "elden tahsil"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
