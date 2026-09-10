using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// MESAİ/1 — OKUNAMAYAN BİR KULLANICI SATIRI "MESAİ DIŞI" DEĞİLDİR.
///
/// ═══ ÖLÇÜLEN KUSUR (2026-09-10, çağrılarak) ═══
///
/// Satırı silinmiş bir kullanıcının hâlâ geçerli jetonuyla
/// `/api/auth/me` çağrıldı. Cevap: 401 "Mesai saatiniz sona erdiği için
/// oturumunuz kapatıldı", `outsideWorkHours:true` — ve denetim kaydına
/// bir `WorkHoursSessionRejected` satırı. Kullanıcıya YANLIŞ sebep
/// söyleniyor, denetime YANLIŞ olay yazılıyordu: satırı okunamayan
/// kullanıcı mesai kapısından "mesai bitti" diye geri çevriliyordu.
///
/// ═══ İDDİA EDİLİP ÖLÇÜLÜNCE ÇÜRÜYEN ZİNCİR ═══
///
/// Aynı gece "okunamayan satır → izleyici ucu 200 `isAllowed:false` →
/// izleyici çıkış çağırır → kalıcı çıkış" zinciri KOD OKUNARAK iddia
/// edildi. Çağrılınca izleyici ucu 401 HesapPasif döndü: izin ara
/// katmanı eksik kullanıcıyı controller'a ulaşmadan reddediyor. Zincir
/// bugün o yoldan kurulamıyor — ama bunu sağlayan şey bir TASARIM
/// değil, ara katman SIRASI. Sıra değişirse ya da yetki çözücüye önbellek
/// girerse zincir açılır. Bu dosya o sırayı değil, SONUCU kilitliyor.
/// </summary>
[Collection("Integration")]
public sealed class OkunamayanSatirMesaiDisiDegildirTests(DatabaseFixture fixture)
{
    private const string HesapPasifMesaji = "Kullanıcı hesabı pasif veya bulunamadı.";

    /// <summary>
    /// Penceresi olmayan rastgele bir rol + 30 dakikalık geçici erişim:
    /// giriş yapabilen ama erişimi kapanınca GERÇEKTEN mesai dışı olan,
    /// test saatinden bağımsız bir kullanıcı.
    /// </summary>
    private async Task<(HttpClient Istemci, Guid Id)> OturumluPenceresizKullaniciAsync(string ek)
    {
        string kullaniciAdi;
        Guid id;
        const string parola = "MesaiKarari!2026";

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var parolaServisi = scope.ServiceProvider.GetRequiredService<PasswordService>();

            var rol = new AppRole { Name = $"MesaiKarari-{ek}-{Guid.NewGuid():N}"[..40] };
            db.Roles.Add(rol);
            await db.SaveChangesAsync();

            kullaniciAdi = $"mesai-karari-{ek}-{Guid.NewGuid():N}"[..40];
            var ozet = parolaServisi.Hash(parola);
            var kullanici = new AppUser
            {
                Username = kullaniciAdi,
                FullName = $"Mesai Karari {ek}",
                PasswordHash = ozet.Hash,
                PasswordSalt = ozet.Salt,
                IsActive = true
            };
            db.Users.Add(kullanici);
            await db.SaveChangesAsync();
            id = kullanici.Id;

            db.UserRoles.Add(new UserRole { UserId = id, RoleId = rol.Id });
            db.UserDataScopes.Add(new UserDataScope { UserId = id, ScopeType = DataScopeType.All });
            db.TemporaryAccessGrants.Add(new TemporaryAccessGrant
            {
                UserId = id,
                GrantedByUserId = id,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
            });
            await db.SaveChangesAsync();
        }

