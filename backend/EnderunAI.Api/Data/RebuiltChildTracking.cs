using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Data;

/// <summary>
/// YENİDEN KURULAN ALT SATIRLARI "YENİ" OLARAK İŞARETLER.
///
/// SORUN: <see cref="Models.BaseEntity"/> kurulumda Id'yi
/// <c>Guid.NewGuid()</c> ile doldurur, yani anahtar dolu gelir. EF,
/// izlenen bir üst kaydın koleksiyonunda bulduğu anahtarı dolu varlığı
/// VAR OLAN satır sayıp <c>Added</c> yerine <c>Modified</c> işaretler.
/// Sonuç: olmayan satıra UPDATE gider ve kayıt "beklenen 1 satır,
/// etkilenen 0" hatasıyla düşer. Aynı kusur RFQ teklif kaydetmeyi de
/// 500'e düşürüyordu; orada teklif DbSet'e eklenerek çözüldü.
///
/// NEDEN BURADA DbSet'E EKLEMİYORUZ: bu akışlar üst kaydı önce
/// veritabanından yüklüyor, alt satırları temizleyip yeniden kuruyor.
/// Alt satırı üst kaydın koleksiyonuna eklemek zorunludur — yabancı
/// anahtar oradan çözülür. Eklemeden sonra durumu düzeltmek, ilişkiyi
/// bozmadan doğru sonucu verir.
///
/// GÜVENLİK KAYDI: yalnız <c>Modified</c> ve <c>Detached</c> durumlar
/// <c>Added</c>'a çevrilir. Veritabanından yüklenmiş bir satır
/// <c>Unchanged</c> ya da <c>Deleted</c> olur; onlara DOKUNULMAZ, yoksa
/// var olan satır ikinci kez eklenmeye çalışılırdı.
/// </summary>
public static class RebuiltChildTracking
{
    public static void MarkRebuiltAsNew<TChild>(
        this DbContext db,
        IEnumerable<TChild> children)
        where TChild : class
    {
        foreach (var child in children)
        {
            var entry = db.Entry(child);

            if (entry.State is EntityState.Modified or EntityState.Detached)
                entry.State = EntityState.Added;
        }
    }
}
