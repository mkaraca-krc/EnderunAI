using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.Collaboration;
using EnderunAI.Api.Services.Upload;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// YORUM VE EK DOSYA — HER KAYDIN ALTINDAKİ TEK ZAMAN ÇİZELGESİ.
///
/// TEMEL FİKİR: görev, atanmış ve terminli bir yorumdur. Bu yüzden
/// yorum ve görev ayrı sistem değil; ikisi de `(varlık tipi + kayıt
/// no)` ile aynı kaydın altında duruyor.
///
/// İŞE BAĞLI: serbest sohbet yok. Bağsız yorum yazılamaz — yazılsaydı
/// sistem zamanla ikinci bir mesajlaşma uygulamasına dönerdi.
///
/// KAPSAM KONTROLÜ LİSTE BAŞINA BİR KEZ: yorumlar zaten tek kaydın
/// altında. Her satır için ayrı çözümleme N+1 sorgu olurdu.
/// </summary>
[ApiController]
[Authorize]
[Route("api/collaboration")]
public sealed class CollaborationController(
    AppDbContext db,
    ICurrentUserService currentUser,
    ICurrentDataScopeService dataScope,
    IEntityContextResolver entityResolver,
    IUploadService uploadService,
    EnderunAI.Api.Services.Notifications.ITaskNotificationWriter notifications)
    : ControllerBase
{
    /// <summary>
    /// DÜZENLEME PENCERESİ: 15 dakika.
    ///
    /// Süresiz düzenleme, konuşmanın geçmişini değiştirilebilir kılar:
    /// birinin cevap verdiği cümle sonradan başka bir cümleye
    /// dönüşebilir. On beş dakika yazım hatasını düzeltmeye yeter,
    /// tartışmayı yeniden yazmaya yetmez.
    /// </summary>
    private static readonly TimeSpan DuzenlemePenceresi = TimeSpan.FromMinutes(15);

    private const int SayfaTavani = 100;

    /// <summary>Ek dosya kategorisi — IUploadService klasörü.</summary>
    private const string EkKategorisi = "collaboration";

    /*
     * TARAYICIDA GÖRÜNTÜLENEBİLEN TÜRLER.
     *
     * HEIC LİSTEDE YOK — BİLEREK: yükleme kabul ediyor (iPhone
     * varsayılanı) ama Chrome ve Firefox HEIC'i GÖSTEREMEZ. Ekran bu
     * bilgiye bakıp "tarayıcıda görüntülenemiyor, indirin" diyor;
     * bozuk resim simgesi göstermiyor.
     *
     * Sunucuda JPEG önizleme üretmek ayrı bir iş (DURUM.md'de açık
     * madde) ve önce ÖLÇÜM bekliyor: iOS çoğu durumda dosya seçimi
     * sırasında HEIC'i kendisi JPEG'e çeviriyor, yani sunucuya hiç
     * HEIC gelmiyor olabilir.
     */
    private static readonly HashSet<string> TarayicidaGoruntulenebilir =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg", "image/png", "image/webp", "image/gif", "application/pdf"
        };

    private async Task<CurrentDataScopeSnapshot> GetScopeAsync(
        CancellationToken cancellationToken) =>
        await dataScope.GetAsync(cancellationToken) ??
        throw new UnauthorizedAccessException("Kullanıcı veri kapsamı bulunamadı.");

    /// <summary>
    /// Kayda erişim kontrolü — TEK NOKTA.
    ///
    /// Yorum listesi, yorum yazma, ek yükleme ve EK İNDİRME aynı
    /// kapıdan geçiyor. İndirme atlanırsa sızıntı ekrandan dosyaya
    /// taşınır; G3/1b'de dışa aktarım uçlarında tam olarak bu
    /// yaşandı.
    /// </summary>
    private async Task<EntityContext?> ErisimKontroluAsync(
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        var scope = await GetScopeAsync(cancellationToken);

        var baglam = await entityResolver.ResolveAsync(
            entityType, entityId, cancellationToken);

        if (baglam is null)
            return null;

        if (scope.HasGlobalAccess)
            return baglam;

        var erisebilir =
            scope.CompanyIds.Contains(baglam.CompanyId) ||
            (baglam.ProjectId is Guid proje && scope.ProjectIds.Contains(proje));

        return erisebilir ? baglam : null;
    }

    // ---------------------------------------------------------------
    // YORUM
    // ---------------------------------------------------------------

    [HttpGet("comments")]
    public async Task<IActionResult> GetComments(
        [FromQuery] string entityType,
        [FromQuery] Guid entityId,
        [FromQuery] int? pageSize,
        [FromQuery] DateTime? cursorCreatedAtUtc,
        [FromQuery] Guid? cursorId,
        CancellationToken cancellationToken)
    {
        // KAPSAM KONTROLÜ BİR KEZ — satır başına değil.
        var baglam = await ErisimKontroluAsync(entityType, entityId, cancellationToken);

        if (baglam is null)
            return NotFound(new { message = "Kayıt bulunamadı." });

        var alinacak = Math.Clamp(pageSize ?? 50, 1, SayfaTavani);

        /*
         * KAPSAM SÜZGECİ İKİNCİ SAVUNMA HATTI.
         *
         * Asıl kontrol yukarıda, KAYIT DÜZEYİNDE ve LİSTE BAŞINA BİR
         * KEZ yapıldı (satır başına çözümleme N+1 olurdu). Buradaki
         * süzgeç onu tekrarlıyor: bir gün erişim kontrolü atlanırsa
         * sorgu yine de başka şirketin yorumunu döndürmesin.
         */
        var query = db.TaskComments
            .AsNoTracking()
            .ApplyScope(await GetScopeAsync(cancellationToken))
            .Where(x => x.EntityType == entityType && x.EntityId == entityId);

        // KEYSET: yorum sayısı görevden de hızlı büyür.
        if (cursorCreatedAtUtc.HasValue && cursorId.HasValue)
        {
            var t = cursorCreatedAtUtc.Value;
            var i = cursorId.Value;

            query = query.Where(x =>
                x.CreatedAtUtc < t || (x.CreatedAtUtc == t && x.Id.CompareTo(i) < 0));
        }

        var satirlar = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(alinacak + 1)
            .ToListAsync(cancellationToken);

        var devamVar = satirlar.Count > alinacak;
        var sayfa = devamVar ? satirlar.Take(alinacak).ToList() : satirlar;
        var son = sayfa.LastOrDefault();

        return Ok(new
        {
            items = sayfa.Select(YorumDto),
            hasMore = devamVar,
            nextCursor = devamVar && son is not null
                ? new { createdAtUtc = son.CreatedAtUtc, id = son.Id }
                : null
        });
    }

    [HttpPost("comments")]
    public async Task<IActionResult> AddComment(
        AddCommentRequest request,
        CancellationToken cancellationToken)
    {
        var metin = request.Body?.Trim();

        if (string.IsNullOrWhiteSpace(metin))
            return BadRequest(new { message = "Yorum metni zorunludur." });

        var baglam = await ErisimKontroluAsync(
            request.EntityType, request.EntityId, cancellationToken);

        if (baglam is null)
            return NotFound(new { message = "Kayıt bulunamadı." });

        var yorum = new TaskComment
        {
            // ŞİRKET KAYITTAN GELİYOR, İSTEKTEN DEĞİL: istemciden
            // alınsaydı kullanıcı başka şirketin altına yorum yazabilirdi.
            CompanyId = baglam.CompanyId,
            EntityType = request.EntityType.Trim(),
            EntityId = request.EntityId,
            Body = metin,
            MentionedUserIds = request.MentionedUserIds is { Count: > 0 }
                ? string.Join(',', request.MentionedUserIds)
                : null,
            CreatedByUserId = currentUser.UserId
        };

        db.TaskComments.Add(yorum);
        await db.SaveChangesAsync(cancellationToken);

        /*
         * @ İLE ANILANLARA HABER — YORUM KAYDEDİLDİKTEN SONRA.
         *
         * Bildirim yazımı asıl işlemi çökertmiyor: yazıcı hatayı
         * kendi sınırında karşılıyor ve kayda düşürüyor. Yorum
         * yazıldıysa yazılmıştır; bildirim gitmese bile.
         *
         * KENDİNİ ANMA BİLDİRİM ÜRETMEZ: kişi zaten yazan.
         */
        if (request.MentionedUserIds is { Count: > 0 })
        {
            foreach (var anilan in request.MentionedUserIds.Distinct())
            {
                if (anilan == currentUser.UserId)
                    continue;

                await notifications.WriteAsync(
                    baglam.CompanyId,
                    anilan,
                    EnderunAI.Api.Services.Notifications.TaskNotificationTypes.Mentioned,

                    // KAYNAK YORUMUN KENDİSİ: aynı kayıtta ikinci kez
                    // anılmak yeni bildirim üretmeli, o yüzden görev
                    // değil YORUM kimliği.
                    yorum.Id,
                    "-",

                    "Bir yorumda anıldınız",
                    metin.Length > 160 ? metin[..160] + "…" : metin,
                    null,
                    Models.Notifications.NotificationSeverity.Info,
                    cancellationToken);
            }
        }

        return Ok(YorumDto(yorum));
    }

    /// <summary>
    /// Yorum düzenleme — YALNIZ İLK 15 DAKİKA ve YALNIZ YAZAN.
    /// </summary>
    [HttpPut("comments/{id:guid}")]
    public async Task<IActionResult> EditComment(
        Guid id,
        EditCommentRequest request,
        CancellationToken cancellationToken)
    {
        var metin = request.Body?.Trim();

        if (string.IsNullOrWhiteSpace(metin))
            return BadRequest(new { message = "Yorum metni zorunludur." });

        var yorum = await db.TaskComments
            .ApplyScope(await GetScopeAsync(cancellationToken))
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (yorum is null)
            return NotFound(new { message = "Yorum bulunamadı." });

        if (await ErisimKontroluAsync(
                yorum.EntityType, yorum.EntityId, cancellationToken) is null)
            return NotFound(new { message = "Kayıt bulunamadı." });

        if (yorum.CreatedByUserId != currentUser.UserId)
            return Forbid();

        if (yorum.HiddenAtUtc is not null)
            return BadRequest(new { message = "Gizlenmiş yorum düzenlenemez." });

        var gecenSure = DateTime.UtcNow - yorum.CreatedAtUtc;

        if (gecenSure > DuzenlemePenceresi)
        {
            return BadRequest(new
            {
                message =
                    "Düzenleme süresi doldu (15 dakika). Yeni bir yorum yazın — " +
                    "eski yorum, cevap verilmiş olabileceği için değiştirilemez."
            });
        }

        yorum.Body = metin;
        yorum.EditedAtUtc = DateTime.UtcNow;
        yorum.EditCount += 1;
        yorum.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(YorumDto(yorum));
    }

    /// <summary>
    /// YORUM SİLİNMEZ, GİZLENİR.
    ///
    /// Silme, cevap verilmiş bir cümleyi konuşmadan çıkarır ve kalan
    /// cevapları anlamsızlaştırır. Gizlenen yorum "silindi" olarak
    /// görünüyor; kim ve ne zaman gizlediği duruyor.
    /// </summary>
    [HttpPost("comments/{id:guid}/hide")]
    public async Task<IActionResult> HideComment(
        Guid id,
        CancellationToken cancellationToken)
    {
        var yorum = await db.TaskComments
            .ApplyScope(await GetScopeAsync(cancellationToken))
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (yorum is null)
            return NotFound(new { message = "Yorum bulunamadı." });

        if (await ErisimKontroluAsync(
                yorum.EntityType, yorum.EntityId, cancellationToken) is null)
            return NotFound(new { message = "Kayıt bulunamadı." });

        if (yorum.CreatedByUserId != currentUser.UserId)
            return Forbid();

        yorum.HiddenAtUtc = DateTime.UtcNow;
        yorum.HiddenByUserId = currentUser.UserId;
        yorum.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(YorumDto(yorum));
    }

    // ---------------------------------------------------------------
    // EK DOSYA
    // ---------------------------------------------------------------

    [HttpGet("attachments")]
    public async Task<IActionResult> GetAttachments(
        [FromQuery] string entityType,
        [FromQuery] Guid entityId,
        CancellationToken cancellationToken)
    {
        // KAPSAM KONTROLÜ BİR KEZ — dosya başına değil.
        if (await ErisimKontroluAsync(entityType, entityId, cancellationToken) is null)
            return NotFound(new { message = "Kayıt bulunamadı." });

        // İkinci savunma hattı — bkz. yorum listesi.
        var ekler = await db.Attachments
            .AsNoTracking()
            .ApplyScope(await GetScopeAsync(cancellationToken))
            .Where(x => x.EntityType == entityType && x.EntityId == entityId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(ekler.Select(EkDto));
    }

    [HttpPost("attachments")]
    [RequestSizeLimit(52_428_800)]
    public async Task<IActionResult> UploadAttachment(
        [FromForm] string entityType,
        [FromForm] Guid entityId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var baglam = await ErisimKontroluAsync(entityType, entityId, cancellationToken);

        if (baglam is null)
            return NotFound(new { message = "Kayıt bulunamadı." });

        try
        {
            var kaydedilen = await uploadService.SaveAsync(
                file, EkKategorisi, cancellationToken);

            var ek = new Attachment
            {
                CompanyId = baglam.CompanyId,
                EntityType = entityType.Trim(),
                EntityId = entityId,
                Category = EkKategorisi,
                StoredName = kaydedilen.StoredName,
                OriginalName = kaydedilen.OriginalName,
                ContentType = kaydedilen.ContentType,
                SizeBytes = kaydedilen.Size,
                UploadedByUserId = currentUser.UserId
            };

            db.Attachments.Add(ek);
            await db.SaveChangesAsync(cancellationToken);

            return Ok(EkDto(ek));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary>
    /// EK İNDİRME — AYRI BİR KAPI, AYRI KONTROL.
    ///
    /// Yorum ucunu kapsamlayıp indirmeyi unutmak, sızıntıyı ekrandan
    /// DOSYAYA taşır. Bu uç da aynı çözümleyiciden geçiyor: kullanıcı
    /// kaydı göremiyorsa eki de indiremiyor.
    ///
    /// DOSYA ADI TAHMİN EDİLEREK ERİŞİLEMEZ: depolanan ad
    /// `zamandamgası_GUID.uzantı` (128 bit rastgelelik), gerçek ad
    /// ayrı alanda. Ama asıl koruma bu değil — asıl koruma kapsam
    /// kontrolü; rastgele ad yalnız ikinci savunma hattı.
    /// </summary>
    [HttpGet("attachments/{id:guid}/download")]
    public async Task<IActionResult> DownloadAttachment(
        Guid id,
        CancellationToken cancellationToken)
    {
        var ek = await db.Attachments
            .AsNoTracking()
            .ApplyScope(await GetScopeAsync(cancellationToken))
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (ek is null)
            return NotFound(new { message = "Ek bulunamadı." });

        if (await ErisimKontroluAsync(
                ek.EntityType, ek.EntityId, cancellationToken) is null)
            return NotFound(new { message = "Ek bulunamadı." });

        var dosya = uploadService.GetFile(ek.Category, ek.StoredName);

        if (dosya is null)
            return NotFound(new { message = "Dosya bulunamadı." });

        var akis = new FileStream(
            dosya.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        // İNDİRİRKEN GERÇEK AD: kullanıcı "20260823_a1b2...pdf" değil,
        // yüklediği adı görüyor.
        return File(akis, ek.ContentType, ek.OriginalName, enableRangeProcessing: true);
    }

    // ---------------------------------------------------------------

    private static object YorumDto(TaskComment x) => new
    {
        x.Id,
        x.EntityType,
        x.EntityId,

        // GİZLENMİŞ YORUMUN METNİ DÖNMÜYOR: gizleme, içeriği
        // saklamaksa gövdeyi göndermek onu anlamsız kılardı.
        Body = x.HiddenAtUtc is null ? x.Body : null,
        IsHidden = x.HiddenAtUtc is not null,

        x.CreatedAtUtc,
        x.CreatedByUserId,
        x.EditedAtUtc,
        x.EditCount,
        x.HiddenAtUtc,
        x.HiddenByUserId,
        MentionedUserIds = x.MentionedUserIds
    };

    private static object EkDto(Attachment x) => new
    {
        x.Id,
        x.EntityType,
        x.EntityId,
        x.OriginalName,
        x.ContentType,
        x.SizeBytes,
        x.CreatedAtUtc,
        x.UploadedByUserId,

        /*
         * TARAYICIDA AÇILABİLİR Mİ.
         *
         * HEIC için `false` dönüyor: yükleme kabul ediliyor ama Chrome
         * ve Firefox gösteremiyor. Ekran bu bilgiye bakıp "indirin"
         * diyor — bozuk resim simgesi göstermiyor.
         */
        IsBrowserViewable = TarayicidaGoruntulenebilir.Contains(x.ContentType),

        DownloadUrl = $"/api/collaboration/attachments/{x.Id}/download"
    };
}

public sealed record AddCommentRequest(
    string EntityType,
    Guid EntityId,
    string Body,
    IReadOnlyCollection<Guid>? MentionedUserIds);

public sealed record EditCommentRequest(string Body);