        var istemci = fixture.Factory.CreateClient();
        var jeton = await AuthHelper.LoginAsync(istemci, kullaniciAdi, parola);
        istemci.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        return (istemci, id);
    }

    private async Task SatiriSilAsync(Guid id)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TemporaryAccessGrants.RemoveRange(db.TemporaryAccessGrants.Where(g => g.UserId == id));
        db.UserRoles.RemoveRange(db.UserRoles.Where(r => r.UserId == id));
        db.UserDataScopes.RemoveRange(db.UserDataScopes.Where(s => s.UserId == id));
        db.Users.Remove(await db.Users.SingleAsync(u => u.Id == id));
        await db.SaveChangesAsync();

        // Silme olmadıysa aşağıdaki iddialar bir şey ölçmez (Kural 48).
        Assert.False(await db.Users.AnyAsync(u => u.Id == id));
    }

    private async Task ErisimiKapatAsync(Guid id)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var erisim = await db.TemporaryAccessGrants.SingleAsync(g => g.UserId == id);
        erisim.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> GovdeAsync(HttpResponseMessage yanit) =>
        await yanit.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>
    /// KIRMIZIYA DÖNERSE: satırı okunamayan kullanıcıya yine "mesainiz
    /// bitti" denir ve denetime sahte bir mesai reddi yazılır — "bu
    /// kullanıcı neden çıkarıldı?" sorusu yanlış cevaplanır.
    /// </summary>
    [Fact]
    public async Task SatiriOlmayanKullanici_MesaiDisiDiyeEtiketlenmez()
    {
        var (istemci, id) = await OturumluPenceresizKullaniciAsync("satirsiz");

        // Pozitif kontrol: silmeden önce oturum çalışıyor.
        Assert.Equal(HttpStatusCode.OK, (await istemci.GetAsync("/api/auth/me")).StatusCode);

        await SatiriSilAsync(id);

        var yanit = await istemci.GetAsync("/api/auth/me");
        var govde = await GovdeAsync(yanit);

        Assert.Equal(HttpStatusCode.Unauthorized, yanit.StatusCode);
        Assert.False(
            govde.TryGetProperty("outsideWorkHours", out _),
            $"Satırı olmayan kullanıcı mesai dışı diye etiketlendi: {govde}");
        Assert.Equal(HesapPasifMesaji, govde.GetProperty("message").GetString());

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.SecurityAuditEvents.CountAsync(
            e => e.ActorUserId == id && e.Action == "WorkHoursSessionRejected"));
    }

    /// <summary>
    /// KIRMIZIYA DÖNERSE: izleyicinin sorduğu uç, satırı olmayan
    /// kullanıcı için bir mesai KARARI üretir; izleyici kalıcı çıkışı
    /// yalnız "mesai-disi" kararında yaptığı için bu karar yanlışsa
    /// oturum düşer. Bugün bunu engelleyen şey ara katman sırası.
    /// </summary>
    [Fact]
    public async Task SatiriOlmayanKullanici_IzleyiciUcuMesaiKarariUretmez()
    {
        var (istemci, id) = await OturumluPenceresizKullaniciAsync("izleyici");
        await SatiriSilAsync(id);

        var yanit = await istemci.GetAsync("/api/auth/work-hours-status");
        var govde = await GovdeAsync(yanit);

        Assert.NotEqual(HttpStatusCode.OK, yanit.StatusCode);
        Assert.False(
            govde.TryGetProperty("karar", out var karar) && karar.GetString() == "mesai-disi",
            $"Satırı olmayan kullanıcı için mesai-disi kararı üretildi: {govde}");
    }

    /// <summary>
    /// KAYNAKTA: değerlendirme, satırı olmayan kullanıcı için KARAR
    /// VERMEDİĞİNİ söyler. HTTP testleri ara katman sırasına bağlı;
    /// bu test sıradan bağımsız, sorunun doğduğu yeri kilitliyor.
    ///
    /// KIRMIZIYA DÖNERSE: servisi çağıran HER yer (giriş, ara katman,
    /// izleyici ucu) okunamayan satırı yine "mesai dışı" sayar.
    /// </summary>
    [Fact]
    public async Task Degerlendirme_SatirYoksa_KararBelirlenemedi()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var servis = scope.ServiceProvider.GetRequiredService<IWorkHourAccessService>();

        var sonuc = await servis.EvaluateAsync(Guid.NewGuid());

        Assert.Equal(MesaiKarari.Belirlenemedi, sonuc.Karar);
        Assert.False(sonuc.IsAllowed);
    }

    /// <summary>
    /// POZİTİF KONTROL — GERÇEK MESAİ DIŞI AÇIKÇA SÖYLENİR.
    ///
    /// KIRMIZIYA DÖNERSE: izleyici gerçek mesai dışını tanıyamaz ve
    /// mesaisi biten kullanıcı OTURUMDA KALIR. Yukarıdaki iki test
    /// "çıkış yok" der; bu test "çıkışı üreten karar hâlâ var" der —
    /// ikisi birlikte yazılmazsa "hiç çıkış yapmayan izleyici" de geçer.
    /// </summary>
    [Fact]
    public async Task GercekMesaiDisi_KararAcikcaMesaiDisi()
    {
        var (istemci, id) = await OturumluPenceresizKullaniciAsync("gercek");

        var acik = await istemci.GetAsync("/api/auth/work-hours-status");
        var acikGovde = await GovdeAsync(acik);
        Assert.Equal(HttpStatusCode.OK, acik.StatusCode);
        Assert.Equal("izinli", acikGovde.GetProperty("karar").GetString());
        Assert.True(acikGovde.GetProperty("isAllowed").GetBoolean());

        await ErisimiKapatAsync(id);

        var kapali = await istemci.GetAsync("/api/auth/work-hours-status");
        var kapaliGovde = await GovdeAsync(kapali);
        Assert.Equal(HttpStatusCode.OK, kapali.StatusCode);
        Assert.Equal("mesai-disi", kapaliGovde.GetProperty("karar").GetString());
        Assert.False(kapaliGovde.GetProperty("isAllowed").GetBoolean());

        // Ara katman da aynı kararı veriyor: satır okunabiliyorsa etiket doğru.
        var me = await istemci.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.True((await GovdeAsync(me)).GetProperty("outsideWorkHours").GetBoolean());
    }
}
