using EnderunAI.Api.Data;
using EnderunAI.Api.Data.HumanResources;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.HumanResources;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Subcontractors;

/// <summary>Taşeron ekibinin bir döneme düşen bordro maliyeti.</summary>
/// <param name="Amount">Şirkete toplam maliyet (brüt + işveren payı
/// primler) — hakedişten kesilecek tutar.</param>
/// <param name="MemberCount">Bordrosu bulunan ekip üyesi sayısı.</param>
/// <param name="Basis">Hesabın dayanağı; kullanıcı rakamı görmeden
/// onaylamak zorunda kalmasın.</param>
public sealed record SubcontractorTeamPayrollCost(
    decimal Amount,
    int MemberCount,
    string Basis);

/// <summary>
/// SGK yükümlülüğü BİZDE olan taşeron sözleşmelerinde ekip yönetimi ve
/// o ekibin bordro maliyeti.
///
/// Neden gerekli: işçi taşeronun ama bordro bizdeyse, aynı işçiliği hem
/// kendi maliyetimizde hem taşerona ödediğimiz hakedişte iki kez saymış
/// oluruz. Bordro maliyeti taşeron hakedişinden kesilerek bu çift sayım
/// kapanıyor.
/// </summary>
public sealed class SubcontractorTeamService(AppDbContext db, HrDbContext hrDb)
{
    /// <summary>
    /// Sözleşmenin ekibini verilen listeyle DEĞİŞTİRİR: listede olmayan
    /// mevcut üyelerin bağı kopar, yeni gelenler bağlanır.
    ///
    /// Tam liste yaklaşımı bilinçli: fark hesabı, ekranda görünen ekiple
    /// kayıttaki ekibin sessizce ayrışmasına yol açardı.
    /// </summary>
    /// <returns>Hata varsa Türkçe mesaj; başarılıysa null.</returns>
    public async Task<string?> ReplaceTeamAsync(
        SubcontractorContract contract,
        IReadOnlyCollection<Guid> personnelIds,
        CancellationToken cancellationToken)
    {
        // SGK taşerondaysa ekip bağlanamaz: o işçiler bizim bordromuzda
        // değil, bağ kurmak hakedişte olmayan bir kesinti üretirdi.
        if (contract.SocialSecurityResponsibility !=
            SubcontractorResponsibility.Us)
        {
            return
                "Bu sözleşmede SGK yükümlülüğü taşeronda. Ekip bağlamak için " +
                "sözleşme kapsamında Sigorta-SGK'yı \"Bizde\" olarak işaretleyin.";
        }

        var distinctIds = personnelIds.Distinct().ToArray();

        if (distinctIds.Length > 0)
        {
            var validCount = await db.Personnel.CountAsync(
                x => distinctIds.Contains(x.Id) &&
                     x.CompanyId == contract.CompanyId,
                cancellationToken);

            if (validCount != distinctIds.Length)
                return "Seçilen personelin tamamı bu şirkete ait değil.";

            // Bir personel aynı anda iki taşeron ekibinde olamaz:
            // bordro maliyeti iki sözleşmeden birden kesilirdi.
            var claimedElsewhere = await db.Personnel
                .Where(x => distinctIds.Contains(x.Id) &&
                            x.SubcontractorContractId != null &&
                            x.SubcontractorContractId != contract.Id)
                .Select(x => x.FirstName + " " + x.LastName)
                .ToListAsync(cancellationToken);

            if (claimedElsewhere.Count > 0)
            {
                return
                    "Şu personel başka bir taşeron ekibinde: " +
                    string.Join(", ", claimedElsewhere) +
                    ". Önce eski ekipten çıkarın.";
            }
        }

        var current = await db.Personnel
            .Where(x => x.SubcontractorContractId == contract.Id)
            .ToListAsync(cancellationToken);

        foreach (var member in current.Where(x => !distinctIds.Contains(x.Id)))
        {
            member.SubcontractorContractId = null;
            member.UpdatedAtUtc = DateTime.UtcNow;
        }

        var currentIds = current.Select(x => x.Id).ToHashSet();
        var added = distinctIds.Where(x => !currentIds.Contains(x)).ToArray();

        if (added.Length > 0)
        {
            var newMembers = await db.Personnel
                .Where(x => added.Contains(x.Id))
                .ToListAsync(cancellationToken);

            foreach (var member in newMembers)
            {
                member.SubcontractorContractId = contract.Id;
                member.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    /// <summary>
    /// Ekibin verilen dönemdeki bordro maliyeti — taşeron hakedişinden
    /// kesilecek tutar.
    ///
    /// Yalnızca ONAYLI/ÖDENMİŞ bordrolar sayılır: taslak bordrodan
    /// kesinti üretmek, sonradan değişen bir rakamı hakedişe yazmak
    /// olurdu.
    ///
    /// İLKE: bordro yoksa öneri üretilmez (null) — sıfır TL'lik bir
    /// kesinti satırı, "hesaplandı ve sıfır çıktı" izlenimi verirdi.
    /// </summary>
    public async Task<SubcontractorTeamPayrollCost?> CalculatePayrollCostAsync(
        Guid contractId,
        SubcontractorResponsibility socialSecurityResponsibility,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        if (socialSecurityResponsibility != SubcontractorResponsibility.Us)
            return null;

        var memberIds = await db.Personnel
            .Where(x => x.SubcontractorContractId == contractId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (memberIds.Count == 0)
            return null;

        var payrolls = await hrDb.PayrollRecords
            .AsNoTracking()
            .Where(x => memberIds.Contains(x.PersonnelId) &&
                        x.Year == year &&
                        x.Month == month &&
                        (x.Status == PayrollStatus.Approved ||
                         x.Status == PayrollStatus.Paid))
            .Select(x => new { x.PersonnelId, x.TotalEmployerCost })
            .ToListAsync(cancellationToken);

        if (payrolls.Count == 0)
            return null;

        var total = decimal.Round(payrolls.Sum(x => x.TotalEmployerCost), 2);

        if (total <= 0m)
            return null;

        return new SubcontractorTeamPayrollCost(
            Amount: total,
            MemberCount: payrolls.Count,
            Basis:
                $"{month:00}/{year} onaylı bordro — {payrolls.Count} kişi, " +
                "brüt + işveren payı primler");
    }
}
