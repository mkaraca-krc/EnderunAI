using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// JETON/1 — İZİN VE ROL JETONDAN DEĞİL, KANONİK ÇÖZÜCÜDEN (2026-09-10).
///
/// ═══ ÖLÇÜLEN KUSUR ═══
///
/// `ICurrentUserService.HasPermission / IsInRole / Roles` izni JETONDAN
/// okuyordu. Jeton 12 saat yaşıyor ve arada tazelenmiyor; izni alınan
/// kullanıcı bu süre boyunca çek geri alma, sipariş işlemi, fatura GM
/// onayı, satın alma onay aşaması, KPI ve yorum erişimi yollarında ESKİ
/// izniyle çalışıyordu. Çağrılarak ölçüldü: `salary.view` rolden
/// silindi, aynı jetonla `auth/me` "yok" derken yönetim KPI ucu "Bordro
/// maliyeti"ni vermeye devam etti.
///
/// ═══ KURAL (Mehmet Karacabey) ═══
///
/// İzin ve rol kaynakta TEK yerden: istek başına kanonik çözücü. Çözücü
/// hata verirse, anlık görüntü yoksa, istek bağlamı yoksa → KAPALI.
/// Paylaşılan sonuç yalnız o HTTP isteğinin ömrü kadar yaşar.
/// </summary>
[Collection("Integration")]
public sealed class JetonTekKaynakTests(DatabaseFixture fixture)
{
    // ── Çek düzeneği: ChequeReversalTests'teki kurulumun aynısı ──

    private sealed record Baglam(Guid CompanyId, Guid ProjectId, Guid SupplierId, Guid BankAccountId);

