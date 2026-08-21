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
/// ÇEK MÜKERRER ENGELİ ve İPTAL SONRASI NUMARA SERBESTLİĞİ.
///
/// PAKETİN VARLIK SEBEBİ: yanlış girilen bir çek iptal edilip aynı
/// numarayla yeniden girilemiyordu. Eski kontrol `(şirket, yön, çek no)`
/// bakıyordu ve DURUM SÜZGECİ YOKTU — iptal edilmiş çek numarayı
/// kalıcı olarak bloke ediyordu.
///
/// Yeni anahtar: şirket + yön + banka + şube + normalize çek no,
/// yalnız iptal edilmemiş ve silinmemiş kayıtlar üzerinde.
/// </summary>
[Collection("Integration")]
public sealed class ChequeUniquenessTests(DatabaseFixture fixture)
{
    private sealed record Scene(
        Guid CompanyId, Guid OtherCompanyId, Guid CustomerId,
        Guid OtherCustomerId, Guid ProjectId, Guid OtherProjectId);

    private static async Task<Scene> BuildAsync(AppDbContext db, string suffix)
    {
        var project = await TestDataFactory.CreateProjectAsync(db, suffix);
        var other = await TestDataFactory.CreateProjectAsync(db, $"{suffix}x");

        foreach (var companyId in new[] { project.CompanyId, other.CompanyId })
        {
            foreach (var (code, name) in new[]
            {
                ("101", "Alınan Çekler"), ("101.01", "Portföy"),
                ("103", "Verilen Çekler"), ("120", "Alıcılar"), ("320", "Satıcılar")
            })
            {
                db.AccountingAccounts.Add(new AccountingAccount
                {
                    CompanyId = companyId,
                    Code = code,
                    Name = name,
                    Nature = AccountingAccountNature.Debit,
                    Level = code.Length > 3 ? 5 : 1,
                    IsPostingAllowed = true
                });
            }
        }

        var customer = new CurrentAccount
        {
            CompanyId = project.CompanyId,
            Code = $"MUS-{suffix}",
            Title = $"Test Müşteri {suffix}",
            Roles = CurrentAccountRoles.Customer | CurrentAccountRoles.Supplier,
            Status = CurrentAccountStatus.Approved
        };

        var otherCustomer = new CurrentAccount
        {
            CompanyId = other.CompanyId,
            Code = $"MUS-{suffix}x",
            Title = $"Diğer Müşteri {suffix}",
            Roles = CurrentAccountRoles.Customer | CurrentAccountRoles.Supplier,
            Status = CurrentAccountStatus.Approved
        };

        db.CurrentAccounts.AddRange(customer, otherCustomer);
        await db.SaveChangesAsync();

        return new Scene(
            project.CompanyId, other.CompanyId, customer.Id,
            otherCustomer.Id, project.Id, other.Id);
    }

    private Task<HttpClient> ClientAsync() =>
        AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

    private static object ChequePayload(
        Scene scene, string chequeNumber, string bank = "Test Bankası",
        string? branch = "Merkez", Guid? companyId = null,
        int direction = (int)ChequeDirection.Received) =>
        new
        {
            companyId = companyId ?? scene.CompanyId,
            direction,
            chequeNumber,
            bankName = bank,
            bankBranch = branch,
            drawer = "Keşideci",
            // Cari HER İKİ YÖNDE de zorunlu; proje de zorunlu
            // (masraf merkezi ya da proje olmadan çek kaydedilmiyor).
            currentAccountId = companyId == scene.OtherCompanyId
                ? scene.OtherCustomerId
                : scene.CustomerId,
            projectId = companyId == scene.OtherCompanyId
                ? scene.OtherProjectId
                : scene.ProjectId,
            amount = 10_000m,
            currencyCode = "TRY",
            issueDate = DateTime.UtcNow.Date,
            dueDate = DateTime.UtcNow.Date.AddMonths(2)
        };

