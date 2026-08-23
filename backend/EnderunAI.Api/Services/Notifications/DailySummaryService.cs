using System.Text;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Notifications;
using EnderunAI.Api.Services.Email;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Notifications;

/// <summary>Bir kişinin günlük özeti — gönderilmeden önce hesaplanan hâli.</summary>
public sealed record DailySummaryRow(
    Guid UserId,
    string Username,
    string? FullName,
    string? Email,
    int OpenTaskCount,
    int OverdueCount,
    int AwaitingApprovalCount,
    int UnreadNotificationCount)
{
    /// <summary>
    /// BOŞ ÖZET GÖNDERİLMEZ.
    ///
    /// Yapacak işi olmayan kişiye "0 açık göreviniz var" e-postası
    /// atmak, zilin kapatılmasıyla aynı sonucu doğurur: insanlar
    /// okumamayı öğrenir ve gerçekten önemli olanı da kaçırır.
    /// </summary>
    public bool HasContent =>
        OpenTaskCount > 0 ||
        OverdueCount > 0 ||
        AwaitingApprovalCount > 0 ||
        UnreadNotificationCount > 0;
}

/// <summary>
/// GÜNLÜK E-POSTA ÖZETİ.
///
/// SAAT: 04:00 UTC = 07:00 Türkiye. Sunucu `Etc/UTC` (ölçüldü) ve
/// Türkiye sabit UTC+3, yaz saati uygulaması YOK. Kodda "07:00" yazıp
/// sunucunun UTC olduğunu unutmak, özetin sabah 10'da gitmesi
/// demekti.
///
/// KİŞİ BAZINDA HATA SINIRI: bir kişinin gönderimi patlarsa tur
/// diğerlerine DEVAM EDER. Tek kişinin bozuk adresi yüzünden kimsenin
/// özet almaması, sessizce yutmaktan farksız bir arıza olurdu.
/// </summary>
public sealed class DailySummaryService(
    AppDbContext db,
    IEmailService email,
    ILogger<DailySummaryService> logger)
{
    /// <summary>Türkiye saatiyle 07:00 — sunucu UTC olduğu için 04:00.</summary>
    public const int GonderimSaatiUtc = 4;

    private static readonly WorkTaskStatus[] AcikDurumlar =
    [
        WorkTaskStatus.Open,
        WorkTaskStatus.InProgress,
        WorkTaskStatus.Returned
    ];

    public async Task<int> RunAsync(
        DailySummaryMode mode,
        IReadOnlyCollection<string> testRecipients,
        CancellationToken cancellationToken)
    {
        if (mode == DailySummaryMode.Off)
            return 0;

        /*
         * E-POSTA YAPILANDIRILMAMIŞSA HİÇ KOŞMA.
         *
         * `dryrun` bunun istisnası: amacı zaten göndermek değil, kime
         * ne gideceğini görmek. Yapılandırma beklemesi gereksiz.
         */
        if (mode != DailySummaryMode.DryRun && !email.IsConfigured)
        {
            logger.LogWarning(
                "Günlük özet atlandı: e-posta yapılandırılmamış (IsConfigured=false).");
            return 0;
        }

        var satirlar = await HesaplaAsync(cancellationToken);
        var gonderilen = 0;

        foreach (var satir in satirlar)
        {
            /*
             * HER KİŞİ KENDİ HATA SINIRINDA.
             *
             * Döngünün dışında tek bir try olsaydı ilk hata turu
             * bitirirdi ve sonraki kişiler sessizce atlanırdı.
             */
            try
            {
                if (mode == DailySummaryMode.DryRun)
                {
                    KuruKayitYaz(satir, gonderilecek: true);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(satir.Email))
                {
                    logger.LogWarning(
                        "Günlük özet atlandı: {Username} için e-posta adresi yok.",
                        satir.Username);
                    continue;
                }

                if (mode == DailySummaryMode.Test &&
                    !testRecipients.Contains(satir.Email, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                await email.SendAsync(
                    satir.Email,
                    satir.FullName,
                    "Günlük iş özetiniz",
                    GovdeUret(satir),
                    cancellationToken);

                gonderilen++;
            }
            catch (Exception exception)
            {
                /*
                 * BİLDİRİM YAZIMINDAKİ DESENİN AYNISI: hata yutulmuyor
                 * ama tur durmuyor. Kayda düşüyor ve sonraki kişiye
                 * geçiliyor.
                 */
                logger.LogError(
                    exception,
                    "Günlük özet gönderilemedi. Username={Username}",
                    satir.Username);

                await HataKaydiYazAsync(satir, exception, cancellationToken);
            }
        }

        return gonderilen;
    }

    /// <summary>
    /// KURU KOŞU KAYDI — sunucu günlüğüne, kalıcı tabloya değil.
    ///
    /// Bir kez okunup atılacak bir bilgi; tabloya yazmak onu kalıcı
    /// bir borç haline getirirdi.
    ///
    /// E-POSTA ADRESİ YAZILMIYOR: kullanıcı adı kimi kastettiğimizi
    /// söylemeye yetiyor ve günlük, adres listesi tutulacak yer değil.
    /// </summary>
    private void KuruKayitYaz(DailySummaryRow satir, bool gonderilecek)
    {
        logger.LogInformation(
            "GÜNLÜK ÖZET (kuru koşu) kullanici={Username} acikGorev={Acik} " +
            "terminGecen={Gecen} onayBekleyen={Onay} okunmamisBildirim={Bildirim} " +
            "gonderilecekMi={Gonderilecek}",
            satir.Username,
            satir.OpenTaskCount,
            satir.OverdueCount,
            satir.AwaitingApprovalCount,
            satir.UnreadNotificationCount,
            gonderilecek);
    }

    /// <summary>
    /// Özeti olan kullanıcılar. BOŞ OLANLAR HİÇ DÖNMÜYOR — filtre
    /// burada, gönderim tarafında değil: kuru koşu kaydı da yalnız
    /// gerçekten e-posta alacak kişileri göstermeli.
    /// </summary>
    public async Task<IReadOnlyList<DailySummaryRow>> HesaplaAsync(
        CancellationToken cancellationToken)
    {
        var simdi = DateTime.UtcNow;

        var kullanicilar = await db.Users
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new { x.Id, x.Username, x.FullName, x.Email })
            .ToListAsync(cancellationToken);

        // TERCİHİ KAPALI OLANLAR ELENİYOR — zil etkilenmiyor, yalnız
        // e-posta.
        var kapatanlar = await db.UserUiPreferences
            .AsNoTracking()
            .Where(x => !x.DailySummaryEmailEnabled)
            .Select(x => x.UserId)
            .ToListAsync(cancellationToken);

        var kapali = kapatanlar.ToHashSet();

        var sonuc = new List<DailySummaryRow>();

        foreach (var kullanici in kullanicilar)
        {
            if (kapali.Contains(kullanici.Id))
                continue;

            var acik = await db.WorkTasks
                .AsNoTracking()
                .CountAsync(
                    x => x.AssignedToUserId == kullanici.Id &&
                         AcikDurumlar.Contains(x.Status),
                    cancellationToken);

            var gecen = await db.WorkTasks
                .AsNoTracking()
                .CountAsync(
                    x => x.AssignedToUserId == kullanici.Id &&
                         AcikDurumlar.Contains(x.Status) &&
                         x.DueDate != null && x.DueDate < simdi,
                    cancellationToken);

            // ONAYIMI BEKLEYENLER: gönderdiğim ve tamamlanmış görevler.
            var onay = await db.WorkTasks
                .AsNoTracking()
                .CountAsync(
                    x => x.AssignedByUserId == kullanici.Id &&
                         x.Status == WorkTaskStatus.Completed,
                    cancellationToken);

            var bildirim = await db.NotificationRecipients
                .AsNoTracking()
                .CountAsync(
                    x => x.UserId == kullanici.Id &&
                         x.ReadAtUtc == null &&
                         x.DismissedAtUtc == null,
                    cancellationToken);

            var satir = new DailySummaryRow(
                kullanici.Id, kullanici.Username, kullanici.FullName, kullanici.Email,
                acik, gecen, onay, bildirim);

            if (satir.HasContent)
                sonuc.Add(satir);
        }

        return sonuc;
    }

    private static string GovdeUret(DailySummaryRow satir)
    {
        var govde = new StringBuilder();

        govde.Append("<p>Merhaba ");
        govde.Append(System.Net.WebUtility.HtmlEncode(satir.FullName ?? satir.Username));
        govde.Append(",</p><p>Bugünkü iş özetiniz:</p><ul>");

        if (satir.OpenTaskCount > 0)
            govde.Append($"<li>Açık göreviniz: <b>{satir.OpenTaskCount}</b></li>");

        // TERMİNİ GEÇEN ÖNCE VE VURGULU: özetin asıl işi bunu
        // göstermek.
        if (satir.OverdueCount > 0)
            govde.Append($"<li><b>Termini geçen: {satir.OverdueCount}</b></li>");

        if (satir.AwaitingApprovalCount > 0)
            govde.Append($"<li>Onayınızı bekleyen: <b>{satir.AwaitingApprovalCount}</b></li>");

        if (satir.UnreadNotificationCount > 0)
            govde.Append($"<li>Okunmamış bildirim: <b>{satir.UnreadNotificationCount}</b></li>");

        govde.Append("</ul><p>Ayrıntı için sisteme giriş yapın.</p>");

        return govde.ToString();
    }

    private async Task HataKaydiYazAsync(
        DailySummaryRow satir,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            db.ChangeTracker.Clear();

            db.SecurityAuditEvents.Add(new SecurityAuditEvent
            {
                ActorUserId = satir.UserId,
                ActorUsername = satir.Username,
                Action = "DailySummaryEmailFailed",
                EntityType = "DailySummary",
                DetailsJson = JsonSerializer.Serialize(new
                {
                    summary = "Günlük özet e-postası gönderilemedi; tur devam etti.",
                    hata = exception.GetType().Name,
                    mesaj = exception.Message
                }),
                OccurredAtUtc = DateTime.UtcNow
            });

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ikincil)
        {
            logger.LogError(ikincil, "Günlük özet hata kaydı yazılamadı.");
        }
    }
}
