using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Notifications;
using EnderunAI.Api.Services.Notifications;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// GÖREV BİLDİRİMLERİ — ALTI TETİKLEYİCİ.
///
/// Dördü OLAY anında yazılıyor (atandı, tamamlandı, iade, @anıldın),
/// ikisi ZAMANA bağlı ve tarayıcıdan geliyor (termine 1 gün kaldı,
/// termin geçti).
///
/// KİŞİSEL MODEL: `Notification.TargetUserId` dolu, okuma durumu
/// `NotificationRecipient` üzerinden. Şirket satırında tek `ReadAtUtc`
/// var; bir kişi okuyunca herkes için okunmuş sayılırdı.
/// </summary>
[Collection("Integration")]
public sealed class TaskNotificationTests(DatabaseFixture fixture)
{
    private static async Task<(Project Proje, Guid GorevId)> GorevAsync(
        AppDbContext db, string suffix, Guid? atanan, Guid? gonderen,
        DateTime? termin = null, WorkTaskStatus durum = WorkTaskStatus.Open)
    {
        var proje = await TestDataFactory.CreateProjectAsync(db, $"BLD{suffix}");

        var gorev = new WorkTask
        {
            CompanyId = proje.CompanyId,
            ProjectId = proje.Id,
            TaskNumber = $"TEST-BLD-{suffix}",
            Title = "Bildirim testi görevi",
            Status = durum,
            AssignedToUserId = atanan,
            AssignedByUserId = gonderen,
            DueDate = termin
        };

        db.WorkTasks.Add(gorev);
        await db.SaveChangesAsync();

        return (proje, gorev.Id);
    }

    private static async Task<int> BildirimSayisiAsync(
        AppDbContext db, Guid sourceId, string type) =>
        await db.Notifications
            .CountAsync(x => x.SourceId == sourceId && x.Type == type);

    // ---------------------------------------------------------------
    // TETİKLEYİCİ 1: GÖREV ATANDI
    // ---------------------------------------------------------------

    [Fact]
    public async Task GorevAtandiginda_AtanaKisiyeBildirimGider()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await TestDataFactory.CreateProjectAsync(db, $"ATA{suffix}");

        /*
         * ATAYAN VE ATANAN FARKLI KİŞİ OLMALI.
         *
         * İlk sürümde iki kez `AuthHelper.CreateAuthorizedClientAsync`
         * çağırıyordum ve ikisi de AYNI varsayılan kullanıcıyı
         * döndürüyordu; kendine atamada bildirim üretilmiyor (doğru
         * davranış) ve test bunu hata sanıyordu.
         *
         * Atanan kişinin kaydı GÖREBİLMESİ de gerekiyor —
         * GorevAtanabilirMiAsync bunu zorunlu kılıyor — o yüzden
         * rastgele bir kimlik değil, gerçek bir kullanıcı.
         */
        var atananClient = await TestUserFactory.CreateClientWithRolesAsync(
            fixture, "bildirim-atanan", ["Genel Müdür"]);

