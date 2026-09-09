using System.Net.Http.Json;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// `kendi-kimligi` KATEGORİSİ — 6 UÇ, HER BİRİ İKİ KOL (EKSİK/1 E3).
///
/// Gerekçenin iddiası: "kimlik OTURUMDAN gelir, istekten değil; çağıran
/// yalnız kendi kaydına ulaşabilir". Bu sınıftaki her test o cümleyi
/// çağırarak sınıyor.
///
/// RİSK SIRASI: bu kategori ÖNCE koşuyor, çünkü gerekçesi yanlışsa
/// sonuç doğrudan "biri başkasının verisini gördü" olur.
///
/// Şekil `MuafUcKosumu`'nda; burada yalnız ÖRNEKLER var.
/// </summary>
[Collection("Integration")]
public sealed class MuafUcKendiKimligiTests(DatabaseFixture fixture)
{
    [Fact]
    public Task Auth_Me() => MuafUcKosumu.IkiKolAsync(fixture, new MuafUc(
        "kendi-kimligi", "Auth.Me",
        // İz kullanıcı adının ve tam adın içinde: ayrıca kurulum gerekmiyor.
        Kur: _ => Task.CompletedTask,
        Cagir: (istemci, _) => istemci.GetAsync("/api/auth/me"),
        SahibinVerisineUlasildiMi: z => z.IzYanittaMi()));

    [Fact]
    public Task Auth_WorkHoursStatus() => MuafUcKosumu.IkiKolAsync(fixture, new MuafUc(
        "kendi-kimligi", "Auth.WorkHoursStatus",
        /*
         * GEREKÇENİN TAM CÜMLESİ SINANIYOR: "istekten kullanıcı kimliği
         * ALINMAZ". O yüzden yabancı, sahibin kimliğini isteğe KOYARAK
         * çağırıyor. Uç kimliği istekten alsaydı yabancı, sahibin
         * durumunu görürdü.
         *
         * AYIRT EDİCİ İŞARET: sahip mesai istisnasından çıkarılıyor,
         * yabancı istisnalı kalıyor. Böylece iki cevap birbirinden
         * ayrılabiliyor. (Bu uç mesai ara katmanından muaf —
         * WorkHourAccessMiddleware.cs:35 — yani istisnasız kullanıcı da
         * çağırabiliyor.)
         */
        Kur: async z => await z.VeritabaniAsync(async db =>
        {
            var sahip = await db.Users.SingleAsync(x => x.Id == z.SahipId);
            sahip.WorkHoursExempt = false;
            await db.SaveChangesAsync();
        }),
        Cagir: (istemci, z) => istemci.GetAsync(
            $"/api/auth/work-hours-status?userId={z.SahipId}"),
        // Sahibin ayırt edici durumu: isExempt=false.
        SahibinVerisineUlasildiMi: z =>
            Task.FromResult(z.SonGovde.Contains("\"isExempt\":false"))));

    [Fact]
    public Task Auth_ChangePassword() => MuafUcKosumu.IkiKolAsync(fixture, new MuafUc(
        "kendi-kimligi", "Auth.ChangePassword",
        /*
         * YAZMA UCU: "ulaşma" burada "sahibin parolası DEĞİŞTİ Mİ"
         * demek. Yabancı kendi parolasını değiştirebilir; sahibinkine
         * dokunamamalı.
         */
        Kur: async z => z.KurulumNotu = await z.VeritabaniAsync(db => db.Users
            .AsNoTracking()
            .Where(x => x.Id == z.SahipId)
            .Select(x => x.PasswordHash)
            .SingleAsync()),
        Cagir: (istemci, z) => istemci.PostAsJsonAsync(
            "/api/auth/change-password",
            new
            {
                CurrentPassword = TestUserFactory.Parola,
                NewPassword = $"Yeni!{z.Iz}Parola9",
                NewPasswordConfirm = $"Yeni!{z.Iz}Parola9"
            }),
        SahibinVerisineUlasildiMi: async z =>
        {
            var oncekiOzet = z.KurulumNotu;
            var simdiki = await z.VeritabaniAsync(db => db.Users
                .AsNoTracking()
                .Where(x => x.Id == z.SahipId)
                .Select(x => x.PasswordHash)
                .SingleAsync());
            return simdiki != oncekiOzet;
        }));

    [Fact]
    public Task UserPreferences_Get() => MuafUcKosumu.IkiKolAsync(fixture, new MuafUc(
        "kendi-kimligi", "UserPreferences.Get",
        Kur: z => z.VeritabaniAsync(async db =>
        {
            db.UserUiPreferences.Add(new UserUiPreference
            {
                UserId = z.SahipId,
                FavoritePaths = [$"/{z.Iz}"]
            });
            await db.SaveChangesAsync();
        }),
        Cagir: (istemci, _) => istemci.GetAsync("/api/user-preferences"),
        SahibinVerisineUlasildiMi: z => z.IzYanittaMi()));

    [Fact]
    public Task UserPreferences_Save() => MuafUcKosumu.IkiKolAsync(fixture, new MuafUc(
        "kendi-kimligi", "UserPreferences.Save",
        Kur: z => z.VeritabaniAsync(async db =>
        {
            db.UserUiPreferences.Add(new UserUiPreference
            {
                UserId = z.SahipId,
                FavoritePaths = ["/sahibin-kendi-yolu"]
            });
            await db.SaveChangesAsync();
        }),
        // Yabancı kendi tercihini yazıyor; sahibinki değişmemeli.
        Cagir: (istemci, z) => istemci.PutAsJsonAsync(
            "/api/user-preferences",
            new { SidebarCollapsed = true, FavoritePaths = new[] { $"/{z.Iz}" } }),
        SahibinVerisineUlasildiMi: async z =>
        {
            var yollar = await z.VeritabaniAsync(db => db.UserUiPreferences
                .AsNoTracking()
                .Where(x => x.UserId == z.SahipId)
                .Select(x => x.FavoritePaths)
                .SingleAsync());
            return yollar.Any(y => y.Contains(z.Iz));
        }));

    [Fact]
    public Task IsgPersonnelRecords_GetOwnCard() => MuafUcKosumu.IkiKolAsync(fixture, new MuafUc(
        "kendi-kimligi", "IsgPersonnelRecords.GetOwnCard",
        Kur: z => z.VeritabaniAsync(async db =>
        {
            var personel = new Personnel
            {
                CompanyId = z.SirketId,
                FirstName = "Sonda",
                LastName = z.Iz,
                EmployeeNumber = z.Iz,
                IsActive = true
            };
            db.Personnel.Add(personel);
            await db.SaveChangesAsync();

            var sahip = await db.Users.SingleAsync(x => x.Id == z.SahipId);
            sahip.PersonnelId = personel.Id;
            await db.SaveChangesAsync();
        }),
        Cagir: (istemci, _) => istemci.GetAsync("/api/isg/benim"),
        SahibinVerisineUlasildiMi: z => z.IzYanittaMi()));
}
