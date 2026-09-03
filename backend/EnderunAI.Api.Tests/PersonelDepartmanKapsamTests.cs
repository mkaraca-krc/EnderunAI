using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// DEPARTMAN YAZMA YOLUNUN KAPSAM SÜZGECİ — DARALTILMIŞ KAPSAMLA.
///
/// `personnel.edit` izni bugün yalnız geniş kapsamlı rollerde
/// (İK Sorumlusu, Teknik Koordinatör). Yani süzgeç canlıda hiçbir
/// isteği reddetmiyor.
///
/// ROLÜN DARALMASINI BEKLEMEK, SAVUNMAYI O GÜNE KADAR TESTSİZ BIRAKMAK
/// olurdu — bu kod tabanının tekrar eden yarası (`2d90c946`) ve
/// İŞEMRİ/2 Faz 1'de aynı borç aynı yöntemle kapatıldı
/// (`PersonelKapsamSuzgeciTests`). Yöntem aynı: rolü değil KAPSAMI
/// daralt.
///
/// NEDEN AYRICA ÖNEMLİ: departman yazmak bir personelin mesaj kanalı
/// üyeliğini belirleyecek (M3). Kapsam dışı bir personelin departmanını
/// değiştirebilmek, onu görmediğiniz bir kanala sokabilmek demektir.
/// </summary>
[Collection("Integration")]
public sealed class PersonelDepartmanKapsamTests(DatabaseFixture fixture)
{
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
        DatabaseFixture fixture, CurrentDataScopeSnapshot kapsam)
    {
        var fabrika = fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICurrentDataScopeService>();
                services.AddScoped<ICurrentDataScopeService>(
                    _ => new SabitKapsam(kapsam));
            }));

        var client = fabrika.CreateClient();
        var token = await AuthHelper.LoginAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private sealed record Duzenek(
        Guid SantiyeId, Guid DepartmanId,
        Guid GorunenId, DateTime GorunenSurum,
        Guid GorunmeyenId, DateTime GorunmeyenSurum);

    private static async Task<Duzenek> KurAsync(DatabaseFixture fixture, string ek)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

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
            db, proje.CompanyId, $"{ek}-G");
        var gorunmeyen = await TestDataFactory.CreatePersonnelAsync(
            db, proje.CompanyId, $"{ek}-Y");

        db.ProjectSiteAssignments.Add(new ProjectSiteAssignment
        {
            PersonnelId = gorunen.Id,
            ProjectSiteId = santiye.Id,
            StartDate = DateTime.UtcNow.AddDays(-10),
            EndDate = null
        });

        var departman = new HrDepartment
        {
            CompanyId = proje.CompanyId,
            Code = $"DEP-{ek}",
            Name = $"Kapsam Departmanı {ek}",
            IsActive = true
        };
        hrDb.Departments.Add(departman);

        await db.SaveChangesAsync();
        await hrDb.SaveChangesAsync();

        return new Duzenek(
            santiye.Id, departman.Id,
            gorunen.Id, gorunen.UpdatedAtUtc ?? gorunen.CreatedAtUtc,
            gorunmeyen.Id, gorunmeyen.UpdatedAtUtc ?? gorunmeyen.CreatedAtUtc);
    }

    [Fact]
    public async Task DarKapsam_GorunmeyenPersonelinDepartmani_Yazilamaz()
    {
        /*
         * PERSONEL VAR, AKTİF, AYNI ŞİRKETTE. Tek eksiği isteği yapan
         * kullanıcının kapsamında olmaması. Süzgeç atlanırsa bu istek
         * 200 döner ve görülmeyen bir personel bir departmana —
         * dolayısıyla bir mesaj kanalına — sokulur.
         *
         * 404 dönüyor, 403 değil: kapsam dışı kaydın VARLIĞI da ifşa
         * edilmiyor (ProjectSitesController'daki desenin aynısı).
         */
        var d = await KurAsync(fixture, "KAPSAM-D1");
        var client = await DarKapsamliIstemciAsync(fixture, YalnizSantiye(d.SantiyeId));

        var yanit = await client.PutAsJsonAsync(
            $"/api/personnel/{d.GorunmeyenId}/departman",
            new { departmentId = d.DepartmanId, recordVersion = d.GorunmeyenSurum });

        Assert.Equal(HttpStatusCode.NotFound, yanit.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var yazildiMi = await db.Personnel.AsNoTracking()
            .Where(x => x.Id == d.GorunmeyenId)
            .Select(x => x.DepartmentId)
            .SingleAsync();

        Assert.Null(yazildiMi);
    }

    [Fact]
    public async Task DarKapsam_GorunenPersonelinDepartmani_Yazilir_POZITIF_KONTROL()
    {
        /*
         * POZİTİF KONTROL — bu olmadan yukarıdaki test boştur: kapsam
         * çözümü tamamen bozulsa ve her sorgu boşalsa da negatif test
         * yeşil kalırdı.
         */
        var d = await KurAsync(fixture, "KAPSAM-D2");
        var client = await DarKapsamliIstemciAsync(fixture, YalnizSantiye(d.SantiyeId));

        var yanit = await client.PutAsJsonAsync(
            $"/api/personnel/{d.GorunenId}/departman",
            new { departmentId = d.DepartmanId, recordVersion = d.GorunenSurum });

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var yazilan = await db.Personnel.AsNoTracking()
            .Where(x => x.Id == d.GorunenId)
            .Select(x => x.DepartmentId)
            .SingleAsync();

        Assert.Equal(d.DepartmanId, yazilan);
    }
}
