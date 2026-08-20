using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Data;

/// <summary>
/// GR/IR HESABI — "faturası gelmemiş mal alımları".
///
/// Mal kabulde stok fiziken girer ama fatura henüz yoktur. Borç bu ara
/// hesapta bekler; fatura gelince buradan 320 Satıcılar'a devreder.
/// Ara hesap olmasaydı ya stok muhasebeye hiç girmez (bugünkü durum:
/// 153 ve 150 sıfır) ya da faturasız borç doğrudan 320'ye yazılıp
/// tedarikçi bakiyesini gerçekte olmayan bir tutarla şişirirdi.
///
/// 379 ana hesabı canlıda FİŞ KESİLEMEZ (`IsPostingAllowed = false`),
/// bu yüzden alt hesap açmak zorunlu — kullanıcı da bu seçeneği
/// onayladı. 159 Verilen Sipariş Avansları kullanılamazdı: orası
/// gerçek tedarikçi avanslarıyla dolu ve aynı hesapta iki farklı
/// anlam karışırdı.
///
/// EKLEMELİ: var olan hesabı ASLA değiştirmez. Mali müşavir adı ya da
/// bağlı olduğu üst hesabı elle düzenlemişse tohum onu ezmemeli.
/// </summary>
public static class GoodsReceivedNotInvoicedAccountSeed
{
    public const string Code = "379.01";
    public const string ParentCode = "379";
    public const string Name = "FATURASI GELMEMİŞ MAL ALIMLARI";

    public static async Task SeedAsync(AppDbContext db)
    {
        var parents = await db.AccountingAccounts
            .Where(x => x.Code == ParentCode)
            .Select(x => new { x.Id, x.CompanyId, x.Nature, x.Level })
            .ToListAsync();

        if (parents.Count == 0) return;

        var existing = await db.AccountingAccounts
            .Where(x => x.Code == Code)
            .Select(x => x.CompanyId)
            .ToListAsync();

        var missing = parents.Where(x => !existing.Contains(x.CompanyId)).ToList();

        if (missing.Count == 0) return;

        foreach (var parent in missing)
        {
            db.AccountingAccounts.Add(new AccountingAccount
            {
                CompanyId = parent.CompanyId,
                ParentAccountId = parent.Id,
                Code = Code,
                Name = Name,
                Description =
                    "Mal kabulü yapılmış ancak faturası gelmemiş alımlar. "
                    + "Fatura kaydedilince bakiye 320 Satıcılar'a devreder; "
                    + "kalıcı bakiye faturası eksik mal kabullerini gösterir.",
                Nature = parent.Nature,
                Level = parent.Level + 1,

                // Ana hesap fiş kesilemez olduğu için alt hesap
                // AÇIKÇA kesilebilir işaretleniyor.
                IsPostingAllowed = true
            });
        }

        await db.SaveChangesAsync();
    }
}
