using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Contracts;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Genel Müdür")]
[Route("api/user-management/permission-matrix")]
public sealed class PermissionMatrixController(
    AppDbContext db,
    ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.UserManagementView)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var roles = await db.Roles
            .AsNoTracking()
            .OrderBy(role => role.Name)
            .Select(role => new
            {
                role.Id,
                role.Name,
                role.Description,
                DataScopePolicy = (int)role.DataScopePolicy
            })
            .ToListAsync(cancellationToken);

        /*
         * GRANTS DA GİZLENEN İZİNLERE GÖRE SÜZÜLÜYOR (KARAR 3b).
         *
         * ÖLÇÜLMÜŞ TUTARSIZLIK: `permissions` süzülüyordu ama
         * `grants` süzülmüyordu. Ekran satırları `permissions`ten
         * geldiği için görüntü DOĞRUYDU — ama yanıt, ekranda hiç
         * karşılığı olmayan kayıtlar taşıyordu.
         *
         * Bunu tarayıcı testi yakaladı: "sütun başına tik sayısı ==
         * o rolün grants sayısı" iddiası düştü, çünkü grants ekranda
         * olmayan satırları da sayıyordu.
         *
         * Sessiz bir tutarsızlıktı: kimse hata görmezdi, yalnız
         * veriye dayanan her sayım yanlış çıkardı.
         */
        var gorunurAnahtarlar = PermissionCatalog.Permissions
            .Where(item => !item.KullanimdanKalkti)
            .Select(item => item.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var grants = (await db.RolePermissions
                .AsNoTracking()
                .Select(item => new
                {
                    item.RoleId,
                    PermissionKey = item.Permission.Key
                })
                .ToListAsync(cancellationToken))
            .Where(item => gorunurAnahtarlar.Contains(item.PermissionKey))
            .ToList();

        return Ok(new
        {
            /*
             * KULLANIMDAN KALKMIŞ İZİNLER MATRİSTE GÖSTERİLMİYOR
             * (KARAR 3b). Kayıtları SİLİNMİYOR — yalnız ekrandan
             * çıkarılıyor.
             *
             * Ölçüldü: bu 7 anahtarın veritabanında 26 rol kaydı ve
             * 17 kişisel kaydı var (5'i açıkça izin veren). Silmek
             * sessizce yetki değiştirirdi; göstermek ise hiçbir şeyi
             * korumayan satırlar için YANLIŞ GÜVEN veriyordu.
             *
             * Kayıtlar duruyor: `security_audit_events` ve
             * `role_permissions` üzerinden hâlâ ölçülebilirler.
             */
            permissions = PermissionCatalog.Permissions
                .Where(item => !item.KullanimdanKalkti)
                .ToList(),
            roles,
            grants
        });
    }

    [HttpPost("toggle")]
    [RequirePermission(PermissionCatalog.Keys.UserManagementEdit)]
    public async Task<IActionResult> Toggle(
        TogglePermissionGrantRequest request,
        CancellationToken cancellationToken)
    {
        var role = await db.Roles.SingleOrDefaultAsync(
            item => item.Id == request.RoleId, cancellationToken);

        if (role is null)
            return NotFound(new { message = "Rol bulunamadı." });

        if (string.Equals(role.Name, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "Admin rolünün yetkileri sabittir, değiştirilemez."
            });
        }

        if (!PermissionCatalog.IsKnownPermission(request.PermissionKey))
            return BadRequest(new { message = "Bilinmeyen bir yetki anahtarı." });

        var permission = await db.Permissions.SingleOrDefaultAsync(
            item => item.Key == request.PermissionKey, cancellationToken);

        if (permission is null)
            return NotFound(new { message = "Yetki bulunamadı." });

        var existing = await db.RolePermissions.SingleOrDefaultAsync(
            item => item.RoleId == role.Id && item.PermissionId == permission.Id,
            cancellationToken);

        /*
         * ═══ KALDIRMA KAYDI — YAZAN TEK YER BURASI (SEED/1 SB2) ═══
         *
         * Tohumlayıcı her açılışta katalogdaki eksik çiftleri geri
         * ekliyor. Kullanıcının kaldırdığı bir izin, bu kayıt olmadan
         * bir sonraki yeniden başlatmada GERİ GELİYORDU — ve kimse
         * görmüyordu: ekran çalışıyor, yalnız kısıtlama kayboluyordu.
         *
         * Kaldırma kaydı bu ucun DIŞINDA yazılmaz. `RolIzinKaydiTekYer`
         * muhafızı bunu tutuyor; ikinci bir yazma yolu açılırsa kural
         * iki yerde yaşamaya başlar ve biri unutulur (Kural 79).
         *
         * SİMETRİ ŞART: açma işlemi kaydı SİLER. Silmezse kullanıcı
         * izni geri verse bile tohumlayıcı bir daha o çifte
         * dokunmazdı; bugünkü kusurun aynadaki hâli olurdu.
         */
        var kaldirmaKaydi = await db.RolePermissionRevocations
            .SingleOrDefaultAsync(
                item => item.RoleId == role.Id && item.PermissionId == permission.Id,
                cancellationToken);

        if (request.Granted)
        {
            if (existing is null)
            {
                db.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.Id,
                    PermissionId = permission.Id
                });
            }

            if (kaldirmaKaydi is not null)
                db.RolePermissionRevocations.Remove(kaldirmaKaydi);
        }
        else
        {
            if (existing is not null)
                db.RolePermissions.Remove(existing);

            // KAYIT HER HÂLDE YAZILIYOR — izin zaten yoksa bile.
            // "Kapalı kalsın" isteği, o an kapalı olmasından
            // bağımsız bir KARARDIR ve tohumlayıcı onu bilmeli.
            if (kaldirmaKaydi is null)
            {
                db.RolePermissionRevocations.Add(new RolePermissionRevocation
                {
                    RoleId = role.Id,
                    PermissionId = permission.Id,
                    RevokedByUserId = currentUser.UserId
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = request.Granted
                ? "Yetki role eklendi."
                : "Yetki rolden kaldırıldı.",
            request.RoleId,
            request.PermissionKey,
            request.Granted
        });
    }

    [HttpPatch("roles/{id:guid}/scope-policy")]
    [RequirePermission(PermissionCatalog.Keys.UserManagementEdit)]
    public async Task<IActionResult> UpdateScopePolicy(
        Guid id,
        UpdateRoleScopePolicyRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(typeof(RoleDataScopePolicy), request.DataScopePolicy))
            return BadRequest(new { message = "Geçersiz veri kapsamı seçimi." });

        var role = await db.Roles.SingleOrDefaultAsync(
            item => item.Id == id, cancellationToken);

        if (role is null)
            return NotFound(new { message = "Rol bulunamadı." });

        role.DataScopePolicy = (RoleDataScopePolicy)request.DataScopePolicy;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Rolün veri kapsamı güncellendi.",
            role.Id,
            DataScopePolicy = (int)role.DataScopePolicy
        });
    }

    [HttpPost("roles")]
    [RequirePermission(PermissionCatalog.Keys.UserManagementCreate)]
    public async Task<IActionResult> CreateRole(
        CreateRoleRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Rol adı zorunludur." });

        if (await db.Roles.AnyAsync(
                item => item.Name.ToLower() == name.ToLower(), cancellationToken))
        {
            return Conflict(new { message = "Bu isimde bir rol zaten var." });
        }

        var role = new AppRole
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description)
                ? null
                : request.Description.Trim()
        };

        db.Roles.Add(role);
        await db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.CopyFromRoleName))
        {
            var sourceRole = await db.Roles
                .Include(item => item.UserRoles)
                .SingleOrDefaultAsync(
                    item => item.Name == request.CopyFromRoleName.Trim(),
                    cancellationToken);

            if (sourceRole is not null)
            {
                var sourcePermissionIds = await db.RolePermissions
                    .Where(item => item.RoleId == sourceRole.Id)
                    .Select(item => item.PermissionId)
                    .ToListAsync(cancellationToken);

                foreach (var permissionId in sourcePermissionIds)
                {
                    db.RolePermissions.Add(new RolePermission
                    {
                        RoleId = role.Id,
                        PermissionId = permissionId
                    });
                }

                role.DataScopePolicy = sourceRole.DataScopePolicy;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return Ok(new
        {
            message = "Rol oluşturuldu.",
            role.Id,
            role.Name,
            role.Description,
            DataScopePolicy = (int)role.DataScopePolicy
        });
    }
}
