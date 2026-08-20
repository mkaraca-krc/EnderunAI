using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Inventory;

/// <summary>
/// KATEGORİ → MUHASEBE HESABI. Stokun hangi hesapta durduğu ve
/// çıkışta nereye yazıldığı YALNIZ buradan çözülür.
///
/// Tek kaynak olması şart: eşleme iki yere yazılsaydı, biri
/// güncellenip diğeri unutulduğunda stok bir hesaba girip başka bir
/// hesaptan çıkardı ve mizan sessizce şişerdi. Fark ancak dönem
/// sonunda, sayım tutmadığında görülürdü.
/// </summary>
public interface IInventoryAccountResolver
{
    /// <summary>Stokun durduğu hesap: sarf → 150, ticari mal → 153.</summary>
    Task<Guid> ResolveStockAccountAsync(
        Guid companyId, InventoryAccountingKind kind, CancellationToken cancellationToken);

    /// <summary>Çıkışta yazılan hesap: sarf → 740, ticari mal → 621.</summary>
    Task<Guid> ResolveConsumptionAccountAsync(
        Guid companyId, InventoryAccountingKind kind, CancellationToken cancellationToken);

    /// <summary>
    /// SATIŞTA yazılan maliyet hesabı: TÜR NE OLURSA OLSUN 621.
    ///
    /// Bilinçli olarak <see cref="ResolveConsumptionAccountAsync"/>'ten
    /// AYRI: o metot malzemenin PROJEDE TÜKETİLMESİNİ karşılıyor ve
    /// sarfı 740 Hizmet Üretim Maliyeti'ne yazıyor. Satılan malzeme
    /// tüketilmemiştir — üretim maliyeti değil, satılan malın
    /// maliyetidir. Sarf malzeme 740'a yazılsaydı satış, hiç
    /// yapılmamış bir işin üretim maliyeti gibi görünür ve proje
    /// maliyet raporları şişerdi.
    ///
    /// Alacak tarafı yine kartın kategorisinden gelir (150 / 153);
    /// ayrılan yalnız borç tarafıdır.
    /// </summary>
    Task<Guid> ResolveCostOfGoodsSoldAccountAsync(
        Guid companyId, CancellationToken cancellationToken);

    /// <summary>
    /// PROJEYE/ŞANTİYEYE çıkan malzemenin gideri — 740.03.09
    /// KULLANILAN MALZEMELER, yoksa 740 ana hesabı.
    ///
    /// Alt hesap ÖNCE deneniyor: canlı hesap planında 740 altında
    /// işçilik, dışarıdan hizmet ve amortisman ayrı ayrı duruyor.
    /// Malzemeyi ana hesaba yazmak, mali müşavirin kurduğu bu ayrımı
    /// bozar ve "projeye ne kadar malzeme gitti" sorusu ancak hareket
    /// kayıtlarına inilerek cevaplanabilirdi.
    ///
    /// TÜRDEN BAĞIMSIZ: ticari mal da projeye giderse 740'a yazılır —
    /// satılmamış, projede tüketilmiştir. Ayrılan yalnız ALACAK
    /// tarafıdır (150 / 153).
    /// </summary>
    Task<Guid> ResolveProjectConsumptionAccountAsync(
        Guid companyId, CancellationToken cancellationToken);

    /// <summary>
    /// PROJESİZ çıkan malzemenin gideri — 770 Genel Yönetim Giderleri.
    ///
    /// KULLANICI KARARI. Ofis/merkez sarfiyatı bir üretim maliyeti
    /// değildir; 740'a yazılsaydı hiç iş yapılmamışken üretim maliyeti
    /// doğar, proje kârlılık raporları ve hakediş maliyet kıyasları
    /// şişerdi.
    /// </summary>
    Task<Guid> ResolveGeneralAdminExpenseAccountAsync(
        Guid companyId, CancellationToken cancellationToken);

