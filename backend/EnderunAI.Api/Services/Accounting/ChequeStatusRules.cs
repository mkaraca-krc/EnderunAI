using EnderunAI.Api.Models;

namespace EnderunAI.Api.Services.Accounting;

/// <summary>
/// ÇEK DURUM KURALLARI — SAF, TEK YERDE.
///
/// ŞİKAYET (ÇEK/1): ödenen çek listede kalmaya ve o ayın toplamına
/// girmeye devam ediyordu. Ölçüm kök nedeni gösterdi: durum
/// veritabanına DOĞRU yazılıyordu (`cheques.Status = 11`, hareket
/// satırı, dengeli fiş) — hata OKUMA tarafındaydı. İki ayrı yerde
/// iki ayrı süzgeç vardı ve ikisi de yalnız İPTAL'i eliyordu:
///   - `ChequeService.GetAllAsync` (liste ucu)
///   - `lib/cheques/totals.ts` (ekrandaki ay toplamı)
///
/// Bu yüzden kural buraya alındı. Ekran artık KENDİ KURALINI
/// YAZMIYOR: sunucu her satırda `countsTowardTotals` bayrağı
/// döndürüyor, ekran yalnız topluyor. Ayrışacak ikinci bir karar
/// yeri kalmadı.
///
/// İKİ SORU AYRIDIR:
///   1. "Hangi çekler LİSTELENİR" → <see cref="AcikMi"/>.
///   2. "Hangi satır TOPLANIR"    → <see cref="ToplamaGirer"/>.
///
/// İkisini tek kurala indirseydim "Ödendi" süzgecini seçen kullanıcı
/// dolu bir liste ve SIFIR toplam görürdü. Liste neyin kapsamda
/// olduğuna, toplam listelenenin hangisinin sayılacağına karar
/// veriyor.
/// </summary>
public static class ChequeStatusRules
{
    /// <summary>
    /// AÇIK ÇEKLER — henüz sonuçlanmamış, defterde canlı duranlar.
    ///
    /// Dizi (küme değil) çünkü EF Core `Contains` çağrısını `IN (...)`
    /// olarak yalnız bu biçimde çeviriyor; küme kullanılsaydı süzgeç
    /// belleğe düşer ve tüm çek tablosu çekilirdi.
    ///
    /// KARARLAR (Mehmet, 2026-08-26):
    ///
    /// - `Bounced` (karşılıksız) KAPANMIŞ sayılır. Çek bitti; alacak
    ///   cariye geri döndü ve orada izleniyor. Açık bırakılsaydı aynı
    ///   alacak hem cari hesapta hem çek yükünde iki kez görünürdü.
    ///
    /// - `AtFactoring` (kırdırılmış) AÇIK kalır. Parası alınmış olsa
    ///   da çek hâlâ tedavülde ve rücu riski taşıyor.
    ///   `CashFlowService` onu beklenen tahsilattan ÇIKARIYOR ve bu
    ///   bir çelişki değil: nakit akışı "ne kadar para gelecek" diye
    ///   sorar, çek defteri "hangi çekler hâlâ canlı" diye. Aynı
    ///   soruyu iki türlü cevaplamıyoruz; iki ayrı soru soruyoruz.
    /// </summary>
    public static readonly ChequeStatus[] AcikDurumlar =
    [
        ChequeStatus.Portfolio,
        ChequeStatus.AtBank,
        ChequeStatus.AtFactoring,
        ChequeStatus.Issued
    ];

    /// <summary>Çek hâlâ canlı mı.</summary>
    public static bool AcikMi(ChequeStatus durum) => AcikDurumlar.Contains(durum);

    /// <summary>
    /// Liste isteğinin durumla ilgili TÜM girdileri.
    ///
    /// Tek kayıt hâlinde taşınıyor ki karar tek fonksiyonda verilsin;
    /// parametreler ayrı ayrı gezseydi çağıran taraf yine kendi
    /// birleştirmesini yazardı.
    /// </summary>
    /// <param name="SecilenDurum">Kullanıcının açıkça seçtiği durum.</param>
    /// <param name="KapanmislarDahil">Kapanmış çekler de gelsin mi.</param>
    /// <param name="IptallerDahil">İptal edilenler de gelsin mi.</param>
    public sealed record ListeDurumIstegi(
        ChequeStatus? SecilenDurum,
        bool KapanmislarDahil,
        bool IptallerDahil);