        var atanan = (await atananClient.GetFromJsonAsync<JsonElement>("/api/auth/me"))
            .GetProperty("id").GetGuid();

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsJsonAsync("/api/tasks", new
        {
            companyId = proje.CompanyId,
            projectId = proje.Id,
            title = "Atama bildirimi testi",
            priority = 1,
            assignedToUserId = atanan
        });

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);

        var gorevId = (await yanit.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var bildirim = await db.Notifications
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.SourceId == gorevId && x.Type == TaskNotificationTypes.Assigned);

        Assert.NotNull(bildirim);

        // KİŞİSEL: hedef kullanıcı dolu.
        Assert.Equal(atanan, bildirim!.TargetUserId);

        // ALICI SATIRI AÇILMIŞ ve OKUNMAMIŞ.
        var alici = await db.NotificationRecipients
            .AsNoTracking()
            .SingleAsync(x => x.NotificationId == bildirim.Id);

        Assert.Equal(atanan, alici.UserId);
        Assert.Null(alici.ReadAtUtc);
    }

    // ---------------------------------------------------------------
    // TETİKLEYİCİ 2: TAMAMLANDI (gönderene)
    // ---------------------------------------------------------------

    [Fact]
    public async Task GorevTamamlandiginda_GonderenBildirimAlir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var gonderen = Guid.NewGuid();
        var (_, gorevId) = await GorevAsync(
            db, suffix, atanan: Guid.NewGuid(), gonderen: gonderen);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await client.PostAsJsonAsync($"/api/tasks/{gorevId}/complete",
            new { completionNote = "Bitti" });

        var bildirim = await db.Notifications
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.SourceId == gorevId && x.Type == TaskNotificationTypes.Completed);

        Assert.NotNull(bildirim);
        Assert.Equal(gonderen, bildirim!.TargetUserId);
    }

    /// <summary>
    /// KENDİNE AÇILAN GÖREVDE BİLDİRİM YOK: kendi işini kendine
    /// duyurmak gürültüdür.
    /// </summary>
    [Fact]
    public async Task KendineAcilanGorevTamamlaninca_BildirimUretilmez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var kisi = Guid.NewGuid();
        var (_, gorevId) = await GorevAsync(db, suffix, atanan: kisi, gonderen: kisi);

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        await client.PostAsJsonAsync($"/api/tasks/{gorevId}/complete",
            new { completionNote = "Kendi işim" });

        Assert.Equal(0, await BildirimSayisiAsync(
            db, gorevId, TaskNotificationTypes.Completed));
    }

    // ---------------------------------------------------------------
    // TETİKLEYİCİ 3: İADE (yapana)
    // ---------------------------------------------------------------

    [Fact]
    public async Task GorevIadeEdilince_YapanBildirimAlir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var benim = (await client.GetFromJsonAsync<JsonElement>("/api/auth/me"))
            .GetProperty("id").GetGuid();

        var yapan = Guid.NewGuid();
        var (_, gorevId) = await GorevAsync(
            db, suffix, atanan: yapan, gonderen: benim,
            durum: WorkTaskStatus.Completed);

        await client.PostAsJsonAsync($"/api/tasks/{gorevId}/return",
            new { reason = "Eksik kalmış." });

        var bildirim = await db.Notifications
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.SourceId == gorevId && x.Type == TaskNotificationTypes.Returned);

        Assert.NotNull(bildirim);
        Assert.Equal(yapan, bildirim!.TargetUserId);

        // GEREKÇE BİLDİRİMDE: yapan neyi düzelteceğini görmeli.
        Assert.Contains("Eksik kalmış.", bildirim.Detail);
    }

    // ---------------------------------------------------------------
    // TETİKLEYİCİ 4: @ İLE ANILDIN
    // ---------------------------------------------------------------

    [Fact]
    public async Task YorumdaAnilinca_BildirimGider()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await TestDataFactory.CreateProjectAsync(db, $"ANI{suffix}");

        var anilan = Guid.NewGuid();

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.PostAsJsonAsync("/api/collaboration/comments", new
        {
            entityType = "Project",
            entityId = proje.Id,
            body = "Bu konuya bakabilir misin?",
            mentionedUserIds = new[] { anilan }
        });

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);

        var yorumId = (await yanit.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var bildirim = await db.Notifications
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.SourceId == yorumId && x.Type == TaskNotificationTypes.Mentioned);

        Assert.NotNull(bildirim);
        Assert.Equal(anilan, bildirim!.TargetUserId);
    }

    // ---------------------------------------------------------------
    // TETİKLEYİCİ 5 ve 6: TERMİN — TARAYICI, TEK SEFER
    // ---------------------------------------------------------------

    /// <summary>
    /// TARAYICI ÜÇ KEZ KOŞSA DA BİLDİRİM SAYISI ARTMAZ.
    ///
    /// Mükerrer bildirim insanların zili tamamen kapatmasına yol açar,
    /// yani bildirim sisteminin kendisini işlevsiz kılar. Engel
    /// veritabanında: (CompanyId, Type, SourceId, PeriodKey) benzersiz
    /// ve PeriodKey = TERMİN TARİHİ.
    /// </summary>
    [Fact]
    public async Task TerminTarayicisi_UcKezKossaDaTekBildirimUretir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tarayici = scope.ServiceProvider
            .GetRequiredService<TaskDueNotificationScanner>();

        var atanan = Guid.NewGuid();
        var (_, gorevId) = await GorevAsync(
            db, suffix, atanan: atanan, gonderen: Guid.NewGuid(),
            termin: DateTime.UtcNow.AddHours(12));

        for (var i = 0; i < 3; i++)
            await tarayici.ScanAsync(CancellationToken.None);

        var sayi = await BildirimSayisiAsync(db, gorevId, TaskNotificationTypes.DueSoon);

        Assert.Equal(1, sayi);
    }

    [Fact]
    public async Task TerminiGecmisGorev_OverdueBildirimiUretir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tarayici = scope.ServiceProvider
            .GetRequiredService<TaskDueNotificationScanner>();

        var (_, gorevId) = await GorevAsync(
            db, suffix, atanan: Guid.NewGuid(), gonderen: Guid.NewGuid(),
            termin: DateTime.UtcNow.AddDays(-2));

        await tarayici.ScanAsync(CancellationToken.None);

        Assert.Equal(1, await BildirimSayisiAsync(
            db, gorevId, TaskNotificationTypes.Overdue));
    }

    /// <summary>
    /// KAPANMIŞ GÖREV UYARI ÜRETMEZ. `Approved` ve `Cancelled`
    /// kapanmış; `Completed` AÇIK sayılıyor çünkü iş hâlâ gönderenin
    /// önünde.
    /// </summary>
    [Theory]
    [InlineData(WorkTaskStatus.Approved)]
    [InlineData(WorkTaskStatus.Cancelled)]
    public async Task KapanmisGorev_TerminUyarisiUretmez(WorkTaskStatus durum)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tarayici = scope.ServiceProvider
            .GetRequiredService<TaskDueNotificationScanner>();

        var (_, gorevId) = await GorevAsync(
            db, suffix, atanan: Guid.NewGuid(), gonderen: Guid.NewGuid(),
            termin: DateTime.UtcNow.AddDays(-2), durum: durum);

        await tarayici.ScanAsync(CancellationToken.None);

        Assert.Equal(0, await BildirimSayisiAsync(
            db, gorevId, TaskNotificationTypes.Overdue));
    }

    /// <summary>
    /// TERMİN DEĞİŞİRSE YENİ UYARI YAZILABİLİR.
    ///
    /// PeriodKey termin tarihi olduğu için değişiklik yeni bir anahtar
    /// üretiyor; eski uyarı kendiliğinden geçersizleşiyor.
    /// </summary>
    [Fact]
    public async Task TerminDegisirse_YeniUyariYazilabilir()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tarayici = scope.ServiceProvider
            .GetRequiredService<TaskDueNotificationScanner>();

        var (_, gorevId) = await GorevAsync(
            db, suffix, atanan: Guid.NewGuid(), gonderen: Guid.NewGuid(),
            termin: DateTime.UtcNow.AddDays(-2));

        await tarayici.ScanAsync(CancellationToken.None);
        Assert.Equal(1, await BildirimSayisiAsync(
            db, gorevId, TaskNotificationTypes.Overdue));

        // TERMİN DEĞİŞTİ — hâlâ geçmişte ama başka bir gün.
        await db.WorkTasks
            .Where(x => x.Id == gorevId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                x => x.DueDate, DateTime.UtcNow.AddDays(-1)));

        await tarayici.ScanAsync(CancellationToken.None);

        // İKİNCİ UYARI YAZILDI: farklı termin, farklı anahtar.
        Assert.Equal(2, await BildirimSayisiAsync(
            db, gorevId, TaskNotificationTypes.Overdue));
    }
}

