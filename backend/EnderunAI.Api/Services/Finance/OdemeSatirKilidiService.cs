using EnderunAI.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Finance;

/// <summary>
/// ÖDEME SATIRI KİLİDİ — ödeme yazan her yolun aynı deseni
/// kullanmasını sağlar (ÖP/1a · S6).
///
/// SORUN: ödeme kaydı oku-karşılaştır-yaz. İki istek aynı satırı aynı
/// anda okursa İKİSİ DE K2 karşılaştırmasını "onaylandığı gibi" geçer,
/// İKİSİ DE K3 sınırını KENDİ payına geçer — çünkü sınır okunan
/// `OdenenTutar` üzerinden hesaplanıyor ve o değer bayat. Sonuç:
/// toplamda ONAYLANANDAN FAZLA ödeme yazılır.
///
/// PostgreSQL varsayılanı Read Committed olduğu için iki işlem
/// çakışmadan tamamlanır: veritabanı hata vermez, satır "ödendi"
/// görünür ama iki kez ödenmiştir. En kötü hata türü — sessiz olanı.
///
/// ÇÖZÜM: satır `FOR UPDATE` ile kilitlenir. İkinci istek birincinin
/// işlemi bitene kadar bekler, sonra TAZE `OdenenTutar` ile K3'ü
/// hesaplar ve "onaylanan tutar aşılıyor" diye TEMİZ hata verir.
///
/// NEDEN `RowVersion` DEĞİL: damga aynı duruma ikinci bir hata yolu
/// açardı ve verdiği mesaj ANLAMSIZ olurdu ("kayıt değişti, tekrar
/// deneyin"). Kilit ise ikinci isteğe ANLAMLI olanı söylüyor:
/// "onaylanan tutar aşılıyor". Kullanıcı ne yapacağını biliyor.
///
/// DESEN `StokSatirKilidiService`TEN ALINDI — aynı sınıf sorun, aynı
/// çözüm. İki ayrı desen kurmak, birinin unutulduğu gün fark
/// edilmemesi demektir.
/// </summary>
public interface IOdemeSatirKilidi
{
    /// <summary>
    /// Satırı, içinde bulunulan işlem bitene kadar kilitler.
    /// </summary>
    Task KilitleAsync(Guid satirId, CancellationToken cancellationToken);
}

public sealed class OdemeSatirKilidiService(AppDbContext db) : IOdemeSatirKilidi
{
    public async Task KilitleAsync(Guid satirId, CancellationToken cancellationToken)
    {
        /*
         * İŞLEM YOKSA KİLİT YOK — SESSİZ DEĞİL, GÜRÜLTÜLÜ HATA.
         *
         * `SELECT ... FOR UPDATE` işlem dışında yalnız o ifade boyunca
         * tutar; ifade biter bitmez kilit serbest kalır ve koruma
         * hiçbir şey yapmaz. Bunu sessizce geçmek, koruması olduğunu
         * sanan ama olmayan bir akış üretir — S6'nın yakaladığı şeyin
         * aynısını, bu kez kilit VARMIŞ gibi görünerek.
         */
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Ödeme satır kilidi işlem dışında alınamaz: FOR UPDATE yalnız "
                + "ifade boyunca tutar ve kilit sessizce hiçbir şey yapmaz. "
                + "Ödeme yazan akış BeginTransactionAsync ile açılmalı.");
        }

        await db.Database.ExecuteSqlRawAsync(
            "SELECT \"Id\" FROM odeme_plani_satirlari WHERE \"Id\" = {0} FOR UPDATE",
            [satirId], cancellationToken);
    }
}