    /// <summary>
    /// GÖSTERİLECEK DURUM KÜMESİ — TEK KARAR NOKTASI.
    ///
    /// NEDEN TEK FONKSİYON: önce iki bağımsız süzgeç vardı ve sorguda
    /// VE ile birleşiyorlardı — biri "açık olmayanı ele", diğeri
    /// "iptal olanı ele". VE ile birleşen iki süzgeçte HER ZAMAN DAR
    /// OLAN SESSİZCE KAZANIR: kullanıcı "iptalleri göster" dese bile
    /// açık süzgeci iptali eliyor ve ekran boş geliyordu.
    ///
    /// Bu gerçekten oldu; mevcut iki test yakaladı
    /// (`IptalEdilenCek_VarsayilanListedeYok_IstenirseGelir`,
    /// `VoidedCheque_StaysVisibleAndFilterable`). O sırada kuralı
    /// "iptali de geçir" diye yamamıştım — yama işe yarıyordu ama
    /// çarpışmayı ORTADAN KALDIRMIYORDU: üçüncü bir süzgeç
    /// eklendiğinde aynı hata yeniden doğardı.
    ///
    /// Artık kümeyi tek yer üretiyor, sorgu tek satır:
    /// `WHERE Status IN (...)`. Çarpışacak ikinci süzgeç yok.
    ///
    /// ÖNCELİK KURALI — AÇIKÇA YAZILI:
    /// **Açık kullanıcı isteği varsayılan süzgeci EZER. Tersi asla
    /// olmaz.** Varsayılan süzgeç bir kolaylıktır; kullanıcı bir şeyi
    /// açıkça istediğinde kolaylık susar.
    /// </summary>
    public static ChequeStatus[] CozumleDurumKumesi(ListeDurumIstegi istek)
    {
        // 1) AÇIK SEÇİM HER ŞEYİ EZER. Kullanıcı "Ödendi" seçtiyse
        //    ödenmişleri ister; varsayılanın onu elemesi, kullanıcıya
        //    "istediğin şeyi göremezsin" demek olurdu.
        if (istek.SecilenDurum is { } secilen)
        {
            return [secilen];
        }

        // 2) Taban küme: varsayılan açıklar, istenirse kapanmışlar da.
        //    İptal bu tabanda YOK; kendi bayrağı var.
        var taban = istek.KapanmislarDahil
            ? Enum.GetValues<ChequeStatus>()
                .Where(x => x != ChequeStatus.Voided)
            : AcikDurumlar.AsEnumerable();

        // 3) İptaller ayrı bayrakla EKLENİR, elenmez. Ekleme biçimi
        //    seçildi çünkü eleme biçimi çarpışmayı geri getirirdi.
        return istek.IptallerDahil
            ? [.. taban, ChequeStatus.Voided]
            : [.. taban];
    }

    /// <summary>
    /// TOPLAMA GİRMEYEN DURUMLAR — yalnız iptal.
    ///
    /// İptalin mali etkileri ters kayıtla geri alındı; ne giriş ne
    /// çıkış sayılır. Satır listede KALIR (denetim izi) ama tutara ve
    /// adede girmez — gizlemek yok saymak değildir.
    ///
    /// Ödenen çek burada elenmez, LİSTEDEN elenir: kullanıcı durum
    /// süzgeciyle ödenenleri istediğinde toplamı da görmeli.
    ///
    /// DİZİ, ÇÜNKÜ SORGUDA DA KULLANILIYOR. Kural önce yalnız bir
    /// metottu (`durum != Voided`); EF Core metot çağrısını SQL'e
    /// çeviremediği için sorgu yerlerinde patlıyordu. Dizi hem
    /// `IN (...)` olarak çevriliyor hem de metodu besliyor — kural
    /// yine tek yerde.
    /// </summary>
    public static readonly ChequeStatus[] ToplamaGirmeyenDurumlar =
    [
        ChequeStatus.Voided
    ];

    /// <summary>Bu satır tutar toplamına girer mi (bellek içi).</summary>
    public static bool ToplamaGirer(ChequeStatus durum) =>
        !ToplamaGirmeyenDurumlar.Contains(durum);
}
