using EnderunAI.Api.Security;

namespace EnderunAI.Api.Services.Notifications;

/// <summary>
/// SAATLİK ERİŞİM RETLERİ ÖZETİ — SESSİZLİĞİN ANLAMI (GÜNLÜK/1).
///
/// ═══ NEDEN VAR ═══
///
/// Günlükte 401 satırı yoksa İKİ ŞEY olabilir: 401 olmadı, ya da
/// günlük öldü. Ayırt edemezsek 9 Eylül'deki yere geri düşeriz —
/// o gece 30 dakikalık pencerede 2034 satır vardı ve hiçbiri erişim
/// kararı değildi; sessizlik "sorun yok" sanılmıştı.
///
/// Bu iş SAAT BAŞI TEK SATIR yazıyor, SAYI SIFIR OLSA BİLE. Böylece
/// sessizlik "olay yok" değil "ÖZET DE DÜŞMEMİŞ, günlük ölmüş"
/// anlamına gelir ve fark edilir.
///
/// ═══ İKİNCİ İŞİ: SEL PENCERELERİNİ KAPATMAK ═══
///
/// `ErisimGunlugu` penceredeki tekrarları biriktiriyor ve pencere
/// KAPANDIĞINDA yazıyor. Kapanışı yalnız "aynı anahtardan yeni bir
/// olay" tetikleseydi, bir daha tekrarlamayan bir sel SONSUZA KADAR
/// yazılmadan kalırdı. Bu iş dakikada bir kapanışları süpürüyor.
///
/// ═══ NEDEN AYRI ZAMANLAYICI KURULMADI ═══
///
/// Uygulamada zaten `IHostedService` deseni var ve NÖBET/1'in
/// altyapısı kullanılıyor. İkinci bir zamanlayıcı, iki ayrı zaman
/// kaynağı demektir; biri durduğunda öteki çalışıyor görünür.
/// </summary>
public sealed class ErisimGunluguOzetService(
    ILogger<ErisimGunluguOzetService> logger) : BackgroundService
{
    private static readonly TimeSpan SupurmeAraligi = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan OzetAraligi = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sonOzet = DateTime.UtcNow;

        // Açılıştan hemen sonra bir özet: "günlük ayakta" diyen ilk
        // satır, bir sonraki saati beklemesin.
        Ozetle(ErisimGunlugu.PencereleriKapat(logger), "acilis");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(SupurmeAraligi, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var dagilim = ErisimGunlugu.PencereleriKapat(logger);

            if (DateTime.UtcNow - sonOzet < OzetAraligi) continue;

            sonOzet = DateTime.UtcNow;
            Ozetle(dagilim, "saatlik");
        }
    }

    private void Ozetle(
        IReadOnlyDictionary<ErisimRetSebebi, int> dagilim,
        string tur)
    {
        var toplam = dagilim.Values.Sum();

        var metin = dagilim.Count == 0
            ? "yok"
            : string.Join(
                " ",
                dagilim.OrderByDescending(x => x.Value)
                    .Select(x => $"{x.Key}={x.Value}"));

        // SIFIRDA DA YAZILIR — satırın YOKLUĞU tek başına bir bulgudur.
        logger.LogInformation(
            "ERISIM-RET-OZET tur={Tur} toplam={Toplam} dagilim={Dagilim}",
            tur, toplam, metin);
    }
}
