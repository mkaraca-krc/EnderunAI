namespace EnderunAI.Api.Security;

/// <summary>
/// JETON/1 — İZİN VE ROLÜN İSTEK İÇİ TEK KAYNAĞI (2026-09-10).
///
/// ═══ NEDEN VAR ═══
///
/// `ICurrentUserService` izni ve rolü JETONDAN okuyordu. Jeton 12 saat
/// yaşıyor ve arada tazelenmiyor; izni alınan kullanıcı çek geri alma,
/// sipariş işlemi, fatura GM onayı, satın alma onay aşaması, KPI ve yorum
/// erişiminde ESKİ izniyle çalışıyordu. Çağrılarak ölçüldü: kapanmış
/// iptal yetkisi rolden alındıktan sonra AYNI jetonla ödenmiş çek geri
/// alındı — gerçekleşmiş para hareketi storno edildi.
///
/// ═══ NE YAPAR ═══
///
/// `PermissionAuthorizationMiddleware` kanonik çözücüyü her kimlikli
/// istekte ZATEN çağırıyor. Sonucu yalnız o isteğin `HttpContext.Items`
/// kutusuna koyar; `CurrentUserService` oradan okur. Ek sorgu yok.
///
/// ═══ İSTEK DIŞINA TAŞMAZ ═══
///
/// `Items` HttpContext'e aittir ve onunla ölür. Statik alan, singleton,
/// `AsyncLocal` YOK. Anahtar özel bir nesne: dışarıdan yazılamaz ve
/// başka bir `Items` girdisiyle çakışmaz.
/// </summary>
public static class IstekYetkisi
{
    private static readonly object Anahtar = new();

    public static void Koy(HttpContext baglam, UserAuthorizationSnapshot yetki) =>
        baglam.Items[Anahtar] = yetki;

    public static UserAuthorizationSnapshot? Al(HttpContext? baglam) =>
        baglam is not null && baglam.Items.TryGetValue(Anahtar, out var deger)
            ? deger as UserAuthorizationSnapshot
            : null;
}
