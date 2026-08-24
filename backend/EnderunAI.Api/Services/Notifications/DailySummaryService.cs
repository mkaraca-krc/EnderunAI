using System.Text;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
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
    IUserAuthorizationService authorization,
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
        CancellationToken cancellationToken)
    {
        if (mode == DailySummaryMode.Kapali)
            return 0;

        var kronometre = System.Diagnostics.Stopwatch.StartNew();

        var satirlar = await HesaplaAsync(cancellationToken);

        kronometre.Stop();

        /*
         * DRYRUN — GÖNDERİM YOLUNA HİÇ GİRİLMİYOR.
         *
         * Buradan sonrası e-posta kodudur ve `DryRun` o koda ULAŞMADAN
         * dönüyor. Sahte bir istemciyle değiştirmek yetmezdi: sahte
         * istemci "gönderim kodu çalıştı ama bir şey olmadı" demektir;
         * burada gönderim kodu HİÇ ÇALIŞMIYOR.
         *
         * Fark, bir gün gönderim yolunda yan etki doğduğunda ortaya
         * çıkar (kota tüketimi, harici kayıt, sıraya yazma).
         */
        if (mode == DailySummaryMode.DryRun)
        {
            KuruKosuKaydiYaz(satirlar, kronometre.ElapsedMilliseconds);
            return 0;
        }

        /*
         * E-POSTA YAPILANDIRILMAMIŞSA GÖNDERİM YOK.
         * `DryRun` bu kontrolün üstünde: amacı zaten göndermek değil.
         */
        if (!email.IsConfigured)
        {
            logger.LogWarning(
                "Günlük özet atlandı: e-posta yapılandırılmamış (IsConfigured=false).");
            return 0;
        }

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
                if (string.IsNullOrWhiteSpace(satir.Email))
                {
                    logger.LogWarning(
                        "Günlük özet atlandı: bir alıcının e-posta adresi yok.");
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
    /// KURU KOŞU KAYDI — TOPLU İSTATİSTİK, KİŞİSEL VERİ YOK.
    ///
    /// Yazılanlar: tarih, alıcı sayısı, alıcı başına satır sayısı
    /// (en az / ortalama / en çok), tetikleyici bazında dağılım,
    /// üretim süresi.
    ///
    /// YAZILMAYANLAR — BİLEREK: görev başlığı, kişi adı, kullanıcı
    /// adı, e-posta adresi, açıklama metni. Kuru koşu kaydının amacı
    /// "kaç kişiye ne kadar iş gidecek" sorusunu cevaplamak; kimin
    /// hangi işi var sorusunu değil. Günlük dosyası, kişisel veri ya
    /// da adres listesi tutulacak yer değildir ve bir kez okunup
    /// atılacak bir bilgi için o riski almaya gerek yok.
    ///
    /// Kalıcı tabloya da yazılmıyor: bu bilgi bir kez okunup
    /// atılacak, tabloya yazmak onu kalıcı bir borç haline getirirdi.
    /// </summary>
    private void KuruKosuKaydiYaz(
        IReadOnlyList<DailySummaryRow> satirlar,
        long sureMs)
    {
        var aliciSayisi = satirlar.Count;

        var satirSayilari = satirlar
            .Select(x =>
                x.OpenTaskCount + x.OverdueCount +
                x.AwaitingApprovalCount + x.UnreadNotificationCount)
            .ToList();

        var enAz = satirSayilari.Count == 0 ? 0 : satirSayilari.Min();
        var enCok = satirSayilari.Count == 0 ? 0 : satirSayilari.Max();
        var ortalama = satirSayilari.Count == 0 ? 0 : satirSayilari.Average();

        logger.LogInformation(
            "GÜNLÜK ÖZET (kuru koşu) tarih={Tarih} aliciSayisi={Alici} " +
            "satirEnAz={EnAz} satirOrtalama={Ortalama:F1} satirEnCok={EnCok} " +
            "acikGorev={Acik} terminGecen={Gecen} onayBekleyen={Onay} " +
            "okunmamisBildirim={Bildirim} uretimSuresiMs={Sure}",
            DateTime.UtcNow.ToString("yyyy-MM-dd"),
            aliciSayisi,
            enAz,
            ortalama,
            enCok,
            satirlar.Sum(x => x.OpenTaskCount),
            satirlar.Sum(x => x.OverdueCount),
            satirlar.Sum(x => x.AwaitingApprovalCount),
            satirlar.Sum(x => x.UnreadNotificationCount),
            sureMs);
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

            /*
             * KAPSAM SÜZGECİ — HER ALICI KENDİ KAPSAMINI GÖRÜR.
             *
             * "Bana atanmış" süzgeci tek başına yetmez: kapsam
             * değişikliğinden ÖNCE atanmış bir görev, kullanıcı artık
             * o projeyi göremese bile üzerinde kalır. Özet, kullanıcının
             * göremeyeceği bir kaydın varlığını sayı olarak bile
             * sızdırmamalı.
             *
             * Kapsam ARKA PLANDA çözülüyor: `ICurrentDataScopeService`
             * çağıran kullanıcıya bağlı ve burada çağıran yok — her
             * alıcının kapsamı `IUserAuthorizationService` üzerinden
             * ayrı ayrı kuruluyor.
             */
            var kapsam = await KullaniciKapsamiAsync(kullanici.Id, cancellationToken);

            var kapsamliGorevler = db.WorkTasks
                .AsNoTracking()
                .ApplyScope(kapsam);

            var acik = await kapsamliGorevler
                .CountAsync(
                    x => x.AssignedToUserId == kullanici.Id &&
                         AcikDurumlar.Contains(x.Status),
                    cancellationToken);

            var gecen = await kapsamliGorevler
                .CountAsync(
                    x => x.AssignedToUserId == kullanici.Id &&
                         AcikDurumlar.Contains(x.Status) &&
                         x.DueDate != null && x.DueDate < simdi,
                    cancellationToken);

            // ONAYIMI BEKLEYENLER: gönderdiğim ve tamamlanmış görevler.
            var onay = await kapsamliGorevler
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

    /// <summary>
    /// Bir kullanıcının veri kapsamı — arka planda, çağıran olmadan.
    ///
    /// `ICurrentDataScopeService` oturumdaki kullanıcıya bağlı; bu
    /// servis arka planda çalışıyor ve HER ALICI için ayrı kapsam
    /// kurmak zorunda.
    ///
    /// Rol adı "Admin" olan ya da `All` kapsamı bulunan kullanıcı
    /// global erişimli sayılıyor — `CurrentDataScopeService` ile aynı
    /// kural; iki yerde farklı davranırsa kullanıcı ekranda gördüğü
    /// işi özette göremezdi.
    /// </summary>
    private async Task<CurrentDataScopeSnapshot> KullaniciKapsamiAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var yetki = await authorization.GetAsync(userId, cancellationToken);

        if (yetki is null)
        {
            // Yetki çözülemedi: HİÇBİR ŞEY GÖRMESİN. Boş kapsam,
            // global kapsamdan güvenli tarafta kalır.
            return new CurrentDataScopeSnapshot(
                false, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>(),
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        }

        var globalErisim =
            yetki.RoleNames.Contains("Admin", StringComparer.OrdinalIgnoreCase) ||
            yetki.DataScopes.Any(x => x.ScopeType == 0);

        return new CurrentDataScopeSnapshot(
            globalErisim,
            yetki.DataScopes.Where(x => x.ScopeType == 1 && x.CompanyId.HasValue)
                .Select(x => x.CompanyId!.Value).ToHashSet(),
            yetki.DataScopes.Where(x => x.ScopeType == 2 && x.BranchId.HasValue)
                .Select(x => x.BranchId!.Value).ToHashSet(),
            yetki.DataScopes.Where(x => x.ScopeType == 3 && x.ProjectId.HasValue)
                .Select(x => x.ProjectId!.Value).ToHashSet(),
            yetki.DataScopes.Where(x => x.CompanyId.HasValue)
                .Select(x => x.CompanyId!.Value).ToHashSet(),
            yetki.DataScopes.Where(x => x.BranchId.HasValue)
                .Select(x => x.BranchId!.Value).ToHashSet(),
            yetki.DataScopes.Where(x => x.ProjectSiteId.HasValue)
                .Select(x => x.ProjectSiteId!.Value).ToHashSet());
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
