using Microsoft.AspNetCore.Diagnostics;

namespace EnderunAI.Api.Security;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>
    /// Kullanıcıya gösterilen genel mesaj. YALNIZ gerçekten beklenmeyen
    /// hatalar için; doğrulama ihlalleri kendi mesajıyla döner.
    /// </summary>
    private const string BeklenmeyenMesaj =
        "Beklenmeyen bir hata oluştu. Lütfen tekrar deneyin veya sorun devam ederse sistem yöneticisiyle iletişime geçin.";

    /// <summary>
    /// DOĞRULAMA İHLALİ Mİ, ÇÖKÜŞ MÜ?
    ///
    /// ═══ NEDEN VAR (K3, ölçüldü 2026-09-13) ═══
    ///
    /// Uçtan uca provada iki gerçek iş kuralı ihlali kullanıcıya şöyle
    /// dönüyordu:
    ///
    ///     500 · "Beklenmeyen bir hata oluştu. Lütfen tekrar deneyin…"
    ///
    /// Oysa sunucu sebebi BİLİYORDU: "150 hesabında proje seçimi
    /// zorunludur." / "770 hesabında masraf merkezi zorunludur."
    /// Kullanıcı düzeltebilecekken "tekrar deneyin" denip sonsuz
    /// tekrara yollanıyordu.
    ///
    /// ═══ AYRIM NEDEN KORUNUYOR ═══
    ///
    /// Hepsini 400 yapmak GİZLİ HATA YUTMA olurdu: gerçek bir çöküş de
    /// "geçerli bir 400" gibi görünür, günlükte hata seviyesinde
    /// görünmez ve kimse fark etmez. Bu yüzden yalnız NİYETİ BELLİ
    /// istisnalar 400'e çevriliyor.
    ///
    /// ═══ HANGİLERİ (ölçüldü, tahmin değil) ═══
    ///
    /// - Depoda `throw new ArgumentException` 207 yerde; **199'u
    ///   `paramName` TAŞIMIYOR** ve düz Türkçe iş kuralı mesajı yazıyor
    ///   ("Poz bulunamadı.", "Fiyat yılı 2000-2100 aralığında olmalıdır.").
    ///   Kalan 8'i `nameof(...)` ile parametre adı taşıyor — bunlar
    ///   programlama sözleşmesi ihlali, yani ÇÖKÜŞ. `paramName` dolu
    ///   olduğunda .NET mesaja "(Parameter 'x')" ekler; o metin
    ///   kullanıcıya gösterilmemeli.
    /// - `*ValidationException` türleri zaten bu iş için yazılmış.
    ///
    /// `InvalidOperationException` BİLEREK DIŞARIDA: 66 dosyada geçiyor
    /// ve çoğu "bu duruma hiç düşmemeliydi" anlamında — 400'e çevirmek
    /// gerçek çöküşleri sessizleştirirdi.
    /// </summary>
    private static string? DogrulamaMesaji(Exception exception) => exception switch
    {
        // Parametre adı taşıyan ArgumentException (ve türevleri
        // ArgumentNullException / ArgumentOutOfRangeException) ÇÖKÜŞTÜR.
        ArgumentException { ParamName: not null } => null,

        // Parametre adı taşımayan ArgumentException: iş kuralı.
        ArgumentException a => a.Message,

        // Bu iş için yazılmış özel türler.
        _ when exception.GetType().Name.EndsWith("ValidationException", StringComparison.Ordinal)
            => exception.Message,

        _ => null,
    };

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = httpContext.TraceIdentifier;
        var dogrulama = DogrulamaMesaji(exception);

        // MASKELENEREK YAZILIR: portal bağlantısı sırrı yolun kendisinde
        // taşıyor ve ham hâliyle loglanırsa ilk hatada anahtar günlüğe düşer.
        var yol = SensitivePathMasker.Mask(httpContext.Request.Path.Value);

        if (dogrulama is not null)
        {
            // DOĞRULAMA İHLALİ GÜNLÜKTEN SİLİNMİYOR, SEVİYESİ DÜŞÜYOR.
            // Sessizce yutulursa "kullanıcı neden ilerleyemiyor"
            // sorusunun izi kalmaz; Error seviyesinde kalırsa gerçek
            // çöküşler bu gürültünün içinde kaybolur.
            logger.LogWarning(
                "Doğrulama ihlali. TraceId={TraceId} Path={Path} Method={Method} Tip={Tip}",
                traceId, yol, httpContext.Request.Method, exception.GetType().Name);

            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            httpContext.Response.ContentType = "application/json; charset=utf-8";

            await httpContext.Response.WriteAsJsonAsync(new
            {
                message = dogrulama,
                traceId
            }, cancellationToken);

            return true;
        }

        logger.LogError(
            exception,
            "İşlenmeyen hata. TraceId={TraceId} Path={Path} Method={Method}",
            traceId, yol, httpContext.Request.Method);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/json; charset=utf-8";

        await httpContext.Response.WriteAsJsonAsync(new
        {
            message = BeklenmeyenMesaj,
            traceId
        }, cancellationToken);

        return true;
    }
}
