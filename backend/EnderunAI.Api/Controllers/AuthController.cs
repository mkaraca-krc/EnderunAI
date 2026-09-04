using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EnderunAI.Api.Contracts;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    AppDbContext db,
    PasswordService passwordService,
    TokenService tokenService,
    ILoginAttemptService loginAttemptService,
    IUserAuthorizationService userAuthorizationService,
    IWorkHourAccessService workHourAccessService,
    IParolaYazici parolaYazici) : ControllerBase
{
    /// <summary>
    /// KENDİ PAROLASINI DEĞİŞTİRME — SİSTEMDE İLK KEZ.
    ///
    /// ── ÖNCEDEN YOKTU ──
    ///
    /// Ölçüldü (2026-09-03): parola değiştirmenin TEK yolu yönetici
    /// sıfırlamasıydı (`user-management.edit`). Yani bir kullanıcı
    /// kendi parolasını değiştiremiyordu — parolasının başkası
    /// tarafından bilindiğini fark etse bile yöneticiye gitmek
    /// zorundaydı.
    ///
    /// ── DOĞRULAMA SIRASI DAVRANIŞIN PARÇASI ──
    ///
    /// ESKİ PAROLA ÖNCE kontrol ediliyor. Uzunluk hatası önce
    /// dönseydi, eski parolayı BİLMEYEN biri de politikayı öğrenirdi:
    /// uç, kendisine yetkisi olmayan birine bilgi veren bir yüzeye
    /// dönüşürdü.
    ///
    /// ── HATA MESAJLARI AYIRT ETTİRMEZ ──
    ///
    /// "Kullanıcı yok" ile "parola yanlış" AYNI mesajı döndürüyor.
    /// Ayırt edilebilir olsalardı, uç bir kullanıcı adı doğrulama
    /// aracına dönüşürdü. (Burada kimlik zaten doğrulanmış olduğu için
    /// "kullanıcı yok" beklenmiyor — ama beklenmedik durumun da ayırt
    /// edilebilir olmaması gerekiyor.)
    ///
    /// ── DİĞER OTURUMLAR DÜŞER ──
    ///
    /// Karar: "dar olan kazanır". Değişimden önce üretilmiş her jeton
    /// geçersiz. Gerekçe `OturumGecerliligi` içinde.
    ///
    /// SIRA: damga ÖNCE yazılır, yeni jeton ONDAN SONRA üretilir.
    /// Tersi olsaydı, kullanıcının kendi yeni jetonu da değişimden
    /// önce üretilmiş sayılıp reddedilebilirdi.
    ///
    /// YENİ JETON CEVAPTA DÖNÜYOR: kendi oturumu da düştüğü için,
    /// dönmeseydi kullanıcı parolasını değiştirir değiştirmez dışarı
    /// atılır ve bunu bir hata sanardı.
    /// </summary>
    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        const string ortakHata =
            "Mevcut parola doğrulanamadı.";

        var kimlik =
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(kimlik, out var kullaniciId))
            return Unauthorized(new { message = ortakHata });

        var user = await db.Users
            .SingleOrDefaultAsync(x => x.Id == kullaniciId, cancellationToken);

        // AYIRT ETTİRMEYEN CEVAP: kullanıcı yok da, parola yanlış da
        // aynı mesajı alıyor.
        if (user is null ||
            !passwordService.Verify(
                request.CurrentPassword ?? string.Empty,
                user.PasswordHash,
                user.PasswordSalt))
        {
            return BadRequest(new { message = ortakHata });
        }

        // ESKİ PAROLA DOĞRULANDIKTAN SONRA politika söyleniyor.
        if (ParolaPolitikasi.Dogrula(request.NewPassword) is string politikaHatasi)
            return BadRequest(new { message = politikaHatasi });

        if (!string.Equals(
                request.NewPassword, request.NewPasswordConfirm, StringComparison.Ordinal))
        {
            return BadRequest(new { message = "Yeni parolalar birbiriyle eşleşmiyor." });
        }

        if (passwordService.Verify(
                request.NewPassword!, user.PasswordHash, user.PasswordSalt))
        {
            return BadRequest(new
            {
                message = "Yeni parola mevcut parolayla aynı olamaz."
            });
        }

        var simdi = DateTime.UtcNow;

        /*
         * TEK NOKTA: karma, damga ve oturum önbelleği BİRLİKTE.
         * Üçünü ayrı ayrı yazmak, birini unutan yeni bir yolun
         * korumayı sessizce kapatması demekti — yönetici sıfırlama
         * yolu tam olarak böyleydi (ölçüldü).
         */
        var jetonSaniyesi = parolaYazici.Uygula(user, request.NewPassword!, simdi);

        /*
         * DENETİM KAYDI AYIRT EDİLEBİLİR.
         *
         * `AppUser` zaten denetleniyor ama olay "Updated" diye
         * yazılıyor — ad değişikliğinden ayırt edilemez. Parola
         * değişikliği ayrı bir eylem adıyla kaydediliyor.
         *
         * PAROLA VE KARMASI KAYDA YAZILMIYOR. Denetim kaydı, koruduğu
         * sırrı ele veren bir yer olamaz — bu ders portal jetonunda
         * ödendi.
         */
        db.Set<SecurityAuditEvent>().Add(new SecurityAuditEvent
        {
            ActorUserId = user.Id,
            ActorUsername = user.Username,
            Action = "PasswordChanged",
            EntityType = nameof(AppUser),
            EntityId = user.Id,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                summary = "Kullanıcı kendi parolasını değiştirdi",
                otherSessionsRevoked = true
            }),
            IpAddress = ResolveClientIp(),
            UserAgent = Request.Headers.UserAgent.ToString(),
            OccurredAtUtc = simdi
        });

        await db.SaveChangesAsync(cancellationToken);

        /*
         * YENİ JETON, OTURUM SINIRIYLA ÜRETİLİYOR — sınırı
         * `ParolaYazici` döndürüyor. Sınır bir SONRAKİ saniye:
         * değişim saniyesindeki tüm eski jetonlar reddedilsin diye.
         * Kendi yeni jetonumuz o saniyede üretilmezse aynı kapıya
         * takılırdı.
         */

        var roller = await db.UserRoles
            .Where(x => x.UserId == user.Id)
            .Select(x => x.Role.Name)
            .ToArrayAsync(cancellationToken);

        var yetki = await userAuthorizationService.GetAsync(user.Id, cancellationToken);
        var izinler = (yetki?.Permissions ?? []).OrderBy(x => x).ToArray();

        return Ok(new
        {
            message =
                "Parola değiştirildi. Diğer oturumlarınız sonlandırıldı.",
            token = tokenService.Create(user, roller, izinler, jetonSaniyesi),
            expiresInSeconds = 43200
        });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var ipAddress = ResolveClientIp();

        if (loginAttemptService.IsLocked(ipAddress, out var remaining))
        {
            var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
            return StatusCode(429, new
            {
                message = $"Çok fazla başarısız giriş denemesi. Lütfen {minutes} dakika sonra tekrar deneyin."
            });
        }

        var username = request.Username.Trim().ToLowerInvariant();
        var user = await db.Users
            .Include(item => item.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(
                item => item.Username.ToLower() == username,
                cancellationToken);

        if (user is null ||
            !user.IsActive ||
            !passwordService.Verify(
                request.Password,
                user.PasswordHash,
                user.PasswordSalt))
        {
            loginAttemptService.RecordFailure(ipAddress);
            return Unauthorized(new { message = "Kullanıcı adı veya şifre hatalı." });
        }

        var workHourEvaluation = await workHourAccessService.EvaluateAsync(user.Id, cancellationToken);
        if (!workHourEvaluation.IsAllowed)
        {
            db.SecurityAuditEvents.Add(new SecurityAuditEvent
            {
                ActorUserId = user.Id,
                ActorUsername = user.Username,
                Action = "LoginRejectedOutsideWorkHours",
                EntityType = "WorkHourAccess",
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    summary = $"{user.Username} mesai saatleri dışında giriş denedi."
                }),
                IpAddress = ipAddress,
                UserAgent = Request.Headers.UserAgent.ToString(),
                OccurredAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);

            return StatusCode(403, new
            {
                message = workHourEvaluation.Reason,
                outsideWorkHours = true
            });
        }

        loginAttemptService.RecordSuccess(ipAddress);

        user.LastLoginAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var roleNames = user.UserRoles
            .Select(userRole => userRole.Role.Name)
            .ToArray();
        var authorization = await userAuthorizationService.GetAsync(
            user.Id,
            cancellationToken);
        var permissions = (authorization?.Permissions ?? [])
            .OrderBy(permission => permission)
            .ToArray();

        return Ok(new
        {
            token = tokenService.Create(user, roleNames, permissions),
            expiresInSeconds = 43200,
            user = new
            {
                user.Id,
                user.Username,
                user.FullName,
                user.Email,
                roles = roleNames,
                permissions,

                // Giriş yanıtı da bayrağı taşıyor: arayüz oturumu
                // /auth/me'yi beklemeden şekillendirebilsin ve iki uç
                // aynı sinyali versin.
                hasAllPermissions = PermissionCatalog.HasEveryPermission(permissions)
            }
        });
    }

    [AllowAnonymous]
    [HttpPost("access-requests")]
    public async Task<IActionResult> SubmitAccessRequest(
        SubmitAccessRequestRequest request,
        CancellationToken cancellationToken)
    {
        var ipAddress = ResolveClientIp();

        if (loginAttemptService.IsLocked(ipAddress, out var remaining))
        {
            var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
            return StatusCode(429, new
            {
                message = $"Çok fazla başarısız giriş denemesi. Lütfen {minutes} dakika sonra tekrar deneyin."
            });
        }

        var username = request.Username.Trim().ToLowerInvariant();
        var user = await db.Users
            .SingleOrDefaultAsync(item => item.Username.ToLower() == username, cancellationToken);

        if (user is null ||
            !user.IsActive ||
            !passwordService.Verify(request.Password, user.PasswordHash, user.PasswordSalt))
        {
            loginAttemptService.RecordFailure(ipAddress);
            return Unauthorized(new { message = "Kullanıcı adı veya şifre hatalı." });
        }

        loginAttemptService.RecordSuccess(ipAddress);

        var reason = request.Reason.Trim();
        if (string.IsNullOrWhiteSpace(reason))
            return BadRequest(new { message = "Gerekçe zorunludur." });

        var existingPending = await db.AccessRequests
            .Where(item => item.UserId == user.Id && item.Status == AccessRequestStatus.Pending)
            .SingleOrDefaultAsync(cancellationToken);

        if (existingPending is not null)
        {
            return Ok(new
            {
                message = "Zaten bekleyen bir erişim talebiniz var, onay bekleniyor.",
                existingPending.Id
            });
        }

        var accessRequest = new AccessRequest
        {
            UserId = user.Id,
            Reason = reason,
            Status = AccessRequestStatus.Pending
        };
        db.AccessRequests.Add(accessRequest);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Erişim talebiniz gönderildi, onay bekleniyor.",
            accessRequest.Id
        });
    }

    [Authorize]
    [HttpGet("work-hours-status")]
    public async Task<IActionResult> WorkHoursStatus(CancellationToken cancellationToken)
    {
        var idValue =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            User.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
            User.FindFirstValue("sub");
        if (!Guid.TryParse(idValue, out var userId))
            return Unauthorized(new { message = "Oturum kullanıcısı doğrulanamadı." });

        var evaluation = await workHourAccessService.EvaluateAsync(userId, cancellationToken);
        var minutesRemaining = evaluation.WindowEndsAtUtc is null
            ? (int?)null
            : Math.Max(0, (int)Math.Ceiling((evaluation.WindowEndsAtUtc.Value - DateTime.UtcNow).TotalMinutes));

        return Ok(new
        {
            isAllowed = evaluation.IsAllowed,
            isExempt = evaluation.IsExempt,
            windowEndsAtUtc = evaluation.WindowEndsAtUtc,
            minutesRemaining
        });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var idValue =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            User.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
            User.FindFirstValue("sub");
        if (!Guid.TryParse(idValue, out var userId))
            return Unauthorized(new { message = "Oturum kullanıcısı doğrulanamadı." });

        var user = await db.Users
            .AsNoTracking()
            .Include(item => item.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);

        if (user is null || !user.IsActive)
            return Unauthorized(new { message = "Kullanıcı hesabı pasif veya bulunamadı." });

        var roles = user.UserRoles
            .Select(userRole => userRole.Role.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var authorization = await userAuthorizationService.GetAsync(
            user.Id,
            cancellationToken);
        var permissions = (authorization?.Permissions ?? [])
            .OrderBy(permission => permission)
            .ToArray();

        return Ok(new
        {
            id = user.Id,
            user.Username,
            user.FullName,
            user.Honorific,
            roles,
            permissions,

            // ARAYÜZÜN SÜPER KULLANICI SİNYALİ. Arayüz bugüne kadar rol
            // ADINA bakıyordu ("Admin" / "Genel Müdür"); rol yeniden
            // adlandırılsa ya da başka bir role tüm izinler verilse
            // yanlış cevap verirdi. Kural token üretimiyle aynı yerden
            // (PermissionCatalog.HasEveryPermission) geliyor.
            hasAllPermissions = PermissionCatalog.HasEveryPermission(permissions)
        });
    }

    private string ResolveClientIp()
    {
        var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            var first = forwardedFor.Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(first))
                return first;
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
