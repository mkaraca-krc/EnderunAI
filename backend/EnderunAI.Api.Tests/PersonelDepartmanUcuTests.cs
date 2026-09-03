using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// PERSONEL DEPARTMAN UCU — `Personnel.DepartmentId`'NİN İLK YAZMA YOLU.
///
/// ── BU DOSYANIN DOĞUŞU ──
///
/// Alan modelde vardı, göçü uygulanmıştı, ama canlıda 79 aktif
/// personelin 0'ında doluydu. İlk teşhis "veri girilmemiş" oldu; ölçüm
/// başka bir şey gösterdi: kod tabanında bu alana YAZAN hiçbir yol
/// yoktu. Bu uç o boşluğu kapatıyor.
///
/// ── NE SINANIYOR ──
///
/// Kuralın kendisi `PersonelDepartmanKuraliTests`'te. Burada kuralın
/// ÇAĞRILDIĞI, TARİHÇENİN yazıldığı ve SÜRÜM KONTROLÜNÜN ısırdığı
/// ölçülüyor — üçü de yalnız uçtan uca görülebilir.
/// </summary>
[Collection("Integration")]
public sealed class PersonelDepartmanUcuTests(DatabaseFixture fixture)
{
    private sealed record Duzenek(
        Guid PersonelId, Guid SirketId, Guid DepartmanId, DateTime Surum);

    private static async Task<Duzenek> KurAsync(
        DatabaseFixture fixture, string ek, bool departmanAktif = true)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var proje = await TestDataFactory.CreateProjectAsync(db, ek);
        var personel = await TestDataFactory.CreatePersonnelAsync(
            db, proje.CompanyId, ek);

        var departman = new HrDepartment
        {
            CompanyId = proje.CompanyId,
            Code = $"DEP-{ek}",
            Name = $"Test Departman {ek}",
            IsActive = departmanAktif
        };
        hrDb.Departments.Add(departman);
        await hrDb.SaveChangesAsync();

