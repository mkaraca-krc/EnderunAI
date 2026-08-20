using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Data;

/// <summary>
/// SAYIM FARKI HESAPLARI — noksan ve fazla.
///
/// Sayım düzeltmesi stoğu değiştiriyor ama S6c öncesinde HİÇ fiş
/// kesmiyordu (ölçüldü): her fark, mutabakat raporunda kalıcı bir
/// sapma bırakıyordu ve "sıfır fark" hiç görülemezdi.
///
/// KULLANICI KARARI: noksan 689 Diğer Olağandışı Gider ve Zararlar'a,
/// fazla 649 Diğer Olağan Gelir ve Kârlar'a. Sayım farkı bir üretim
/// maliyeti değildir; 740'a karışsaydı kayıp ile maliyet ayrımı
/// kaybolur ve fire oranı bir daha ölçülemezdi.
///
/// NEDEN ALT HESAP: canlı planda 689 ana hesabına FİŞ KESİLEMİYOR
/// (`IsPostingAllowed = false`) — noksan tarafında alt hesap zorunlu.
/// 649 ana hesabına kesilebiliyor ama fazla tarafına da kardeş bir
/// hesap açılıyor: aynı olayın iki yakası biri adlı biri genel
/// hesapta dursaydı, raporu okuyan neden farklı olduklarını arardı.
/// Mali müşavirin kendi deseni de bu (649.01 SGK TEŞVİKİ,
/// 649.02 KURUŞ FARKLARI).
///
/// EKLEMELİ: var olan hesabı ASLA değiştirmez.
/// </summary>
public static class StockVarianceAccountSeed
{
    public const string ShortageCode = "689.02";
    public const string ShortageParentCode = "689";
    public const string ShortageName = "STOK SAYIM NOKSANLARI";

    public const string SurplusCode = "649.03";
    public const string SurplusParentCode = "649";
    public const string SurplusName = "STOK SAYIM FAZLALARI";

    public static async Task SeedAsync(AppDbContext db)
    {
        await EnsureAsync(
            db, ShortageParentCode, ShortageCode, ShortageName,
            "Sayımda eksik çıkan stokun gideri. Kalıcı bakiye fire, "
            + "kayıp ve hatalı girişlerin toplam maliyetini gösterir.");

        await EnsureAsync(
            db, SurplusParentCode, SurplusCode, SurplusName,
            "Sayımda fazla çıkan stokun geliri. Kalıcı bakiye, kaydı "
            + "yapılmamış girişlerin veya hatalı çıkışların izidir.");
    }

    private static async Task EnsureAsync(
        AppDbContext db, string parentCode, string code, string name, string description)
    {
        var parents = await db.AccountingAccounts
            .Where(x => x.Code == parentCode)
            .Select(x => new { x.Id, x.CompanyId, x.Nature, x.Level })
            .ToListAsync();

        if (parents.Count == 0) return;

        var existing = await db.AccountingAccounts
            .Where(x => x.Code == code)
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
                Code = code,
                Name = name,
                Description = description,
                Nature = parent.Nature,
                Level = parent.Level + 1,

                // Ana hesap fiş kesilemez olabiliyor; alt hesap
                // AÇIKÇA kesilebilir işaretleniyor.
                IsPostingAllowed = true
            });
        }

        await db.SaveChangesAsync();
    }
}
