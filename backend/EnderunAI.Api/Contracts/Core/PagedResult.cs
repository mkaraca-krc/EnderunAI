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
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Take,
    bool HasMore)
{
    public static PagedResult<T> From(IReadOnlyList<T> items, int total, int take) =>
        new(items, total, take, total > items.Count);
}
