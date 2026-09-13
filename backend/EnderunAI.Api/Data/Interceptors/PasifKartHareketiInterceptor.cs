using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EnderunAI.Api.Data.Interceptors;

/// <summary>
/// PASİF MALZEME KARTINA STOK GİRİŞİ YASAK — TEK KAPI.
///
/// ═══ ÖLÇÜLEN AÇIK (2026-09-13) ═══
///
/// Pasif kart yalnız SEÇİCİDE gizleniyordu; servis düzeyinde engel
/// yoktu. İki ayaklı ölçüm, aynı kart, aynı gövde: kart AKTİF → 200
/// (`CIKIS-2026-000008`), kart PASİF → **yine 200**
/// (`CIKIS-2026-000009`) ve her ikisinde de muhasebe fişi üretildi.
/// Kimlik elindeyse (eski sekme, kaydedilmiş istek, doğrudan çağrı)
/// pasif karta hareket girip yasal deftere fiş yazılabiliyordu.
///
/// ═══ NEDEN DÜZ YASAK DEĞİL — İKİ YÖNLÜ KURAL ═══
///
/// "Pasif karta hareket yasak" demek, ÜZERİNDE STOK KALMIŞ bir arşiv
/// kartını sonsuza kilitlerdi: ne çıkış, ne transfer, ne sayım
/// noksanı — kalan stok hiç boşaltılamazdı.
///
///   stok ARTIRAN  (Receipt, TransferIn, Return, pozitif Adjustment)
///                 → PASİF kartta YASAK
///   stok AZALTAN  (Issue, TransferOut, negatif Adjustment)
///                 → PASİF kartta İZİNLİ
///
/// Pasif kartta sayım FAZLASI girilmesi gerekiyorsa kart geçici olarak
/// aktif edilir, düzeltilir, tekrar pasife alınır. **Bu bilinçli bir
/// kısıt, kusur değildir.**
///
/// ═══ NEDEN KESİCİ, NEDEN ÇAĞRI YERLERİ DEĞİL (Kural 79) ═══
///
/// `new StockMovement` ÖLÇÜLDÜ: 6 dosyada 11 yer. Her birine kontrol
/// koymak, yarın eklenen 12.'nin kontrolsüz kalması demekti. Kural
/// hareketin YAZILDIĞI tek noktada duruyor.
///
/// Mesaj NE OLDUĞUNU değil NE YAPILACAĞINI söyler; `ArgumentException`
/// (ParamName null) K3'ten sonra 400 + mesaj olarak dönüyor.
/// </summary>
public sealed class PasifKartHareketiInterceptor : SaveChangesInterceptor
{
    public const string Mesaj =
        "Bu malzeme kartı pasif. Kullanmak için malzeme kartından aktif edin.";

    /// <summary>
    /// Stok ARTIRAN tipler. `Adjustment` listede YOK: işareti miktara
    /// bakılarak belirlenir (sayım fazlası artırır, noksanı azaltır).
    /// `Count` ölü değer — hiç yazılmıyor; yazılırsa artıran sayılır.
    /// </summary>
    private static bool StokArtiran(StockMovement hareket) => hareket.Type switch
    {
        StockMovementType.Receipt => true,
        StockMovementType.TransferIn => true,
        StockMovementType.Return => true,
        StockMovementType.Count => true,
        StockMovementType.Adjustment => hareket.Quantity > 0,
        _ => false,
    };

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Dogrula(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Dogrula(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Dogrula(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var eklenenler = context.ChangeTracker.Entries<StockMovement>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity)
            .Where(StokArtiran)
            .ToList();

        if (eklenenler.Count == 0)
        {
            return;
        }

        var kimlikler = eklenenler.Select(x => x.InventoryItemId).Distinct().ToList();

        // ÖNCE İZLENENLER: kart zaten yüklenmişse sorgu açılmaz.
        var durum = context.ChangeTracker.Entries<InventoryItem>()
            .Where(x => kimlikler.Contains(x.Entity.Id))
            .ToDictionary(x => x.Entity.Id, x => x.Entity.IsActive);

        var eksikler = kimlikler.Where(x => !durum.ContainsKey(x)).ToList();
        if (eksikler.Count > 0)
        {
            foreach (var satir in context.Set<InventoryItem>()
                         .Where(x => eksikler.Contains(x.Id))
                         .Select(x => new { x.Id, x.IsActive })
                         .AsNoTracking()
                         .ToList())
            {
                durum[satir.Id] = satir.IsActive;
            }
        }

        // KARTI BULAMADIYSAK SUSMAYIZ: bulunamayan kart zaten yabancı
        // anahtarda düşer, ama burada "aktif varsay" demek sessiz bir
        // muafiyet olurdu. Bulunamayan kimlik için kural uygulanmaz,
        // veritabanı kısıtı işini yapar.
        foreach (var hareket in eklenenler)
        {
            if (durum.TryGetValue(hareket.InventoryItemId, out var aktif) && !aktif)
            {
                throw new ArgumentException(Mesaj);
            }
        }
    }
}
