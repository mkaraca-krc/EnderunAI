using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Expenses;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using EnderunAI.Api.Services.Common;
namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/tasks")]
public sealed class WorkTasksController(
    AppDbContext db,
    ICurrentUserService currentUser,
    EnderunAI.Api.Services.DocumentNumbers.IDocumentNumberService documentNumbers,
    ICurrentDataScopeService dataScope,
    IScopedData scoped,
    IUserAuthorizationService authorization,
    EnderunAI.Api.Services.Notifications.ITaskNotificationWriter notifications)
    : ControllerBase
{
    /// <summary>Sayfa boyutu tavanı — istemci daha fazlasını isteyemez.</summary>
    private const int SayfaTavani = 100;

    private async Task<CurrentDataScopeSnapshot> GetScopeAsync(
        CancellationToken cancellationToken) =>
        await dataScope.GetAsync(cancellationToken) ??
        throw new UnauthorizedAccessException("Kullanıcı veri kapsamı bulunamadı.");

    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.TasksView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? assignedToUserId,
        [FromQuery] int? status,
        [FromQuery] int? priority,
        [FromQuery] int? kind,
        [FromQuery] bool? overdueOnly,
        [FromQuery] int? pageSize,
        [FromQuery] DateTime? cursorCreatedAtUtc,
        [FromQuery] Guid? cursorId,
        CancellationToken cancellationToken)
    {
        /*
         * KAPSAM SÜZGECİ HER ZAMAN — `companyId` PARAMETRESİ KAPSAM
         * DEĞİLDİR.
         *
         * `companyId` kullanıcının yazdığı bir TERCİH; başka şirketin
         * kimliğini yazsa bile kapsam süzgeci sonucu boşaltır.
         * G3 paketinin tamamının dersi buydu.
         */
        var query = db.WorkTasks
            .AsNoTracking()
            .ApplyScope(await GetScopeAsync(cancellationToken));

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);
        if (projectId.HasValue)
            query = query.Where(x => x.ProjectId == projectId.Value);
        if (assignedToUserId.HasValue)
            query = query.Where(x => x.AssignedToUserId == assignedToUserId.Value);
        if (status.HasValue)
            query = query.Where(x => (int)x.Status == status.Value);
        if (priority.HasValue)
            query = query.Where(x => (int)x.Priority == priority.Value);

        /*
         * TÜR SÜZGECİ — VARSAYILAN "HEPSİ" DEĞİL, "İŞ EMRİ".
         *
         * Görev kütüğü bir İŞ kaydıdır. Hızır hatırlatmaları kişinin
         * kendine koyduğu notlardır ve başkasına iş yüklemezler;
         * ikisi aynı listede durunca kütük, iş takibi için
         * okunamaz hâle geliyordu.
         *
         * VARSAYILAN DARDIR, BİLEREK: süzgeç GÖNDERİLMEDİĞİNDE liste
         * yalnız iş emri gösterir. Gizleme değil daraltma — hatırlatmalar
         * `kind=2` ile, hepsi `kind=0` ile görülebiliyor ve
         * `/yapilacaklar` iki bölümünü aynen koruyor (o ekran `kind=0`
         * gönderiyor).
         *
         * `kind = 0` NEDEN "HEPSİ" DEMEK: `Belirsiz = 0` artık
         * veritabanına YAZILAMAZ (`CK_WorkTasks_Kind_Belirsiz_Degil`,
         * göç 20260904202822). Yani sıfır hiçbir satırla eşleşemez;
         * gerçek bir süzgeç değeri olma ihtimali kalıcı olarak
         * kapandığı için "süzgeç yok" anlamını taşıyabiliyor. Bu
         * anlam, o kısıt kalktığı gün geçersizleşir — kısıt bu
         * yorumun DAYANAĞIDIR, süsü değil.
         */
        if (kind is null)
            query = query.Where(x => x.Kind == WorkTaskKind.IsEmri);
        else if (kind.Value != 0)
            query = query.Where(x => (int)x.Kind == kind.Value);

        var now = DateTime.UtcNow;
        if (overdueOnly == true)
        {
            query = query.Where(x =>
                x.DueDate.HasValue &&
                x.DueDate.Value < now &&
                x.Status != WorkTaskStatus.Completed &&
                x.Status != WorkTaskStatus.Cancelled);
        }

        /*
         * KEYSET SAYFALAMA — LIMIT/OFFSET DEĞİL.
         *
         * Görev tablosu hızlı büyüyen tablolardan: her kayıt altında
         * görev açılabiliyor ve kapananlar silinmiyor. OFFSET'te
         * veritabanı atlanan satırları YİNE DE okumak zorunda, yani
         * son sayfanın maliyeti tablo büyüdükçe artıyor.
         *
         * SIRALAMA ANAHTARI (CreatedAtUtc, Id): tarih tek başına
         * benzersiz değil; aynı saniyede açılan iki görev sayfa
         * sınırında birbirini gizlerdi. İndeks M1/1'de kondu.
         *
         * İMLEÇ İSTEMCİDEN GELİYOR ama güvenlik sınırı değil: kapsam
         * süzgeci imleçten bağımsız uygulanıyor, uydurma bir imleç
         * yalnız boş sayfa döndürür.
         */
        var alinacak = Math.Clamp(pageSize ?? 50, 1, SayfaTavani);

        if (cursorCreatedAtUtc.HasValue && cursorId.HasValue)
        {
            var imlecTarih = cursorCreatedAtUtc.Value;
            var imlecId = cursorId.Value;

            query = query.Where(x =>
                x.CreatedAtUtc < imlecTarih ||
                (x.CreatedAtUtc == imlecTarih && x.Id.CompareTo(imlecId) < 0));
        }

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(alinacak + 1)
            .ToListAsync(cancellationToken);

        // TAVANI BİR AŞAN KAYIT YALNIZ "DAHA VAR MI" SORUSUNU
        // CEVAPLIYOR; listeye girmiyor. COUNT(*) atılmıyor: bu
        // tabloda her sayfa için tam sayım, sayfalamanın kendisinden
        // pahalı olurdu.
        var devamVar = items.Count > alinacak;
        var sayfa = devamVar ? items.Take(alinacak).ToList() : items;
        var son = sayfa.LastOrDefault();

        var adlar = await AdlariGetirAsync(sayfa, cancellationToken);

        return Ok(new
        {
            items = sayfa.Select(x => ToDto(x, adlar)),
            hasMore = devamVar,
            nextCursor = devamVar && son is not null
                ? new { createdAtUtc = son.CreatedAtUtc, id = son.Id }
                : null
        });
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.TasksView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.WorkTasks
            .AsNoTracking()
            .ApplyScope(await GetScopeAsync(cancellationToken))
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null)
            return NotFound(new { message = "Görev bulunamadı." });

        var adlar = await AdlariGetirAsync([item], cancellationToken);

        return Ok(ToDto(item, adlar));
    }

    [HttpGet("dashboard")]
    [RequirePermission(PermissionCatalog.Keys.TasksView)]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] Guid? companyId,
        CancellationToken cancellationToken)
    {
        var query = db.WorkTasks
            .AsNoTracking()
            .ApplyScope(await GetScopeAsync(cancellationToken));
        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        var rows = await query
            .Select(x => new
            {
                x.Status,
                x.Priority,
                x.DueDate,
                x.AssignedToUserId,
                x.CompletedAtUtc
            })
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var today = now.Date;
        /*
         * "AÇIK" SAYILAN DURUMLAR — ÇİFT ADIMLI KAPANIŞA GÖRE.
         *
         * `Completed` DE AÇIK SAYILIYOR: yapan bitirdi ama gönderen
         * henüz onaylamadı, yani iş HÂLÂ BİRİNİN ÖNÜNDE. Kapanmış
         * saymak, onay kuyruğunda bekleyen işleri gözden kaçırırdı.
         *
         * `Returned` de açık: iade edilmiş görev yapana geri döndü.
         *
         * Kapanmış olanlar yalnız `Approved` ve `Cancelled`.
         *
         * (`Waiting` kaldırıldı — kimin işi olduğunu belirsizleştiriyordu;
         * bkz. WorkTaskStatus.)
         */
        var openStatuses = new[]
        {
            WorkTaskStatus.Open,
            WorkTaskStatus.InProgress,
            WorkTaskStatus.Completed,
            WorkTaskStatus.Returned
        };

        return Ok(new
        {
            totalOpen = rows.Count(x => openStatuses.Contains(x.Status)),
            assignedToMe = rows.Count(x =>
                openStatuses.Contains(x.Status) &&
                x.AssignedToUserId == currentUser.UserId),
            dueToday = rows.Count(x =>
                openStatuses.Contains(x.Status) &&
                x.DueDate.HasValue &&
                x.DueDate.Value.Date == today),
            overdue = rows.Count(x =>
                openStatuses.Contains(x.Status) &&
                x.DueDate.HasValue &&
                x.DueDate.Value < now),
            critical = rows.Count(x =>
                openStatuses.Contains(x.Status) &&
                x.Priority == WorkTaskPriority.Critical),
            completedToday = rows.Count(x =>
                x.CompletedAtUtc.HasValue &&
                x.CompletedAtUtc.Value.Date == today)
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.TasksManage)]
    public async Task<IActionResult> Create(
        CreateWorkTaskRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { message = "Görev başlığı zorunludur." });

        /*
         * NUMARA MERKEZÎ ÜRETEÇTEN — YARIŞ HATASI KAPATILDI.
         *
         * `CountAsync + 1` iki eşzamanlı isteğe AYNI numarayı verir.
         * Ayrıca sayım silinmiş kayıtları saymadığı için numara
         * geriye bile gidebiliyordu.
         *
         * Bu hata Hızır'ın görev üretiminde düzeltilmişti ama BURADA
         * da vardı; sözleşme bekçisi (BelgeNumarasiSozlesmeTests)
         * yakaladı — sonda "GRV taşımasının testi yok" dediğinde
         * eklenen bekçi.
         */
        /*
         * SERBEST GÖREVDE MASRAF MERKEZİ ZORUNLU.
         *
         * Masraf merkezi olmayan serbest görev, faaliyet raporunda
         * KARŞILIĞI OLMAYAN İŞ demektir: ay sonunda "bu emek nereye
         * gitti" sorusunun cevabı olmaz.
         *
         * ZORUNLULUK TÜRE BAĞLI (2026-09-04): merkez yalnız İŞ EMRİ
         * için zorunlu. Hatırlatmanın masrafı yoktur.
         *
         * Buradaki eski yorum "kayda bağlı görevde (SourceModule dolu)
         * merkez zorunlu değil" diyordu; O KAÇIŞ KAPANDI
         * (KURAL-KATMAN/1) ve yorum artık yanlıştı. Yanlış yorum,
         * yorumsuz koddan kötüdür: okuyan onu kural sanır.
         */
        var merkezHatasi = await MerkezDogrulaAsync(
            request.Kind,
            request.ProjectId,
            request.BranchId,
            request.ProjectSiteId,
            request.CenterType,
            cancellationToken);

        if (merkezHatasi is not null)
            return BadRequest(new { message = merkezHatasi });

        /*
         * TÜR VE ATAMA KAPISI — ÜÇ YAZMA YOLUNUN ORTAK KURALI.
         *
         * Kural denetleyicinin gövdesinde değil, `GorevAtamaKurali`
         * içinde. Sebebi bu dosyanın kendi tarihinde yazılı: merkez
         * kuralı burada yaşarken Hızır onu hiç görmedi, PUT ise ikinci
         * bir kopya taşıdı.
         */
        var turHatasi = GorevAtamaKurali.Dogrula(
            request.Kind,
            request.AssignedToUserId,
            request.AssignedToPersonnelId);

        if (turHatasi is not null)
            return BadRequest(new { message = turHatasi });

        if (request.AssignedToPersonnelId is Guid atananPersonel)
        {
            var personelHatasi = await PersonelAtanabilirMiAsync(
                request.CompanyId, atananPersonel, cancellationToken);

            if (personelHatasi is not null)
                return BadRequest(new { message = personelHatasi });
        }

        /*
         * ATANAN KİŞİ KAYDI GÖREBİLMELİ.
         *
         * Göremeyeceği bir göreve atanan kullanıcı, gelen kutusunda
         * açamadığı bir satır görür. Daha kötüsü: görev, kapsam
         * disiplinine açılmış gizli bir kapı olurdu.
         *
         * ── BU BLOK BİR KEZ SESSİZCE SİLİNDİ ──
         *
         * `2d90c946` (MERKEZ/1) merkez kuralını ortak metoda taşırken
         * POST gövdesini METİN ARALIĞIYLA kesti; aralıkta duran bu blok
         * da gitti ve 26 satır kayıtsız şekilde canlıya çıktı. 2965
         * testin hiçbiri görmedi çünkü blok TESTSİZDİ; yetim muhafızı da
         * görmedi çünkü `GorevAtanabilirMiAsync` başka iki yerde
         * yaşamaya devam ediyordu — yetim değildi, yalnız en önemli
         * çağıranını kaybetmişti.
         *
         * Artık `AtamaKapisiTests` bu bloğu sınıyor. Silinirse test
         * kırmızı verir.
         */
        if (request.AssignedToUserId is Guid atanan)
        {
            var taslak = new WorkTask
            {
                CompanyId = request.CompanyId,
                ProjectId = request.ProjectId,
                BranchId = request.BranchId,
                ProjectSiteId = request.ProjectSiteId
            };

            if (!await GorevAtanabilirMiAsync(taslak, atanan, cancellationToken))
            {
                return BadRequest(new
                {
                    message =
                        "Seçilen kullanıcı bu görevin kaydını göremiyor, " +
                        "dolayısıyla göreve atanamaz. Önce yetki verin."
                });
            }
        }

        var taskNumber = await documentNumbers.GenerateAsync(
            request.CompanyId, "WORK_TASK", "GRV", cancellationToken);

        var item = new WorkTask
        {
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            TaskNumber = taskNumber,
            // TÜR SEÇİMDEN TÜRER: istekten gelen değer yalnızca
            // çelişki kontrolünde okundu, saklanmıyor.
            CenterType = MasrafMerkeziKurali.TuruTuret(
                request.ProjectId, request.BranchId, request.ProjectSiteId),
            BranchId = request.BranchId,
            ProjectSiteId = request.ProjectSiteId,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            Priority = request.Priority,
            Status = WorkTaskStatus.Open,
            Kind = request.Kind,
            AssignedToUserId = request.AssignedToUserId,
            AssignedToPersonnelId = request.AssignedToPersonnelId,
            AssignedByUserId = currentUser.UserId,
            StartDate = ToUtcDate(request.StartDate),
            DueDate = ToUtcDate(request.DueDate),
            SourceModule = request.SourceModule?.Trim(),
            SourceEntityId = request.SourceEntityId,
            SourceEventCode = request.SourceEventCode?.Trim(),
            Tags = request.Tags?.Trim()
        };

        db.WorkTasks.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        /*
         * BİLDİRİM ASIL İŞLEMDEN SONRA VE AYRI.
         *
         * Görev KAYDEDİLDİ; bildirim yazımı bundan sonra ve kendi
         * hata sınırı içinde. Yazıcı hatayı yutmuyor ama fırlatmıyor
         * da — kayda düşürüyor. Aynı transaction'da olsaydı bildirim
         * yüzünden görev atanamazdı; sessizce yutulsaydı görev
         * atanır, kimse haber almaz ve kimse fark etmezdi.
         */
        if (item.AssignedToUserId is Guid yeniSorumlu &&
            yeniSorumlu != currentUser.UserId)
        {
            await notifications.WriteAsync(
                item.CompanyId,
                yeniSorumlu,
                Services.Notifications.TaskNotificationTypes.Assigned,
                item.Id,
                "-",
                $"Yeni görev: {item.TaskNumber}",
                item.Title,
                $"/gorevler/{item.Id}",
                Models.Notifications.NotificationSeverity.Info,
                cancellationToken);
        }

        return Ok(ToDto(item, await AdlariGetirAsync([item], cancellationToken)));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.TasksManage)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateWorkTaskRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.WorkTasks
            .ApplyScope(await GetScopeAsync(cancellationToken))
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Görev bulunamadı." });

        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { message = "Görev başlığı zorunludur." });

        /*
         * PUT DE MERKEZ YAZAR — VE AYNI KAPIDAN GEÇER.
         *
         * Önce `UpdateWorkTaskRequest` merkez alanlarını hiç taşımıyordu:
         * merkez yalnız oluşturmada konabiliyor, yanlış konmuşsa BİR DAHA
         * DÜZELTİLEMİYORDU. Alanlar eklendi ve doğrulama POST ile aynı
         * metoda bağlandı — ikinci bir kapı doğmasın.
         */
        var merkezHatasi = await MerkezDogrulaAsync(
            request.Kind,
            request.ProjectId,
            request.BranchId,
            request.ProjectSiteId,
            request.CenterType,
            cancellationToken);

        if (merkezHatasi is not null)
            return BadRequest(new { message = merkezHatasi });

        item.ProjectId = request.ProjectId;
        item.BranchId = request.BranchId;
        item.ProjectSiteId = request.ProjectSiteId;
        item.CenterType = MasrafMerkeziKurali.TuruTuret(
            request.ProjectId, request.BranchId, request.ProjectSiteId);

        /*
         * PUT DE AYNI KAPIDAN GEÇER.
         *
         * ACIL/2'nin dersi: bir kapı eksiği bulunduğunda aynı kaynağın
         * BÜTÜN yazma fiilleri aynı turda sınanır. Tür ve atama kapısı
         * yalnız POST'a konsaydı, tür güncelleme ile `Belirsiz`e
         * çevrilebilir ve iki atama alanı PUT üzerinden birlikte
         * doldurulabilirdi.
         */
        var turHatasi = GorevAtamaKurali.Dogrula(
            request.Kind,
            request.AssignedToUserId,
            request.AssignedToPersonnelId);

        if (turHatasi is not null)
            return BadRequest(new { message = turHatasi });

        if (request.AssignedToPersonnelId is Guid guncelPersonel)
        {
            var personelHatasi = await PersonelAtanabilirMiAsync(
                item.CompanyId, guncelPersonel, cancellationToken);

            if (personelHatasi is not null)
                return BadRequest(new { message = personelHatasi });
        }

        item.Kind = request.Kind;
        item.Title = request.Title.Trim();
        item.Description = request.Description?.Trim();
        item.Priority = request.Priority;
        /*
         * PUT DE ATAMAYI DOĞRULAR — ACIL/2.
         *
         * ACIL/1 POST'taki kapıyı kapattı; AYNI AÇIK PUT'TA DURUYORDU.
         * Kayıt yetkili bir kişiyle açılır, sonra PUT ile görevi
         * göremeyen birine devredilirdi — POST'un reddettiği şey bir
         * güncelleme üzerinden geçerdi.
         *
         * DERS: bir kapı eksiği bulunduğunda aynı kaynağın BÜTÜN yazma
         * fiilleri aynı turda sınanır. ACIL/1'de yalnız POST bakıldı,
         * PUT bir gün sonra çıktı.
         *
         * DOĞRULAMA GÜNCELLENMİŞ MERKEZ ALANLARIYLA YAPILIR: atanan
         * kişinin görmesi gereken şey, kaydın YENİ hâli. Eski merkeze
         * göre doğrulamak, kişiyi göremeyeceği bir kaydın içine
         * yerleştirirdi.
         *
         * ── SAĞLAMLIĞIN KAYNAĞI: `request.*` OKUNUYOR ──
         *
         * Bu blok satır sırasına GÜVENMİYOR. Yukarıda `item.ProjectId`
         * zaten `request.ProjectId`'ye yazılmış durumda; o yüzden
         * `item.*` okunsa bile bugün aynı sonuç çıkardı. Ama o doğruluk
         * SIRAYA bağlı olurdu ve bir sonraki düzenleyen bu bloğu merkez
         * yazımının üstüne alsaydı sessizce bozulurdu.
         *
         * ÖLÇÜLDÜ: sabotaj tek başına "yukarı taşı" ya da tek başına
         * "eski alanları oku" biçiminde yapıldığında test YEŞİL kalıyor
         * — ikisi birlikte yapıldığında kırmızıya dönüyor. Yani
         * `request.*` okumak, sıra değişse bile iddiayı ayakta tutuyor.
         *
         * `PUT_AtamaYeniMerkezeGoreDogrulanir` bunu koruyor. Adı
         * davranışı anlatıyor, satır sırasını değil.
         */
        if (request.AssignedToUserId is Guid yeniAtanan)
        {
            var taslak = new WorkTask
            {
                CompanyId = item.CompanyId,
                ProjectId = request.ProjectId,
                BranchId = request.BranchId,
                ProjectSiteId = request.ProjectSiteId
            };

            if (!await GorevAtanabilirMiAsync(taslak, yeniAtanan, cancellationToken))
            {
                return BadRequest(new
                {
                    message =
                        "Seçilen kullanıcı bu görevin kaydını göremiyor, " +
                        "dolayısıyla göreve atanamaz. Önce yetki verin."
                });
            }
        }

        item.AssignedToUserId = request.AssignedToUserId;
        item.AssignedToPersonnelId = request.AssignedToPersonnelId;
        item.StartDate = ToUtcDate(request.StartDate);
        item.DueDate = ToUtcDate(request.DueDate);
        item.Tags = request.Tags?.Trim();
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(item, await AdlariGetirAsync([item], cancellationToken)));
    }

    [HttpPost("{id:guid}/start")]
    [RequirePermission(PermissionCatalog.Keys.TasksManage)]
    public async Task<IActionResult> Start(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.WorkTasks
            .ApplyScope(await GetScopeAsync(cancellationToken))
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Görev bulunamadı." });

        item.Status = WorkTaskStatus.InProgress;
        item.StartedAtUtc = DateTime.UtcNow;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(item, await AdlariGetirAsync([item], cancellationToken)));
    }

    [HttpPost("{id:guid}/complete")]
    [RequirePermission(PermissionCatalog.Keys.TasksManage)]
    public async Task<IActionResult> Complete(
        Guid id,
        CompleteWorkTaskRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.WorkTasks
            .ApplyScope(await GetScopeAsync(cancellationToken))
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Görev bulunamadı." });

        /*
         * ÇİFT ADIMLI KAPANIŞ.
         *
         * Yapanın "bitti" demesi görevi KAPATMAZ: görev gönderene
         * düşer ve o onaylayınca kapanır. Tek adımlı kapanışta
         * gönderen, istediği işin yapılıp yapılmadığını hiç görmeden
         * görevin listeden düştüğünü görürdü.
         *
         * GÖNDEREN KENDİNE AÇTIYSA TEK ADIM: kendini onaylatmak
         * anlamsız bir tören olurdu ve gelen kutusunu kendi
         * onaylarıyla doldururdu.
         */
        var kendineAcmis =
            item.AssignedByUserId is not null &&
            item.AssignedByUserId == item.AssignedToUserId;

        item.CompletedAtUtc = DateTime.UtcNow;
        item.CompletedByUserId = currentUser.UserId;
        item.CompletionNote = request.CompletionNote?.Trim();
        item.UpdatedAtUtc = DateTime.UtcNow;

        if (kendineAcmis)
        {
            item.Status = WorkTaskStatus.Approved;
            item.ApprovedAtUtc = DateTime.UtcNow;
            item.ApprovedByUserId = currentUser.UserId;
        }
        else
        {
            item.Status = WorkTaskStatus.Completed;
        }

        await db.SaveChangesAsync(cancellationToken);

        // GÖNDERENE HABER: onayı bekleyen bir iş var. Kendine açtıysa
        // bildirim yok — kendi işini kendine duyurmak gürültüdür.
        if (!kendineAcmis && item.AssignedByUserId is Guid gonderen)
        {
            await notifications.WriteAsync(
                item.CompanyId,
                gonderen,
                Services.Notifications.TaskNotificationTypes.Completed,
                item.Id,
                "-",
                $"Onay bekliyor: {item.TaskNumber}",
                item.Title,
                $"/gorevler/{item.Id}",
                Models.Notifications.NotificationSeverity.Info,
                cancellationToken);
        }

        return Ok(ToDto(item, await AdlariGetirAsync([item], cancellationToken)));
    }

    /// <summary>
    /// GÖNDEREN ONAYLAR — görev kapanır.
    ///
    /// Yalnız gönderen onaylayabilir: başkası onaylasaydı çift adımlı
    /// kapanış tören olurdu, işi isteyen kişi sonucu görmeden görev
    /// kapanırdı.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [RequirePermission(PermissionCatalog.Keys.TasksManage)]
    public async Task<IActionResult> Approve(
        Guid id,
        CancellationToken cancellationToken)
    {
        var item = await db.WorkTasks
            .ApplyScope(await GetScopeAsync(cancellationToken))
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null)
            return NotFound(new { message = "Görev bulunamadı." });

        if (item.Status != WorkTaskStatus.Completed)
            return BadRequest(new { message = "Yalnızca tamamlanmış görev onaylanabilir." });

        if (item.AssignedByUserId != currentUser.UserId)
            return Forbid();

        item.Status = WorkTaskStatus.Approved;
        item.ApprovedAtUtc = DateTime.UtcNow;
        item.ApprovedByUserId = currentUser.UserId;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(item, await AdlariGetirAsync([item], cancellationToken)));
    }

    /// <summary>
    /// GÖNDEREN İADE EDER — GEREKÇE ZORUNLU.
    ///
    /// Gerekçesiz iade sessiz bir "beğenmedim"dir; yapan neyi
    /// düzelteceğini bilemez.
    ///
    /// TERMİN KORUNUR: gönderen isterse yeni termin verir, vermezse
    /// ESKİSİ KALIR. Termini geçmiş bir iade görevi listede hemen
    /// kırmızı görünür — öyle görünmeli, gecikme iade ile
    /// gizlenmemeli.
    /// </summary>
    [HttpPost("{id:guid}/return")]
    [RequirePermission(PermissionCatalog.Keys.TasksManage)]
    public async Task<IActionResult> Return(
        Guid id,
        ReturnWorkTaskRequest request,
        CancellationToken cancellationToken)
    {
        var gerekce = request.Reason?.Trim();

        if (string.IsNullOrWhiteSpace(gerekce))
            return BadRequest(new { message = "İade gerekçesi zorunludur." });

        var item = await db.WorkTasks
            .ApplyScope(await GetScopeAsync(cancellationToken))
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null)
            return NotFound(new { message = "Görev bulunamadı." });

        if (item.Status != WorkTaskStatus.Completed)
            return BadRequest(new { message = "Yalnızca tamamlanmış görev iade edilebilir." });

        if (item.AssignedByUserId != currentUser.UserId)
            return Forbid();

        /*
         * GÖREV YAPANA GERİ DÖNER: durum `Open`.
         *
         * `Returned` durumu enum'da var ama kalıcı değil — "iade
         * edildi ama henüz görülmedi" anını temsil ediyordu. Burada
         * doğrudan `Open`'a çekiliyor: iş yeniden yapanın önünde ve
         * gelen kutusunda öyle görünmeli.
         *
         * TAMAMLANMA İZİ SİLİNİYOR: görev yeniden açıldığına göre
         * "bitirildi" damgası da kalkmalı, yoksa liste onu bitmiş
         * sayar.
         */
        item.Status = WorkTaskStatus.Open;
        item.ReturnedAtUtc = DateTime.UtcNow;
        item.ReturnedByUserId = currentUser.UserId;
        item.ReturnReason = gerekce;
        item.ReturnCount += 1;

        item.CompletedAtUtc = null;
        item.CompletedByUserId = null;

        // TERMİN: yeni verilmediyse eskisine DOKUNULMUYOR.
        if (request.NewDueDate.HasValue)
            item.DueDate = DateTime.SpecifyKind(request.NewDueDate.Value, DateTimeKind.Utc);

        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        // YAPANA HABER: iş geri döndü, gerekçesiyle.
        if (item.AssignedToUserId is Guid yapan)
        {
            await notifications.WriteAsync(
                item.CompanyId,
                yapan,
                Services.Notifications.TaskNotificationTypes.Returned,
                item.Id,

                /*
                 * PERİYOT ANAHTARI İADE SAYISI: aynı görev ikinci kez
                 * iade edilirse YENİ bildirim yazılabilsin. Sabit
                 * anahtar olsaydı ikinci iade sessiz kalırdı.
                 */
                item.ReturnCount.ToString(),

                $"Görev iade edildi: {item.TaskNumber}",
                gerekce,
                $"/gorevler/{item.Id}",
                Models.Notifications.NotificationSeverity.Warning,
                cancellationToken);
        }

        return Ok(ToDto(item, await AdlariGetirAsync([item], cancellationToken)));
    }

    /// <summary>
    /// GÖREVİ DEVRET — izi kayıtta ve denetimde.
    ///
    /// Devralan kişinin görevi GÖRME yetkisi yoksa devredilemez:
    /// görev üzerinden kapsam disiplinine gizli kapı açılmaz. Çözüm
    /// o kişiye yetki vermektir, atamayı zorlamak değil.
    /// </summary>
    [HttpPost("{id:guid}/delegate")]
    [RequirePermission(PermissionCatalog.Keys.TasksManage)]
    public async Task<IActionResult> Delegate(
        Guid id,
        DelegateWorkTaskRequest request,
        CancellationToken cancellationToken)
    {
        var gerekce = request.Reason?.Trim();

        if (string.IsNullOrWhiteSpace(gerekce))
            return BadRequest(new { message = "Devretme gerekçesi zorunludur." });

        var item = await db.WorkTasks
            .ApplyScope(await GetScopeAsync(cancellationToken))
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null)
            return NotFound(new { message = "Görev bulunamadı." });

        if (item.Status is WorkTaskStatus.Approved or WorkTaskStatus.Cancelled)
            return BadRequest(new { message = "Kapanmış görev devredilemez." });

        if (!await GorevAtanabilirMiAsync(item, request.ToUserId, cancellationToken))
        {
            return BadRequest(new
            {
                message =
                    "Bu kullanıcı görevin bağlı olduğu kaydı göremiyor, " +
                    "dolayısıyla görev devredilemez. Önce yetki verin."
            });
        }

        var oncekiSorumlu = item.AssignedToUserId;

        /*
         * DEVRETME DÖRDÜNCÜ YAZMA YOLU — ÖLÇÜMLE BULUNDU.
         *
         * Paketin kapsamı "üç yazma yolu" olarak konmuştu: POST, PUT,
         * Hızır. Ölçüm dördüncüsünü gösterdi: `delegate` de
         * `AssignedToUserId` YAZIYOR ve `GorevAtamaKurali`'ndan
         * GEÇMİYOR.
         *
         * SESSİZ SONUCU: personele atanmış bir görev bir kullanıcıya
         * devredilirse İKİ ALAN DA dolu kalırdı. Kural isteğin içindeki
         * çelişkiyi reddediyor ama bu yol kaydın içinde çelişki
         * ÜRETİYORDU — ve `AssignedToDisplayName` sessizce kullanıcıyı
         * seçip personeli gizlerdi. Tam olarak kaçınmak için kurulan
         * desen, kapının atlandığı yerden geri girerdi.
         *
         * NEDEN TEMİZLEME, NEDEN RET DEĞİL: devretme işin sahibini
         * değiştirmektir; personele verilmiş bir işi bir kullanıcıya
         * devretmek meşru bir istektir. Reddetseydik, sahadaki işi
         * ofise devretmenin tek yolu görevi silip yeniden açmak olurdu.
         *
         * İZ KAYBOLMUYOR: `DelegatedFromUserId` bir KULLANICI alanı,
         * personel kimliğini taşıyamaz. O yüzden önceki personel
         * aşağıdaki denetim kaydına yazılıyor — modelin kendi notu da
         * zaten "tam zincir denetim kaydında" diyor.
         */
        var oncekiPersonel = item.AssignedToPersonnelId;

        item.DelegatedFromUserId = oncekiSorumlu;
        item.DelegatedAtUtc = DateTime.UtcNow;
        item.DelegationCount += 1;
        item.AssignedToUserId = request.ToUserId;
        item.AssignedToPersonnelId = null;
        item.UpdatedAtUtc = DateTime.UtcNow;

        // DENETİM: kim, kimden kime, ne zaman, neden.
        db.SecurityAuditEvents.Add(new SecurityAuditEvent
        {
            ActorUserId = currentUser.UserId,
            ActorUsername = currentUser.Username,
            Action = "WorkTaskDelegated",
            EntityType = "WorkTask",
            EntityId = item.Id,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                summary = $"{item.TaskNumber} devredildi.",
                oncekiSorumlu,
                // PERSONELDEN DEVRALMANIN TEK İZİ BURASI: kayıt
                // üzerindeki `DelegatedFromUserId` yalnız kullanıcı
                // taşıyabiliyor.
                oncekiPersonel,
                yeniSorumlu = request.ToUserId,
                gerekce
            }),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            OccurredAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);

        // DEVRALANA HABER: onun için bu bir "yeni görev".
        await notifications.WriteAsync(
            item.CompanyId,
            request.ToUserId,
            Services.Notifications.TaskNotificationTypes.Assigned,
            item.Id,

            // Devretme sayısı: aynı görev tekrar devredilirse yeni
            // bildirim yazılabilsin.
            $"devir-{item.DelegationCount}",

            $"Görev devredildi: {item.TaskNumber}",
            item.Title,
            $"/gorevler/{item.Id}",
            Models.Notifications.NotificationSeverity.Info,
            cancellationToken);

        return Ok(ToDto(item, await AdlariGetirAsync([item], cancellationToken)));
    }

    /// <summary>
    /// GÖREVE ATANABİLECEK KULLANICILAR.
    ///
    /// Ekran bu listeden seçim yaptırıyor; göremeyeceği bir kişiyi
    /// hiç göstermiyor. Kural yalnız uçta zorlansaydı kullanıcı
    /// listeden birini seçer, kaydeder ve hata alırdı — sebebini
    /// anlamadan.
    /// </summary>
    [HttpGet("{id:guid}/assignable-users")]
    [RequirePermission(PermissionCatalog.Keys.TasksView)]
    public async Task<IActionResult> AssignableUsers(
        Guid id,
        CancellationToken cancellationToken)
    {
        var item = await db.WorkTasks
            .AsNoTracking()
            .ApplyScope(await GetScopeAsync(cancellationToken))
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null)
            return NotFound(new { message = "Görev bulunamadı." });

        var adaylar = await db.Users
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new { x.Id, x.Username, x.FullName })
            .ToListAsync(cancellationToken);

        var sonuc = new List<object>();

        foreach (var aday in adaylar)
        {
            if (await GorevAtanabilirMiAsync(item, aday.Id, cancellationToken))
                sonuc.Add(new { aday.Id, aday.Username, aday.FullName });
        }

        return Ok(sonuc);
    }

    /// <summary>
    /// ATANAN KİŞİ GÖREVİN KAYDINI GÖREBİLİYOR MU.
    ///
    /// İki şart birden: görev iznine sahip olmalı VE görevin şirket/
    /// proje kapsamı onun veri kapsamına düşmeli. Yalnız izne
    /// bakılsaydı, başka şirketin görevine atanmak mümkün olurdu ve
    /// görev, kapsam disiplinine açılmış gizli bir kapı haline
    /// gelirdi.
    /// </summary>
    private async Task<bool> GorevAtanabilirMiAsync(
        WorkTask gorev,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var yetki = await authorization.GetAsync(userId, cancellationToken);

        if (yetki is null || !yetki.IsActive)
            return false;

        if (!yetki.Permissions.Contains(
                PermissionCatalog.Keys.TasksView, StringComparer.OrdinalIgnoreCase))
            return false;

        // Global kapsam: her görevi görebilir.
        if (yetki.DataScopes.Any(x => x.ScopeType == 0))
            return true;

        return yetki.DataScopes.Any(x =>
            (x.CompanyId is Guid sirket && sirket == gorev.CompanyId) ||
            (x.ProjectId is Guid proje && gorev.ProjectId == proje) ||
            (x.BranchId is Guid sube && gorev.BranchId == sube) ||
            (x.ProjectSiteId is Guid santiye && gorev.ProjectSiteId == santiye));
    }

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(PermissionCatalog.Keys.TasksManage)]
    public async Task<IActionResult> Cancel(
        Guid id,
        CancelWorkTaskRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.WorkTasks
            .ApplyScope(await GetScopeAsync(cancellationToken))
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
            return NotFound(new { message = "Görev bulunamadı." });

        item.Status = WorkTaskStatus.Cancelled;
        item.CancelledAtUtc = DateTime.UtcNow;
        item.CancelledByUserId = currentUser.UserId;
        item.CancellationReason = request.Reason?.Trim();
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(item, await AdlariGetirAsync([item], cancellationToken)));
    }

    private static DateTime? ToUtcDate(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;

    /*
     * ADLAR DTO'YA SÖZLÜKLE GİRİYOR, İÇERİDE ÇÖZÜLMÜYOR.
     *
     * DTO'nun kendisi veritabanına gitseydi liste sayfası satır
     * başına üç sorgu atardı. Adlar çağıran tarafta TEK sorguda
     * toplanıyor. Sözlük verilmezse alanlar null döner — eski
     * davranış korunur.
     */
    private static object ToDto(WorkTask x, IReadOnlyDictionary<Guid, string>? adlar = null) => new
    {
        x.Id,
        x.CompanyId,
        x.ProjectId,
        x.TaskNumber,
        x.Title,
        x.Description,
        Priority = (int)x.Priority,
        PriorityName = x.Priority.ToString(),
        Status = (int)x.Status,
        StatusName = x.Status.ToString(),
        Kind = (int)x.Kind,
        KindName = x.Kind.ToString(),
        x.AssignedToUserId,
        x.AssignedToPersonnelId,
        x.AssignedByUserId,
        x.StartDate,
        x.DueDate,
        x.StartedAtUtc,
        x.CompletedAtUtc,
        x.CompletionNote,
        x.SourceModule,
        x.SourceEntityId,
        x.SourceEventCode,
        x.Tags,

        // ÇİFT ADIMLI KAPANIŞ VE İADE İZİ EKRANDA GÖRÜNSÜN.
        x.ApprovedAtUtc,
        x.ApprovedByUserId,
        x.ReturnedAtUtc,
        x.ReturnReason,

        // İADE SAYISI: üçüncü kez iade edilen iş, tek seferde biten
        // işle aynı satırda görünmemeli.
        x.ReturnCount,

        x.DelegatedFromUserId,
        x.DelegatedAtUtc,
        x.DelegationCount,

        CenterType = x.CenterType.HasValue ? (int)x.CenterType.Value : (int?)null,
        x.BranchId,
        x.ProjectSiteId,
        /*
         * GECİKME İADE İLE GİZLENMEZ.
         *
         * `Completed` gecikmiş sayılmıyor (iş yapanın elinden çıktı,
         * top gönderende) ama İADE EDİLEN görev yeniden `Open` olduğu
         * için termini geçmişse HEMEN KIRMIZI görünür. Termin iade
         * sırasında korunuyor; yeni termin verilmediyse eski tarih
         * duruyor ve gecikme olduğu gibi görünüyor.
         */
        IsOverdue = x.DueDate.HasValue &&
                    x.DueDate.Value < DateTime.UtcNow &&
                    x.Status != WorkTaskStatus.Completed &&
                    x.Status != WorkTaskStatus.Approved &&
                    x.Status != WorkTaskStatus.Cancelled,
        x.CreatedAtUtc,

        /*
         * KİM YAPACAK, KİM İSTEDİ, KİM ONAYLADI — İSİMLE.
         *
         * Ekranda GUID gösteren bir görev künyesi okunamaz. Ad
         * çözülemezse (kullanıcı silinmişse) sessizce boş geçmiyor:
         * açık bir metin dönüyor, yoksa alan hiç yokmuş gibi görünür.
         */
        ProjectName = AdBul(adlar, x.ProjectId),
        BranchName = AdBul(adlar, x.BranchId),
        ProjectSiteName = AdBul(adlar, x.ProjectSiteId),
        AssignedToName = AdBul(adlar, x.AssignedToUserId),
        AssignedToPersonnelName = AdBul(
            adlar, x.AssignedToPersonnelId, "(bilinmeyen personel)"),

        /*
         * "YAPACAK" SLOTUNUN TEK KAYNAĞI.
         *
         * Ekran bu alanı okur; iki alanı yan yana koyup kendi önceliğini
         * kurmaz. Bugün dördüncü kez aynı deseni düzelttik (ETİKET/1):
         * aynı soruyu iki yerden cevaplayan kod bir gün ayrışıyor.
         *
         * BURADA ÖNCELİK KURALI YOK, OLAMAZ DA: `GorevAtamaKurali`
         * ikisinin birden dolmasını REDDEDİYOR, dolayısıyla en fazla
         * biri doludur. Öncelik kuralı yazsaydık, kapı bir gün
         * gevşediğinde hangisinin doğru olduğunu sessizce seçerdik.
         */
        AssignedToDisplayName =
            AdBul(adlar, x.AssignedToUserId) ??
            AdBul(adlar, x.AssignedToPersonnelId, "(bilinmeyen personel)"),

        AssignedByName = AdBul(adlar, x.AssignedByUserId),
        ApprovedByName = AdBul(adlar, x.ApprovedByUserId),
        DelegatedFromName = AdBul(adlar, x.DelegatedFromUserId)
    };

    private static string? AdBul(
        IReadOnlyDictionary<Guid, string>? adlar,
        Guid? kimlik,
        string bulunamadi = "(bilinmeyen kullanıcı)")
    {
        if (adlar is null || kimlik is null)
            return null;

        return adlar.TryGetValue(kimlik.Value, out var ad)
            ? ad
            : bulunamadi;
    }

    /// <summary>
    /// Görev satırlarındaki tüm kullanıcı adlarını TEK sorguda
    /// toplar — satır başına arama N+1 olurdu.
    /// </summary>
    /// <summary>
    /// Merkez doğrulamasının TEK giriş noktası. POST ve PUT bunu çağırır.
    ///
    /// Kural saf bir metotta (<see cref="MasrafMerkeziKurali"/>) yaşıyor;
    /// buradaki tek iş, şantiyenin projesini veritabanından okuyup ona
    /// vermek. Böylece kuralın kendisi test edilebilir kalıyor ve iki
    /// çağıran arasında kopya çıkmıyor.
    /// </summary>
    /// <summary>
    /// Masraf merkezini doğrular.
    ///
    /// `sourceModule` PARAMETRESİ KALDIRILDI (KURAL-KATMAN/1,
    /// 2026-09-04): kural artık kaynak modül adına BAKMIYOR. Dolu bir
    /// dizge tüm kuralı atlıyordu ve ölçüldü ki o kaçış, kurulduğu
    /// sebep için bir kez bile kullanılmamıştı.
    /// </summary>
    private async Task<string?> MerkezDogrulaAsync(
        WorkTaskKind kind,
        Guid? projectId,
        Guid? branchId,
        Guid? projectSiteId,
        ExpenseCenterType? centerType,
        CancellationToken cancellationToken)
    {
        Guid? santiyeninProjesi = null;

        if (projectSiteId.HasValue)
        {
            santiyeninProjesi = await db.ProjectSites
                .AsNoTracking()
                .Where(x => x.Id == projectSiteId.Value)
                .Select(x => (Guid?)x.ProjectId)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return MasrafMerkeziKurali.Dogrula(
            kind, projectId, branchId, projectSiteId,
            centerType, santiyeninProjesi);
    }

    /// <summary>
    /// ATANAN PERSONEL GERÇEK, ÇALIŞIYOR VE KAPSAM İÇİNDE Mİ.
    ///
    /// ÜÇ AYRI SORU, ÜÇ AYRI HATA MESAJI DEĞİL — bilerek. Kapsam
    /// dışındaki bir personelin "var ama göremiyorsun" diye ayrı bir
    /// cevap alması, kapsamın kendisini bir arama aracına çevirirdi:
    /// kimliği deneyerek kimin var olduğu öğrenilebilirdi. Kapsam dışı
    /// personel, olmayan personelle AYNI cevabı alır.
    ///
    /// AYRILAN TEK DURUM İŞTEN AYRILMIŞ PERSONEL: onu kullanıcı zaten
    /// görebiliyor (kapsam içinde), dolayısıyla ayrı mesaj bilgi
    /// sızdırmıyor ve "listede vardı ama atayamadım" şaşkınlığını
    /// önlüyor.
    /// </summary>
    private async Task<string?> PersonelAtanabilirMiAsync(
        Guid companyId,
        Guid personnelId,
        CancellationToken cancellationToken)
    {
        /*
         * HAM `db.Personnel` KULLANILMIYOR: kontrolcülerde kapsamsız
         * personel okuması bekçi test tarafından yasak (ScopedData.cs).
         * Atama kapısı bir OKUMA yapıyor ve o okuma da kapsamlı.
         */
        var personel = await (await scoped.PersonnelAsync(cancellationToken))
            .AsNoTracking()
            .Where(x => x.Id == personnelId && x.CompanyId == companyId)
            .Select(x => new { x.Status })
            .SingleOrDefaultAsync(cancellationToken);

        if (personel is null)
            return "Seçilen personel bulunamadı.";

        if (personel.Status != PersonnelStatus.Active)
        {
            return
                "Seçilen personel aktif çalışan değil; " +
                "göreve atanamaz.";
        }

        return null;
    }

    private async Task<Dictionary<Guid, string>> AdlariGetirAsync(
        IEnumerable<WorkTask> gorevler, CancellationToken cancellationToken)
    {
        var liste = gorevler
            .SelectMany(x => new[]
            {
                x.AssignedToUserId, x.AssignedByUserId,
                x.ApprovedByUserId, x.DelegatedFromUserId
            })
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();

        // ERKEN ÇIKIŞ KALDIRILDI: kullanıcı kimliği olmayan ama
        // merkezi olan bir görevde merkez adı da çözülmeliydi.
        var merkezVarMi = gorevler.Any(x =>
            x.ProjectId.HasValue || x.BranchId.HasValue || x.ProjectSiteId.HasValue);

        /*
         * PERSONEL DE ERKEN ÇIKIŞA GİRER.
         *
         * Erken çıkış listesi bir kez zaten eksik kalmıştı (merkezi olan
         * ama kullanıcısı olmayan görev). Aynı hatanın personel biçimi:
         * yalnız personele atanmış bir görevde `liste` boş, merkez de
         * boşsa erken çıkılır ve ad ÇÖZÜLMEZDİ — ekran "Yapacak" yerine
         * hiçbir şey gösterirdi.
         */
        var personeller = gorevler
            .Select(x => x.AssignedToPersonnelId)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();

        if (liste.Count == 0 && personeller.Count == 0 && !merkezVarMi)
            return [];

        var sonuc = await db.Users
            .AsNoTracking()
            .Where(x => liste.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        /*
         * MERKEZ ADLARI DA AYNI SÖZLÜKTE.
         *
         * Ekranda GUID gösteren bir merkez sütunu okunamaz. Liste
         * ekranı adları kendi çektiği listelerden çözebiliyordu ama
         * DETAY ekranı hiçbir liste çekmiyor — aynı bilgi iki ayrı
         * yoldan üretilseydi ikisi bir gün ayrışırdı.
         *
         * Kimlikler GUID ve tablolar arası çakışmadığı için tek
         * sözlük yetiyor; `ToDto` hepsini `AdBul` ile okuyor.
         */
        /*
         * MERKEZ ADLARI DA KAPSAM SÜZGECİNDEN GEÇER.
         *
         * İlk yazımım `db.Projects` ve `db.Branches`'i süzgeçsiz
         * okuyordu ve `CoverageBaselineTests` bunu yakaladı. "Kimliği
         * zaten kapsamlı bir görevden geldi, dolayısıyla güvenli"
         * diye düşünmüştüm — ama o bir ÇIKARIM; süzgeç bir ÖLÇÜM.
         * Kapsamı dar bir kullanıcı, göreceği bir görevin bağlı olduğu
         * ama KENDİ kapsamı dışındaki bir projenin kodunu ve adını
         * görebilirdi. Ad çözülemezse ekran "Proje" yazar — bilgi
         * sızmaz, ekran da kırılmaz.
         */
        var kapsam = await GetScopeAsync(cancellationToken);

        if (personeller.Count > 0)
        {
            /*
             * PERSONEL ADI DA KAPSAM SÜZGECİNDEN GEÇER.
             *
             * Ham `db.Personnel` kontrolcüde yasak; `scoped.PersonnelAsync`
             * kullanıcının görebileceği personeli veriyor. Kapsam dışı bir
             * personelin adı çözülmez ve ekran "(bilinmeyen personel)"
             * yazar — bilgi sızmaz, ekran kırılmaz.
             */
            foreach (var satir in await (await scoped.PersonnelAsync(cancellationToken))
                .AsNoTracking()
                .Where(x => personeller.Contains(x.Id))
                .Select(x => new { x.Id, Ad = x.FirstName + " " + x.LastName })
                .ToListAsync(cancellationToken))
            {
                sonuc[satir.Id] = satir.Ad.Trim();
            }
        }

        var projeler = gorevler.Select(x => x.ProjectId).Where(x => x.HasValue)
            .Select(x => x!.Value).Distinct().ToList();
        var subeler = gorevler.Select(x => x.BranchId).Where(x => x.HasValue)
            .Select(x => x!.Value).Distinct().ToList();
        var santiyeler = gorevler.Select(x => x.ProjectSiteId).Where(x => x.HasValue)
            .Select(x => x!.Value).Distinct().ToList();

        if (projeler.Count > 0)
        {
            foreach (var satir in await db.Projects.AsNoTracking().ApplyScope(kapsam)
                .Where(x => projeler.Contains(x.Id))
                .Select(x => new { x.Id, Ad = x.Code + " — " + x.Name })
                .ToListAsync(cancellationToken))
            {
                sonuc[satir.Id] = satir.Ad;
            }
        }

        if (subeler.Count > 0)
        {
            foreach (var satir in await db.Branches.AsNoTracking().ApplyScope(kapsam)
                .Where(x => subeler.Contains(x.Id))
                .Select(x => new { x.Id, Ad = x.Code + " — " + x.Name })
                .ToListAsync(cancellationToken))
            {
                sonuc[satir.Id] = satir.Ad;
            }
        }

        if (santiyeler.Count > 0)
        {
            /*
             * ŞANTİYE KAPSAMI GEÇİŞLİ KAPATILIYOR.
             *
             * `ProjectSite` `CompanyId` taşımadığı için
             * `CoverageBaselineTests`'in kapsamı dışında — cırcır bunu
             * BİLDİRMEDİ. Ama sızıntı sınıfı proje/şubeyle aynı ve
             * `ProjectSite` için bir `Apply` aşırı yüklemesi yok.
             *
             * Şantiye kendi projesinin altında yaşıyor: projesi
             * kullanıcının kapsamındaysa şantiyesi de öyle. Süzgeci
             * proje üzerinden kuruyoruz.
             *
             * Cırcırın görmediği bir sızıntıyı kapatmak, cırcırın
             * kapsamının ölçüm sınırı olduğunu unutmamak demek.
             */
            var kapsamliProjeler = db.Projects.AsNoTracking().ApplyScope(kapsam)
                .Select(x => x.Id);

            foreach (var satir in await db.ProjectSites.AsNoTracking()
                .Where(x => santiyeler.Contains(x.Id)
                    && kapsamliProjeler.Contains(x.ProjectId))
                .Select(x => new { x.Id, Ad = x.Code + " — " + x.Name })
                .ToListAsync(cancellationToken))
            {
                sonuc[satir.Id] = satir.Ad;
            }
        }

        return sonuc;
    }
}

public sealed record CreateWorkTaskRequest(
    Guid CompanyId,
    Guid? ProjectId,
    string Title,
    string? Description,
    WorkTaskPriority Priority,
    Guid? AssignedToUserId,
    DateTime? StartDate,
    DateTime? DueDate,
    string? SourceModule,
    Guid? SourceEntityId,
    string? SourceEventCode,
    string? Tags,

    /// <summary>
    /// SERBEST GÖREVDE ZORUNLU masraf merkezi. Kayda bağlı görevde
    /// merkez kaydın kendisinden türetilebiliyor, o yüzden serbest
    /// bırakıldı.
    /// </summary>
    ExpenseCenterType? CenterType = null,
    Guid? BranchId = null,
    Guid? ProjectSiteId = null,

    /// <summary>
    /// GÖREV TÜRÜ — ZORUNLU. Varsayılanı <c>Belirsiz</c> olması bir
    /// kolaylık değil, bir KAPI: gönderilmezse istek reddedilir.
    /// Sözdizimi gereği son parametrelerin varsayılanı olmak zorunda;
    /// o varsayılan, geçerli bir tür değil REDDEDİLEN değer seçildi.
    /// </summary>
    WorkTaskKind Kind = WorkTaskKind.Belirsiz,

    /// <summary>
    /// Sistem hesabı olmayan personele atama. <c>AssignedToUserId</c>
    /// ile birlikte gönderilemez — bkz. <c>GorevAtamaKurali</c>.
    /// </summary>
    Guid? AssignedToPersonnelId = null);

public sealed record UpdateWorkTaskRequest(
    string Title,
    string? Description,
    WorkTaskPriority Priority,
    Guid? AssignedToUserId,
    DateTime? StartDate,
    DateTime? DueDate,
    string? Tags,

    /// <summary>
    /// MERKEZ ALANLARI PUT'A EKLENDİ. Önce yoktu ve yanlış konmuş bir
    /// masraf merkezi düzeltilemiyordu. Doğrulama POST ile aynı metotta.
    /// </summary>
    Guid? ProjectId = null,
    Guid? BranchId = null,
    Guid? ProjectSiteId = null,
    ExpenseCenterType? CenterType = null,

    /// <summary>
    /// GÖREV TÜRÜ — ZORUNLU. Varsayılanı <c>Belirsiz</c> olması bir
    /// kolaylık değil, bir KAPI: gönderilmezse istek reddedilir.
    /// Sözdizimi gereği son parametrelerin varsayılanı olmak zorunda;
    /// o varsayılan, geçerli bir tür değil REDDEDİLEN değer seçildi.
    /// </summary>
    WorkTaskKind Kind = WorkTaskKind.Belirsiz,

    /// <summary>
    /// Sistem hesabı olmayan personele atama. <c>AssignedToUserId</c>
    /// ile birlikte gönderilemez — bkz. <c>GorevAtamaKurali</c>.
    /// </summary>
    Guid? AssignedToPersonnelId = null);

public sealed record CompleteWorkTaskRequest(string? CompletionNote);

public sealed record CancelWorkTaskRequest(string Reason);

/// <summary>
/// İade isteği. Gerekçe ZORUNLU; yeni termin seçimli — verilmezse
/// eski termin korunur ve gecikme gizlenmez.
/// </summary>
public sealed record ReturnWorkTaskRequest(string Reason, DateTime? NewDueDate);

/// <summary>Devretme isteği. Gerekçe zorunlu: devretme bir karardır.</summary>
public sealed record DelegateWorkTaskRequest(Guid ToUserId, string Reason);
