namespace EnderunAI.Api.Security.UcKapisi;

/// <summary>
/// AÇILIŞ DENETİMİ — beyansız uç varsa uygulama başlamaz.
///
/// ÇAĞRI YERİ: bütün `Map...` çağrılarından SONRA, `app.Run()`'dan önce.
/// Daha erken çağrılsaydı yönlendirme tablosu eksik olurdu ve denetim
/// göremediği ucu "yok" sayardı.
///
/// TEST İLE AYNI TARAYICI: burada da, testte de
/// <see cref="UcKapisiDenetimi.BeyansizlariBul"/> çağrılır. İki ayrı
/// tarayıcı yazılsaydı biri gevşetilip diğeri kalırdı.
/// </summary>
public static class UcKapisiAcilisDenetimi
{
    public static void Dogrula(IEndpointRouteBuilder yonlendirme)
    {
        var uclar = yonlendirme.DataSources
            .SelectMany(kaynak => kaynak.Endpoints)
            .ToList();

        /*
         * POZİTİF KONTROL: tablo boşsa denetim her iddiayı doğrular ve
         * hiçbir şey kanıtlamaz. Boş bir yüzey, boş bir küme gibidir.
         */
        if (uclar.Count == 0)
            throw new InvalidOperationException(
                "Uç kapısı denetimi: yönlendirme tablosu BOŞ. Denetim bu " +
                "hâliyle hiçbir şey kanıtlamaz; çağrı yeri Map... " +
                "çağrılarından sonra olmalıdır.");

        // Kaynak okunamazsa burada istisna atar — kapalı tarafa düşer.
        var muaflar = MuafiyetListesi.Anahtarlar();

        var beyansiz = UcKapisiDenetimi.BeyansizlariBul(uclar, muaflar);
        var olu = UcKapisiDenetimi.OluMuafiyetler(uclar, muaflar);
        var belirsiz = UcKapisiDenetimi.BelirsizMuafiyetler(uclar, muaflar);

        if (beyansiz.Count == 0 && olu.Count == 0 && belirsiz.Count == 0)
            return;

        var metin = new System.Text.StringBuilder();
        metin.AppendLine("UÇ KAPISI — UYGULAMA AÇILAMAZ.");

        if (beyansiz.Count > 0)
        {
            metin.AppendLine();
            metin.AppendLine(
                $"BEYANSIZ UÇ ({beyansiz.Count}): her uç ya [RequirePermission] " +
                "taşımalı, ya [AllowAnonymous] taşımalı, ya da muaf uç " +
                "listesinde kategorisi ve gerekçesiyle yer almalıdır.");

            foreach (var uc in beyansiz)
                metin.AppendLine($"  - {uc.Anahtar}   ({uc.Sablon})");
        }

        if (olu.Count > 0)
        {
            metin.AppendLine();
            metin.AppendLine(
                $"ÖLÜ MUAFİYET ({olu.Count}): karşılığı kalmamış muafiyet " +
                "satırı, bir gün adı aynı olan başka bir ucu sessizce affeder. " +
                "Listeden silin.");

            foreach (var anahtar in olu)
                metin.AppendLine($"  - {anahtar}");
        }

        if (belirsiz.Count > 0)
        {
            metin.AppendLine();
            metin.AppendLine(
                $"BELİRSİZ MUAFİYET ({belirsiz.Count}): tek bir muafiyet " +
                "satırı birden fazla uca karşılık geliyor; yazan kişi " +
                "affettiğinin ne olduğunu bilemez. Anahtarı netleştirin.");

            foreach (var anahtar in belirsiz)
                metin.AppendLine($"  - {anahtar}");
        }

        throw new InvalidOperationException(metin.ToString());
    }
}
