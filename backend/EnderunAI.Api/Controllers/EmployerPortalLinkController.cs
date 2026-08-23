using System.Net.Mail;
using System.Security.Cryptography;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Security.CurrentUser;
using EnderunAI.Api.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/employer-portal-link")]
public sealed class EmployerPortalLinkController(
    AppDbContext db,
    ICurrentUserService currentUser,
    IEmailService emailService) : ControllerBase
{
    /// <summary>
    /// VARSAYILAN GEÇERLİLİK: 6 AY.
    ///
    /// Bağlantı e-postayla paylaşılıyor ve kimlik doğrulaması yok.
    /// Süresiz bırakılırsa e-posta kutusu yıllar sonra başkasının
    /// eline geçse bile kapı açık kalır. Altı ay, bir inşaat
    /// projesinin işveren raporlaması için makul bir dönem; yetmezse
    /// UZATMA var ve uzatma denetim kaydına yazılıyor.
    /// </summary>
    private const int VarsayilanGecerlilikAyi = 6;

    /// <summary>Ekranın "sarı" göstereceği eşik.</summary>
    private const int YaklasiyorGunu = 30;

    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.EmployerPortalView)]
    public async Task<IActionResult> Get(Guid projectId, CancellationToken cancellationToken)
    {
        var link = await db.EmployerPortalLinks.AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.Token,
                x.IsActive,
                x.CreatedAtUtc,
                x.RevokedAtUtc,
                x.EmployerName,
                x.EmployerEmail,
                x.ExpiresAtUtc,
                x.LastAccessedAtUtc,
                x.AccessCount,
                x.LastExtendedAtUtc,
                x.ExtensionCount
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (link is null)
        {
            return Ok(new
            {
                link = (object?)null,
                emailConfigured = emailService.IsConfigured
            });
        }

        /*
         * DURUMU SUNUCU SÖYLÜYOR, EKRAN HESAPLAMIYOR.
         *
         * "Süresi geçti mi" kararı tarayıcının saatine bırakılsaydı,
         * saati geri alınmış bir makinede bağlantı geçerli görünürdü.
         * Görünen durum ile ucun uyguladığı kural aynı yerden gelmeli.
         */
        var simdi = DateTime.UtcNow;

        var durum =
            link.RevokedAtUtc != null || !link.IsActive ? "iptal" :
            link.ExpiresAtUtc <= simdi ? "suresi_gecti" :
            link.ExpiresAtUtc <= simdi.AddDays(YaklasiyorGunu) ? "yaklasiyor" :
            "aktif";

        return Ok(new
        {
            link = new
            {
                link.Id,
                link.Token,
                link.IsActive,
                link.CreatedAtUtc,
                link.RevokedAtUtc,
                link.EmployerName,
                link.EmployerEmail,
                link.ExpiresAtUtc,
                link.LastAccessedAtUtc,
                link.AccessCount,
                link.LastExtendedAtUtc,
                link.ExtensionCount,
                durum,
                kalanGun = (int)Math.Floor((link.ExpiresAtUtc - simdi).TotalDays)
            },
            emailConfigured = emailService.IsConfigured
        });
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.EmployerPortalCreate)]
    public async Task<IActionResult> Create(Guid projectId, CancellationToken cancellationToken)
    {
        var projectExists = await db.Projects.AsNoTracking()
            .AnyAsync(x => x.Id == projectId, cancellationToken);
        if (!projectExists)
            return NotFound(new { message = "Proje bulunamadı." });

        var existingActive = await db.EmployerPortalLinks
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.IsActive, cancellationToken);

        if (existingActive is not null)
        {
            existingActive.IsActive = false;
            existingActive.RevokedAtUtc = DateTime.UtcNow;
            existingActive.RevokedByUserId = currentUser.UserId;
        }

        var link = new EmployerPortalLink
        {
            ProjectId = projectId,
            Token = GenerateToken(),
            CreatedByUserId = currentUser.UserId,
            ExpiresAtUtc = DateTime.UtcNow.AddMonths(VarsayilanGecerlilikAyi)
        };

        db.EmployerPortalLinks.Add(link);
        DenetimYaz("PortalLinkCreated", link,
            $"İşveren portalı bağlantısı oluşturuldu. " +
            $"Geçerlilik: {link.ExpiresAtUtc:yyyy-MM-dd}.");

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "İşveren portalı linki oluşturuldu.",
            link.Id,
            link.Token
        });
    }

    [HttpPost("revoke")]
    [RequirePermission(PermissionCatalog.Keys.EmployerPortalDelete)]
    public async Task<IActionResult> Revoke(
        Guid projectId,
        [FromBody] PortalLinkActionRequest? request,
        CancellationToken cancellationToken)
    {
        var link = await db.EmployerPortalLinks
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.IsActive, cancellationToken);

        if (link is null)
            return NotFound(new { message = "Aktif bir işveren portalı linki bulunamadı." });

        link.IsActive = false;
        link.RevokedAtUtc = DateTime.UtcNow;
        link.RevokedByUserId = currentUser.UserId;
        link.UpdatedAtUtc = DateTime.UtcNow;

        DenetimYaz("PortalLinkRevoked", link,
            "İşveren portalı bağlantısı iptal edildi.",
            request?.Reason);

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "İşveren portalı linki iptal edildi." });
    }

    /// <summary>
    /// UZATMA — YENİ TOKEN ÜRETMEZ.
    ///
    /// Uzatma yeni token üretseydi işverene gönderilmiş bağlantı
    /// ölür ve e-postanın yeniden gönderilmesi gerekirdi; "uzatma"
    /// adı altında sessizce bir iptal olurdu. Burada yalnız son
    /// geçerlilik ileri alınıyor.
    /// </summary>
    [HttpPost("extend")]
    [RequirePermission(PermissionCatalog.Keys.EmployerPortalCreate)]
    public async Task<IActionResult> Extend(
        Guid projectId,
        [FromBody] PortalLinkExtendRequest? request,
        CancellationToken cancellationToken)
    {
        var link = await db.EmployerPortalLinks
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.IsActive, cancellationToken);

        if (link is null)
            return NotFound(new { message = "Aktif bir işveren portalı linki bulunamadı." });

        if (link.RevokedAtUtc != null)
            return BadRequest(new { message = "İptal edilmiş bağlantı uzatılamaz." });

        var ay = request?.Months ?? VarsayilanGecerlilikAyi;

        if (ay is < 1 or > 24)
            return BadRequest(new { message = "Uzatma süresi 1 ile 24 ay arasında olmalıdır." });

        /*
         * BUGÜNDEN İLERİ, ESKİ TARİHTEN DEĞİL.
         *
         * Süresi geçmiş bir bağlantıda eski tarihe eklemek, uzatma
         * yapıldığı halde bağlantının hâlâ ölü kalmasına yol açardı —
         * kullanıcı "uzattım" der, portal 404 dönmeye devam ederdi.
         */
        var taban = link.ExpiresAtUtc > DateTime.UtcNow
            ? link.ExpiresAtUtc
            : DateTime.UtcNow;

        var eskiTarih = link.ExpiresAtUtc;
        link.ExpiresAtUtc = taban.AddMonths(ay);
        link.LastExtendedAtUtc = DateTime.UtcNow;
        link.LastExtendedByUserId = currentUser.UserId;
        link.ExtensionCount += 1;
        link.UpdatedAtUtc = DateTime.UtcNow;

        DenetimYaz("PortalLinkExtended", link,
            $"İşveren portalı bağlantısı uzatıldı: " +
            $"{eskiTarih:yyyy-MM-dd} -> {link.ExpiresAtUtc:yyyy-MM-dd} ({ay} ay).",
            request?.Reason);

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "İşveren portalı bağlantısı uzatıldı.",
            link.ExpiresAtUtc
        });
    }

    /// <summary>
    /// Denetim kaydı: KİM, NE ZAMAN, NEDEN.
    ///
    /// TOKEN YAZILMIYOR — yalnız bağlantı kimliği. Denetim kaydı,
    /// koruduğu sırrı ele veren bir yer olamaz.
    /// </summary>
    private void DenetimYaz(
        string action,
        EmployerPortalLink link,
        string ozet,
        string? gerekce = null)
    {
        db.SecurityAuditEvents.Add(new SecurityAuditEvent
        {
            ActorUserId = currentUser.UserId,
            ActorUsername = currentUser.Username,
            Action = action,
            EntityType = "EmployerPortalLink",
            EntityId = link.Id,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                summary = ozet,
                projeId = link.ProjectId,
                gerekce,
                sonGecerlilik = link.ExpiresAtUtc
            }),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString(),
            OccurredAtUtc = DateTime.UtcNow
        });
    }

    [HttpPost("send-email")]
    [RequirePermission(PermissionCatalog.Keys.EmployerPortalEdit)]
    public async Task<IActionResult> SendEmail(
        Guid projectId,
        SendPortalEmailRequest request,
        CancellationToken cancellationToken)
    {
        if (!emailService.IsConfigured)
        {
            return BadRequest(new { message = "E-posta yapılandırılmamış." });
        }

        if (string.IsNullOrWhiteSpace(request.EmployerEmail) ||
            !MailAddress.TryCreate(request.EmployerEmail, out _))
        {
            return BadRequest(new { message = "Geçerli bir e-posta adresi girin." });
        }

        var project = await db.Projects.AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => new { x.Name })
            .SingleOrDefaultAsync(cancellationToken);

        if (project is null)
            return NotFound(new { message = "Proje bulunamadı." });

        var company = await db.Companies.AsNoTracking()
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new { x.Name, HasLogo = x.LogoPath != null })
            .FirstOrDefaultAsync(cancellationToken);

        var companyLogoUrl = company?.HasLogo == true
            ? "https://enderunai.com.tr/api/backend/company-settings/logo"
            : null;

        var link = await db.EmployerPortalLinks
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.IsActive, cancellationToken);

        if (link is null)
            return NotFound(new { message = "Aktif bir işveren portalı linki bulunamadı." });

        if (string.IsNullOrWhiteSpace(request.PortalUrl) ||
            !request.PortalUrl.TrimEnd('/').EndsWith($"/portal/{link.Token}", StringComparison.Ordinal))
        {
            return BadRequest(new { message = "Portal linki geçersiz." });
        }

        link.EmployerName = request.EmployerName?.Trim();
        link.EmployerEmail = request.EmployerEmail.Trim();
        link.UpdatedAtUtc = DateTime.UtcNow;

        var log = new EmployerPortalEmailLog
        {
            EmployerPortalLinkId = link.Id,
            ProjectId = projectId,
            RecipientEmail = link.EmployerEmail,
            RecipientName = link.EmployerName,
            SentAtUtc = DateTime.UtcNow,
            CreatedByUserId = currentUser.UserId
        };

        try
        {
            var subject = $"{project.Name} - Saha Takip Portalı";
            var html = EmployerPortalEmailTemplate.Build(
                project.Name,
                request.PortalUrl,
                link.EmployerName,
                company?.Name,
                companyLogoUrl);

            await emailService.SendAsync(
                link.EmployerEmail,
                link.EmployerName,
                subject,
                html,
                cancellationToken);

            log.IsSuccess = true;
        }
        catch (Exception exception)
        {
            log.IsSuccess = false;
            log.ErrorMessage = "E-posta gönderilemedi: e-posta servisine ulaşılamadı veya istek reddedildi.";

            db.EmployerPortalEmailLogs.Add(log);
            await db.SaveChangesAsync(cancellationToken);

            Console.Error.WriteLine($"[EmployerPortalEmail] Gönderim hatası: {exception}");

            return StatusCode(502, new
            {
                message = "E-posta gönderilemedi: e-posta servisine ulaşılamadı veya istek reddedildi."
            });
        }

        db.EmployerPortalEmailLogs.Add(log);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "E-posta gönderildi." });
    }

    [HttpGet("email-log")]
    [RequirePermission(PermissionCatalog.Keys.EmployerPortalView)]
    public async Task<IActionResult> GetEmailLog(Guid projectId, CancellationToken cancellationToken)
    {
        var items = await db.EmployerPortalEmailLogs.AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.SentAtUtc)
            .Take(20)
            .Select(x => new
            {
                x.Id,
                x.RecipientEmail,
                x.RecipientName,
                x.SentAtUtc,
                x.IsSuccess,
                x.ErrorMessage
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}

public sealed record SendPortalEmailRequest(
    string? EmployerName,
    string EmployerEmail,
    string PortalUrl);

public sealed record PortalLinkActionRequest(string? Reason);

public sealed record PortalLinkExtendRequest(int? Months, string? Reason);
