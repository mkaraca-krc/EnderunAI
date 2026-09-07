using Microsoft.AspNetCore.Http.Connections;

namespace EnderunAI.Api.Hubs;

/// <summary>
/// `MesajHub` uç noktasının bağlantı seçenekleri.
///
/// AYRI BİR DOSYADA, ÇÜNKÜ ÖLÇÜLDÜ: bu ayar `Program.cs` içinde bir
/// lambda olarak yazıldığında Release derlemesi
/// `System.OutOfMemoryException` ile düşüyor (Roslyn `LocalRewriter`,
/// IOperation ağacı). Program.cs üst düzey deyimlerden oluşuyor —
/// tamamı tek bir `&lt;Main&gt;$` metodu — ve eklenen her lambda o
/// metodun operasyon ağacını büyütüyor. Dosya o sınırın kenarında.
/// </summary>
public static class MesajHubSecenekleri
{
    public static void Uygula(HttpConnectionDispatcherOptions options)
    {
        /*
         * BAĞLANTI, JETONUNDAN UZUN YAŞAMAZ.
         *
         * VARSAYILAN `false` VE SESSİZ: SignalR bağlantısı el sıkışmada
         * kimlik doğrular, sonra KENDİ BAŞINA yaşar. Jeton 12 saat sonra
         * ölür; soket açık kalmaya devam ederdi. REST tarafında süresi
         * geçmiş jeton 401 alır — aynı kullanıcının açık sekmesi mesaj
         * almaya devam ederdi. İki yüzey aynı jetona farklı ömür biçemez.
         *
         * BU AYAR TEK BAŞINA YETMEZ ve yeterli sayılmamalıdır: yalnız
         * SÜREYİ dinler, izin/rol değişikliğini duymaz. İzni alınan
         * kullanıcı bu ayarla da bağlı kalır. Onun cevabı
         * `MesajlasmaService.AlicilariCozAsync`: alıcılar YAYIN ANINDA
         * çözülüyor. Bağlantıyı zorla düşürmek ayrı bir iş (AK-10).
         */
        options.CloseOnAuthenticationExpiration = true;
    }
}
