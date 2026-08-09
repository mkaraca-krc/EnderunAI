using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.HumanResources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// İşe alım öncesi tekrar işe alım kontrolü.
///
/// Form dolmadan, TC girilip doğrulanır doğrulanmaz çalışması için
/// ayrı ve hafif bir uç: kayıt oluşturmaz, yalnızca "bu kişi daha
/// önce bizde çalıştı mı, nasıl ayrıldı" sorusunu cevaplar.
///
/// KÖRLEMESİNE ENGEL DEĞİL: kırmızı eşleşmede kim, ne zaman ayrıldı,
/// hangi kod ve GEREKÇE birlikte döner. İşe alan kişi neyi geçtiğini
/// bilmeden karar veremez.
///
/// Silinmiş personel kaydı da taranır: yumuşak silme, kişinin bizde
/// çalışmış olduğu gerçeğini değiştirmez ve silinmiş kaydın arkasına
/// saklanarak yeniden giriş yapılmamalı.
///
/// KAPSAM: yalnızca ESKİ PERSONEL. Hiç çalışmamış kişiler için genel
/// kara liste bu sürümde yok.
/// </summary>
[ApiController]
[Authorize]
[Route("api/hr/ise-alim")]
public sealed class RehireCheckController(AppDbContext db) : ControllerBase
{
    /// <summary>Kararlar — arayüz bunlara göre davranır.</summary>
    private const string Blocked = "blocked";
    private const string Warning = "warning";
    private const string Clear = "clear";
    private const string NoMatch = "no-match";

    [HttpGet("tc-kontrol")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelEdit)]
    public async Task<IActionResult> Check(
        [FromQuery] string? identityNumber,
        CancellationToken cancellationToken)
    {
        var identity = identityNumber?.Trim();

        if (string.IsNullOrWhiteSpace(identity))
            return BadRequest(new { message = "Kimlik numarası zorunludur." });

        // Geçersiz numarayla arama yapmak yanlış kişiyi eşleştirebilir;
        // kontrol ancak doğrulanmış numarayla anlamlı.
        if (TurkishIdentityNumber.Describe(identity) is string problem)
            return BadRequest(new { message = problem });

        var match = await db.Personnel
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(x => x.IdentityNumber == identity)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                FullName = x.FirstName + " " + x.LastName,
                x.EmployeeNumber,
                x.Status,
                x.IsDeleted,
                x.EmploymentStartDate,
                x.EmploymentEndDate
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (match is null)
        {
            return Ok(new
            {
                identityNumber = identity,
                decision = NoMatch,
                matched = false,
                message = "Bu kimlik numarasıyla geçmiş kaydımız yok."
            });
        }

        // En son çıkış kaydı esastır: birden çok giriş-çıkışı olan
        // kişide eski değerlendirme değil, en güncel olan geçerlidir.
        var termination = await db.PersonnelTerminations
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(x => x.PersonnelId == match.Id && !x.IsDeleted)
            .OrderByDescending(x => x.TerminationDate)
            .Select(x => new
            {
                x.Id,
                x.TerminationDate,
                Reason = (int)x.Reason,
                RehireCode = (int?)x.RehireCode,
                x.RehireNote,
                x.RehireMarkedAtUtc,
                x.RehireMarkedByUserId
            })
            .FirstOrDefaultAsync(cancellationToken);

        var code = (RehireCode?)termination?.RehireCode;

        var decision = code switch
        {
            Models.RehireCode.Red => Blocked,
            Models.RehireCode.Yellow => Warning,
            _ => Clear
        };

        string? markedByName = null;

        if (termination?.RehireMarkedByUserId is Guid markedBy)
        {
            markedByName = await db.Users
                .AsNoTracking()
                .Where(x => x.Id == markedBy)
                .Select(x => x.FullName)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return Ok(new
        {
            identityNumber = identity,
            decision,
            matched = true,

            personnelId = match.Id,
            personnelFullName = match.FullName,
            match.EmployeeNumber,
            personnelStatus = (int)match.Status,
            // Kayıt silinmiş olsa da geçmiş görünür kalır.
            recordDeleted = match.IsDeleted,
            match.EmploymentStartDate,
            match.EmploymentEndDate,

            hasTermination = termination is not null,
            terminationId = termination?.Id,
            terminationDate = termination?.TerminationDate,
            terminationReason = termination?.Reason,

            rehireCode = termination?.RehireCode,
            rehireCodeName = PersonnelTerminationsController.RehireCodeName(code),
            // Gerekçe kırmızı ve sarıda zorunlu olduğu için burada da
            // dolu gelir; işe alan kişi engeli gerekçesiyle görür.
            rehireNote = termination?.RehireNote,
            rehireMarkedAtUtc = termination?.RehireMarkedAtUtc,
            rehireMarkedByName = markedByName,

            message = BuildMessage(decision, match.FullName, termination is not null)
        });
    }

    private static string BuildMessage(
        string decision, string fullName, bool hasTermination) => decision switch
    {
        Blocked =>
            $"{fullName} daha önce bizde çalıştı ve KIRMIZI olarak " +
            "işaretlendi: işe alınamaz. Gerekçeyi okuyup üst yetkiyle " +
            "geçebilirsiniz.",

        Warning =>
            $"{fullName} daha önce bizde çalıştı ve SARI olarak " +
            "işaretlendi: dikkatle değerlendirin. Gerekçeyi okuyun.",

        _ when !hasTermination =>
            $"{fullName} kayıtlarımızda var ama çıkış kaydı yok. " +
            "Hâlâ çalışıyor olabilir.",

        _ =>
            $"{fullName} daha önce bizde çalıştı; ayrılış " +
            "değerlendirmesinde engel yok."
    };
}