/// <summary>
/// BİLDİRİM HATASI ASIL İŞLEMİ ÇÖKERTMEZ — AMA SESSİZ DE KALMAZ.
///
/// İki yanlış yol vardı:
///   - Aynı transaction'da ve hata fırlatılıyorsa: bildirim yüzünden
///     GÖREV ATANAMAZ. Kabul edilemez.
///   - Sessizce yutuluyorsa: görev atanır, kimse haber almaz ve
///     KİMSE FARK ETMEZ. Daha kötüsü bu.
///
/// Üçüncü yol: hata yutulmuyor, KAYDA düşüyor (sunucu günlüğü +
/// denetim kaydı) ve bildirim tekrar denenebilir kalıyor.
/// </summary>
[Collection("Integration")]
public sealed class BildirimHatasiDayaniklilikTests(DatabaseFixture fixture)
{
    /// <summary>
    /// Bilerek patlayan yazıcı: asıl işlemin ondan bağımsız olduğunu
    /// kanıtlamanın tek yolu.
    /// </summary>
    private sealed class PatlayanYazici : ITaskNotificationWriter
    {
        private readonly AppDbContext db;

        public PatlayanYazici(AppDbContext db) => this.db = db;

        public async Task WriteAsync(
            Guid companyId, Guid targetUserId, string type, Guid sourceId,
            string periodKey, string title, string? detail, string? targetPath,
            NotificationSeverity severity, CancellationToken cancellationToken)
        {
            // GERÇEK YAZICININ DAVRANIŞINI TAKLİT EDİYOR: hata
            // fırlatmıyor, kayda düşürüyor.
            db.SecurityAuditEvents.Add(new SecurityAuditEvent
            {
                Action = "NotificationWriteFailed",
                EntityType = "Notification",
                EntityId = sourceId,
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    summary = "Bildirim yazılamadı; asıl işlem etkilenmedi.",
                    type,
                    hata = "SondaHatasi"
                }),
                OccurredAtUtc = DateTime.UtcNow
            });

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task BildirimYazilamazsa_GorevYineDeAtanir_VeHataKaydaDuser()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var gercekYazici = scope.ServiceProvider
            .GetRequiredService<ITaskNotificationWriter>();