    private async Task<Baglam> CekBaglamiAsync(string ek)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await TestDataFactory.CreateProjectAsync(db, ek);
        foreach (var (kod, ad) in new[] { ("102", "Bankalar"), ("103", "Verilen Çekler"), ("320", "Satıcılar"),
                     ("101", "Alınan Çekler"), ("101.01", "Portföy"), ("101.02", "Tahsildeki Çekler"),
                     ("120", "Alıcılar"), ("780.01.01", "Finansman Giderleri") })
        {
            db.AccountingAccounts.Add(new AccountingAccount
            {
                CompanyId = proje.CompanyId, Code = kod, Name = ad, Nature = AccountingAccountNature.Debit,
                Level = kod.Length > 3 ? 5 : 1, IsPostingAllowed = true
            });
        }
        var tedarikci = new CurrentAccount
        {
            CompanyId = proje.CompanyId, Code = $"TED-{ek}", Title = $"Tedarikçi {ek}",
            Roles = CurrentAccountRoles.Supplier, Status = CurrentAccountStatus.Approved
        };
        db.CurrentAccounts.Add(tedarikci);
        await db.SaveChangesAsync();
        var bankaHesabi = await db.AccountingAccounts
            .Where(x => x.CompanyId == proje.CompanyId && x.Code == "102").Select(x => x.Id).SingleAsync();
        var banka = new CashAccount
        {
            CompanyId = proje.CompanyId, Type = CashAccountType.Bank, Code = $"BNK-{ek}", Name = $"Banka {ek}",
            BankName = "Test Bankası", CurrencyCode = "TRY", OpeningBalance = 0m, AccountingAccountId = bankaHesabi
        };
        db.CashAccounts.Add(banka);
        await db.SaveChangesAsync();
        return new Baglam(proje.CompanyId, proje.Id, tedarikci.Id, banka.Id);
    }

    private async Task<Guid> OdenmisCekAsync(HttpClient yonetici, Baglam b)
    {
        var acildi = await yonetici.PostAsJsonAsync("/api/cheques", new
        {
            companyId = b.CompanyId, direction = (int)ChequeDirection.Issued,
            chequeNumber = $"CK{Guid.NewGuid():N}"[..10], bankName = "Test Bankası", bankBranch = "Merkez",
            drawer = "Test", currentAccountId = b.SupplierId, projectId = b.ProjectId, amount = 50_000m,
            currencyCode = "TRY", issueDate = DateTime.UtcNow.Date, dueDate = DateTime.UtcNow.Date.AddDays(30),
            progressPaymentId = (Guid?)null, supplierInvoiceId = (Guid?)null, description = "JETON/1"
        });
        Assert.Equal(HttpStatusCode.OK, acildi.StatusCode);
        var id = JsonDocument.Parse(await acildi.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        var odendi = await yonetici.PostChequeAsync($"/api/cheques/{id}/status", id, new
        {
            toStatus = (int)ChequeStatus.Paid, movementDate = DateTime.UtcNow.Date,
            cashAccountId = b.BankAccountId, description = "Ödendi"
        });
        Assert.Equal(HttpStatusCode.OK, odendi.StatusCode);
        return id;
    }

    private async Task<ChequeStatus> CekDurumuAsync(Guid id)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Cheques.AsNoTracking().Where(x => x.Id == id).Select(x => x.Status).SingleAsync();
    }

    // ── Kullanıcı: kendi rolü, verilen izinler ──

    private sealed record Kisi(HttpClient Istemci, string Jeton, Guid RolId, string KullaniciAdi);

    private async Task<Kisi> KisiAsync(params string[] izinler)
    {
        var ek = Guid.NewGuid().ToString("N")[..8];
        const string parola = "JetonTek!2026";
        Guid rolId;
        string ad = $"jeton1-{ek}";
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sifre = scope.ServiceProvider.GetRequiredService<PasswordService>();
            var rol = new AppRole { Name = $"Jeton1-{ek}" };
            db.Roles.Add(rol);
            await db.SaveChangesAsync();
            rolId = rol.Id;
            var ozet = sifre.Hash(parola);
            var u = new AppUser
            {
                Username = ad, FullName = "Jeton1 Test", PasswordHash = ozet.Hash, PasswordSalt = ozet.Salt,
                IsActive = true, WorkHoursExempt = true
            };
            db.Users.Add(u);
            await db.SaveChangesAsync();
            db.UserRoles.Add(new UserRole { UserId = u.Id, RoleId = rol.Id });
            db.UserDataScopes.Add(new UserDataScope { UserId = u.Id, ScopeType = DataScopeType.All });
            await db.SaveChangesAsync();
        }
        // İZİNLER ÜRÜNÜN KENDİ YOLUNDAN (Kural 81): izin matrisi toggle'ı
        // elle verilme kaydını da yazıyor. Doğrudan tabloya yazılan izni
        // ikinci bir host açılınca katalog uzlaştırması siliyordu.
        foreach (var izin in izinler)
            await ToggleAsync(rolId, izin, true);

        var istemci = fixture.Factory.CreateClient();
        var jeton = await AuthHelper.LoginAsync(istemci, ad, parola);
        istemci.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        return new Kisi(istemci, jeton, rolId, ad);
    }

    private async Task ToggleAsync(Guid rolId, string izin, bool ver)
    {
        var yonetici = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var cevap = await yonetici.PostAsJsonAsync("/api/user-management/permission-matrix/toggle",
            new { roleId = rolId, permissionKey = izin, granted = ver });
        Assert.True(cevap.IsSuccessStatusCode, $"toggle {izin}={ver}: {(int)cevap.StatusCode}");
    }

    private Task RoldenAlAsync(Guid rolId, string izin) => ToggleAsync(rolId, izin, false);

    private async Task<int> RolIzinSayisiAsync(Guid rolId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .RolePermissions.CountAsync(x => x.RoleId == rolId);
    }

    private static readonly string[] CekGeriAlan =
    [
        PermissionCatalog.Keys.FinanceView, PermissionCatalog.Keys.FinanceEdit,
        PermissionCatalog.Keys.FinanceApprove, PermissionCatalog.Keys.ChequeVoidClosed
    ];

    private static Task<HttpResponseMessage> GeriAl(HttpClient c, Guid cek) =>
        c.PostChequeAsync($"/api/cheques/{cek}/durum-geri-al", cek, new { reason = "JETON/1 sondası" });

    // ═══ T1 — KPI ═══

    /// <summary>
    /// KIRMIZIYA DÖNERSE: izni alınan kullanıcı jetonu ölene kadar (≤12 sa)
    /// bordro maliyeti gibi KPI'ları görmeye devam eder.
    /// </summary>
    [Fact]
    public async Task T1_Kpi_IzinAlininca_AyniJetonlaBordroKpisiKaybolur()
    {
        var k = await KisiAsync(PermissionCatalog.Keys.SalaryView, PermissionCatalog.Keys.DashboardView);
        var yol = $"/api/yonetim/kpi?companyId={Guid.NewGuid()}";

        Assert.Contains("payroll.cost", await (await k.Istemci.GetAsync(yol)).Content.ReadAsStringAsync());

        await RoldenAlAsync(k.RolId, PermissionCatalog.Keys.SalaryView);

        var sonra = await (await k.Istemci.GetAsync(yol)).Content.ReadAsStringAsync();
        Assert.DoesNotContain("payroll.cost", sonra);
    }

    // ═══ T2 — PARA YOLU: ÇEK GERİ ALMA ═══

    /// <summary>
    /// KIRMIZIYA DÖNERSE: "Çek — Kapanmış İptal" yetkisi alınan kullanıcı,
    /// jetonu ölene kadar ödenmiş çekleri geri alıp gerçekleşmiş para
    /// hareketini STORNO edebilir.
    /// </summary>
    [Fact]
    public async Task T2_CekGeriAlma_KapanmisYetkisiAlininca_AyniJetonlaReddedilir()
    {
        var b = await CekBaglamiAsync(Guid.NewGuid().ToString("N")[..8]);
        var yonetici = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var cek = await OdenmisCekAsync(yonetici, b);
        var k = await KisiAsync(CekGeriAlan);

        await RoldenAlAsync(k.RolId, PermissionCatalog.Keys.ChequeVoidClosed);

        var cevap = await GeriAl(k.Istemci, cek);
        Assert.Equal(HttpStatusCode.Forbidden, cevap.StatusCode);
        Assert.Equal(ChequeStatus.Paid, await CekDurumuAsync(cek));
    }

    // ═══ T3 — POZİTİF KONTROL ═══

    /// <summary>
    /// KIRMIZIYA DÖNERSE: yetkisi DURAN kullanıcı da geri alamıyor —
    /// T2'nin "reddedildi" iddiası, yolu tamamen kapatan bir hatadan da
    /// geçebilirdi. İkisi birlikte yazılmazsa "hiç çalışmayan" yol da yeşil.
    /// </summary>
    [Fact]
    public async Task T3_CekGeriAlma_YetkisiDuranKullanici_GeriAlabilir()
    {
        var b = await CekBaglamiAsync(Guid.NewGuid().ToString("N")[..8]);
        var yonetici = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var cek = await OdenmisCekAsync(yonetici, b);
        var k = await KisiAsync(CekGeriAlan);

        Assert.Equal(HttpStatusCode.OK, (await GeriAl(k.Istemci, cek)).StatusCode);
        Assert.Equal(ChequeStatus.Issued, await CekDurumuAsync(cek));
    }

    // ═══ T4 — KAPALI DÜŞ: ÇÖZÜCÜ HATA VERİRSE ═══

    private sealed class HataVerenCozucu : IUserAuthorizationService
    {
        public Task<UserAuthorizationSnapshot?> GetAsync(Guid userId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SABOTAJ: çözücü hata veriyor");
    }

    /// <summary>
    /// KIRMIZIYA DÖNERSE: çözücü çöktüğünde erişim AÇIK kalıyor — "emin
    /// değilim, izin vereyim" (fail-open). En pahalı yol açık düşer.
    /// </summary>
    [Fact]
    public async Task T4_CozucuHataVerirse_ParaYoluKapaliKalir()
    {
        var b = await CekBaglamiAsync(Guid.NewGuid().ToString("N")[..8]);
        var yonetici = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var cek = await OdenmisCekAsync(yonetici, b);
        var k = await KisiAsync(CekGeriAlan);

        // DÜZENEK (Kural 81): çekin damgası SAĞLAM sunucudan, sabote host
        // kurulmadan ÖNCE okunur; yalnız geri alma isteği sabote sunucuya
        // gider. İlk yazımda damga okuması sabote sunucuya gidip 500
        // alıyor ve sonda geri alma çağrısına HİÇ ulaşmıyordu.
        var damga = await k.Istemci.ChequeRowVersionAsync(cek);
        var oncesi = await RolIzinSayisiAsync(k.RolId);

        await using var sabote = fixture.Factory.WithWebHostBuilder(x => x.ConfigureTestServices(s =>
        {
            s.RemoveAll<IUserAuthorizationService>();
            s.AddScoped<IUserAuthorizationService, HataVerenCozucu>();
        }));
        var istemci = sabote.CreateClient(new() { AllowAutoRedirect = false });
        istemci.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", k.Jeton);

        // SAĞLIK: ikinci host kullanıcının iznine dokunmadı — reddin tek
        // sebebi sabotaj olmalı. İzin silinmişse "kapalı" sonucu hiçbir
        // şey kanıtlamaz (ölçüldü: doğrudan tabloya yazılan izin, ikinci
        // host açılınca katalog uzlaştırmasıyla 4 → 0 oluyordu).
        Assert.Equal(oncesi, await RolIzinSayisiAsync(k.RolId));

        HttpResponseMessage? cevap = null;
        try
        {
            cevap = await istemci.PostChequeAsync($"/api/cheques/{cek}/durum-geri-al", cek,
                new { reason = "JETON/1 sondası", rowVersion = damga });
        }
        catch (InvalidOperationException) { /* hata test sunucusundan fırladı: istek kapandı */ }

        Assert.True(cevap is null || !cevap.IsSuccessStatusCode,
            $"Çözücü çökerken para yolu AÇIK kaldı: {(int?)cevap?.StatusCode}");
        Assert.Equal(ChequeStatus.Paid, await CekDurumuAsync(cek));
    }

    // ═══ T5 — İSTEK İÇİ SONUÇ İSTEK DIŞINA TAŞMAZ ═══

    /// <summary>
    /// KIRMIZIYA DÖNERSE: bir isteğin izin sonucu başka bir isteğe (başka
    /// bir kullanıcıya) taşıyor — B, A'nın yetkisiyle para hareketi yapar.
    /// </summary>
    [Fact]
    public async Task T5_ArdisikIkiKullanici_IzinSonucuBirbirineTasmaz()
    {
        var b = await CekBaglamiAsync(Guid.NewGuid().ToString("N")[..8]);
        var yonetici = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var cek1 = await OdenmisCekAsync(yonetici, b);
        var cek2 = await OdenmisCekAsync(yonetici, b);
        var cek3 = await OdenmisCekAsync(yonetici, b);

        var a = await KisiAsync(CekGeriAlan);
        var bKisi = await KisiAsync(
            PermissionCatalog.Keys.FinanceView, PermissionCatalog.Keys.FinanceEdit, PermissionCatalog.Keys.FinanceApprove);

        Assert.Equal(HttpStatusCode.OK, (await GeriAl(a.Istemci, cek1)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GeriAl(bKisi.Istemci, cek2)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GeriAl(a.Istemci, cek3)).StatusCode);
        Assert.Equal(ChequeStatus.Paid, await CekDurumuAsync(cek2));
    }

    // ═══ T6 — JETONA GERİ DÜŞÜŞ YOK (birim) ═══

    /// <summary>
    /// KIRMIZIYA DÖNERSE: istekte kanonik anlık görüntü yokken servis
    /// JETONDAKİ izne geri düşüyor — eski kopya yeniden karar veriyor.
    /// </summary>
    [Fact]
    public void T6_AnlikGoruntuYoksa_JetondakiIzneDusmez_Kapali()
    {
        var kimlik = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "Admin"),
            .. JetonIzinKodlamasi.Yaz([PermissionCatalog.Keys.ChequeVoidClosed])
        ], "Test");
        var baglam = new DefaultHttpContext { User = new ClaimsPrincipal(kimlik) };
        var servis = new CurrentUserService(new HttpContextAccessor { HttpContext = baglam });

        Assert.False(servis.HasPermission(PermissionCatalog.Keys.ChequeVoidClosed));
        Assert.False(servis.IsInRole("Admin"));
        Assert.Empty(servis.Permissions);

        var baglamsiz = new CurrentUserService(new HttpContextAccessor { HttpContext = null });
        Assert.False(baglamsiz.HasPermission(PermissionCatalog.Keys.ChequeVoidClosed));
        Assert.False(baglamsiz.IsInRole("Admin"));
    }
}
