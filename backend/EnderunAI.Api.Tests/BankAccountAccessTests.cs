using System.Net;
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
/// BANKA HESABI ERİŞİMİ.
///
/// KAPATILAN ARIZA: bordro ekranı `bank-accounts` çağırıyordu ama
/// banka hesapları için HİÇBİR YOLDA GET ucu yoktu. Ekran 404
/// alıyor, "Gerçek Ödeme" hiç işaretlenemiyordu.
///
/// IBAN HASSAS VERİ: liste maskeli döner (son dört hane). Tam IBAN
/// ayrı uçtan, tek hesap için ve HER ÇAĞRI kayda düşerek gelir —
/// kayda IBAN'ın kendisi YAZILMAZ.
/// </summary>
[Collection("Integration")]
public sealed class BankAccountAccessTests(DatabaseFixture fixture)
{
    /// <summary>
    /// DEĞİŞMEZ: ödeme eylemini yapabilen her rol, ekranı da
    /// açabilmeli.
    ///
    /// NEDEN TEST: yeni bir izin anahtarı sessiz kırılmanın en kolay
    /// yoludur. `bank_account.view` bir role verilir ama o rol
    /// `payroll.view` taşımıyorsa anahtar İŞE YARAMAZ — kullanıcı
    /// ödeme ekranına hiç ulaşamaz ve kimse sebebini anlamaz.
    /// Kural akılda değil burada duruyor.
    ///
    /// Katalog üzerinden koşuyor, çalışma zamanında değil: canlıdaki
    /// grant'lar admin tarafından değiştirilebiliyor, ama katalog
    /// kurulumun ve yeniden kurulumun kaynağı.
    /// </summary>
    [Fact]
    public void BankAccountViewTasiyanHerRol_BordroEkraniniAcabilmeli()
    {
        var eksik = RoleCatalog.Roles
            .Where(rol => rol.PermissionKeys.Contains(
                PermissionCatalog.Keys.BankAccountView,
                StringComparer.OrdinalIgnoreCase))
            .Where(rol => !rol.PermissionKeys.Contains(
                PermissionCatalog.Keys.PayrollView,
                StringComparer.OrdinalIgnoreCase))
            .Select(rol => rol.Name)
            .ToList();

        Assert.True(
            eksik.Count == 0,
            "Bu roller `bank_account.view` taşıyor ama `payroll.view` " +
            "taşımıyor; ödeme ekranına ulaşamayacakları için anahtar " +
            "onlarda işe yaramaz:\n  " + string.Join("\n  ", eksik));
    }

    /// <summary>
    /// Ters yön: anahtarı taşıyan EN AZ BİR rol olmalı. Olmasaydı
    /// yukarıdaki test boş kümeyle yeşil kalır ve hiçbir şey ölçmezdi.
    /// </summary>
    [Fact]
    public void BankAccountView_EnAzBirRolde()
    {
        var tasiyanlar = RoleCatalog.Roles
            .Where(rol => rol.PermissionKeys.Contains(
                PermissionCatalog.Keys.BankAccountView,
                StringComparer.OrdinalIgnoreCase))
            .Select(rol => rol.Name)
            .ToList();

        Assert.True(
            tasiyanlar.Count > 0,
            "Hiçbir rol `bank_account.view` taşımıyor — uç kimseye " +
            "açık değil ve değişmez testi boş kümeyle yeşil kalıyor.");
    }

    /// <summary>
    /// UÇLAR [RequirePermission(bank_account.view)] TAŞIR.
    ///
    /// NEDEN AYRI TEST — KURAL 25: `Yetkisiz_ListeyiAlamaz` niteliği
    /// kaldırsan bile YEŞİL kalıyor, çünkü
    /// `PermissionAuthorizationMiddleware:61`'deki path-heuristiği
    /// ikinci bir bariyer olarak `/api/company-settings/*` yolunu
    /// zaten koruyor. İki bariyer aynı gözlemi (403) ürettiği için o
    /// test hangisinin çalıştığını KANITLAMIYOR.
    ///
    /// Tehlike gerçek: heuristik "Faz 2 tamamlandıkça daralacak"
    /// diye yazılmış geçici bir ağ. Daraldığı gün nitelik yoksa uç
    /// sessizce açılır. Bu test niteliği doğrudan gözlüyor.
    /// </summary>
    [Theory]
    [InlineData(nameof(EnderunAI.Api.Controllers.CompanySettingsController.GetBankAccounts))]
    [InlineData(nameof(EnderunAI.Api.Controllers.CompanySettingsController.GetBankAccountIban))]
    public void Uclar_BankAccountViewNiteligiTasir(string metotAdi)
    {
        var metot = typeof(EnderunAI.Api.Controllers.CompanySettingsController)
            .GetMethod(metotAdi);

        Assert.True(metot is not null, $"{metotAdi} bulunamadı.");

        var nitelikler = metot!
            .GetCustomAttributes(typeof(RequirePermissionAttribute), inherit: false)
            .Cast<RequirePermissionAttribute>()
            .Select(x => x.Permission)
            .ToList();

        Assert.Contains(
            PermissionCatalog.Keys.BankAccountView,
            nitelikler);
    }

    private static async Task<(Guid CompanyId, Guid AccountId)> HesapAsync(
        AppDbContext db, string suffix)
    {
        var proje = await TestDataFactory.CreateProjectAsync(db, $"BNK{suffix}");

        var hesap = new CompanyBankAccount
        {
            CompanyId = proje.CompanyId,
            BankName = $"Test Bank {suffix}",
            Iban = "TR330006100519786457841326",
            AccountHolder = "Test Şirketi",
            CurrencyCode = "TRY"
        };

        db.CompanyBankAccounts.Add(hesap);
        await db.SaveChangesAsync();

        return (proje.CompanyId, hesap.Id);
    }