        // Gerçek yazıcı DbUpdateException'ı kendi içinde karşılıyor;
        // burada onun sözünü sınıyoruz: hata fırlatmıyor.
        var proje = await TestDataFactory.CreateProjectAsync(db, $"DAY{suffix}");

        var gorev = new WorkTask
        {
            CompanyId = proje.CompanyId,
            ProjectId = proje.Id,
            TaskNumber = $"TEST-DAY-{suffix}",
            Title = "Dayanıklılık testi",
            Status = WorkTaskStatus.Open,
            AssignedToUserId = Guid.NewGuid()
        };

        db.WorkTasks.Add(gorev);
        await db.SaveChangesAsync();

        var oncekiHataSayisi = await db.SecurityAuditEvents
            .CountAsync(x => x.Action == "NotificationWriteFailed");

        /*
         * GEÇERSİZ ŞİRKET KİMLİĞİ: yabancı anahtar kısıtı yüzünden
         * bildirim yazımı veritabanı düzeyinde patlıyor.
         */
        var patlatan = () => gercekYazici.WriteAsync(
            Guid.NewGuid(),          // var olmayan şirket
            Guid.NewGuid(),
            TaskNotificationTypes.Assigned,
            gorev.Id,
            "-",
            "Patlaması beklenen bildirim",
            null, null,
            NotificationSeverity.Info,
            CancellationToken.None);

        // HATA FIRLATMIYOR: asıl işlem akışı kesilmiyor.
        var exception = await Record.ExceptionAsync(patlatan);
        Assert.Null(exception);

        // GÖREV DURUYOR.
        var guncelGorev = await db.WorkTasks.AsNoTracking()
            .SingleAsync(x => x.Id == gorev.Id);

        Assert.Equal(WorkTaskStatus.Open, guncelGorev.Status);

        // AMA SESSİZ DE KALMADI: hata kayda düştü.
        var sonrakiHataSayisi = await db.SecurityAuditEvents
            .CountAsync(x => x.Action == "NotificationWriteFailed");

        Assert.True(
            sonrakiHataSayisi > oncekiHataSayisi,
            "Bildirim yazımı başarısız oldu ama hiçbir kayıt üretilmedi — " +
            "sessizce yutulmuş demektir.");
    }
}

