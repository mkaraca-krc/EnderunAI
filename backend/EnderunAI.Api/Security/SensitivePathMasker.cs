using System.Text.RegularExpressions;

namespace EnderunAI.Api.Security;

/// <summary>
/// URL YOLUNDAKİ SIRLARI MASKELER — KAYDA YAZILMADAN ÖNCE.
///
/// NEDEN VAR: bazı bağlantılar sırrı yolun kendisinde taşıyor.
/// İşveren portalı `/api/portal/{token}` biçiminde ve o token 256
/// bitlik bir anahtar. Yolu olduğu gibi loglayan her nokta — hata
/// kaydı, ara katman, denetim olayı — o anahtarı düz metin olarak
/// diske yazar.
///
/// 2026-08-23'te bu üç ayrı yerde bulundu ve üçü de ayrı ayrı
/// kapatıldı: nginx erişim kaydı, denetim kesicisi ve
/// PortalTokenRejected olayı. Dördüncüsü — GlobalExceptionHandler'ın
/// yazdığı `Path=` — henüz gerçekleşmemişti: portal ucunda bugüne
/// kadar işlenmeyen bir hata olmamıştı. Şans eseri temiz kalan bir
/// yeri düzeltmek, sızdıktan sonra düzeltmekten ucuzdur.
///
/// TEK NOKTA: maskeleme kuralı burada duruyor. Her log satırında
/// tekrar yazılsaydı biri unutulur ve o nokta sessizce sızdırırdı —
/// tam olarak bu paketin tekrar tekrar öğrendiği ders.
///
/// NGINX İLE AYNI MANTIK: `deploy/nginx/portal-token-maskeleme.conf`
/// aynı yolları aynı biçimde (`/portal/***`) maskeliyor. İki taraf
/// ayrı teknoloji ama aynı kural; biri değişirse diğeri de
/// değişmeli.
/// </summary>
public static class SensitivePathMasker
{
    /*
     * SIR TAŞIYAN YOL DESENLERİ.
     *
     * Ölçüt "gizli mi" değil, "URL'de bir SIR mı taşıyor": tahmin
     * edilemez olması gereken, ele geçirildiğinde erişim veren bir
     * dizgi. Kimlik (Guid) bunlara girmez — kaydın kimliği sır
     * değildir ve teşhis için gereklidir.
     *
     * Yeni bir "bağlantıyla erişim" özelliği eklendiğinde (parola
     * sıfırlama, davet, e-posta doğrulama) deseni BURAYA eklemek
     * gerekir. Eklenmezse o özellik ilk hatasında sırrını günlüğe
     * yazar.
     */
    private static readonly Regex[] Desenler =
    [
        // /api/portal/{token} ve /portal/{token} (frontend rotası da
        // aynı biçimde; bir gün burada da loglanabilir).
        new(@"(?<onek>/(?:api/)?portal/)[^/?#]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // Gelecekteki bağlantıyla-erişim akışları. Uç henüz yok;
        // desen şimdiden duruyor ki eklendiğinde maskeleme
        // kendiliğinden çalışsın — sonradan hatırlanması gereken bir
        // adım olmasın.
        new(@"(?<onek>/api/auth/(?:reset|invite|verify|dogrula)/)[^/?#]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    /// <summary>
    /// Yolu maskeler. Sır taşımayan yollar OLDUĞU GİBİ döner —
    /// teşhis gücü korunsun diye: yolu hiç yazmamak da bir seçenekti
    /// ama o zaman hatanın hangi uçta olduğu kaybolurdu.
    /// </summary>
    public static string Mask(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        var sonuc = path;

        foreach (var desen in Desenler)
            sonuc = desen.Replace(sonuc, "${onek}***");

        return sonuc;
    }
}
