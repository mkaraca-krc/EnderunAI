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
