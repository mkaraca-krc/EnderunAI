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
/// ÇEK DÜZENLEME ve İPTAL SINIRLARI.
///
/// Amaç: yanlış giriş için ASIL yol düzenleme olsun, iptal yalnız
/// gerçek iptaller için kalsın. İptal artık numarayı da serbest
/// bıraktığı için kapanmış bir çeki iptal etmek daha ağır bir işlem —
/// ayrı yetki ve ayrı neden listesi istiyor.
/// </summary>
[Collection("Integration")]
public sealed class ChequeEditAndVoidTests(DatabaseFixture fixture)
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

    private static object Payload(Scene scene, string number, decimal amount = 10_000m) =>
        new
        {
            companyId = scene.CompanyId,
            direction = (int)ChequeDirection.Received,
            chequeNumber = number,
            bankName = "Test Bankası",
            bankBranch = "Merkez",
            drawer = "Keşideci",
            currentAccountId = scene.CustomerId,
            projectId = scene.ProjectId,
            amount,
            currencyCode = "TRY",
            issueDate = DateTime.UtcNow.Date,
            dueDate = DateTime.UtcNow.Date.AddMonths(2)
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

    private static object EditPayload(
        Scene scene, string number, DateTime rowVersion, decimal amount = 10_000m,
        string? description = null) =>
        new
        {
            chequeNumber = number,
            bankName = "Test Bankası",
            bankBranch = "Merkez",
            drawer = "Keşideci",
            currentAccountId = scene.CustomerId,
            projectId = scene.ProjectId,
            amount,
            issueDate = DateTime.UtcNow.Date,
            dueDate = DateTime.UtcNow.Date.AddMonths(2),
            description,
            rowVersion
        };

    // ---------------------------------------------------------------
    // DÜZENLEME
    // ---------------------------------------------------------------

    /// <summary>
    /// PORTFÖYDEKİ ÇEK DÜZENLENEBİLİR ve detay yanıtı bunu söylüyor.
    /// Kural fazla geniş olmasın: "her şey kapalı" da bir hata olurdu.
    /// </summary>
    [Fact]
    public async Task PortfoydekiCek_Duzenlenebilir()
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
            EditPayload(scene, $"CK{suffix}", detail.GetProperty("rowVersion").GetDateTime(),
                description: "düzeltildi"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// İŞLEM GÖRMÜŞ ÇEKTE DÜZENLEME KAPALI — hem API reddediyor hem
    /// detay yanıtı `canEdit=false` diyor ve NEDENİ somut.
    ///
    /// UI düğmeyi bu bilgiye göre pasifleştiriyor; ikisi aynı metottan
    /// beslendiği için ayrışamıyorlar.
    /// </summary>
    [Fact]
    public async Task IslemGormusCek_DuzenlenemezVeNedeniSomut()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var id = await CreateAsync(client, Payload(scene, $"CK{suffix}"));

        // Bankaya tahsile ver → artık işlem görmüş sayılır.
        var moved = await client.PostAsJsonAsync($"/api/cheques/{id}/status", new
        {
            toStatus = (int)ChequeStatus.AtBank,
            movementDate = DateTime.UtcNow.Date,
            cashAccountId = scene.BankAccountId,
            description = "tahsile verildi"
        });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        var detail = await DetailAsync(client, id);

        Assert.False(detail.GetProperty("canEdit").GetBoolean());

        var reason = detail.GetProperty("editBlockedReason").GetString() ?? "";
        Assert.Contains("düzenlenemez", reason);
        Assert.Contains("İptal edip yeniden girin", reason);

        // API de reddetmeli — düğmeyi gizlemek yetmez.
        var response = await client.PutAsJsonAsync(
            $"/api/cheques/{id}",
            EditPayload(scene, $"CK{suffix}", detail.GetProperty("rowVersion").GetDateTime()));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// ROWVERSION ZORUNLU: damga yollanmazsa istek reddedilir.
    /// Opsiyonel olsaydı korumayı atlatmak için alanı göndermemek
    /// yeterdi.
    /// </summary>
    [Fact]
    public async Task RowVersionYoksa_IstekReddedilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var id = await CreateAsync(client, Payload(scene, $"CK{suffix}"));

        var response = await client.PutAsJsonAsync($"/api/cheques/{id}", new
        {
            chequeNumber = $"CK{suffix}",
            bankName = "Test Bankası",
            bankBranch = "Merkez",
            currentAccountId = scene.CustomerId,
            projectId = scene.ProjectId,
            amount = 10_000m,
            issueDate = DateTime.UtcNow.Date,
            dueDate = DateTime.UtcNow.Date.AddMonths(2)
            // rowVersion YOK
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("sayfayı yenileyin", body.GetProperty("message").GetString() ?? "",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// BAYAT DAMGA: başka biri araya girdiyse sessizce üzerine
    /// yazılmıyor.
    /// </summary>
    [Fact]
    public async Task BayatRowVersion_SessizceUzerineYazmaz()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var id = await CreateAsync(client, Payload(scene, $"CK{suffix}"));

        var stale = (await DetailAsync(client, id)).GetProperty("rowVersion").GetDateTime();

        // Araya giren kullanıcı.
        var first = await client.PutAsJsonAsync(
            $"/api/cheques/{id}",
            EditPayload(scene, $"CK{suffix}", stale, description: "ilk düzeltme"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Bayat damgayla ikinci istek.
        var second = await client.PutAsJsonAsync(
            $"/api/cheques/{id}",
            EditPayload(scene, $"CK{suffix}", stale, description: "ikinci düzeltme"));

        Assert.NotEqual(HttpStatusCode.OK, second.StatusCode);

        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("başka bir kullanıcı", body.GetProperty("message").GetString() ?? "");

        // Ve ilk düzeltme AYAKTA — ikincisi sessizce üzerine yazmadı.
        var detail = await DetailAsync(client, id);
        Assert.Equal("ilk düzeltme", detail.GetProperty("description").GetString());
    }

    /// <summary>
    /// TUTAR DEĞİŞİNCE MUHASEBE FİŞİ YENİDEN KESİLİYOR.
    ///
    /// Bayrak koymak "etkiledi" demek, düzeltmek değil: fiş
    /// güncellenmezse çek toplamı ile mizan sessizce ayrışır.
    /// Açıklamalar orijinal fiş numarasını referans veriyor ki altı ay
    /// sonra üç fişin hangisinin ne olduğu okunabilsin.
    /// </summary>
    [Fact]
    public async Task TutarDegisince_FisTersKaydedilipYenisiKesilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var id = await CreateAsync(client, Payload(scene, $"CK{suffix}", amount: 10_000m));

        var before = await DetailAsync(client, id);
        var rowVersion = before.GetProperty("rowVersion").GetDateTime();

        var response = await client.PutAsJsonAsync(
            $"/api/cheques/{id}",
            EditPayload(scene, $"CK{suffix}", rowVersion, amount: 15_000m));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Giriş hareketi ters kaydedilmiş ve yerine yenisi yazılmış olmalı.
        var movements = await db.ChequeMovements
            .AsNoTracking()
            .Where(x => x.ChequeId == id && x.FromStatus == null)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync();

        Assert.Equal(2, movements.Count);
        Assert.NotNull(movements[0].ReversalVoucherId);
        Assert.NotNull(movements[0].ReversedAtUtc);
        Assert.NotNull(movements[1].AccountingVoucherId);

        // AÇIKLAMALAR REFERANSLI: "hangi fişin yerine" okunabilmeli.
        Assert.Contains("yerine", movements[1].Description ?? "");

        // Denetim kaydı: tutar değişikliği muhasebeyi etkiliyor diye
        // işaretli olmalı — rapor bununla süzülüyor.
        var amountLog = await db.ChequeChangeLogs
            .AsNoTracking()
            .Where(x => x.ChequeId == id && x.FieldName == "Amount")
            .SingleAsync();

        Assert.True(amountLog.AffectsAccounting);
        Assert.Equal("10000.00", amountLog.OldValue);
        Assert.Equal("15000.00", amountLog.NewValue);
    }

    /// <summary>
    /// SADECE AÇIKLAMA DEĞİŞİRSE FİŞ YENİDEN KESİLMEZ.
    ///
    /// Her düzeltmede fiş kesilseydi defter, hiçbir mali sonucu olmayan
    /// düzeltmelerin ters kayıtlarıyla dolardı.
    /// </summary>
    [Fact]
    public async Task SadeceAciklamaDegisince_FisYenidenKesilmez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var id = await CreateAsync(client, Payload(scene, $"CK{suffix}"));

        var rowVersion = (await DetailAsync(client, id))
            .GetProperty("rowVersion").GetDateTime();

        await client.PutAsJsonAsync(
            $"/api/cheques/{id}",
            EditPayload(scene, $"CK{suffix}", rowVersion, description: "yalnız not"));

        var entryMovements = await db.ChequeMovements
            .AsNoTracking()
            .Where(x => x.ChequeId == id && x.FromStatus == null)
            .ToListAsync();

        Assert.Single(entryMovements);
        Assert.Null(entryMovements[0].ReversedAtUtc);
    }

    // ---------------------------------------------------------------
    // İPTAL SINIRI
    // ---------------------------------------------------------------

    /// <summary>
    /// PORTFÖYDEKİ ÇEK NORMAL YETKİYLE İPTAL EDİLEBİLİR — henüz para
    /// hareketi yok.
    /// </summary>
    [Fact]
    public async Task PortfoydekiCek_NormalYetkiyleIptalEdilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        // Çek görebilen ama "kapanmış iptal" yetkisi OLMAYAN rol.
        var client = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, suffix, ["Ön Muhasebe"]);

        var admin = await AdminAsync();
        var id = await CreateAsync(admin, Payload(scene, $"CK{suffix}"));

        var rowVersion = (await DetailAsync(admin, id))
            .GetProperty("rowVersion").GetDateTime();

        var response = await admin.PostAsJsonAsync($"/api/cheques/{id}/iptal", new
        {
            reason = "yanlış girildi",
            rowVersion,
            reasonKind = (int)ChequeVoidReason.DataEntryError
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// KAPANMIŞ ÇEKTE "YANLIŞ GİRİŞ" NEDENİ REDDEDİLİR.
    ///
    /// Tahsil edilmiş bir çek yanlış giriş nedeniyle iptal edilmez; o
    /// çek gerçekten tahsil edilmiştir. Yazım hatası varsa yol
    /// düzenlemedir. Seçenek ekranda gösterilmiyor ama API de kendi
    /// başına reddediyor — düğmeyi gizlemek yetmez.
    /// </summary>
    [Fact]
    public async Task KapanmisCekte_YanlisGirisNedeniReddedilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var id = await CreateAsync(client, Payload(scene, $"CK{suffix}"));

        await client.PostAsJsonAsync($"/api/cheques/{id}/status", new
        {
            toStatus = (int)ChequeStatus.AtBank,
            movementDate = DateTime.UtcNow.Date,
            cashAccountId = scene.BankAccountId,
            description = "tahsile verildi"
        });

        var rowVersion = (await DetailAsync(client, id))
            .GetProperty("rowVersion").GetDateTime();

        var response = await client.PostAsJsonAsync($"/api/cheques/{id}/iptal", new
        {
            reason = (string?)null,
            rowVersion,
            reasonKind = (int)ChequeVoidReason.DataEntryError
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("yanlış giriş", (body.GetProperty("message").GetString() ?? "").ToLowerInvariant());
    }

    /// <summary>
    /// İPTAL NEDENİ ZORUNLU — serbest metin nedenin yerine geçmiyor.
    /// </summary>
    [Fact]
    public async Task IptalNedeniSecilmezse_Reddedilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var id = await CreateAsync(client, Payload(scene, $"CK{suffix}"));

        var rowVersion = (await DetailAsync(client, id))
            .GetProperty("rowVersion").GetDateTime();

        var response = await client.PostAsJsonAsync($"/api/cheques/{id}/iptal", new
        {
            reason = "bir sebep yazdım ama neden seçmedim",
            rowVersion
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// "DİĞER" SEÇİLİRSE AÇIKLAMA ZORUNLU: yoksa neden sayılabilir
    /// görünür ama içi boş kalır.
    /// </summary>
    [Fact]
    public async Task DigerNedeniAciklamasiz_Reddedilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var id = await CreateAsync(client, Payload(scene, $"CK{suffix}"));

        var rowVersion = (await DetailAsync(client, id))
            .GetProperty("rowVersion").GetDateTime();

        var response = await client.PostAsJsonAsync($"/api/cheques/{id}/iptal", new
        {
            reason = (string?)null,
            rowVersion,
            reasonKind = (int)ChequeVoidReason.Other
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// KAPANMIŞ DURUMDAN İPTAL EDİLEN ÇEK İŞARETLENİYOR — listede
    /// rozetle ayrılabilsin, "bu para nereye gitti" sorusu tek bakışta
    /// cevaplanabilsin.
    /// </summary>
    [Fact]
    public async Task KapanmisIptal_Isaretleniyor()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await AdminAsync();
        var id = await CreateAsync(client, Payload(scene, $"CK{suffix}"));

        await client.PostAsJsonAsync($"/api/cheques/{id}/status", new
        {
            toStatus = (int)ChequeStatus.AtBank,
            movementDate = DateTime.UtcNow.Date,
            cashAccountId = scene.BankAccountId,
            description = "tahsile verildi"
        });

        var rowVersion = (await DetailAsync(client, id))
            .GetProperty("rowVersion").GetDateTime();

        var response = await client.PostAsJsonAsync($"/api/cheques/{id}/iptal", new
        {
            reason = "müşteri geri istedi",
            rowVersion,
            reasonKind = (int)ChequeVoidReason.ReturnedToParty
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = await DetailAsync(client, id);

        Assert.True(detail.GetProperty("voidedFromClosedState").GetBoolean());
        Assert.Equal("Müşteriye iade", detail.GetProperty("voidReasonName").GetString());
    }
}
