using System.Net;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// ATAMA KAPISI — OLUŞTURMA YOLUNDA.
///
/// NEDEN VAR: `2d90c946` (MERKEZ/1) merkez kuralını ortak metoda
/// taşırken POST gövdesini METİN ARALIĞIYLA kesti ve aralıkta duran
/// ATAMA DOĞRULAMASINI da götürdü. 26 satır sessizce silindi ve canlıya
/// çıktı.
///
/// 2965 testin hiçbiri bunu yakalamadı çünkü SİLİNEN KOD TESTSİZDİ.
/// `YetimMuhafizTests` de görmedi: `GorevAtanabilirMiAsync` iki çağrı
/// yerinde daha yaşıyordu (delegate ve assignable-users), yani "yetim"
/// değildi — yalnız EN ÖNEMLİ çağıranını kaybetmişti.
///
/// Bu dosya o boşluğu kapatıyor. Testler DÜZELTMEDEN ÖNCE yazıldı ve
/// bugünkü canlı koda karşı KIRMIZI verdikleri gözlendi; sonra düzeltme
/// uygulandı ve yeşile döndüler.
/// </summary>
[Collection("Integration")]
public sealed class AtamaKapisiTests(DatabaseFixture fixture)
{
    /// <summary>
    /// `tasks.view` TAŞIMAYAN rol. Canlıda ölçüldü: `tasks.view` yalnız
    /// Admin ve Genel Müdür rollerinde; diğer on üç rolde yok.
    /// </summary>
    private const string GorevGoremeyenRol = "Şantiye Şefi";

    private static async Task<Project> ProjeAsync(DatabaseFixture fixture, string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await TestDataFactory.CreateProjectAsync(db, suffix);
    }

    private static object Govde(Guid companyId, Guid projectId, Guid? atanan) => new
    {
        companyId,
        projectId,
        title = "Atama sonda iş emri",
        priority = (int)WorkTaskPriority.Normal,
        kind = (int)WorkTaskKind.IsEmri,
        assignedToUserId = atanan,
    };

