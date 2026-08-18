using System.Text.RegularExpressions;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// KIRPAN UÇ TOPLAM DA DÖNDÜRMEK ZORUNDA.
///
/// NEDEN VAR: poz kütüphanesi ekranı canlıda 23.531 kayıtlık kütüphane
/// için "Toplam Poz: 100" gösteriyordu. Uç bir tavan uyguluyordu (doğru
/// karar — 23 bin satır tarayıcıyı kilitler) ama yalnız diziyi
/// döndürüyordu; arayüz kırpıldığını bilemediği için gelen kaydı TOPLAM
/// sanıyordu.
///
/// Bu, ekranın "çalıştığı" ama YANLIŞ olduğu bir hata sınıfı: sayı
/// biçimli, uyarı yok, kimse fark etmiyor. Tek tek ekran denetleyerek
/// yakalanamaz — kural UÇTA zorlanmalı.
///
/// KURAL: sorgu dizesinden `take`/`limit` alan bir uç, sonucu
/// <see cref="EnderunAI.Api.Contracts.Core.PagedResult{T}"/> ile
/// döndürür. Çağıranın tavan verebildiği yerde "daha var mı" sorusu
/// her zaman sorulabilir demektir.
///
/// SABİT tavanlar (`.Take(8)`, `.Take(20)`) bu kuralın DIŞINDA: onlar
/// kırpma değil TASARIM SINIRI — "son 8 rapor", "en iyi 20 aday" gibi.
/// Etiketi zaten sınırı söylüyor. Ölçüt tavanın VARLIĞI değil, tavanı
/// ÇAĞIRANIN verebilmesidir.
/// </summary>
public sealed class PagedEndpointContractTests
{
    private static string ControllersPath()
    {
        var dir = AppContext.BaseDirectory;

        while (dir is not null &&
               !Directory.Exists(Path.Combine(dir, "EnderunAI.Api", "Controllers")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!, "EnderunAI.Api", "Controllers");
    }

    /// <summary>
    /// Çağıranın tavan verdiği HÂLDE toplam döndürmeyen uçlar ve
    /// GEREKÇELERİ. Bu liste BORÇ değil KARAR kaydı: buradakiler
    /// sıralı sonuç üretiyor, kırpılmış liste değil.
    /// </summary>
    private static readonly Dictionary<string, string> ToplamGerekmez = new()
    {
        ["EngineeringPositionsController.Suggest"] =
            "SIRALI ÖNERİ. Serbest metinden poz öneriyor; adayları " +
            "kütüphane üretiyor, dil modeli sıralıyor. Sonuç bir liste " +
            "değil ilk N ÖNERİ — 'kaç öneri var' anlamlı bir sayı değil.",

        ["PurchaseRequestsController.SearchPositions"] =
            "SIRALI ARAMA. Benzerliğe göre puanlanmış poz araması; " +
            "eşleşme sayısı değil en yakın N kayıt anlamlı. Ayrıca " +
            "iki aşamalı (kelime tutmazsa benzerliğe düşüyor), tek bir " +
            "toplam sayı iki aşamayı da temsil edemezdi.",
    };

    [Fact]
    public void CagiranTavanVerebiliyorsa_UcToplamDaDondurur()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.GetFiles(
                     ControllersPath(), "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);

            // Yorumları soy: gerekçe metinleri "take" yazabilir.
            var code = Regex.Replace(text, @"/\*[\s\S]*?\*/", " ");
            code = Regex.Replace(code, @"//[^\n]*", " ");

            var controller = Path.GetFileNameWithoutExtension(file);

            foreach (var method in SplitMethods(code))
            {
                var callerControlled = Regex.IsMatch(
                    method.Body,
                    @"\[FromQuery\]\s*int\??\s+(take|limit)\b");

                if (!callerControlled)
                    continue;

                /*
                 * KIRPMANIN NEREDE YAPILDIĞINA BAKILMAZ.
                 *
                 * İlk sürüm `.Take(` arıyordu ve bu bir KÖR NOKTAYDI:
                 * `Suggest` tavanı servise devrediyor
                 * (`matcher.SuggestAsync(..., take, ...)`), gövdesinde
                 * `.Take(` geçmiyor. Yani uç kural kapsamına hiç
                 * girmiyordu ve istisna listesindeki kaydı boştaydı —
                 * sonda bunu yakaladı (istisna kaldırıldığında test
                 * düşmedi).
                 *
                 * Doğru ölçüt şu: ÇAĞIRAN TAVAN VEREBİLİYORSA "daha var
                 * mı" sorusu sorulabilir demektir. Kırpmanın kontrolcüde
                 * mi serviste mi yapıldığı kullanıcıyı ilgilendirmez.
                 */
                if (method.Body.Contains("PagedResult"))
                    continue;

                var key = $"{controller}.{method.Name}";

                if (!ToplamGerekmez.ContainsKey(key))
                    offenders.Add(key);
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Bu uçlar çağıranın verdiği tavanla kırpıyor ama TOPLAM " +
            "döndürmüyor: " + string.Join(", ", offenders) +
            ". Arayüz kırpıldığını bilemez ve gelen kaydı toplam sanar " +
            "(poz ekranı tam olarak buna düşmüştü). PagedResult<T> ile " +
            "döndürün; sonuç sıralı bir öneri/arama ise " +
            "PagedEndpointContractTests içindeki listeye GEREKÇESİYLE " +
            "ekleyin.");
    }

    /// <summary>
    /// PagedResult TOPLAMI TAVANDAN ÖNCE saymalı — `items.Count`
    /// vermek asıl kusuru geri getirir.
    /// </summary>
    [Fact]
    public void PagedResult_ToplamiListedenSaymaz()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.GetFiles(
                     ControllersPath(), "*.cs", SearchOption.AllDirectories))
        {
            var code = File.ReadAllText(file);

            if (Regex.IsMatch(code, @"PagedResult<[^>]+>\.From\(\s*\w+\s*,\s*\w+\.Count\b"))
                offenders.Add(Path.GetFileName(file));
        }

