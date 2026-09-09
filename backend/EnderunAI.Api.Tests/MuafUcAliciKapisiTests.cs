using System.Net.Http.Json;
using EnderunAI.Api.Models.Notifications;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// `alici-kapisi` KATEGORİSİ — 5 UÇ, HER BİRİ İKİ KOL (EKSİK/1 E3).
///
/// Gerekçenin iddiası: "satır başına alıcı/izin süzgeci okuma anında
/// uygulanır", ve yazma uçlarında "çağıranın kendi alıcı satırına
/// kilitli".
///
/// ═══ NEDEN KİŞİSEL BİLDİRİM SEÇİLDİ ═══
///
/// Zilde İKİ model var: ŞİRKET satırları (`TargetUserId` boş, görünürlük
/// `RequiredPermission` ile) ve KİŞİSEL satırlar (`TargetUserId` dolu).
///
/// Sonda kişisel satırı kuruyor ve `RequiredPermission` alanını BOŞ
/// bırakıyor — çünkü riskli olan şekil budur: izin süzgeci devreye
/// girmez, geriye yalnız alıcı süzgeci kalır. Şirket satırıyla
/// sınasaydık iki kullanıcı aynı rolde olduğu için izin süzgeci ikisini
/// de aynı yere koyardı ve sonda alıcı kapısını HİÇ ölçmezdi (Kural 65).
/// </summary>
[Collection("Integration")]
public sealed class MuafUcAliciKapisiTests(DatabaseFixture fixture)
{
    /// <summary>Sahibe ait, izinsiz (yalnız alıcıyla korunan) kişisel bildirim.</summary>
    private static Task KisiselBildirimKur(MuafUcZemini z) =>
        z.VeritabaniAsync(async db =>
        {
            var bildirim = new Notification
            {
                CompanyId = z.SirketId,
                Type = "sonda.muaf-uc",
                PeriodKey = z.Iz,
                Title = $"Kişisel bildirim {z.Iz}",
                Detail = z.Iz,
                Severity = NotificationSeverity.Info,
                Status = NotificationStatus.Open,
                TargetUserId = z.SahipId,
                RequiredPermission = null,
                FirstSeenAtUtc = DateTime.UtcNow,
                LastSeenAtUtc = DateTime.UtcNow
            };
            db.Notifications.Add(bildirim);

            var alici = new NotificationRecipient
            {
                NotificationId = bildirim.Id,
                UserId = z.SahipId
            };
            db.NotificationRecipients.Add(alici);

            await db.SaveChangesAsync();

            z.HedefId = bildirim.Id;
            z.KurulumNotu = alici.Id.ToString();
        });

    /// <summary>Sahibin bildiriminin durumu değişti mi (yazma uçlarının cevabı).</summary>
    private static Task<bool> BildirimDegistiMi(MuafUcZemini z) =>
        z.VeritabaniAsync(async db =>
        {
            var b = await db.Notifications.AsNoTracking()
                .SingleAsync(x => x.Id == z.HedefId);
            return b.Status != NotificationStatus.Open ||
                   b.ReadAtUtc is not null ||
                   b.DismissedAtUtc is not null ||
                   b.SnoozedUntil is not null;
        });

    [Fact]
    public Task Notifications_List() => MuafUcKosumu.IkiKolAsync(fixture, new MuafUc(
        "alici-kapisi", "Notifications.List",
        Kur: KisiselBildirimKur,
        Cagir: (istemci, z) => istemci.GetAsync(
            $"/api/bildirimler?companyId={z.SirketId}"),
        SahibinVerisineUlasildiMi: z => z.IzYanittaMi()));

    [Fact]
    public Task Notifications_MarkRead() => MuafUcKosumu.IkiKolAsync(fixture, new MuafUc(
        "alici-kapisi", "Notifications.MarkRead",
        Kur: KisiselBildirimKur,
        Cagir: (istemci, z) => istemci.PostAsync(
            $"/api/bildirimler/{z.HedefId}/okundu", null),
        SahibinVerisineUlasildiMi: BildirimDegistiMi));

    [Fact]
    public Task Notifications_Dismiss() => MuafUcKosumu.IkiKolAsync(fixture, new MuafUc(
        "alici-kapisi", "Notifications.Dismiss",
        Kur: KisiselBildirimKur,
        Cagir: (istemci, z) => istemci.PostAsync(
            $"/api/bildirimler/{z.HedefId}/kapat", null),
        SahibinVerisineUlasildiMi: BildirimDegistiMi));

    [Fact]
    public Task Notifications_Snooze() => MuafUcKosumu.IkiKolAsync(fixture, new MuafUc(
        "alici-kapisi", "Notifications.Snooze",
        Kur: KisiselBildirimKur,
        Cagir: (istemci, z) => istemci.PostAsJsonAsync(
            $"/api/bildirimler/{z.HedefId}/ertele",
            new { Until = DateTime.UtcNow.AddDays(3) }),
        SahibinVerisineUlasildiMi: BildirimDegistiMi));

    [Fact]
    public Task Notifications_MarkPersonalRead() => MuafUcKosumu.IkiKolAsync(fixture, new MuafUc(
        "alici-kapisi", "Notifications.MarkPersonalRead",
        Kur: KisiselBildirimKur,
        Cagir: (istemci, z) => istemci.PostAsync(
            $"/api/bildirimler/kisisel/{z.KurulumNotu}/okundu", null),
        SahibinVerisineUlasildiMi: z => z.VeritabaniAsync(async db =>
        {
            var aliciId = Guid.Parse(z.KurulumNotu);
            var a = await db.NotificationRecipients.AsNoTracking()
                .SingleAsync(x => x.Id == aliciId);
            return a.ReadAtUtc is not null;
        })));
}
