using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// PERSONEL ATAMASININ KAPSAM SÜZGECİ — DARALTILMIŞ KAPSAMLA ÖLÇÜLÜYOR.
///
/// ── BU DOSYA BİR BORCU KAPATIYOR ──
///
/// İŞEMRİ/2 Faz 1'in KAPI 2 raporunda dürüst sınır olarak şu yazıldı:
/// <c>PersonelAtanabilirMiAsync</c> personeli <c>IScopedData</c>
/// üzerinden okuyor ama SÜZGECİN ISIRDIĞI ÖLÇÜLMEDİ — çünkü
/// <c>tasks.manage</c> izni bugün yalnız Admin ve Genel Müdür'de, ikisi
/// de küresel kapsamlı. Yani bugünkü rol dağılımında süzgeç hiçbir zaman
/// devreye girmiyor.
///
/// "Bugün devreye girmiyor" ile "doğru çalışıyor" aynı şey değildir.
/// Rolün ne zaman daralacağını bekleyip test etmek, savunmayı o güne
/// kadar TESTSİZ bırakmak demekti — ve bu kod tabanının tekrar eden
/// yarası tam olarak testsiz savunma (<c>2d90c946</c>).
///
/// ── YÖNTEM: ROLÜ DEĞİL, KAPSAMI DARALT ──
///
/// Yeni bir rol uydurmak yerine <see cref="ICurrentDataScopeService"/>
/// doğrudan değiştiriliyor ve daraltılmış bir anlık görüntü
/// döndürülüyor. Böylece izin katmanına hiç dokunulmuyor: aynı Admin,
/// aynı uç, aynı istek — tek değişen, kullanıcının VERİ KAPSAMI.
///
/// Bu izolasyon ölçüldü: POST gövdesinde kapsam kullanan TEK yer
/// <c>PersonelAtanabilirMiAsync</c> (WorkTasksController:1134). Masraf
/// merkezi doğrulaması ham <c>db.ProjectSites</c> okuyor, kapsamdan
/// etkilenmiyor. Yani bu testlerde 400 dönerse sebebi başka bir kapı
/// olamaz.
///
/// ── DAR KAPSAM NE DEMEK ──
///
/// Şantiye kapsamlı bir kullanıcı (Şantiye Şefi, Formen) personeli
/// yalnızca AKTİF ŞANTİYE ATAMASI üzerinden görür
/// (<see cref="CurrentDataScopeSnapshot"/>.Apply). Burada şirket, şube
/// ve proje kümeleri boş bırakılıp yalnız tek bir şantiye veriliyor —
/// gerçek bir Formen'in kapsamının aynısı.
/// </summary>
[Collection("Integration")]
public sealed class PersonelKapsamSuzgeciTests(DatabaseFixture fixture)
{
    /// <summary>
    /// Kapsamı sabit bir anlık görüntüye çiviler. <c>null</c> vermek
    /// "kapsam çözülemedi" demektir — <see cref="ScopedData"/> orada
    /// fail-closed davranmak zorunda.
    /// </summary>
    private sealed class SabitKapsam(CurrentDataScopeSnapshot? kapsam)
        : ICurrentDataScopeService
    {
        public Task<CurrentDataScopeSnapshot?> GetAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(kapsam);
    }

    private static CurrentDataScopeSnapshot YalnizSantiye(Guid santiyeId) =>
        new(
            HasGlobalAccess: false,
            CompanyIds: new HashSet<Guid>(),
            BranchIds: new HashSet<Guid>(),
            ProjectIds: new HashSet<Guid>(),
            VisibleCompanyIds: new HashSet<Guid>(),
            VisibleBranchIds: new HashSet<Guid>(),
            SiteIds: new HashSet<Guid> { santiyeId });

