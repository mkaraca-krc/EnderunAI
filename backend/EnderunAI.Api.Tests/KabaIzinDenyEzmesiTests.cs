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
/// KABA İZİN, İNCE İZNİN DENY'INI EZİYOR MU (GÖRÜNÜRLÜK/1 · Adım 3).
///
/// ═══ SORU ═══
///
/// Bir kullanıcıda `accounting.edit` için Deny kaydı varken, rolünde
/// `accounting.manage` de varsa — kullanıcı muhasebe kaydını yine de
/// düzenleyebiliyor mu?
///
/// Endişenin kaynağı: `PermissionAuthorizationMiddleware` nitelik
/// bulamadığında yolu heuristikle bir izne türetiyor ve muhasebe
/// yolları için `accounting.manage` döndürüyor. Türetme devreye
/// girerse ince izne konulan Deny ATLANIRDI.
///
/// ═══ NEDEN GREP YETMEZ ═══
///
/// Türetme yalnız nitelik YOKSA çalışıyor; `UcKapisi` her `api/` ucuna
/// nitelik zorunlu kıldığı için dalın ölü olması BEKLENİYOR. Ama
/// "beklenen" ile "ölçülen" ayrı şeyler (Kural 70) ve bu tam olarak
/// yetki sorusudur — yanılmanın bedeli yüksek.
///
/// ═══ İKİ AYAK ═══
///
///   (a) Deny VAR      -> 403 gelmeli. Gelmezse Deny atlanıyor demektir.
///   (b) Deny YOK      -> 403 GELMEMELİ (pozitif kontrol).
///
/// (b) olmadan (a)'nın 403'ü "uç zaten herkese 403 veriyor" ile
/// karışırdı.
///
/// UÇ VE OKUMA: `PUT /api/accounting-accounts/{id}` rastgele kimlikle
/// çağrılıyor. 403 = izin reddi, 404 = izin GEÇTİ ve kayıt bulunamadı.
/// Yani izin kapısının sonucu durum kodundan tek anlamlı okunuyor.
///
/// ROL SEÇİMİ: `Ön Muhasebe` hem `accounting.edit` hem
/// `accounting.manage` taşıyor (canlıda ölçüldü). İkisi bir arada
/// olmasaydı soru sorulamazdı.
/// </summary>
[Collection("Integration")]
public sealed class KabaIzinDenyEzmesiTests(DatabaseFixture fixture)
{
    private const string Rol = "Ön Muhasebe";

    private async Task<HttpResponseMessage> CagirAsync(bool denyKoy)
    {
        var kullanici = await TestUserFactory.KullaniciKurAsync(
            fixture, denyKoy ? "deny-var" : "deny-yok", [Rol]);

        if (denyKoy)
        {
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var izinId = await db.Permissions
                .Where(x => x.Key == "accounting.edit")
                .Select(x => x.Id)
                .SingleAsync();

            db.UserPermissionOverrides.Add(new UserPermissionOverride
            {
                UserId = kullanici.Id,
                PermissionId = izinId,
                Effect = PermissionOverrideEffect.Deny
            });
            await db.SaveChangesAsync();
        }

        return await kullanici.Istemci.PutAsJsonAsync(
            $"/api/accounting-accounts/{Guid.NewGuid()}",
            new { code = "999", name = "sonda", type = 1 });
    }

    [Fact]
    public async Task DenyVarken_KabaIzinTasisaBile_403_Geliyor()
    {
        using var yanit = await CagirAsync(denyKoy: true);

        Assert.True(
            yanit.StatusCode == HttpStatusCode.Forbidden,
            $"SIZINTI: `accounting.edit` üzerinde Deny olan kullanıcı " +
            $"HTTP {(int)yanit.StatusCode} aldı, 403 değil.\n" +
            "Rolünde `accounting.manage` var; kaba izin ince iznin Deny " +
            "kaydını eziyor. Kullanıcı bazlı kısıtlama, kapsayıcı izin " +
            "taşıyan kişide çalışmıyor demektir.");
    }

    /// <summary>POZİTİF KONTROL — 403'ün sebebi Deny mi, uç mu.</summary>
    [Fact]
    public async Task DenyYokken_AyniUc_403_Vermiyor()
    {
        using var yanit = await CagirAsync(denyKoy: false);

        Assert.True(
            yanit.StatusCode != HttpStatusCode.Forbidden,
            "POZİTİF KONTROL DÜŞTÜ: Deny OLMADAN da 403 geldi. " +
            "Bu durumda öteki ayağın yeşili hiçbir şey kanıtlamaz — " +
            "uç zaten bu role kapalı olabilir.");
    }
}
