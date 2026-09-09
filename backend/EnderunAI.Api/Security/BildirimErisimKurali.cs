namespace EnderunAI.Api.Security;

/// <summary>
/// BİLDİRİM ÜZERİNDE İŞLEM YAPABİLİR Mİ — TEK KURAL, TEK YER (BİLDİRİM/1).
///
/// ═══ ÖLÇÜLDÜ: KURALIN YARISI YAZILMIŞTI ═══
///
/// `TransitionAsync` yalnız `RequiredPermission`'a bakıyordu;
/// `TargetUserId` HİÇ kontrol edilmiyordu. Kişisel bildirimde
/// `RequiredPermission` boş olduğu için geriye hiçbir kontrol kalmıyor,
/// giriş yapmış herhangi bir kullanıcı kimliğini bildiği bir kişisel
/// bildirimi başkası adına okundu/kapalı/ertelenmiş yapabiliyordu.
///
/// ÇAĞIRARAK ÖLÇÜLDÜ (EKSİK/1 E3, 2026-09-09): üç uç da HTTP 200 döndü
/// ve sahibin kaydını değiştirdi. Kod okumasıyla değil, sondayla.
///
/// AÇILDIĞI AN: 2026-08-23, `8f928090` (M1/1 — `TargetUserId` alanı
/// eklendi). Uç 2026-08-11'de doğduğunda kişisel bildirim YOKTU ve kural
/// o gün eksiksizdi; delik, yeni alan eklenirken bu ucun
/// güncellenmemesinden açıldı. Kusurun yeri "yanlış yazılmış kod" değil,
/// "yeni durum eklenirken güncellenmeyen kural" — bu yüzden kural
/// buraya, tek bir yere alındı.
///
/// ═══ İKİ KOŞUL, VE İLE BAĞLI ═══
///
///   sahiplik : `TargetUserId` DOLUYSA çağıran o kullanıcı olmalı.
///   izin     : `RequiredPermission` DOLUYSA çağıranda o izin olmalı.
///
/// "BİRİ SAĞLANIYORSA YETER" YAZILAMAZ: kişisel bildirimde izin alanı
/// boştur, yani VEYA yazıldığında izin koşulu kendiliğinden sağlanır ve
/// sahiplik kontrolü hiç çalışmaz. Aynı delik, başka kılıkta.
///
/// ═══ NEDEN ÜÇ UCA AYRI AYRI EKLENMEDİ ═══
///
/// `MarkRead`, `Dismiss` ve `Snooze` üçü de `TransitionAsync`'ten
/// geçiyor. Kontrol üç uca ayrı yazılsaydı, yarın eklenecek dördüncü
/// geçiş ucu aynı deliği yeniden açardı — bugün `TargetUserId`
/// eklenirken olanın aynısı (Kural 79).
///
/// ═══ İKİ AYRI SONUÇ, ÇÜNKÜ İKİ AYRI ANLAM ═══
///
/// `SahibiDegil` -> 404. Kişisel bir bildirimin VARLIĞI bile
/// açıklanmamalı; 403 dönseydi yabancı, denediği kimliğin gerçek bir
/// kişisel bildirime ait olduğunu öğrenirdi.
/// `IzinYok` -> 403. Şirket bildirimi zaten herkesin bildiği bir
/// kayıttır; orada gizlenecek varlık yok.
/// </summary>
public static class BildirimErisimKurali
{
    public enum Sonuc
    {
        Serbest,
        SahibiDegil,
        IzinYok
    }

    /// <param name="hedefKullaniciId">
    /// `Notification.TargetUserId` — boşsa bildirim şirket geneli.
    /// Alıcı satırı üzerinden çağrılıyorsa o satırın `UserId`'si.
    /// </param>
    /// <param name="gerekenIzin">`Notification.RequiredPermission`.</param>
    /// <param name="cagiranId">Oturumdaki kullanıcı.</param>
    /// <param name="cagiraninIzinleri">Kanonik çözücüden gelen izin kümesi.</param>
    public static Sonuc Degerlendir(
        Guid? hedefKullaniciId,
        string? gerekenIzin,
        Guid cagiranId,
        IReadOnlyCollection<string> cagiraninIzinleri)
    {
        // SAHİPLİK ÖNCE: sıra önemli. İzin önce bakılsaydı, izni
        // olmayan yabancı 403 alır ve kişisel bildirimin varlığını
        // yine öğrenirdi.
        if (hedefKullaniciId is Guid hedef && hedef != cagiranId)
            return Sonuc.SahibiDegil;

        if (gerekenIzin is string gerekli && !cagiraninIzinleri.Contains(gerekli))
            return Sonuc.IzinYok;

        return Sonuc.Serbest;
    }
}