    private static async Task<HttpClient> DarKapsamliIstemciAsync(
        DatabaseFixture fixture, CurrentDataScopeSnapshot? kapsam)
    {
        var fabrika = fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICurrentDataScopeService>();
                services.AddScoped<ICurrentDataScopeService>(
                    _ => new SabitKapsam(kapsam));
            }));

        var client = fabrika.CreateClient();

        // AuthHelper.CreateAuthorizedClientAsync somut fabrika tipini
        // istiyor; WithWebHostBuilder taban tipi döndürdüğü için giriş
        // burada elle yapılıyor. Kullanıcı ve şifre aynı.
        var token = await AuthHelper.LoginAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    /// <summary>
    /// Proje, şantiye ve iki personel kurar: biri şantiyeye ATANMIŞ
    /// (dar kapsamda görünür), biri atanmamış (görünmez).
    /// </summary>
    private static async Task<(Project Proje, ProjectSite Santiye,
        Personnel Gorunen, Personnel Gorunmeyen)>
        DuzenekAsync(DatabaseFixture fixture, string ek)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var proje = await TestDataFactory.CreateProjectAsync(db, ek);

        var santiye = new ProjectSite
        {
            ProjectId = proje.Id,
            Code = $"SNT-{ek}",
            Name = $"Test Şantiye {ek}"
        };
        db.ProjectSites.Add(santiye);
        await db.SaveChangesAsync();

        var gorunen = await TestDataFactory.CreatePersonnelAsync(
            db, proje.CompanyId, $"{ek}-GORUNEN");
        var gorunmeyen = await TestDataFactory.CreatePersonnelAsync(
            db, proje.CompanyId, $"{ek}-GORUNMEYEN");

        // Yalnız birine aktif şantiye ataması. Süzgecin tanımı bu:
        // IsActive && !IsDeleted && EndDate == null.
        db.ProjectSiteAssignments.Add(new ProjectSiteAssignment
        {
            PersonnelId = gorunen.Id,
            ProjectSiteId = santiye.Id,
            StartDate = DateTime.UtcNow.AddDays(-30),
            EndDate = null
        });
        await db.SaveChangesAsync();

        return (proje, santiye, gorunen, gorunmeyen);
    }

    private static Dictionary<string, object?> Govde(
        Project proje, string baslik, Guid personelId) =>
        new()
        {
            ["companyId"] = proje.CompanyId,
            ["projectId"] = proje.Id,
            ["title"] = baslik,
            ["priority"] = (int)WorkTaskPriority.Normal,
            ["kind"] = (int)WorkTaskKind.IsEmri,
            ["assignedToPersonnelId"] = personelId,
        };

    [Fact]
    public async Task DarKapsam_GorunmeyenPersonele_Atama_Reddedilir()
    {
        /*
         * ASIL İDDİA: personel VAR, AKTİF ve AYNI ŞİRKETTE. Tek eksiği,
         * isteği yapan kullanıcının kapsamında olmaması.
         *
         * Süzgeç atlanırsa (ör. `scoped.PersonnelAsync` yerine ham
         * `db.Personnel` yazılırsa) bu istek 200 döner ve kapsam
         * dışındaki bir personel göreve atanmış olur — üstelik adı
         * "Yapacak" slotunda görünerek.
         */
        var (proje, santiye, _, gorunmeyen) =
            await DuzenekAsync(fixture, "KAPSAM-RET");

        var client = await DarKapsamliIstemciAsync(
            fixture, YalnizSantiye(santiye.Id));

        var yanit = await client.PostAsJsonAsync(
            "/api/tasks",
            Govde(proje, "Kapsam dışı personele iş emri", gorunmeyen.Id));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Contains(
            "Seçilen personel bulunamadı",
            await yanit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DarKapsam_GorunenPersonele_Atama_Kabul_POZITIF_KONTROL()
    {
        /*
         * POZİTİF KONTROL — bu olmadan yukarıdaki test boştur.
         *
         * Dar kapsamda uç her personel atamasını reddetseydi (ör.
         * kapsam çözümü bozulup her sorgu boşalsaydı) negatif test yine
         * yeşil kalırdı. Aynı kapsam, aynı istek, tek fark: bu personel
         * şantiyeye ATANMIŞ.
         */
        var (proje, santiye, gorunen, _) =
            await DuzenekAsync(fixture, "KAPSAM-KABUL");

        var client = await DarKapsamliIstemciAsync(
            fixture, YalnizSantiye(santiye.Id));

        var yanit = await client.PostAsJsonAsync(
            "/api/tasks",
            Govde(proje, "Kapsam içi personele iş emri", gorunen.Id));

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
    }

    [Fact]
    public async Task Kapsam_Cozulemezse_HicbirPersonel_Atanamaz_FAIL_CLOSED()
    {
        /*
         * FAIL-CLOSED: kapsam çözülemediğinde `ScopedData` "kısıtlama
         * yok"a değil, HİÇBİR ŞEY görünmez'e düşer.
         *
         * Bu ayrı bir iddia: yukarıdaki iki test kapsam ÇÖZÜLDÜĞÜNDE
         * doğru süzdüğünü gösteriyor. Burada ölçülen, kapsamın hiç
         * çözülemediği anda kapının AÇILMADIĞI. `ScopedData`'nın
         * docstring'i bunu vaat ediyor; vaat artık test ediliyor.
         *
         * Şantiyeye ATANMIŞ personel seçiliyor ki reddin sebebi
         * "zaten görünmezdi" olmasın.
         */
        var (proje, _, gorunen, _) =
            await DuzenekAsync(fixture, "KAPSAM-YOK");

        var client = await DarKapsamliIstemciAsync(fixture, kapsam: null);

        var yanit = await client.PostAsJsonAsync(
            "/api/tasks",
            Govde(proje, "Kapsamsız bağlamda iş emri", gorunen.Id));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Contains(
            "Seçilen personel bulunamadı",
            await yanit.Content.ReadAsStringAsync());
    }
}
