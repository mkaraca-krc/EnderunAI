using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Inventory;

/// <summary>
/// STOK SATIR KİLİDİ — depo stok kaydını değiştiren her yolun aynı
/// deseni kullanmasını sağlar.
///
/// SORUN: stok değişimi oku-değiştir-yaz. İki istek aynı kalemi aynı
/// anda okursa ikisi de "1 adet var" görür, ikisi de düşer ve tek
/// maldan iki çıkış yapılır. PostgreSQL varsayılanı Read Committed
/// olduğu için bu iki işlem çakışmadan tamamlanır: veritabanı hata
/// vermez, stok 0 görünür ama iki adet çıkmıştır. En kötü hata türü —
/// sessiz olanı.
///
/// ÇÖZÜM: değişiklikten önce ilgili satır `FOR UPDATE` ile kilitlenir.
/// İkinci istek birincinin işlemi bitene kadar bekler, sonra TAZE
/// miktarı okur ve "stok yetersiz" diye temiz hata verir.
///
/// TEK KAPI OLMASI ŞART — `IStockCountLockService` ile aynı gerekçe:
/// bir yol kilidi almazsa koruma yarımdır; kilidi bir taraf alıp
/// diğeri almadığında yarış aynen sürer. `StockMovementContractTests`
/// içindeki kaynak taraması bunu unutulamaz hâle getiriyor.
/// </summary>
public interface IStokSatirKilidi
{
    /// <summary>
    /// Bu depo + kart satırını, içinde bulunulan işlem bitene kadar
    /// kilitler ve izlenen varlığı tazeler.
    /// </summary>
    Task KilitleAsync(
        Guid warehouseId, Guid inventoryItemId, CancellationToken cancellationToken);
}

/// <summary>
/// TAZELEME KARARI — SAF, TEK YERDE.
///
/// İki ayrı bariyer aynı sonucu üretiyordu: (1) aynı işlemde aynı
/// satırın ikinci kez işlenmemesi, (2) yalnız `Unchanged` kaydın
/// tazelenmesi. Sondada ikisi de TEK TEK kanıtlanamadı — birini
/// kaldırınca diğeri sonucu aynı tuttu, test yeşil kaldı ve yeşil
/// hiçbir şey söylemedi (Kural 25).
///
/// Karar buraya çıkarıldı: artık iki koşul da doğrudan sınanabiliyor
/// ve hangisinin ne koruduğu belli.
///
/// - `ilkKilit` false ise: bu işlem satırı zaten işledi. Tazeleme
///   kendi bekleyen düşüşümüzü geri alır — aynı kalem faturada iki
///   satırsa 5 stoktan 2+2 çıkınca 3 yerine 1 kalırdı.
/// - Kayıt `Unchanged` değilse: üzerinde bu işlemin kaydedilmemiş
///   değişikliği var. Tazeleme onu siler.
/// </summary>
public static class StokSatirKilidiKarari
{
    public static bool TazelenmeliMi(bool ilkKilit, EntityState? izlenenDurum) =>
        ilkKilit && izlenenDurum == EntityState.Unchanged;
}

public sealed class StokSatirKilidiService(AppDbContext db) : IStokSatirKilidi
{
    /// <summary>
    /// Bu bağlamda hangi işlemde hangi satır kilitlendi.
    ///
    /// İŞLEM KİMLİĞİ ANAHTARIN PARÇASI: kilit işlem bitince serbest
    /// kalır. Anahtar yalnız depo+kart olsaydı, aynı istek içindeki
    /// İKİNCİ işlem "zaten kilitlemiştim" deyip kilitsiz çalışırdı.
    /// </summary>
    private readonly HashSet<(Guid Islem, Guid Depo, Guid Kalem)> alinanlar = [];

    public async Task KilitleAsync(
        Guid warehouseId, Guid inventoryItemId, CancellationToken cancellationToken)
    {
        /*
         * İŞLEM YOKSA KİLİT YOK — SESSİZ DEĞİL, GÜRÜLTÜLÜ HATA.
         *
         * `SELECT ... FOR UPDATE` işlem dışında yalnız o ifade
         * boyunca tutar; ifade biter bitmez kilit serbest kalır ve
         * koruma hiçbir şey yapmaz. Bunu sessizce geçmek, koruması
         * olduğunu sanan ama olmayan bir akış üretir.
         *
         * Bugün stok değiştiren yolların HEPSİ çağıran tarafta
         * `BeginTransactionAsync` ile açılıyor (ölçüldü). Yarın işlem
         * açmayan yeni bir yol yazılırsa burada patlasın.
         */
        if (db.Database.CurrentTransaction is not { } islem)
        {
            throw new InvalidOperationException(
                "Stok satır kilidi işlem dışında alınamaz: FOR UPDATE yalnız "
                + "ifade boyunca tutar ve kilit sessizce hiçbir şey yapmaz. "
                + "Stok değiştiren akış BeginTransactionAsync ile açılmalı.");
        }

        // AYNI İŞLEMDE AYNI SATIR İKİNCİ KEZ KİLİTLENMEZ.
        //
        // Kilidi tekrar almak zararsız ama gereksiz bir gidiş-dönüş.
        // Asıl önemi TAZELEME kararında: bkz. StokSatirKilidiKarari.
        var ilkKilit = alinanlar.Add((islem.TransactionId, warehouseId, inventoryItemId));

        if (!ilkKilit)
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM warehouse_stocks "
            + "WHERE \"WarehouseId\" = {0} AND \"InventoryItemId\" = {1} "
            + "FOR UPDATE",
            [warehouseId, inventoryItemId],
            cancellationToken);

        /*
         * EF KİMLİK HARİTASI TUZAĞI.
         *
         * Satır bu bağlamda daha önce okunmuşsa, kilitten sonraki
         * sorgu veritabanına GİTMEZ: EF izlediği bayat nesneyi döner.
         * O zaman kilit alınır, beklenir, ve sonra yine eski miktar
         * kullanılır — koruma görünürde vardır, gerçekte yoktur.
         *
         * Yalnız DEĞİŞMEMİŞ kayıt tazelenir: değişmiş kayıt bu işlemin
         * kendi bekleyen değişikliğidir, tazelemek onu silerdi.
         */
        var izlenen = db.ChangeTracker.Entries<WarehouseStock>()
            .FirstOrDefault(x => x.Entity.WarehouseId == warehouseId
                                 && x.Entity.InventoryItemId == inventoryItemId);

        if (StokSatirKilidiKarari.TazelenmeliMi(ilkKilit, izlenen?.State))
        {
            await izlenen!.ReloadAsync(cancellationToken);
        }
    }
}
