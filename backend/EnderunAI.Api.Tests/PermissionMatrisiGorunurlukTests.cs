using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// MATRİS HİÇBİR YÜRÜRLÜKTEKİ İZNİ GİZLEMEZ (GÖRÜNÜRLÜK/1).
///
/// ═══ NEDEN VAR: ÖLÇÜLMEDEN TAŞINAN BİR İNANÇ ═══
///
/// `PermissionMatrixController` `KullanimdanKalkti` işaretli izinleri
/// matristen çıkarıyordu ve gerekçesi "bunlar hiçbir şeyi korumuyor"
/// idi. O cümle ÖLÇÜLMEMİŞTİ ve yanlıştı: gizlenen 7 anahtarın hepsi
/// yürürlükteydi — middleware yoldan türetmede yedisini de döndürüyor,
/// üçü ayrıca açık `RequirePermission` niteliğinde kullanılıyor.
///
/// Sonuç: 9 role verilmiş, fiilen uygulanan ve ekranda GÖRÜNMEYEN
/// izinler. Kullanıcı `accounting.manage`'i matriste bulamıyor ama
/// middleware onu aramaya devam ediyordu.
///
/// ═══ BU TEST NEYİ TUTUYOR ═══
///
/// Matrisin döndürdüğü izin kümesi, katalogdaki izin kümesinin
/// TAMAMI olmalı. Yani bugün hiçbir izin gizli değil.
///
/// GİZLEMEK YASAK DEĞİL — BEYAN ŞART. Gerçekten emekliye ayrılmış bir
/// izni gizlemek meşru; ama bu testi değiştirmek zorunda kalan kişi,
/// gizlediği anahtarın HİÇBİR UÇTA aranmadığını göstermek zorunda.
/// "Kullanımdan kalktı" işareti, UYGULAMADAN kalktı anlamına gelmiyor
/// ve bu ayrım aylarca yapılmadı.
///
/// ═══ İKİNCİ AYAK NEDEN VAR ═══
///
/// Yalnız izin sayısını saymak yetmez: matris grants listesini de
/// görünür anahtarlara göre süzüyor. İzinler tam ama grants süzülmüş
/// kalsaydı, "sütun başına tik sayısı == rolün grant sayısı" iddiası
/// yine sessizce bozulurdu. İkisi ayrı ayrı sınanıyor.
/// </summary>
[Collection("Integration")]
public sealed class PermissionMatrisiGorunurlukTests(DatabaseFixture fixture)
{
    private sealed record Yanit(
        List<JsonElement> Permissions,
        List<JsonElement> Roles,
        List<JsonElement> Grants);

    private async Task<Yanit> MatrisiGetirAsync()
    {
        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.GetAsync("/api/user-management/permission-matrix");
        yanit.EnsureSuccessStatusCode();

        return (await yanit.Content.ReadFromJsonAsync<Yanit>())!;
    }

    [Fact]
    public async Task Matris_KatalogdakiHerIzniDonduruyor()
    {
        var matris = await MatrisiGetirAsync();

        var beklenen = PermissionCatalog.Permissions.Count;

        Assert.True(
            matris.Permissions.Count == beklenen,
            $"Matris {matris.Permissions.Count} izin döndürdü, katalogda {beklenen} var.\n" +
            "Aradaki fark GİZLENEN izinlerdir. Bir izni gizlemek için önce " +
            "onun hiçbir uçta aranmadığını gösterin: middleware'in yoldan " +
            "türetmesi ve RequirePermission nitelikleri taranmalı. " +
            "'Kullanımdan kalktı' işareti, UYGULAMADAN kalktı demek DEĞİLDİR.");
    }

    [Fact]
    public async Task Matris_HicbirGrantiSuzmuyor()
    {
        var matris = await MatrisiGetirAsync();

        int veritabanindaki;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            veritabanindaki = await db.RolePermissions.CountAsync();
        }

        Assert.True(
            matris.Grants.Count == veritabanindaki,
            $"Matris {matris.Grants.Count} grant döndürdü, veritabanında " +
            $"{veritabanindaki} satır var.\n" +
            "Süzülen grantlar, ekranda karşılığı olmayan yetkilerdir: " +
            "kullanıcı kaldıramadığı bir izni taşımaya devam eder.");
    }
}
