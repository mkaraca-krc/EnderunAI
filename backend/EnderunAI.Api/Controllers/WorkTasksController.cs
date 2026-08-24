using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Expenses;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/tasks")]
public sealed class WorkTasksController(
    AppDbContext db,
    ICurrentUserService currentUser,
    EnderunAI.Api.Services.DocumentNumbers.IDocumentNumberService documentNumbers,
    ICurrentDataScopeService dataScope,
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
         * Kayda bağlı görevde (SourceModule dolu) merkez kaydın
         * kendisinden türetilebiliyor — hakediş projeye, mal kabul
         * depoya bağlı — o yüzden orada zorunlu değil.
         */
        var kaydaBagli = !string.IsNullOrWhiteSpace(request.SourceModule);

        if (!kaydaBagli)
        {
            var merkezVar =
                request.ProjectId.HasValue ||
                request.BranchId.HasValue ||
                request.ProjectSiteId.HasValue;

            if (!merkezVar)
            {
                return BadRequest(new
                {
                    message =
                        "Kayda bağlı olmayan görevde masraf merkezi zorunludur: " +
                        "proje, şube ya da şantiye seçin."
                });
            }
        }

        /*
         * ATANAN KİŞİ KAYDI GÖREBİLMELİ.
         *
         * Göremeyeceği bir göreve atanan kullanıcı, gelen kutusunda
         * açamadığı bir satır görür. Daha kötüsü: görev, kapsam
         * disiplinine açılmış gizli bir kapı olurdu.
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
            CenterType = request.CenterType,
            BranchId = request.BranchId,
            ProjectSiteId = request.ProjectSiteId,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            Priority = request.Priority,
            Status = WorkTaskStatus.Open,
            AssignedToUserId = request.AssignedToUserId,
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

        item.Title = request.Title.Trim();
        item.Description = request.Description?.Trim();
        item.Priority = request.Priority;
        item.AssignedToUserId = request.AssignedToUserId;
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

        item.DelegatedFromUserId = oncekiSorumlu;
        item.DelegatedAtUtc = DateTime.UtcNow;
        item.DelegationCount += 1;
        item.AssignedToUserId = request.ToUserId;
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
        x.AssignedToUserId,
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
        AssignedToName = AdBul(adlar, x.AssignedToUserId),
        AssignedByName = AdBul(adlar, x.AssignedByUserId),
        ApprovedByName = AdBul(adlar, x.ApprovedByUserId),
        DelegatedFromName = AdBul(adlar, x.DelegatedFromUserId)
    };

    private static string? AdBul(
        IReadOnlyDictionary<Guid, string>? adlar, Guid? kimlik)
    {
        if (adlar is null || kimlik is null)
            return null;

        return adlar.TryGetValue(kimlik.Value, out var ad)
            ? ad
            : "(bilinmeyen kullanıcı)";
    }

    /// <summary>
    /// Görev satırlarındaki tüm kullanıcı adlarını TEK sorguda
    /// toplar — satır başına arama N+1 olurdu.
    /// </summary>
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

        if (liste.Count == 0)
            return [];

        return await db.Users
            .AsNoTracking()
            .Where(x => liste.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);
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
    Guid? ProjectSiteId = null);

public sealed record UpdateWorkTaskRequest(
    string Title,
    string? Description,
    WorkTaskPriority Priority,
    Guid? AssignedToUserId,
    DateTime? StartDate,
    DateTime? DueDate,
    string? Tags);

public sealed record CompleteWorkTaskRequest(string? CompletionNote);

public sealed record CancelWorkTaskRequest(string Reason);

/// <summary>
/// İade isteği. Gerekçe ZORUNLU; yeni termin seçimli — verilmezse
/// eski termin korunur ve gecikme gizlenmez.
/// </summary>
public sealed record ReturnWorkTaskRequest(string Reason, DateTime? NewDueDate);

/// <summary>Devretme isteği. Gerekçe zorunlu: devretme bir karardır.</summary>
public sealed record DelegateWorkTaskRequest(Guid ToUserId, string Reason);