    private static async Task<Guid> CreateAsync(
        HttpClient client, object payload)
    {
        var response = await client.PostAsJsonAsync("/api/cheques", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> VoidAsync(
        HttpClient client, Guid id, DateTime rowVersion,
        int reasonKind = (int)ChequeVoidReason.DataEntryError, string? reason = null) =>
        client.PostAsJsonAsync($"/api/cheques/{id}/iptal", new
        {
            reason,
            rowVersion,
            reasonKind
        });

    private static async Task<JsonElement> DetailAsync(HttpClient client, Guid id) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/cheques/{id}");

    // ---------------------------------------------------------------
    // ASIL SENARYO
    // ---------------------------------------------------------------

    /// <summary>
    /// AKTİF ÇEK VARKEN AYNI NUMARA ENGELLENİR — ve mesaj somut olur.
    ///
    /// "Zaten kayıtlı" tek başına kullanıcıyı hangi kayıtla çakıştığını
    /// aramaya gönderir; kayıt no, durum ve vade doğrudan söyleniyor.
    /// </summary>
    [Fact]
    public async Task AktifCekVarken_AyniNumaraEngellenir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ClientAsync();
        var number = $"CK{suffix}";

        await CreateAsync(client, ChequePayload(scene, number));

        var second = await client.PostAsJsonAsync(
            "/api/cheques", ChequePayload(scene, number));

        Assert.NotEqual(HttpStatusCode.OK, second.StatusCode);

        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        var message = body.GetProperty("message").GetString() ?? "";

        Assert.Contains("zaten kayıtlı", message);
        Assert.Contains("Kayıt No:", message);
        Assert.Contains("Durum:", message);
        Assert.Contains("Vade:", message);
    }

    /// <summary>
    /// PAKETİN VARLIK SEBEBİ: İPTALDEN SONRA AYNI NUMARA KAYDEDİLİR.
    ///
    /// Bildirilen hata buydu — yanlış girilip iptal edilen çek numarası
    /// bir daha kullanılamıyordu.
    /// </summary>
    [Fact]
    public async Task IptalSonrasi_AyniNumaraYenidenKaydedilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ClientAsync();
        var number = $"CK{suffix}";

        var first = await CreateAsync(client, ChequePayload(scene, number));

        var detail = await DetailAsync(client, first);
        var rowVersion = detail.GetProperty("rowVersion").GetDateTime();

        var voided = await VoidAsync(client, first, rowVersion);
        Assert.Equal(HttpStatusCode.OK, voided.StatusCode);

        // ASIL İDDİA: aynı numara yeniden kaydedilebilmeli.
        var again = await client.PostAsJsonAsync(
            "/api/cheques", ChequePayload(scene, number));

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    /// <summary>
    /// İPTALDEN SONRA İKİ KEZ AYNI NUMARA: ikincisi engellenir.
    ///
    /// Numara serbest kalması "sınırsız serbest" demek değil; yeni
    /// kayıt aktif olduğu anda kapı yeniden kapanmalı.
    /// </summary>
    [Fact]
    public async Task IptalSonrasi_IkinciTekrarEngellenir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ClientAsync();
        var number = $"CK{suffix}";

        var first = await CreateAsync(client, ChequePayload(scene, number));
        var detail = await DetailAsync(client, first);

        await VoidAsync(client, first, detail.GetProperty("rowVersion").GetDateTime());

        await CreateAsync(client, ChequePayload(scene, number));

        var third = await client.PostAsJsonAsync(
            "/api/cheques", ChequePayload(scene, number));

        Assert.NotEqual(HttpStatusCode.OK, third.StatusCode);
    }

    // ---------------------------------------------------------------
    // ANAHTAR SINIRLARI — kural fazla geniş olmamalı
    // ---------------------------------------------------------------

    [Fact]
    public async Task AyniNumara_FarkliBanka_Kaydedilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ClientAsync();
        var number = $"CK{suffix}";

        await CreateAsync(client, ChequePayload(scene, number, bank: "Ziraat"));