    /// <summary>
    /// SAYIM NOKSANI — 689.02. Alt hesap ZORUNLU: canlı planda 689 ana
    /// hesabına fiş kesilemiyor (ölçüldü).
    /// </summary>
    Task<Guid> ResolveInventoryShortageAccountAsync(
        Guid companyId, CancellationToken cancellationToken);

    /// <summary>SAYIM FAZLASI — 649.03.</summary>
    Task<Guid> ResolveInventorySurplusAccountAsync(
        Guid companyId, CancellationToken cancellationToken);

    /// <summary>Bir kartın kategorisinden muhasebe karşılığı.</summary>
    Task<InventoryAccountingKind> ResolveKindAsync(
        Guid inventoryItemId, CancellationToken cancellationToken);

    /// <summary>
    /// Faturası gelmemiş mal alımları (GR/IR) — 379.01. Mal kabulde
    /// borç burada bekler, fatura gelince 320'ye devreder.
    /// </summary>
    Task<Guid> ResolveGoodsReceivedNotInvoicedAccountAsync(
        Guid companyId, CancellationToken cancellationToken);
}

public sealed class InventoryAccountResolver(AppDbContext db) : IInventoryAccountResolver
{
    /// <summary>
    /// Tekdüzen hesap planı kodları. Kategori sistem geneli olduğu için
    /// eşleme de kod bazında; kimliğe çevirme şirket bazında yapılır.
    /// </summary>
    public const string ConsumableStockCode = "150";
    public const string TradeGoodStockCode = "153";
    public const string ConsumableExpenseCode = "740";
    public const string TradeGoodCostCode = "621";

    /// <summary>
    /// GR/IR ara hesabı. Alt hesap: 379 ana hesabı fiş kesilemez
    /// olduğu için hareket buraya yazılır.
    /// </summary>
    public const string GoodsReceivedNotInvoicedCode = "379.01";

    /// <summary>
    /// Projede kullanılan malzeme. Canlı planda mali müşavirin açtığı
    /// alt hesap; yoksa 740 ana hesabına düşülür.
    /// </summary>
    public const string ProjectMaterialExpenseCode = "740.03.09";

    /// <summary>Projesiz (merkez/ofis) sarfiyat.</summary>
    public const string GeneralAdminExpenseCode = "770";

    /// <summary>Sayım noksanı. 689 ana hesabı fiş kesmeye kapalı.</summary>
    public const string InventoryShortageCode = "689.02";

    /// <summary>Sayım fazlası.</summary>
    public const string InventorySurplusCode = "649.03";

    public Task<Guid> ResolveStockAccountAsync(
        Guid companyId, InventoryAccountingKind kind, CancellationToken cancellationToken) =>
        FindAsync(
            companyId,
            kind == InventoryAccountingKind.TradeGood ? TradeGoodStockCode : ConsumableStockCode,
            kind == InventoryAccountingKind.TradeGood
                ? "Ticari mal stok hesabı (153)"
                : "Sarf malzeme stok hesabı (150)",
            cancellationToken);

    public Task<Guid> ResolveConsumptionAccountAsync(
        Guid companyId, InventoryAccountingKind kind, CancellationToken cancellationToken) =>
        FindAsync(
            companyId,
            kind == InventoryAccountingKind.TradeGood ? TradeGoodCostCode : ConsumableExpenseCode,
            kind == InventoryAccountingKind.TradeGood
                ? "Satılan ticari mallar maliyeti (621)"
                : "Hizmet üretim maliyeti (740)",
            cancellationToken);

    public Task<Guid> ResolveCostOfGoodsSoldAccountAsync(
        Guid companyId, CancellationToken cancellationToken) =>
        FindAsync(
            companyId,
            TradeGoodCostCode,
            "Satılan ticari mallar maliyeti (621)",
            cancellationToken);

