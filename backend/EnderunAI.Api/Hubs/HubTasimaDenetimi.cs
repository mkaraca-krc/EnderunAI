namespace EnderunAI.Api.Hubs;

/// <summary>
/// WEBSOCKET YÜKSELTMESİNİN VEKİLDEN GEÇTİĞİNİ KANITLAYAN UÇ.
///
/// ═══ NEDEN VAR (2026-09-07) ═══
///
/// Duman kontrolü hub yolunu ölçemiyordu. Kimliksiz bir WebSocket
/// el sıkışması **401** alıyor (ölçüldü) — yani 101 hiç görülemiyor.
/// Ve 401, iki AYRI durumda birebir aynı: vekil `Upgrade` başlığını
/// geçirmiş de olabilir, hiç geçirmemiş de. Durum kodu ikisini
/// ayırmıyor.
///
/// Ayırmayan bir ölçüm, ölçüm değildir: hub location'ı bozulsa
/// uygulama sessizce LongPolling'e düşer, her mesaj için saniyede
/// bir HTTP isteği atar ve yayın "başarılı" der. Bu hafta üç kez
/// bulduğumuz sınıfın aynısı — "çalışıyor" ile "doğru çalışıyor"
/// dışarıdan aynı görünüyor.
///
/// ═══ NE YAPIYOR ═══
///
/// Kestrel'e ULAŞAN başlıklara bakıp iki BOOL döndürüyor. İstek
/// `Upgrade: websocket` + `Connection: Upgrade` ile atılır:
///
///   ikisi de true  → vekil bu yolda yükseltme başlıklarını geçiriyor
///   biri false     → `location ^~ /api/hubs/` eşleşmemiş ya da
///                    `proxy_set_header Upgrade` düşmüş
///
/// ═══ İKİ BOOL AYNI ŞEYİ ÖLÇMÜYOR (ölçüldü 2026-09-07) ═══
///
///   `baglantiBasligiGeldi` → nginx `Connection`ı SABİT değer olarak
///     yazıyor (`proxy_set_header Connection "upgrade"`). İstemci
///     hiç göndermese bile true gelir. Yani bu bool tek başına
///     **hub location eşleşti mi** sorusunun cevabı.
///   `yukseltmeBasligiGeldi` → `$http_upgrade` geçirimini ölçüyor,
///     yani istemcinin başlığının Kestrel'e ULAŞTIĞINI.
///
/// SONDAYLA DOĞRULANDI: hub bloğu yapılandırmadan çıkarılıp nginx
/// yeniden yüklendiğinde ikisi de false döndü; blok geri konunca
/// ikisi de true. İkisini birlikte istemek bu yüzden şart.
///
/// ═══ NEDEN GÜVENLİ ═══
///
/// Çağıranın KENDİ gönderdiği iki başlığın VARLIĞINI söylüyor,
/// değerlerini değil. Gövde almıyor, veriye dokunmuyor, oturum
/// açmıyor, yan etkisi yok. Anonim çünkü kimlik isteseydi 401
/// döner ve ölçmek istediği yere HİÇ ULAŞAMAZDI — gövdesiz sağlık
/// ucunda birebir aynı hata bir kez yapıldı (Kural 65).
///
/// ═══ NEDEN LAMBDA DEĞİL ═══
///
/// `Program.cs` çözümleyici bellek sınırının kenarında (AK-11):
/// oraya eklenen tek bir lambda Release derlemesini
/// `OutOfMemoryException` ile düşürdü. Bu yüzden metot grubu olarak
/// bağlanıyor.
/// </summary>
public static class HubTasimaDenetimi
{
    public static IResult Oku(HttpRequest istek)
    {
        var yukseltme = istek.Headers.Upgrade.ToString();
        var baglanti = istek.Headers.Connection.ToString();

        return Results.Ok(new
        {
            yukseltmeBasligiGeldi =
                yukseltme.Contains("websocket", StringComparison.OrdinalIgnoreCase),
            baglantiBasligiGeldi =
                baglanti.Contains("upgrade", StringComparison.OrdinalIgnoreCase),
        });
    }
}