    [Fact]
    public async Task Yetkili_MaskeliListeyiAlir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (companyId, _) = await HesapAsync(db, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.GetAsync(
            $"/api/company-settings/bank-accounts?companyId={companyId}");

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);

        var govde = await yanit.Content.ReadAsStringAsync();

        // MASKELİ: tam IBAN listede GEÇMEMELİ.
        Assert.DoesNotContain("TR330006100519786457841326", govde);
        Assert.Contains("1326", govde);
        Assert.Contains("ibanMasked", govde, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Yetkisiz_ListeyiAlamaz()
    {
        var client = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, "banka-yetkisiz", ["Araç Sorumlusu"]);

        var yanit = await client.GetAsync(
            $"/api/company-settings/bank-accounts?companyId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, yanit.StatusCode);
    }

    /// <summary>
    /// KAPSAM DIŞI ŞİRKETİN HESAPLARI GELMEZ.
    ///
    /// `companyId` istemciden geliyor; kapsam süzgeci olmasaydı
    /// kullanıcı başka şirketin kimliğini yazıp hesaplarını okurdu.
    ///
    /// İZİN TEKİL OLARAK VERİLİYOR, ROLLE DEĞİL. `bank_account.view`
    /// bugün yalnız Admin ve Genel Müdür'de ve o iki rol GLOBAL
    /// erişimli — yani rolle kurulan bir test kapsam süzgecini HİÇ
    /// çalıştırmaz ve hiçbir şey ölçmez. (İlk denememde tam bu oldu:
    /// Admin rolüyle kurulan "kapsam dışı" testi, süzgeç çalışmadığı
    /// için kırmızı verdi ve kusuru KODDA sandım.)
    ///
    /// Kapsamı sınırlı bir role izin TEKİL override ile veriliyor:
    /// böylece süzgeç gerçekten devreye giriyor.
    /// </summary>
    [Fact]
    public async Task KapsamDisiSirket_VeriDondurmez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (baskaSirket, _) = await HesapAsync(db, suffix);
        var kendiSirketi = await TestDataFactory.CreateProjectAsync(db, $"KPS{suffix}");

        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, "banka-kapsam", ["Araç Sorumlusu"], kendiSirketi.CompanyId);

        // İzni role değil KULLANICIYA ver: rol global olmasın.
        var kullanici = await db.Users
            .Where(x => x.Username.StartsWith("test-banka-kapsam"))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstAsync();

        var izin = await db.Permissions.SingleAsync(
            x => x.Key == PermissionCatalog.Keys.BankAccountView);

        db.UserPermissionOverrides.Add(new UserPermissionOverride
        {
            UserId = kullanici.Id,
            PermissionId = izin.Id,
            Effect = PermissionOverrideEffect.Allow
        });

        await db.SaveChangesAsync();

        // Override sonrası yeni oturum: izinler token'a giriyor.
        var yetkiliClient = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, "banka-kapsam2", ["Araç Sorumlusu"], kendiSirketi.CompanyId);

        var kullanici2 = await db.Users
            .Where(x => x.Username.StartsWith("test-banka-kapsam2"))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstAsync();

        db.UserPermissionOverrides.Add(new UserPermissionOverride
        {
            UserId = kullanici2.Id,
            PermissionId = izin.Id,
            Effect = PermissionOverrideEffect.Allow
        });

        await db.SaveChangesAsync();

        var yanit = await yetkiliClient.GetAsync(
            $"/api/company-settings/bank-accounts?companyId={baskaSirket}");

        // İzin verildiyse 200 + BOŞ liste; verilmediyse 403.
        // İkisi de "veri dönmedi" demek — ama hangisi olduğunu
        // ayırmak, testin ne ölçtüğünü bilmek için gerekli.
        if (yanit.StatusCode == HttpStatusCode.Forbidden)
        {
            Assert.Fail(
                "İzin override'ı token'a yansımadı; test kapsam " +
                "süzgecini ölçemiyor. Kurulum düzeltilmeli.");
        }

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);

        var liste = JsonDocument.Parse(
            await yanit.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(0, liste.GetArrayLength());
    }

    /// <summary>
    /// TAM IBAN KAYDA GEÇER — VE KAYDA IBAN YAZILMAZ.
    /// </summary>
    [Fact]
    public async Task TamIban_DenetimKaydiYazar_AmaIbaniKaydaYazmaz()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (_, hesapId) = await HesapAsync(db, suffix);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.GetAsync(
            $"/api/company-settings/bank-accounts/{hesapId}/iban");

        yanit.EnsureSuccessStatusCode();

        var govde = await yanit.Content.ReadAsStringAsync();
        Assert.Contains("TR330006100519786457841326", govde);

        var kayit = await db.SecurityAuditEvents
            .AsNoTracking()
            .Where(x => x.EntityId == hesapId && x.Action == "BankAccountIbanRevealed")
            .SingleOrDefaultAsync();

        Assert.True(kayit is not null, "Tam IBAN alındı ama denetim kaydı yazılmadı.");

        Assert.False(
            (kayit!.DetailsJson ?? string.Empty).Contains("TR3300061005"),
            "Denetim kaydına IBAN YAZILMIŞ — korunan veri ikinci bir yere kopyalanıyor.");
    }
}