        Assert.True(
            offenders.Count == 0,
            "Toplam dönen listeden sayılmış: " + string.Join(", ", offenders) +
            ". Toplam, süzgeçler uygulandıktan SONRA ve tavan " +
            "uygulanmadan ÖNCE sayılır.");
    }

    private sealed record MethodBlock(string Name, string Body);

    /// <summary>
    /// Kontrolcü metinini metotlara böler. Tam bir C# çözümleyicisi
    /// değil — süslü parantez sayarak gövdeyi çıkarır; bu testin
    /// ihtiyacı olan tek şey "hangi imza hangi gövdeyle beraber".
    /// </summary>
    private static IEnumerable<MethodBlock> SplitMethods(string code)
    {
        // `class Foo(...)` BİRİNCİL KURUCUSU metot değil: onu metot
        // sayınca gövdesi TÜM SINIF oluyor ve sınıftaki herhangi bir
        // `[FromQuery] take` ile herhangi bir `.Take(` yan yana
        // görünüyor. Test bu yüzden ilk koşuda yanlış suçlama yaptı.
        var signature = new Regex(
            @"public\s+(?:async\s+)?(?!class\b|sealed\b|record\b)"
            + @"[\w<>\[\],\s\?]+\s+(\w+)\s*\(",
            RegexOptions.Compiled);

        foreach (Match match in signature.Matches(code))
        {
            // Kurucu: adı tipin kendisiyle aynı.
            if (Regex.IsMatch(code, $@"class\s+{Regex.Escape(match.Groups[1].Value)}\b"))
                continue;

            var open = code.IndexOf('{', match.Index);
            if (open < 0) continue;

            var depth = 0;
            var end = -1;

            for (var i = open; i < code.Length; i++)
            {
                if (code[i] == '{') depth++;
                else if (code[i] == '}')
                {
                    depth--;
                    if (depth == 0) { end = i; break; }
                }
            }

            if (end < 0) continue;

            // İmzayı da gövdeye kat: [FromQuery] parametreleri orada.
            yield return new MethodBlock(
                match.Groups[1].Value,
                code[match.Index..(end + 1)]);
        }
    }
}
