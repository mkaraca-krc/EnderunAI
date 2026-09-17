using System.Text.Json;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security.CurrentUser;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PurchaseOrderEntity = EnderunAI.Api.Models.PurchaseOrder.PurchaseOrder;

namespace EnderunAI.Api.Data.Interceptors;

public sealed class AuditSaveChangesInterceptor(
    ICurrentUserService currentUserService,
    IHttpContextAccessor httpContextAccessor)
    : SaveChangesInterceptor
{
    /// <summary>
    /// DENETLENMEYECEK TÜRLER — VARSAYILAN "DENETLENİR".
    ///
    /// ═══ NEDEN DIŞLAMA LİSTESİ (2026-09-13) ═══
    ///
    /// Burası 18 türlük bir İZİN listesiydi ve kimse kapsamını
    /// ölçmemişti. Ölçüm şunu gösterdi: `Cheque`, `CurrentAccount`,
    /// `StockMovement`, `GoodsReceipt`, `InventoryItem` ve
    /// `AccountingAccount` listede YOKTU — yani **çek değiştirildiğinde,
    /// cari bakiyesi elle düzeltildiğinde, hesap bayrağı çevrildiğinde
    /// sistemde hiçbir iz kalmıyordu.** Paranın yaşadığı yer.
    ///
    /// İzin listesinin kusuru yapısaldır: yarın eklenen varlık
    /// KORUMASIZ doğar ve bunu kimse fark etmez. Dışlama listesinde ise
    /// korumalı doğar; dışlamak için gerekçe yazmak gerekir.
    ///
    /// ═══ HER DIŞLAMA ÖLÇÜLDÜ, TAHMİN EDİLMEDİ ═══
    ///
    /// Hacim bir gerekçe DEĞİL: 30 günde tüm iş tablolarında 797 yeni
    /// kayıt var, bugünkü denetim üretimi 23,4 satır/gün, tablo 2107
    /// satır = 672 kB (~320 bayt/satır). Tam kapsamda bile yılda ~10 MB.
    ///
    /// Geçerli tek ölçüt: KURAL 80'İN DAYANAĞI YOKSA. Denetim kaydı
    /// "kim yaptı" sorusuna cevap verir; aktörü olmayan satırda
    /// verecek cevap yoktur.
    /// </summary>
    private static readonly HashSet<Type> DenetlenmeyenTurler =
    [
        // YAPISAL — kendini denetlemek sonsuz döngü üretir.
        typeof(SecurityAuditEvent),

        // ÖLÇÜLDÜ (2026-09-13): 255/255 satır aktörsüz (CreatedByUserId
        // null). Kuru fiyat verisi; makine çekiyor, insan yapmıyor.
        typeof(Models.Market.ExchangeRate),

        // ÖLÇÜLDÜ: 92/92 satır aktörsüz. Aynı gerekçe.
        typeof(Models.Market.CommodityPrice),

        // ÖLÇÜLDÜ: 11/11 satır aktörsüz. Sistem üretiyor; okundu
        // işaretlemesi de kullanıcının "yaptığı" bir iş değil.
        typeof(Models.Notifications.Notification),
    ];

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyAuditInformation(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditInformation(eventData.Context);

        return base.SavingChangesAsync(
            eventData,
            result,
            cancellationToken);
    }

    private void ApplyAuditInformation(DbContext? context)
    {
        if (context is null)
            return;

        var now = DateTime.UtcNow;
        var userId = currentUserService.UserId;

        RecordSecurityAuditEvents(context, userId);

        foreach (var entry in context.ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = now;
                    entry.Entity.CreatedByUserId = userId;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAtUtc = now;
                    entry.Entity.UpdatedByUserId = userId;

                    entry.Property(x => x.CreatedAtUtc).IsModified = false;
                    entry.Property(x => x.CreatedByUserId).IsModified = false;

                    if (entry.Entity.IsDeleted)
                    {
                        entry.Entity.DeletedAtUtc ??= now;
                        entry.Entity.DeletedByUserId ??= userId;
                        entry.Entity.IsActive = false;
                    }

                    break;

                case EntityState.Deleted:
                    entry.State = EntityState.Modified;

                    entry.Entity.IsDeleted = true;
                    entry.Entity.IsActive = false;
                    entry.Entity.DeletedAtUtc = now;
                    entry.Entity.DeletedByUserId = userId;
                    entry.Entity.UpdatedAtUtc = now;
                    entry.Entity.UpdatedByUserId = userId;

                    entry.Property(x => x.CreatedAtUtc).IsModified = false;
                    entry.Property(x => x.CreatedByUserId).IsModified = false;
                    break;
            }
        }
    }

    private void RecordSecurityAuditEvents(DbContext context, Guid? userId)
    {
        var httpContext = httpContextAccessor.HttpContext;

        //
        // ═══ BAĞLANTI ADRESİ DEĞİL, İSTEMCİ ADRESİ (VEKİL/2, 2026-09-17) ═══
        //
        // Burada `Connection.RemoteIpAddress` okunuyordu. Üretim zinciri
        // istemci → nginx → Next → arka uç olduğu için o adres HER ZAMAN
        // vekilin kendisiydi: denetim kaydındaki her `Created`/`Updated`
        // satırı `127.0.0.1` yazıyordu.
        //
        // ÖLÇÜLDÜ (17.09): `[...path]` vekilinden geçen iki istek, iki
        // farklı adresten — iki denetim satırı da 127.0.0.1.
        //
        // Çözücü `X-Forwarded-For`a YALNIZ istek bizim vekilimizden
        // geldiğinde bakar; gelmiyorsa başlığı yok sayar ve sonucu
        // `(vekilsiz)` diye İŞARETLER. Sessiz geri düşüş yok.
        //
        var ipAddress = EnderunAI.Api.Security.Adres.IstemciAdresCozucu
            .KayitAdresi(httpContext);
        var userAgent = httpContext?.Request.Headers.UserAgent.ToString();
        var username = currentUserService.Username;

        var entries = context.ChangeTracker.Entries().ToList();

        foreach (var entry in entries)
        {
            // VARSAYILAN DENETLENİR. Yeni bir varlık türü eklendiğinde
            // hiçbir şey yapılmasa da denetim izine girer; dışarıda
            // kalması için buraya gerekçesiyle yazılması gerekir.
            if (entry.Entity is not BaseEntity)
                continue;

            if (DenetlenmeyenTurler.Contains(entry.Entity.GetType()))
                continue;

            var action = entry.State switch
            {
                EntityState.Added => "Created",
                EntityState.Modified => "Updated",
                EntityState.Deleted => "Deleted",
                _ => null
            };

            if (action is null)
                continue;

            var (entityId, summary) = Describe(entry.Entity);

            context.Set<SecurityAuditEvent>().Add(new SecurityAuditEvent
            {
                ActorUserId = userId,
                ActorUsername = username,
                Action = action,
                EntityType = entry.Entity.GetType().Name,
                EntityId = entityId,
                DetailsJson = summary is null
                    ? null
                    : JsonSerializer.Serialize(new { summary }),
                IpAddress = ipAddress,
                UserAgent = userAgent,
                OccurredAtUtc = DateTime.UtcNow
            });
        }
    }

    private static (Guid? Id, string? Summary) Describe(object entity) => entity switch
    {
        AppUser u => (u.Id, u.Username),
        UserRole ur => ((Guid?)null, $"UserId={ur.UserId} RoleId={ur.RoleId}"),
        Personnel p => (p.Id, $"{p.EmployeeNumber} {p.FullName}".Trim()),
        Project pr => (pr.Id, $"{pr.Code} {pr.Name}".Trim()),
        ProjectCostTransaction ct => (ct.Id, ct.Description),
        PurchaseOrderEntity po => (po.Id, po.OrderNumber),
        PurchaseRequest req => (req.Id, req.RequestNumber),
        AccountingVoucher v => (v.Id, v.VoucherNumber),
        /*
         * TOKEN DENETİM KAYDINA YAZILMAZ.
         *
         * Eski hali `link.EmployerEmail ?? link.Token` idi: e-posta
         * boş olan bağlantılarda 256 bitlik anahtarın TAMAMI düz metin
         * olarak security_audit_events'e yazılıyordu. Portal, sistemin
         * kimlik doğrulaması olmayan tek veri kapısı; o kaydı
         * okuyabilen herkes çalışan bir anahtar elde ederdi.
         *
         * Denetim kaydı, koruduğu sırrı ele veren bir yer olamaz —
         * nginx erişim kaydında ve PortalTokenRejected olayında token
         * zaten maskeleniyordu; burası açıkta kalmıştı.
         *
         * Kim/hangi proje sorusuna cevap vermeye devam ediyor:
         * e-posta varsa o, yoksa proje kimliği.
         */
        EmployerPortalLink link => (
            link.Id,
            link.EmployerEmail ?? $"ProjectId={link.ProjectId}"),
        RolePermission rp => ((Guid?)null, $"RoleId={rp.RoleId} PermissionId={rp.PermissionId}"),
        UserPermissionOverride upo => (upo.Id, $"UserId={upo.UserId} PermissionId={upo.PermissionId} Effect={upo.Effect}"),
        UserDataScope uds => (uds.Id, $"UserId={uds.UserId} ScopeType={uds.ScopeType}"),
        RoleWorkHourWindow w => ((Guid?)null, $"RoleId={w.RoleId} Day={w.DayOfWeek} {w.StartTime}-{w.EndTime}"),
        AccessRequest ar => (ar.Id, $"UserId={ar.UserId} Status={ar.Status}"),
        TemporaryAccessGrant g => (g.Id, $"UserId={g.UserId} ExpiresAtUtc={g.ExpiresAtUtc:o}"),
        ProjectDocument pd => (pd.Id, $"ProjectId={pd.ProjectId} {pd.Folder}/{pd.FileName} v{pd.VersionNumber}"),
        /*
         * VARSAYILAN — ÖZEL ÖZET YAZILMAMIŞ TÜR DE KAYDA GİRER.
         *
         * Eskiden bu dal `(null, null)` dönüyordu ve zaten yalnız izin
         * listesindeki türler buraya geliyordu. Artık her `BaseEntity`
         * geliyor: kimliği yazmamak, kaydı "bir şey değişti ama neyi
         * bilmiyoruz"a çevirirdi.
         *
         * Özet YOK bırakılıyor (null): tür başına anlamlı bir özet
         * uydurmak, yanlış özet üretmekten kötüdür. Tür adı ve kimlik
         * zaten kaydın kendisinde duruyor.
         */
        BaseEntity be => (be.Id, null),

        _ => (null, null)
    };
}