    [Fact]
    public async Task GorevuGoremeyenKullaniciyaAtama_Reddedilir()
    {
        /*
         * ASIL İDDİA.
         *
         * Göremeyeceği bir göreve atanan kullanıcı, gelen kutusunda
         * açamadığı bir satır görür. Daha kötüsü: görev, kapsam
         * disiplinine açılmış gizli bir kapı olur — `tasks.manage`
         * taşıyan biri, kendi kapsamı dışındaki bir kullanıcıyı
         * bir kaydın içine yerleştirebilir.
         */
        var proje = await ProjeAsync(fixture, "ATM-1");

        Guid yetkisizId;
        await TestUserFactory.CreateClientWithRolesAsync(
            fixture, "atama-goremez", [GorevGoremeyenRol]);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            yetkisizId = (await db.Users.AsNoTracking()
                .SingleAsync(x => x.Username.Contains("atama-goremez"))).Id;
        }

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsJsonAsync(
            "/api/tasks", Govde(proje.CompanyId, proje.Id, yetkisizId));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Contains(
            "göreve atanamaz",
            await yanit.Content.ReadAsStringAsync());
    }

    // ═══════════════════════════════════════════════════════════
    //  AYNI KAYNAĞIN BÜTÜN YAZMA FİİLLERİ — ACIL/2
    //
    //  DERS: ACIL/1'de POST'taki eksik kapı kapatıldı ama aynı
    //  kaynağın DİĞER fiilleri o anda sınanmadı. PUT bir gün sonra
    //  çıktı. Bir kapı eksiği bulunduğunda aynı kaynağın bütün yazma
    //  fiilleri (POST/PUT/PATCH/DELETE ve eylem uçları) AYNI TURDA
    //  sınanır.
    // ═══════════════════════════════════════════════════════════

    /// <summary>Görevi oluşturup kimliğini döndürür.</summary>
    private async Task<Guid> GorevAcAsync(HttpClient client, Project proje)
    {
        var yanit = await client.PostAsJsonAsync(
            "/api/tasks", Govde(proje.CompanyId, proje.Id, null));

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return (await db.WorkTasks.AsNoTracking()
            .SingleAsync(x => x.ProjectId == proje.Id)).Id;
    }

    private async Task<Guid> YetkisizKullaniciAsync(string suffix)
    {
        await TestUserFactory.CreateClientWithRolesAsync(
            fixture, suffix, [GorevGoremeyenRol]);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return (await db.Users.AsNoTracking()
            .SingleAsync(x => x.Username.Contains(suffix))).Id;
    }

    [Fact]
    public async Task PUT_GorevuGoremeyenKullaniciyaAtama_Reddedilir()
    {
        /*
         * ACIL/2'NİN ASIL İDDİASI.
         *
         * PUT `item.AssignedToUserId = request.AssignedToUserId` yazıyor
         * ama doğrulamıyordu. POST'taki kapı ACIL/1'de kapatıldı; aynı
         * açık PUT'ta duruyordu — kayıt yetkili bir kişiyle açılır,
         * sonra PUT ile yetkisiz birine devredilirdi.
         */
        var proje = await ProjeAsync(fixture, "ATM-PUT");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var id = await GorevAcAsync(client, proje);
        var yetkisizId = await YetkisizKullaniciAsync("atama-put-goremez");

        var yanit = await client.PutAsJsonAsync(
            $"/api/tasks/{id}",
            new
            {
                title = "Yetkisize devredilmiş iş emri",
                priority = (int)WorkTaskPriority.Normal,
                kind = (int)WorkTaskKind.IsEmri,
                assignedToUserId = yetkisizId,
                projectId = (Guid?)proje.Id,
            });

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Contains(
            "göreve atanamaz",
            await yanit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PUT_YetkiliKullaniciyaAtama_Kabul_POZITIF_KONTROL()
    {
        var proje = await ProjeAsync(fixture, "ATM-PUT-OK");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var id = await GorevAcAsync(client, proje);

        Guid adminId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            adminId = (await db.Users.AsNoTracking()
                .SingleAsync(x => x.Username == AuthHelper.AdminUsername)).Id;
        }

        var yanit = await client.PutAsJsonAsync(
            $"/api/tasks/{id}",
            new
            {
                title = "Yetkiliye atanmış iş emri",
                priority = (int)WorkTaskPriority.Normal,
                kind = (int)WorkTaskKind.IsEmri,
                assignedToUserId = adminId,
                projectId = (Guid?)proje.Id,
            });

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
    }

    [Fact]
    public async Task PUT_AtamaYeniMerkezeGoreDogrulanir()
    {
        /*
         * BU PAKETİN EN İNCE KARARI — VE TEK BAŞINA ATAMA SONDASI ONU
         * ÖLÇMEZ.
         *
         * PUT hem merkezi hem atananı aynı anda değiştirebiliyor.
         * Doğrulama HANGİ merkeze göre yapılmalı: kaydın yüklenmiş
         * (ESKİ) hâline mi, istekteki (YENİ) hâline mi?
         *
         * YENİ olmalı. Atanan kişinin görmesi gereken şey, kaydın
         * kaydedildikten SONRAKİ hâli; eski merkeze göre doğrulamak,
         * kişiyi göremeyeceği bir kaydın içine yerleştirir.
         *
         * SENARYO: kullanıcının kapsamı YALNIZ A projesi.
         *   - Görev A projesindeyken ona atanabilir (eski merkez).
         *   - Aynı PUT'ta merkez B'ye taşınıp kişi atanırsa REDDEDİLİR.
         *
         * ADI DAVRANIŞI ANLATIYOR, SIRAYI DEĞİL: satır numaraları
         * değişince test adı yalan söylemesin. Sıra (merkez → atama →
         * yazma) bir sözleşme ve bu test onu davranış üzerinden korur;
         * bir sonraki düzenleyen merkez doğrulamasını atamanın altına
         * alırsa bu test düşer.
         */
        Project projeA;
        Guid projeBId;
        Guid kullaniciId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            projeA = await TestDataFactory.CreateProjectAsync(db, "ATM-YENI-A");

            // İKİNCİ PROJE AYNI ŞİRKETTE: şirketler arası tuhaf bir
            // kurulum, ölçmek istediğimiz şeyi bulandırırdı.
            var projeB = new Project
            {
                CompanyId = projeA.CompanyId,
                BranchId = projeA.BranchId,
                EmployerCurrentAccountId = projeA.EmployerCurrentAccountId,
                Code = "PRJ-ATM-YENI-B",
                Name = "Test Proje ATM-YENI-B",
                CurrencyCode = "TRY",
                Status = ProjectStatus.Active
            };
            db.Projects.Add(projeB);
            await db.SaveChangesAsync();
            projeBId = projeB.Id;
        }

        // Kullanıcı: görev iznini taşıyan bir rol, ama kapsamı DAR.
        await TestUserFactory.CreateClientWithRolesAsync(
            fixture, "atama-proje-kapsamli", ["Genel Müdür"]);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var kullanici = await db.Users
                .SingleAsync(x => x.Username.Contains("atama-proje-kapsamli"));
            kullaniciId = kullanici.Id;

            /*
             * KAPSAM ELLE DARALTILIYOR: `TestUserFactory` yalnız ŞİRKET
             * kapsamı kurabiliyor ve şirket kapsamı bu senaryoyu
             * ÖLÇEMEZ — şirket kapsamlı bir kullanıcı o şirketin BÜTÜN
             * projelerini görür, yani A'yı da B'yi de. Ayrımın
             * görünmesi için kapsam PROJE düzeyinde olmalı.
             */
            var eskiler = await db.UserDataScopes
                .Where(x => x.UserId == kullaniciId).ToListAsync();
            db.UserDataScopes.RemoveRange(eskiler);

            db.UserDataScopes.Add(new UserDataScope
            {
                UserId = kullaniciId,
                ScopeType = DataScopeType.Project,
                ProjectId = projeA.Id
            });

            await db.SaveChangesAsync();
        }

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var id = await GorevAcAsync(client, projeA);

        // ÖNCE POZİTİF: eski merkezde (A) atama GEÇMELİ.
        var kabul = await client.PutAsJsonAsync(
            $"/api/tasks/{id}",
            new
            {
                title = "A projesinde atanmış",
                priority = (int)WorkTaskPriority.Normal,
                kind = (int)WorkTaskKind.IsEmri,
                assignedToUserId = kullaniciId,
                projectId = (Guid?)projeA.Id,
            });

        Assert.Equal(HttpStatusCode.OK, kabul.StatusCode);

        // ASIL İDDİA: aynı PUT'ta merkez B'ye taşınıp kişi atanırsa red.
        var yanit = await client.PutAsJsonAsync(
            $"/api/tasks/{id}",
            new
            {
                title = "B projesine taşınıp atanmış",
                priority = (int)WorkTaskPriority.Normal,
                kind = (int)WorkTaskKind.IsEmri,
                assignedToUserId = kullaniciId,
                projectId = (Guid?)projeBId,
            });

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Contains(
            "göreve atanamaz",
            await yanit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DELEGATE_GorevuGoremeyenKullaniciya_Reddedilir()
    {
        /*
         * DELEGATE ZATEN DOĞRULUYORDU (ölçüldü) — ama bu turda o da
         * SINANIYOR. "Zaten var" demek ölçüm değildir; ACIL/1'in
         * dersi tam olarak buydu.
         */
        var proje = await ProjeAsync(fixture, "ATM-DLG");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var id = await GorevAcAsync(client, proje);
        var yetkisizId = await YetkisizKullaniciAsync("atama-dlg-goremez");

        var yanit = await client.PostAsJsonAsync(
            $"/api/tasks/{id}/delegate",
            new { toUserId = yetkisizId, reason = "Sonda devri" });

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
    }

    [Fact]
    public async Task AtamasizOlusturma_Kabul_POZITIF_KONTROL()
    {
        /*
         * POZİTİF KONTROL: yukarıdaki test, uç HER isteği reddetse de
         * yeşil kalırdı. Atamasız oluşturma bozulmamalı — sahipsiz iş
         * emri geçerli bir durumdur.
         */
        var proje = await ProjeAsync(fixture, "ATM-2");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsJsonAsync(
            "/api/tasks", Govde(proje.CompanyId, proje.Id, null));

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
    }

    [Fact]
    public async Task GoreviGorebilenKullaniciyaAtama_Kabul_POZITIF_KONTROL()
    {
        /*
         * İKİNCİ POZİTİF KONTROL: kural HER ATAMAYI reddetseydi ilk test
         * yine yeşil olurdu. Yetkili bir kullanıcıya atama geçmeli.
         */
        var proje = await ProjeAsync(fixture, "ATM-3");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        Guid adminId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            adminId = (await db.Users.AsNoTracking()
                .SingleAsync(x => x.Username == AuthHelper.AdminUsername)).Id;
        }

        var yanit = await client.PostAsJsonAsync(
            "/api/tasks", Govde(proje.CompanyId, proje.Id, adminId));

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
    }
}
