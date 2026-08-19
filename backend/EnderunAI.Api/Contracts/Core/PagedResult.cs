namespace EnderunAI.Api.Contracts.Core;

/// <summary>
/// KIRPILMIŞ LİSTE SONUCU — "kaç kayıt döndü" ile "kaç kayıt VAR"
/// birbirinden ayrılır.
///
/// NEDEN VAR: uçların çoğu büyük tabloları sessiz bir tavanla
/// kırpıyordu (<c>.Take(100)</c>) ve yalnızca diziyi döndürüyordu.
/// Arayüz kırpıldığını anlayamadığı için gelen kaydı TOPLAM sanıyordu.
/// Poz kütüphanesi ekranı tam olarak buna düşmüştü: kütüphanede 23.531
/// poz varken ekranda "Toplam Poz: 100" yazıyordu.
///
/// Tavanın kendisi doğru — 23 bin satırı tarayıcıya yollamak ekranı
/// kilitler. Hatalı olan, tavanın KULLANICIYA SÖYLENMEMESİydi.
///
/// <see cref="Total"/> süzgeçler uygulandıktan SONRA, tavan
/// uygulanmadan ÖNCE sayılır: yani "aramanıza uyan 812 kayıt var,
/// ilk 100'ü gösteriliyor" denebilsin.
/// </summary>
/// <param name="Items">Bu istekte dönen kayıtlar (en fazla <paramref name="Take"/> adet).</param>
/// <param name="Total">Süzgeçlere uyan TOPLAM kayıt sayısı.</param>
/// <param name="Take">Bu istekte uygulanan tavan.</param>
/// <param name="HasMore">Gösterilmeyen kayıt var mı — arayüz bunu uyarıya çevirir.</param>
/// <param name="Page">Kaçıncı sayfa döndü (1'den başlar).</param>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Take,
    bool HasMore,
    int Page = 1)
{
    /// <summary>Sayfalanmayan, yalnız tavan uygulayan uçlar için.</summary>
    public static PagedResult<T> From(IReadOnlyList<T> items, int total, int take) =>
        new(items, total, take, total > items.Count);

    /// <summary>
    /// SAYFALANAN uçlar için.
    ///
    /// <c>HasMore</c> burada <c>items.Count</c>'a bakamaz: son sayfada
    /// tavandan az kayıt döner ama bu "daha yok" demek değildir —
    /// 3. sayfada 7 kayıt varken toplam 57 olabilir. Sayfa ve tavan
    /// üzerinden hesaplanır.
    /// </summary>
    public static PagedResult<T> FromPage(
        IReadOnlyList<T> items, int total, int take, int page) =>
        new(items, total, take, (long)page * take < total, page);
}