        var response = await client.PostAsJsonAsync(
            "/api/cheques", ChequePayload(scene, number, bank: "Vakıfbank"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AyniNumara_FarkliSube_Kaydedilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ClientAsync();
        var number = $"CK{suffix}";

        await CreateAsync(client, ChequePayload(scene, number, branch: "Kadıköy"));

        var response = await client.PostAsJsonAsync(
            "/api/cheques", ChequePayload(scene, number, branch: "Beşiktaş"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AyniNumara_FarkliSirket_Kaydedilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ClientAsync();
        var number = $"CK{suffix}";

        await CreateAsync(client, ChequePayload(scene, number));

        // Diğer şirkette cari yok; alınan çek yerine VERİLEN çek açıyoruz.
        var response = await client.PostAsJsonAsync(
            "/api/cheques",
            ChequePayload(scene, number, companyId: scene.OtherCompanyId,
                direction: (int)ChequeDirection.Issued));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// AYNI NUMARA FARKLI YÖN: alınan çek ile verilen çek aynı numarayı
    /// taşıyabilir — ikisi ayrı defterdir.
    /// </summary>
    [Fact]
    public async Task AyniNumara_FarkliYon_Kaydedilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ClientAsync();
        var number = $"CK{suffix}";

        await CreateAsync(client, ChequePayload(scene, number));

        var response = await client.PostAsJsonAsync(
            "/api/cheques",
            ChequePayload(scene, number, direction: (int)ChequeDirection.Issued));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------------------------------------------------------
    // NORMALİZASYON — uçtan uca
    // ---------------------------------------------------------------

    /// <summary>
    /// BOŞLUKLU/BOŞLUKSUZ AYNI ÇEKTİR. Canlıda aynı çekin iki kez
    /// girilmesinin en sık yolu buydu.
    /// </summary>
    [Fact]
    public async Task Normalizasyon_BosluklariYokSayar()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ClientAsync();

        await CreateAsync(client, ChequePayload(scene, $"CK {suffix}"));

        var response = await client.PostAsJsonAsync(
            "/api/cheques", ChequePayload(scene, $"CK{suffix}"));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// BAŞTAKİ SIFIR FARK YARATIR: "0012345" ile "12345" ayrı çeklerdir.
    /// </summary>
    [Fact]
    public async Task Normalizasyon_BastakiSifirFarkliCektir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ClientAsync();

        await CreateAsync(client, ChequePayload(scene, $"12345{suffix}"));

        var response = await client.PostAsJsonAsync(
            "/api/cheques", ChequePayload(scene, $"0012345{suffix}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------------------------------------------------------------
    // EŞZAMANLILIK
    // ---------------------------------------------------------------

    /// <summary>
    /// AYNI ÇEK İKİ EŞZAMANLI İSTEK: biri kaydeder, diğeri TEMİZ hata
    /// alır. 500 dönmemeli — ön kontrol ikisine de "yok" diyebilir,
    /// kısmi tekil indeks diyemez ve kaybeden istek anlaşılır bir
    /// mesaja çevrilir.
    /// </summary>
    [Fact]
    public async Task Eszamanli_IkiIstek_BiriKaydederDigeriTemizHataAlir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scene = await BuildAsync(db, suffix);

        var client = await ClientAsync();
        var payload = ChequePayload(scene, $"CK{suffix}");

        var first = client.PostAsJsonAsync("/api/cheques", payload);
        var second = client.PostAsJsonAsync("/api/cheques", payload);

        var responses = await Task.WhenAll(first, second);

        var ok = responses.Count(x => x.StatusCode == HttpStatusCode.OK);
        var failed = responses.Single(x => x.StatusCode != HttpStatusCode.OK);

        Assert.Equal(1, ok);

        // HAM 500 YOK: kullanıcı "beklenmeyen hata" değil, ne olduğunu
        // söyleyen bir mesaj görmeli.
        Assert.NotEqual(HttpStatusCode.InternalServerError, failed.StatusCode);

        var body = await failed.Content.ReadFromJsonAsync<JsonElement>();
        var message = body.GetProperty("message").GetString() ?? "";

        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.DoesNotContain("Exception", message);
    }
}
