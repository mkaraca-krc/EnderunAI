using System.Net;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Expenses;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// MERKEZ KAPISI — DENETLEYİCİ SEVİYESİ.
///
/// NEDEN AYRI DOSYA: `MasrafMerkeziKuraliTests` KURALI sınıyor ve saf.
/// Ama "kural TEK METOTTA yaşıyor ve POST ile PUT ikisi de onu çağırıyor"
/// iddiası kuralın kendisiyle sınanamaz — o iddia ÇAĞIRANLARLA ilgili.
///
/// Bunu fark etmeden "tek kapı" dedim: on bir saf test yeşildi ama
/// PUT'tan doğrulamayı sökseydim HİÇBİRİ kırmızı vermezdi. İddia
/// ölçülmemişti. Bu dosya o boşluğu kapatıyor.
/// </summary>
[Collection("Integration")]
public sealed class MerkezKapisiTests(DatabaseFixture fixture)
{
    private static async Task<(Project proje, ProjectSite santiye)> SahneAsync(
        DatabaseFixture fixture, string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var proje = await TestDataFactory.CreateProjectAsync(db, suffix);

        var santiye = new ProjectSite
        {
            ProjectId = proje.Id,
            Code = $"STY-{suffix}",
            Name = $"Test Şantiye {suffix}",
        };

        db.ProjectSites.Add(santiye);
        await db.SaveChangesAsync();

        return (proje, santiye);
    }

    private static object Govde(
        Guid companyId,
        Guid? projectId = null,
        Guid? branchId = null,
        Guid? projectSiteId = null,
        ExpenseCenterType? centerType = null,
        string baslik = "Merkez sonda iş emri") => new
        {
            companyId,
            projectId,
            branchId,
            projectSiteId,
            centerType = centerType.HasValue ? (int?)centerType.Value : null,
            title = baslik,
            priority = (int)WorkTaskPriority.Normal,
            kind = (int)WorkTaskKind.IsEmri,
        };

    // ───────── S1: üçü de boş ─────────

