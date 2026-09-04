using System.Net;
using System.Net.Http.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models.HumanResources;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// DEPARTMAN SİLME MUHAFIZI — PERSONELİ OLAN DEPARTMAN SİLİNEMEZ.
///
/// ── DOĞURAN OLAY ──
///
/// Muhafız alt birim ve pozisyon kontrol ediyor, PERSONEL kontrol
/// ETMİYORDU. Bulunduğunda alan zaten boştu (79 aktif personelin
/// 0'ında departman doluydu) — risk GİZİLDİ.
///
/// DEPARTMAN/1 o alanın yazma yolunu açtı ve risk AKTİF hâle geldi.
/// Personel ataması başlamadan önce kapatılması şart koşuldu.
///
/// ── NEDEN SESSİZ BİR BOZULMA ──
///
/// `Personnel` AppDbContext'te, `HrDepartment` HrDbContext'te. İki
/// bağlam arasında YABANCI ANAHTAR YOK; veritabanı bu bağı
/// doğrulamıyor. Silme, personeldeki `DepartmentId`'yi olduğu gibi
/// bırakır ve kimlik artık hiçbir kayda çözülmez.
/// </summary>
[Collection("Integration")]
public sealed class DepartmanSilmeMuhafiziTests(DatabaseFixture fixture)
{
    private static async Task<(Guid DepartmanId, Guid PersonelId, Guid SirketId)>
        KurAsync(DatabaseFixture fixture, string ek, bool personelBagla)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var proje = await TestDataFactory.CreateProjectAsync(db, ek);

        var departman = new HrDepartment
        {
            CompanyId = proje.CompanyId,
            Code = $"DEP-SIL-{ek}",
            Name = $"Silme Testi {ek}",
            IsActive = true
        };
        hrDb.Departments.Add(departman);
        await hrDb.SaveChangesAsync();

        var personel = await TestDataFactory.CreatePersonnelAsync(
            db, proje.CompanyId, $"SIL-{ek}");

        if (personelBagla)
        {
            personel.DepartmentId = departman.Id;
            await db.SaveChangesAsync();
        }

        return (departman.Id, personel.Id, proje.CompanyId);
    }

    [Fact]
    public async Task PersoneliOlanDepartman_SILINEMEZ()
    {
        var (departmanId, _, _) = await KurAsync(fixture, "M1", personelBagla: true);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.DeleteAsync($"/api/hr/departments/{departmanId}");

        Assert.Equal(HttpStatusCode.Conflict, yanit.StatusCode);

        var govde = await yanit.Content.ReadAsStringAsync();

        // SEBEP AYIRT EDİLEBİLİR OLMALI: "alt birim/pozisyon" ile
        // "personel" farklı işler gerektirir.
        Assert.Contains("personel", govde, StringComparison.OrdinalIgnoreCase);

        // VERİTABANINDAN DOĞRULANIYOR: yanıt doğru olup silme yine de
        // olmuş olabilirdi.
        using var scope = fixture.Factory.Services.CreateScope();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        var departman = await hrDb.Departments.AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == departmanId);

        Assert.False(departman.IsDeleted);
        Assert.True(departman.IsActive);
    }

    [Fact]
    public async Task PersoneliOlmayanDepartman_Silinir_POZITIF_KONTROL()
    {
        /*
         * POZİTİF KONTROL — bu olmadan yukarıdaki test boştur: uç her
         * silmeyi reddetse de yeşil kalırdı.
         */
        var (departmanId, _, _) = await KurAsync(fixture, "M2", personelBagla: false);
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.DeleteAsync($"/api/hr/departments/{departmanId}");

        Assert.Equal(HttpStatusCode.NoContent, yanit.StatusCode);

        using var scope = fixture.Factory.Services.CreateScope();
        var hrDb = scope.ServiceProvider.GetRequiredService<HrDbContext>();

        /*
         * `IgnoreQueryFilters` ŞART: HrDbContext yumuşak silinmiş
         * kayıtları genel sorgu süzgeciyle eliyor. Süzgeçsiz okumazsak
         * "satır yok" hatası alırız ve bu, silmenin BAŞARILI olduğunu
         * gösterse de testi kırar — nitekim ilk yazımda tam olarak bu
         * oldu.
         */
        var departman = await hrDb.Departments.AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == departmanId);

        Assert.True(departman.IsDeleted);
    }

    [Fact]
    public async Task SilinmisPersonel_Silmeyi_ENGELLEMEZ()
    {
        /*
         * YUMUŞAK SİLİNMİŞ PERSONEL SAYILMIYOR.
         *
         * Aksi hâlde bir departman, yıllar önce silinmiş bir kayıt
         * yüzünden sonsuza kadar silinemez hâle gelirdi — muhafız
         * koruma olmaktan çıkıp engele dönüşürdü (Kural 42).
         */
        var (departmanId, personelId, _) =
            await KurAsync(fixture, "M3", personelBagla: true);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var personel = await db.Personnel.SingleAsync(x => x.Id == personelId);
            personel.IsDeleted = true;
            await db.SaveChangesAsync();
        }

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var yanit = await client.DeleteAsync($"/api/hr/departments/{departmanId}");

        Assert.Equal(HttpStatusCode.NoContent, yanit.StatusCode);
    }
}
