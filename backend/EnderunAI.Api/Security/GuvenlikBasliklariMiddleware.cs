namespace EnderunAI.Api.Security;

/// <summary>
/// GÜVENLİK BAŞLIKLARI — TEK KANONİK YER (BAŞLIK/1).
///
/// ═══ ÖLÇÜLEN EKSİK (2026-09-07) ═══
///
/// `X-Content-Type-Options: nosniff` HİÇBİR YERDE yoktu — ne arka
/// uçta ne nginx'te (ikisi de tarandı, sıfır eşleşme). Dosya servis
/// eden **13 kontrolcü** var ve hepsi bu başlık olmadan yanıt
/// veriyordu.
///
/// ═══ NEDEN ÖNEMLİ ═══
///
/// Tarayıcı, `Content-Type` ile içeriğin uyuşmadığını düşünürse
/// içeriği KOKLAYIP kendi kararını verebiliyor. Kullanıcının
/// yüklediği bir dosya `text/plain` olarak servis edilse bile
/// tarayıcı onu HTML sanıp ÇALIŞTIRABİLİR — depolanmış XSS.
/// `nosniff` bu davranışı kapatıyor.
///
/// ═══ NEDEN MIDDLEWARE, NEDEN 13 KONTROLCÜ DEĞİL ═══
///
/// 13 yere elle eklenen bir başlık, 14'üncü uç yazıldığında
/// unutulur ve kimse fark etmez (Kural 79). Tek yerde durursa
/// unutmak imkânsız.
///
/// ═══ NEDEN TÜM YANITLARA, YALNIZ DOSYALARA DEĞİL ═══
///
/// `nosniff` JSON ve HTML yanıtlar için de zararsız ve faydalı.
/// "Yalnız dosya uçlarında" demek, hangi ucun dosya döndürdüğünü
/// bilen ikinci bir liste tutmak demekti — o liste ayrışırdı.
///
/// ═══ SINIR — AÇIKÇA ═══
///
/// Bu middleware `Content-Disposition` KOYMUYOR. O başlık yanıtın
/// ne olduğuna bağlı (JSON'a `attachment` koymak yanlış olurdu) ve
/// `File(akış, tür, dosyaAdı)` çağrısı zaten `attachment` üretiyor.
/// Onu tutan şey bu middleware değil, `IndirmeUcuBasliklariTests`.
/// </summary>
public sealed class GuvenlikBasliklariMiddleware(RequestDelegate next)
{
    public const string NosniffBasligi = "X-Content-Type-Options";
    public const string NosniffDegeri = "nosniff";

    public Task InvokeAsync(HttpContext context)
    {
        /*
         * BAŞLIK YANIT GÖNDERİLMEDEN ÖNCE YAZILIYOR.
         *
         * `OnStarting` kullanılıyor çünkü başlığı burada doğrudan
         * yazmak, aşağıdaki katmanlar yanıtı çoktan başlatmışsa
         * istisna fırlatır. `OnStarting` ilk baytın hemen öncesinde
         * koşuyor ve her yol için çalışıyor.
         */
        context.Response.OnStarting(BasligiYaz, context);
        return next(context);
    }

    private static Task BasligiYaz(object durum)
    {
        var context = (HttpContext)durum;

        // ÜZERİNE YAZMIYOR: bir uç bilerek farklı bir değer koyduysa
        // onun kararı korunur. Bugün öyle bir uç yok; kural yine de
        // burada, çünkü sessizce ezmek en zor bulunan hata türüdür.
        if (!context.Response.Headers.ContainsKey(NosniffBasligi))
            context.Response.Headers[NosniffBasligi] = NosniffDegeri;

        return Task.CompletedTask;
    }
}