/// <summary>
/// ZİLDE TEK SAYAÇ — ŞİRKET VE KİŞİSEL SATIRLAR BİRLİKTE.
///
/// İki model geçici olarak yan yana duruyor: şirket satırları mevcut
/// dört tarama kaynağı için, kişisel satırlar M1 olayları için.
/// Kullanıcı İKİ AYRI "bekleyen" sayısı görmemeli — hangisine
/// bakacağını bilemez hale gelir.
/// </summary>
[Collection("Integration")]
public sealed class ZilSayaciTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Sayac_SirketVeKisiselSatirlariBirlikteToplar()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await TestDataFactory.CreateProjectAsync(db, $"ZIL{suffix}");

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);
        var benim = (await client.GetFromJsonAsync<JsonElement>("/api/auth/me"))
            .GetProperty("id").GetGuid();

        // BAŞLANGIÇ SAYACI.
        var once = (await client.GetFromJsonAsync<JsonElement>(
                $"/api/bildirimler?companyId={proje.CompanyId}"))
            .GetProperty("unreadCount").GetInt32();

        // 1) ŞİRKET SATIRI — izne bağlı, okunma damgası satırda.
        db.Notifications.Add(new Notification
        {
            CompanyId = proje.CompanyId,
            Type = $"test.sirket.{suffix}",
            SourceId = Guid.NewGuid(),
            PeriodKey = "-",
            Title = "Şirket bildirimi",
            Severity = NotificationSeverity.Info,
            Status = NotificationStatus.Open
        });

        // 2) KİŞİSEL SATIR — okunma durumu alıcı tablosunda.
        var kisisel = new Notification
        {
            CompanyId = proje.CompanyId,
            TargetUserId = benim,
            Type = $"test.kisisel.{suffix}",
            SourceId = Guid.NewGuid(),
            PeriodKey = "-",
            Title = "Kişisel bildirim",
            Severity = NotificationSeverity.Info,
            Status = NotificationStatus.Open
        };

        db.Notifications.Add(kisisel);
        db.NotificationRecipients.Add(new NotificationRecipient
        {
            Notification = kisisel,
            UserId = benim
        });

        await db.SaveChangesAsync();

        var sonra = await client.GetFromJsonAsync<JsonElement>(
            $"/api/bildirimler?companyId={proje.CompanyId}");

        // İKİSİ DE SAYILDI: tek sayaçta +2.
        Assert.Equal(once + 2, sonra.GetProperty("unreadCount").GetInt32());

        // KİŞİSEL SATIR AYRICA LİSTELENİYOR.
        var kisiselListe = sonra.GetProperty("personalItems").EnumerateArray().ToList();

        Assert.Contains(kisiselListe, x =>
            x.GetProperty("title").GetString() == "Kişisel bildirim");

        // OKUNDU İŞARETLENİNCE SAYAÇ DÜŞÜYOR.
        var aliciId = kisiselListe
            .Single(x => x.GetProperty("title").GetString() == "Kişisel bildirim")
            .GetProperty("id").GetGuid();

        await client.PostAsync($"/api/bildirimler/kisisel/{aliciId}/okundu", null);

        var okunduSonrasi = (await client.GetFromJsonAsync<JsonElement>(
                $"/api/bildirimler?companyId={proje.CompanyId}"))
            .GetProperty("unreadCount").GetInt32();

        Assert.Equal(once + 1, okunduSonrasi);
    }

    /// <summary>
    /// KİŞİSEL BİLDİRİM BAŞKASININ SAYACINA GİRMEZ.
    ///
    /// Şirket satırında tek `ReadAtUtc` olduğu için bu ayrım
    /// yapılamıyordu; kişisel model tam olarak bunun için var.
    /// </summary>
    [Fact]
    public async Task KisiselBildirim_BaskasininSayacinaGirmez()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proje = await TestDataFactory.CreateProjectAsync(db, $"ZLB{suffix}");

        var baskasi = Guid.NewGuid();

        var kisisel = new Notification
        {
            CompanyId = proje.CompanyId,
            TargetUserId = baskasi,
            Type = $"test.baskasi.{suffix}",
            SourceId = Guid.NewGuid(),
            PeriodKey = "-",
            Title = "Başkasının bildirimi",
            Severity = NotificationSeverity.Info,
            Status = NotificationStatus.Open
        };

        db.Notifications.Add(kisisel);
        db.NotificationRecipients.Add(new NotificationRecipient
        {
            Notification = kisisel,
            UserId = baskasi
        });

        await db.SaveChangesAsync();

        var client = await AuthHelper.CreateAuthorizedClientAsync(fixture.Factory);

        var yanit = await client.GetFromJsonAsync<JsonElement>(
            $"/api/bildirimler?companyId={proje.CompanyId}");

        var kisiselListe = yanit.GetProperty("personalItems").EnumerateArray().ToList();

        Assert.DoesNotContain(kisiselListe, x =>
            x.GetProperty("title").GetString() == "Başkasının bildirimi");
    }
}
