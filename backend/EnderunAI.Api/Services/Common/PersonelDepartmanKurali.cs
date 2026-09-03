namespace EnderunAI.Api.Services.Common;

/// <summary>
/// PERSONEL DEPARTMAN ATAMASININ KURALI — SAF, VERİTABANISIZ.
///
/// ── NEDEN AYRI BİR SINIF ──
///
/// `GorevAtamaKurali` ile aynı gerekçe ve aynı tarih: bu kod tabanında
/// kontrolcü gövdesinde yaşayan kurallar bir yazma yolunda unutuldu ve
/// kimse görmedi (`2d90c946`). Kural saf bir sınıfta yaşarsa hem
/// milisaniyede test edilir, hem de her yazma yolundan çağrılabilir.
///
/// ── BU ALANIN HİÇ YAZMA YOLU YOKTU ──
///
/// `Personnel.DepartmentId` modelde vardı, göçü uygulanmıştı, ama
/// canlıda **79 aktif personelin 0'ında** doluydu. Ölçüm sebebini
/// gösterdi: kod tabanında bu alana yazan hiçbir uç, servis ya da ekran
/// yoktu (`DepartmentId = …` eşleşmelerinin tamamı
/// `HrPosition.DepartmentId` idi). Yani boşluk bir veri girme ihmali
/// değil, eksik bir yazma yoluydu.
///
/// ── DEPARTMANI BOŞALTMAK GEÇERLİ BİR İŞLEMDİR ──
///
/// `null` bir hata değil, bir karar: "bu personel hiçbir departmana
/// bağlı değil". Modelin kendi notu da bunu söylüyor — departmanı boş
/// personel kanal üyeliği almaz, o kadar. Bu yüzden kural `null`ı
/// reddetmez; reddettiği şey KAYIP DEPARTMAN (var olmayan, pasif,
/// silinmiş ya da başka şirketin departmanı).
///
/// ── NEDEN "AYNI ŞİRKET" KONTROLÜ BURADA ──
///
/// Departman `HrDbContext`'te, personel `AppDbContext`'te yaşıyor. EF
/// iki bağlam arasında yabancı anahtar kuramaz; yani veritabanı bu
/// bağı DOĞRULAMIYOR. Kontrol tamamen uygulama katmanında — bu yüzden
/// tek bir yerde ve testli olmak zorunda.
/// </summary>
public static class PersonelDepartmanKurali
{
    /// <summary>
    /// Departman atamasını doğrular. Hata varsa Türkçe mesaj, yoksa
    /// <c>null</c> döner.
    /// </summary>
    /// <param name="departmanId">
    /// Atanacak departman. <c>null</c> = departmandan çıkar (geçerli).
    /// </param>
    /// <param name="departmanVarMi">
    /// Departman kaydı bulundu mu. <c>departmanId</c> null ise anlamsız.
    /// </param>
    /// <param name="departmanAktifMi">Departman aktif ve silinmemiş mi.</param>
    /// <param name="departmanSirketId">Departmanın şirketi.</param>
    /// <param name="personelSirketId">Personelin şirketi.</param>
    public static string? Dogrula(
        Guid? departmanId,
        bool departmanVarMi,
        bool departmanAktifMi,
        Guid? departmanSirketId,
        Guid personelSirketId)
    {
        // DEPARTMANDAN ÇIKARMA: kontrol edilecek bir departman yok.
        if (departmanId is null)
            return null;

        if (!departmanVarMi)
            return "Seçilen departman bulunamadı.";

        if (!departmanAktifMi)
            return "Seçilen departman aktif değil; personel atanamaz.";

        /*
         * ŞİRKET ÇAPRAZI — SESSİZ SIZINTI OLURDU.
         *
         * Bugün canlıda tek şirket var; bu kontrol bugün hiçbir isteği
         * reddetmiyor. Ama tek şirketli olmak bir GARANTİ değil, bir
         * DURUM: ikinci şirket açıldığı gün, kontrol yoksa bir şirketin
         * personeli diğerinin departmanına — ve dolayısıyla o
         * departmanın mesaj kanalına — düşerdi.
         *
         * "Bugün gerçekleşmiyor" ile "olamaz" aynı şey değil; bu ayrım
         * bu kod tabanında iki kez bedel ödetti (`MANUAL` kaçışı ve
         * kapsam süzgeci).
         */
        if (departmanSirketId != personelSirketId)
            return "Departman başka bir şirkete ait; personel atanamaz.";

        return null;
    }

    /// <summary>
    /// Atama gerçekten bir DEĞİŞİKLİK mi?
    ///
    /// NEDEN VAR: tarihçe tablosu "ne zaman neye geçti" sorusunu
    /// cevaplıyor. Aynı departmanı ikinci kez göndermek bir geçiş
    /// değildir; kaydedilirse tarihçe, hiç olmamış değişikliklerle
    /// dolar ve M3'ün "ayrıldığı tarihe kadarki geçmiş" hesabı yanlış
    /// cevaplar üretir.
    /// </summary>
    public static bool DegisiklikMi(Guid? mevcut, Guid? yeni) => mevcut != yeni;
}