    public async Task<InventoryAccountingKind> ResolveKindAsync(
        Guid inventoryItemId, CancellationToken cancellationToken)
    {
        var kind = await db.InventoryItems
            .Where(x => x.Id == inventoryItemId)
            .Select(x => x.InventoryCategory != null
                ? (InventoryAccountingKind?)x.InventoryCategory.AccountingKind
                : null)
            .SingleOrDefaultAsync(cancellationToken);

        // Kategorisiz kart (S1 öncesinden kalanlar) SARF sayılır:
        // taahhüt işinin varsayılanı bu ve yanlış tarafa düşmek
        // istenirse ticari mal tarafı olmamalı — 153'e yazılan bir
        // sarf malzeme mali tabloda satılabilir mal gibi görünür.
        return kind ?? InventoryAccountingKind.Consumable;
    }

    public Task<Guid> ResolveProjectConsumptionAccountAsync(
        Guid companyId, CancellationToken cancellationToken) =>
        FindFirstAsync(
            companyId,
            [ProjectMaterialExpenseCode, ConsumableExpenseCode],
            "Projede kullanılan malzeme gideri hesabı (740.03.09 ya da 740)",
            cancellationToken);

    public Task<Guid> ResolveGeneralAdminExpenseAccountAsync(
        Guid companyId, CancellationToken cancellationToken) =>
        FindAsync(
            companyId,
            GeneralAdminExpenseCode,
            "Genel yönetim giderleri hesabı (770)",
            cancellationToken);

    public Task<Guid> ResolveInventoryShortageAccountAsync(
        Guid companyId, CancellationToken cancellationToken) =>
        FindAsync(
            companyId,
            InventoryShortageCode,
            "Stok sayım noksanları hesabı (689.02)",
            cancellationToken);

    public Task<Guid> ResolveInventorySurplusAccountAsync(
        Guid companyId, CancellationToken cancellationToken) =>
        FindAsync(
            companyId,
            InventorySurplusCode,
            "Stok sayım fazlaları hesabı (649.03)",
            cancellationToken);

    public Task<Guid> ResolveGoodsReceivedNotInvoicedAccountAsync(
        Guid companyId, CancellationToken cancellationToken) =>
        FindAsync(
            companyId,
            GoodsReceivedNotInvoicedCode,
            "Faturası gelmemiş mal alımları hesabı (379.01)",
            cancellationToken);

    /// <summary>
    /// Kodları SIRAYLA dener, ilk bulduğunu döndürür. Tercih edilen
    /// alt hesap yoksa ana hesaba düşmek için — hesap planı her
    /// şirkette birebir aynı derinlikte değil.
    /// </summary>
    private async Task<Guid> FindFirstAsync(
        Guid companyId, string[] codes, string label, CancellationToken cancellationToken)
    {
        foreach (var code in codes)
        {
            var id = await db.AccountingAccounts
                .Where(x => x.CompanyId == companyId
                    && x.Code == code
                    && x.IsPostingAllowed)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (id is not null) return id.Value;
        }

        throw new InvalidOperationException(
            $"{label} hesap planında bulunamadı. Muhasebe → Hesap Planı'ndan açılmalı.");
    }

    private async Task<Guid> FindAsync(
        Guid companyId, string code, string label, CancellationToken cancellationToken)
    {
        // Önce tam kod, sonra o kodun altındaki ilk alt hesap: hesap
        // planında "150" ana hesabı bazen hareket görmez, kayıtlar
        // "150.01.02" gibi alt hesaplara yazılır.
        var id = await db.AccountingAccounts
            .Where(x => x.CompanyId == companyId && x.Code == code)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (id is not null) return id.Value;

        id = await db.AccountingAccounts
            .Where(x => x.CompanyId == companyId && x.Code.StartsWith(code + "."))
            .OrderBy(x => x.Code)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return id ?? throw new InvalidOperationException(
            $"{label} hesap planında bulunamadı ({code}). "
            + "Muhasebe → Hesap Planı'ndan açılmalı.");
    }
}