        return new Duzenek(
            personel.Id, proje.CompanyId, departman.Id,
            personel.UpdatedAtUtc ?? personel.CreatedAtUtc);
    }

    private static async Task<(Guid? Departman, int TarihceSayisi)> OkuAsync(
        DatabaseFixture fixture, Guid personelId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var dep = await db.Personnel.AsNoTracking()
            .Where(x => x.Id == personelId)
            .Select(x => x.DepartmentId)
            .SingleAsync();

        var tarihce = await db.PersonnelDepartmentHistories.AsNoTracking()
            .CountAsync(x => x.PersonnelId == personelId);

        return (dep, tarihce);
    }

    private static async Task<DateTime> SurumOkuAsync(
        DatabaseFixture fixture, Guid personelId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var kayit = await db.Personnel.AsNoTracking()
            .Where(x => x.Id == personelId)
            .Select(x => new { x.UpdatedAtUtc, x.CreatedAtUtc })
            .SingleAsync();

        return kayit.UpdatedAtUtc ?? kayit.CreatedAtUtc;
    }

    [Fact]
    public async Task Atama_Kabul_VE_TarihceyeYazilir()
    {
        var d = await KurAsync(fixture, "DEP-A1");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PutAsJsonAsync(
            $"/api/personnel/{d.PersonelId}/departman",
            new { departmentId = d.DepartmanId, recordVersion = d.Surum });

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);

        var (departman, tarihce) = await OkuAsync(fixture, d.PersonelId);

        // VERİTABANINDAN OKUNUYOR, YANITTAN DEĞİL: yanıt doğru olup
        // yazma yine de olmayabilirdi.
        Assert.Equal(d.DepartmanId, departman);
        Assert.Equal(1, tarihce);
    }

    [Fact]
    public async Task TarihcedeOncekiDepartman_Tutuluyor()
    {
        /*
         * M3'ÜN "AYRILDIĞI TARİHE KADARKİ GEÇMİŞ" KURALI BUNA DAYANIYOR.
         * `Personnel.DepartmentId` yalnız BUGÜNÜ söyler; dünkü cevabı
         * yalnız tarihçe verebilir.
         */
        var d = await KurAsync(fixture, "DEP-A2");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await client.PutAsJsonAsync(
            $"/api/personnel/{d.PersonelId}/departman",
            new { departmentId = d.DepartmanId, recordVersion = d.Surum });

        var yeniSurum = await SurumOkuAsync(fixture, d.PersonelId);

        var cikar = await client.PutAsJsonAsync(
            $"/api/personnel/{d.PersonelId}/departman",
            new { departmentId = (Guid?)null, recordVersion = yeniSurum });

        Assert.Equal(HttpStatusCode.OK, cikar.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var satirlar = await db.PersonnelDepartmentHistories.AsNoTracking()
            .Where(x => x.PersonnelId == d.PersonelId)
            .OrderBy(x => x.ChangedAtUtc)
            .ToListAsync();

        Assert.Equal(2, satirlar.Count);

        Assert.Null(satirlar[0].PreviousDepartmentId);
        Assert.Equal(d.DepartmanId, satirlar[0].DepartmentId);

        Assert.Equal(d.DepartmanId, satirlar[1].PreviousDepartmentId);
        Assert.Null(satirlar[1].DepartmentId);
    }

    [Fact]
    public async Task AyniDepartmanTekrar_TarihceyeYAZILMAZ()
    {
        var d = await KurAsync(fixture, "DEP-A3");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await client.PutAsJsonAsync(
            $"/api/personnel/{d.PersonelId}/departman",
            new { departmentId = d.DepartmanId, recordVersion = d.Surum });

        var yeniSurum = await SurumOkuAsync(fixture, d.PersonelId);

        var ikinci = await client.PutAsJsonAsync(
            $"/api/personnel/{d.PersonelId}/departman",
            new { departmentId = d.DepartmanId, recordVersion = yeniSurum });

        // İSTEK HATA DEĞİL: toplu atama ekranında aynı değeri yeniden
        // seçmek olağan.
        Assert.Equal(HttpStatusCode.OK, ikinci.StatusCode);

        var govde = JsonDocument.Parse(await ikinci.Content.ReadAsStringAsync());
        Assert.False(govde.RootElement.GetProperty("changed").GetBoolean());

        var (_, tarihce) = await OkuAsync(fixture, d.PersonelId);
        Assert.Equal(1, tarihce);
    }

    [Fact]
    public async Task SurumGonderilmezse_Reddedilir()
    {
        var d = await KurAsync(fixture, "DEP-A4");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PutAsJsonAsync(
            $"/api/personnel/{d.PersonelId}/departman",
            new { departmentId = d.DepartmanId });

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);

        var (departman, tarihce) = await OkuAsync(fixture, d.PersonelId);
        Assert.Null(departman);
        Assert.Equal(0, tarihce);
    }

    [Fact]
    public async Task EskiSurum_Cakisma_Doner()
    {
        /*
         * TOPLU ATAMA EKRANI 79 SATIRI AYNI ANDA GÖSTERİYOR; iki kişinin
         * aynı listeyi açıp aynı satırı değiştirmesi olağan. İkincisi
         * sessizce kazanırsa birincinin ataması izsiz kaybolurdu.
         */
        var d = await KurAsync(fixture, "DEP-A5");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await client.PutAsJsonAsync(
            $"/api/personnel/{d.PersonelId}/departman",
            new { departmentId = d.DepartmanId, recordVersion = d.Surum });

        // Aynı (artık eski) sürümle ikinci bir değişiklik.
        var ikinci = await client.PutAsJsonAsync(
            $"/api/personnel/{d.PersonelId}/departman",
            new { departmentId = (Guid?)null, recordVersion = d.Surum });

        Assert.Equal(HttpStatusCode.Conflict, ikinci.StatusCode);

        var (departman, _) = await OkuAsync(fixture, d.PersonelId);
        Assert.Equal(d.DepartmanId, departman);
    }

    [Fact]
    public async Task OlmayanDepartman_Reddedilir()
    {
        var d = await KurAsync(fixture, "DEP-A6");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PutAsJsonAsync(
            $"/api/personnel/{d.PersonelId}/departman",
            new { departmentId = Guid.NewGuid(), recordVersion = d.Surum });

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Contains(
            "Seçilen departman bulunamadı",
            await yanit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PasifDepartman_Reddedilir()
    {
        var d = await KurAsync(fixture, "DEP-A7", departmanAktif: false);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PutAsJsonAsync(
            $"/api/personnel/{d.PersonelId}/departman",
            new { departmentId = d.DepartmanId, recordVersion = d.Surum });

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Contains(
            "aktif değil", await yanit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task BaskaSirketinDepartmani_Reddedilir()
    {
        /*
         * İKİ BAĞLAM ARASINDA YABANCI ANAHTAR YOK: departman
         * HrDbContext'te, personel AppDbContext'te. Veritabanı bu bağı
         * doğrulamıyor; kontrol tamamen uygulama katmanında. O yüzden
         * uçtan uca ölçülüyor.
         */
        var a = await KurAsync(fixture, "DEP-A8");
        var b = await KurAsync(fixture, "DEP-A8B");

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PutAsJsonAsync(
            $"/api/personnel/{a.PersonelId}/departman",
            new { departmentId = b.DepartmanId, recordVersion = a.Surum });

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Contains(
            "başka bir şirkete ait",
            await yanit.Content.ReadAsStringAsync());

        var (departman, tarihce) = await OkuAsync(fixture, a.PersonelId);
        Assert.Null(departman);
        Assert.Equal(0, tarihce);
    }

    [Fact]
    public async Task ListeUcu_DepartmanAdiniDonduruyor_POZITIF_KONTROL()
    {
        /*
         * TOPLU ATAMA EKRANI BUNA BAĞLI: departman adı listede
         * gelmiyorsa ekran her satır için ayrı sorgu atmak zorunda
         * kalırdı. Ad iki bağlam arasında elle çözülüyor (LINQ
         * birleştiremiyor), bu yüzden ayrıca ölçülüyor.
         */
        var d = await KurAsync(fixture, "DEP-A9");
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await client.PutAsJsonAsync(
            $"/api/personnel/{d.PersonelId}/departman",
            new { departmentId = d.DepartmanId, recordVersion = d.Surum });

        var liste = await client.GetAsync(
            $"/api/personnel?companyId={d.SirketId}");

        Assert.Equal(HttpStatusCode.OK, liste.StatusCode);

        var govde = JsonDocument.Parse(await liste.Content.ReadAsStringAsync());
        var satir = govde.RootElement.EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == d.PersonelId);

        Assert.Equal(
            d.DepartmanId, satir.GetProperty("departmentId").GetGuid());
        Assert.Equal(
            "Test Departman DEP-A9",
            satir.GetProperty("departmentName").GetString());
    }
}
