using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Projects;

/// <summary>
/// Projenin işveren cari kartı kuralı.
///
/// TEK YERDE: aynı kural hem proje açılış/düzenleme ekranında hem de
/// keşif "kazanıldı" akışında geçerli — kazanmak projeyi aktife alır
/// ve aktif projenin işvereni onaylı bir müşteri cari kartı olmak
/// zorundadır. Kural iki yere kopyalansaydı biri değişip diğeri
/// kalırdı ve keşiften gelen proje ekranın izin vermeyeceği bir
/// durumda aktife düşerdi.
/// </summary>
public static class ProjectEmployerRule
{
    /// <summary>
    /// Keşif statüsünde işveren opsiyoneldir (verilirse sadece
    /// şirkete ait olduğu kontrol edilir, Onaylı/Müşteri şartı
    /// aranmaz). Keşif dışındaki statülerde işveren zorunludur ve
    /// Onaylı + Müşteri rolünde olmalıdır.
    /// </summary>
    public static async Task<(CurrentAccount? Employer, string? Error)> ValidateAsync(
        AppDbContext db,
        ProjectStatus status,
        Guid? employerCurrentAccountId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        if (employerCurrentAccountId is null)
        {
            return status == ProjectStatus.Kesif
                ? (null, null)
                : (null, "Keşif dışındaki statülerde işveren cari kartı zorunludur.");
        }

        var employer = await db.CurrentAccounts
            .SingleOrDefaultAsync(
                x => x.Id == employerCurrentAccountId.Value && x.CompanyId == companyId,
                cancellationToken);

        if (employer is null)
            return (null, "İşveren cari kartı bulunamadı.");

        if (status != ProjectStatus.Kesif)
        {
            if (employer.Status != CurrentAccountStatus.Approved)
                return (null, "Proje yalnızca onaylanmış cari kart ile açılabilir.");

            if (!employer.Roles.HasFlag(CurrentAccountRoles.Customer))
                return (null, "Seçilen cari kartın müşteri rolü bulunmuyor.");
        }

        return (employer, null);
    }
}