    [Fact]
    public async Task S1_MerkezsizPost_Reddedilir()
    {
        var (proje, _) = await SahneAsync(fixture, "MRK-S1");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsJsonAsync(
            "/api/tasks", Govde(proje.CompanyId));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Contains(
            "Masraf merkezi zorunludur",
            await yanit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task S1b_MerkezliPost_Kabul_PozitifKontrol()
    {
        /*
         * POZİTİF KONTROL: S1, uç HER İSTEĞİ reddetse de yeşil kalırdı.
         */
        var (proje, _) = await SahneAsync(fixture, "MRK-S1B");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsJsonAsync(
            "/api/tasks", Govde(proje.CompanyId, projectId: proje.Id));

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
    }

    // ───────── S2: CenterType çelişkisi ─────────

    [Fact]
    public async Task S2_CenterTypeSecimleCelisirse_Reddedilir()
    {
        var (proje, _) = await SahneAsync(fixture, "MRK-S2");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        // Proje seçilmiş ama tür "Şube" yazılmış.
        var yanit = await client.PostAsJsonAsync(
            "/api/tasks",
            Govde(proje.CompanyId, projectId: proje.Id,
                centerType: ExpenseCenterType.Branch));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Contains(
            "türü seçilen merkezle uyuşmuyor",
            await yanit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task S2b_TurSecimdenTuretilir_IstektenSaklanmaz()
    {
        /*
         * Tür HİÇ gönderilmese bile kayda yazılmalı — sunucu onu
         * seçimden türetiyor. Bu, "tek kaynak" iddiasının kanıtı.
         */
        var (proje, santiye) = await SahneAsync(fixture, "MRK-S2B");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsJsonAsync(
            "/api/tasks",
            Govde(proje.CompanyId, projectId: proje.Id, projectSiteId: santiye.Id));

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var kayit = await db.WorkTasks.AsNoTracking()
            .SingleAsync(x => x.ProjectSiteId == santiye.Id);

        Assert.Equal(ExpenseCenterType.ProjectSite, kayit.CenterType);
    }

    // ───────── S3: başka projenin şantiyesi ─────────

    [Fact]
    public async Task S3_BaskaProjeninSantiyesi_Reddedilir()
    {
        var (projeA, santiyeA) = await SahneAsync(fixture, "MRK-S3A");
        var (projeB, _) = await SahneAsync(fixture, "MRK-S3B");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsJsonAsync(
            "/api/tasks",
            Govde(projeB.CompanyId, projectId: projeB.Id, projectSiteId: santiyeA.Id));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Contains(
            "seçilen projeye ait değil",
            await yanit.Content.ReadAsStringAsync());
    }

    // ───────── S4: PUT AYNI KAPIDAN GEÇİYOR MU ─────────

    [Fact]
    public async Task S4_PutMerkezsizGuncelleme_Reddedilir()
    {
        /*
         * BU SONDA "TEK METOT" İDDİASINI SINIYOR.
         *
         * Doğrulama yalnız POST'a bağlı olsaydı PUT ikinci bir kapı
         * olurdu: kayıt merkezli açılır, sonra merkezi silinerek
         * güncellenebilirdi ve kural hiç devreye girmezdi.
         *
         * Saf kural testleri bunu YAKALAYAMAZ — onlar kuralı sınıyor,
         * çağıranı değil.
         */
        var (proje, _) = await SahneAsync(fixture, "MRK-S4");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var olustur = await client.PostAsJsonAsync(
            "/api/tasks", Govde(proje.CompanyId, projectId: proje.Id));

        Assert.Equal(HttpStatusCode.OK, olustur.StatusCode);

        Guid id;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            id = (await db.WorkTasks.AsNoTracking()
                .SingleAsync(x => x.ProjectId == proje.Id)).Id;
        }

        // Merkezi boşaltarak güncelle: aynı kapı reddetmeli.
        var yanit = await client.PutAsJsonAsync(
            $"/api/tasks/{id}",
            new
            {
                title = "Merkezi silinmiş iş emri",
                priority = (int)WorkTaskPriority.Normal,
                kind = (int)WorkTaskKind.IsEmri,
                projectId = (Guid?)null,
                branchId = (Guid?)null,
                projectSiteId = (Guid?)null,
            });

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Contains(
            "Masraf merkezi zorunludur",
            await yanit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task S4b_PutMerkezliGuncelleme_Kabul_PozitifKontrol()
    {
        var (proje, santiye) = await SahneAsync(fixture, "MRK-S4B");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await client.PostAsJsonAsync(
            "/api/tasks", Govde(proje.CompanyId, projectId: proje.Id));

        Guid id;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            id = (await db.WorkTasks.AsNoTracking()
                .SingleAsync(x => x.ProjectId == proje.Id && x.ProjectSiteId == null)).Id;
        }

        /*
         * MERKEZ DÜZELTİLEBİLİR OLMALI. Önce PUT merkez alanlarını hiç
         * taşımıyordu: yanlış konmuş bir merkez bir daha düzeltilemezdi.
         */
        var yanit = await client.PutAsJsonAsync(
            $"/api/tasks/{id}",
            new
            {
                title = "Merkezi düzeltilmiş iş emri",
                priority = (int)WorkTaskPriority.Normal,
                kind = (int)WorkTaskKind.IsEmri,
                projectId = (Guid?)proje.Id,
                projectSiteId = (Guid?)santiye.Id,
            });

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);

        using var son = fixture.Factory.Services.CreateScope();
        var db2 = son.ServiceProvider.GetRequiredService<AppDbContext>();
        var kayit = await db2.WorkTasks.AsNoTracking().SingleAsync(x => x.Id == id);

        Assert.Equal(santiye.Id, kayit.ProjectSiteId);
        Assert.Equal(ExpenseCenterType.ProjectSite, kayit.CenterType);
    }

    // ───────── S5: mevcut kayıtlar bozulmadan listeleniyor ─────────

    [Fact]
    public async Task S5_MerkezliKayitListedeAdiylaGorunur()
    {
        var (proje, _) = await SahneAsync(fixture, "MRK-S5");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await client.PostAsJsonAsync(
            "/api/tasks", Govde(proje.CompanyId, projectId: proje.Id));

        var liste = await client.GetAsync($"/api/tasks?companyId={proje.CompanyId}");
        Assert.Equal(HttpStatusCode.OK, liste.StatusCode);

        var govde = await liste.Content.ReadAsStringAsync();

        // Merkez adı DTO'dan geliyor: ekran GUID göstermemeli.
        Assert.Contains(proje.Code, govde);
        Assert.Contains("projectName", govde, StringComparison.OrdinalIgnoreCase);
    }
}
