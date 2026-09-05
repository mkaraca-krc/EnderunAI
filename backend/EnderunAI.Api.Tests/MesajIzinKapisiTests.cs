using System.Net;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// MESAJLAŞMA İZİN KAPISI — BEYAN VE ZORLAMA.
///
/// Sekiz mesaj ucu bugüne kadar yalnız `[Authorize]` taşıyordu ve
/// KAPI/1'in muafiyet listesinde `uyelik-kapisi` gerekçesiyle
/// duruyordu. Gerekçe doğruydu; eksik olan BEYANDI: "izin
/// gerekmiyor" ile "izin yazılmamış" dışarıdan aynı görünür
/// (KURAL 72/E).
///
/// İKİ KAPI ÜST ÜSTE DURUYOR VE İKİSİ AYRI ŞEY SINIYOR:
///   · anahtar  -> mesajlaşma ÖZELLİĞİNİ kullanabilir mi
///   · üyelik   -> BU konuşmanın tarafı mı
/// Bu dosya birincisini sınıyor. İkincisi
/// `MessagingAccessTests`'in işi ve orada duruyor.
/// </summary>
public sealed class RolMesajlasmaTests
{
    /// <summary>
    /// İDDİA: `RoleCatalog`'daki HER rol iki mesajlaşma anahtarını da
    /// taşır.
    ///
    /// NEDEN BU TEST VAR — ÖLÇÜLMÜŞ BİR RİSK: Admin ve Genel Müdür
    /// anahtarları `K` yansımasıyla alıyor, kalan 13 rol listesini
    /// ELLE taşıyor. Elle yazılan 13 yerden biri unutulsaydı o rol
    /// sessizce mesajlaşamazdı ve kimse fark etmezdi — sessiz yetki
    /// kaybı, gürültülü hatadan kötüdür.
    ///
    /// Ortak küme (`HerRolde`) unutmayı tek noktaya indirdi; bu test
    /// o tek noktayı sınıyor. Yarın eklenecek bir rol de kapsanıyor:
    /// listeye eklendiği an bu test onu da okur.
    /// </summary>
    [Fact]
    public void HerRol_IkiMesajlasmaAnahtariniDaTasir()
    {
        Assert.NotEmpty(RoleCatalog.Roles);

        var eksikler = RoleCatalog.Roles
            .Where(rol =>
                !rol.PermissionKeys.Contains(
                    PermissionCatalog.Keys.MesajlarView, StringComparer.OrdinalIgnoreCase) ||
                !rol.PermissionKeys.Contains(
                    PermissionCatalog.Keys.MesajlarSend, StringComparer.OrdinalIgnoreCase))
            .Select(rol => rol.Name)
            .ToList();

        Assert.True(
            eksikler.Count == 0,
            "Mesajlaşma anahtarı taşımayan rol(ler): " + string.Join(", ", eksikler));
    }

    /// <summary>
    /// POZİTİF KONTROL — SAYAÇ GERÇEKTEN ROL SAYIYOR.
    ///
    /// Üstteki test, `Roles` bir şekilde boşalsaydı da yeşil kalırdı
    /// (`NotEmpty` onu yakalar, ama sayının beklenen büyüklükte
    /// olduğunu göstermez). Kural 48: boş sonuç yokluğun kanıtı
    /// değildir.
    /// </summary>
    [Fact]
    public void RolSayisi_BeklenenTabaninUstunde()
    {
        Assert.True(
            RoleCatalog.Roles.Count >= 15,
            $"Rol sayısı beklenenden az: {RoleCatalog.Roles.Count}");
    }
}

/// <summary>
/// UÇTAN UCA: anahtarı olmayan kullanıcı mesaj uçlarını göremez.
/// </summary>
[Collection("Integration")]
public sealed class MesajIzinKapisiTests(DatabaseFixture fixture)
{
    /// <summary>
    /// İZİNSİZ ROL TESTİN İÇİNDE ÜRETİLİYOR.
    ///
    /// `RoleCatalog`'daki 15 rolün 15'i artık mesajlaşma anahtarını
    /// taşıyor — bu paketin amacı buydu. Dolayısıyla hazır bir rolü
    /// "yetkisiz" diye kullanmak MÜMKÜN DEĞİL; kullansaydım test
    /// yeşil görünür ama hiçbir şey ölçmezdi. İzinsizlik burada
    /// kurulur.
    /// </summary>
    private async Task<string> IzinsizRolAsync()
    {
        const string ad = "TEST-Mesajsiz-Rol";

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!await db.Roles.AnyAsync(x => x.Name == ad))
        {
            db.Roles.Add(new AppRole
            {
                Name = ad,
                Description = "Mesajlaşma izni olmayan test rolü.",
                DataScopePolicy = RoleDataScopePolicy.All
            });
            await db.SaveChangesAsync();
        }

        return ad;
    }

    public static TheoryData<string> OkumaYollari =>
        new()
        {
            "/api/mesajlar/konusmalar",
            "/api/mesajlar/okunmamis",
            "/api/mesajlar/ara?q=deneme",
        };

    [Theory]
    [MemberData(nameof(OkumaYollari))]
    public async Task IzinsizKullanici_403(string yol)
    {
        var rol = await IzinsizRolAsync();
        var client = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, "mesaj-izinsiz", [rol]);

        var yanit = await client.GetAsync(yol);

        Assert.Equal(HttpStatusCode.Forbidden, yanit.StatusCode);
    }

    /// <summary>
    /// POZİTİF KONTROL — KAPI HERKESİ KESMİYOR.
    ///
    /// Üstteki testler, uç bozulup HERKESE 403 dönse de yeşil
    /// kalırdı. Anahtarı olan kullanıcının 200 alması, 403'ün
    /// iznin YOKLUĞUNDAN geldiğini gösteriyor.
    ///
    /// `Formen` bilerek seçildi: 15 rolün en az yetkilisinden biri
    /// (10 izin). Anahtarı yansımayla değil, ortak kümeden alıyor —
    /// yani bu test `HerRolde` yaymasının canlıdaki karşılığını da
    /// ölçüyor.
    /// </summary>
    [Fact]
    public async Task IzinliKullanici_200_POZITIF_KONTROL()
    {
        var client = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, "mesaj-izinli", ["Formen"]);

        var yanit = await client.GetAsync("/api/mesajlar/konusmalar");

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
    }
}
