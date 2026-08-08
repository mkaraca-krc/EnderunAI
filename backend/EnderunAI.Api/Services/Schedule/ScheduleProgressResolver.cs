namespace EnderunAI.Api.Services.Schedule;

/// <summary>
/// Bir Gantt çubuğunun gerçekleşme yüzdesi nereden geliyor.
/// Ekranda YAZILIR: kullanıcı bir yüzdeye bakarken onun ölçülmüş mü
/// yoksa elle mi girilmiş olduğunu bilmeli.
/// </summary>
public enum ScheduleProgressSource
{
    /// <summary>Ölçülemiyor — hiçbir kaynağa bağlı değil.</summary>
    None = 0,

    /// <summary>İcmal satırının saha gerçekleşmesi.</summary>
    BoqItem = 1,

    /// <summary>İcmal kısmının saha gerçekleşmesi (tutar ağırlıklı).</summary>
    Section = 2,

    /// <summary>Alt aktivitelerin süre ağırlıklı ortalaması.</summary>
    Children = 3,

    /// <summary>Elle girildi.</summary>
    Manual = 4
}

public sealed record ScheduleProgressInput(
    Guid Id,
    Guid? ParentId,
    Guid? SectionId,
    Guid? BoqItemId,
    decimal? ManualRate,
    int DurationWorkDays);

/// <param name="EmployerRate">İşverenin hakedişte kabul ettiği yüzde.
/// Saha ile arasındaki fark "devreden iş"tir. Yalnızca icmale bağlı
/// çubuklarda vardır.</param>
public sealed record ScheduleProgressResult(
    Guid Id,
    decimal Rate,
    ScheduleProgressSource Source,
    string SourceName,
    decimal? EmployerRate);

/// <summary>
/// Çubuk başına gerçekleşme yüzdesini çözer.
///
/// Saf ve veritabanısız. Kaynak sırası bilinçli:
///   icmal satırı → icmal kısmı → alt aktiviteler → elle → yok
///
/// Ölçülmüş veri elle girilene her zaman ÜSTÜN gelir. İcmale bağlı bir
/// çubukta elle yüzde girilmesi zaten reddediliyor; buradaki sıra o
/// kuralın hesap tarafındaki karşılığı.
///
/// Kısma bağlı bir ana çubuk, alt aktivitelerinin ortalamasını DEĞİL
/// kısmın kendi saha oranını kullanır: kısım oranı sözleşme tutarıyla
/// ağırlıklı gerçek veridir, alt aktivite kırılımı ise kullanıcının
/// tercihine bağlı bir ayrıntı.
/// </summary>
public static class ScheduleProgressResolver
{
    public static IReadOnlyDictionary<Guid, ScheduleProgressResult> Resolve(
        IReadOnlyCollection<ScheduleProgressInput> activities,
        IReadOnlyDictionary<Guid, decimal> sectionFieldRates,
        IReadOnlyDictionary<Guid, decimal> sectionEmployerRates,
        IReadOnlyDictionary<Guid, decimal> boqItemFieldRates,
        IReadOnlyDictionary<Guid, decimal> boqItemEmployerRates)
    {
        var results = new Dictionary<Guid, ScheduleProgressResult>();
        var children = activities
            .Where(x => x.ParentId is not null)
            .GroupBy(x => x.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 1. Doğrudan ölçülebilen ve elle girilenler.
        foreach (var activity in activities)
        {
            var direct = ResolveDirect(
                activity, sectionFieldRates, sectionEmployerRates,
                boqItemFieldRates, boqItemEmployerRates);

            if (direct is not null)
                results[activity.Id] = direct;
        }

        // 2. Kaynağı olmayan YAPRAKLAR önce kapatılır. Ana çubuklar
        //    onlardan besleneceği için sıra önemli: alt aktivite henüz
        //    çözülmemişken ortalamaya girerse sıfır sayılırdı.
        foreach (var activity in activities)
        {
            if (results.ContainsKey(activity.Id))
                continue;

            if (children.ContainsKey(activity.Id))
                continue;

            results[activity.Id] = new ScheduleProgressResult(
                activity.Id, 0m, ScheduleProgressSource.None,
                SourceName(ScheduleProgressSource.None), null);
        }

        // 3. Kendi kaynağı olmayan ana çubuklar alt aktivitelerinden
        //    beslenir. Ağırlık SÜREdir: iki günlük bir işle iki aylık
        //    bir iş eşit sayılırsa yüzde gerçeği yansıtmaz.
        foreach (var activity in activities)
        {
            if (results.ContainsKey(activity.Id))
                continue;

            var list = children[activity.Id];

            var weights = list
                .Select(x => (
                    Weight: Math.Max(1, x.DurationWorkDays),
                    Rate: results.TryGetValue(x.Id, out var child) ? child.Rate : 0m))
                .ToList();

            var totalWeight = weights.Sum(x => x.Weight);

            var rate = totalWeight <= 0
                ? 0m
                : decimal.Round(
                    weights.Sum(x => x.Rate * x.Weight) / totalWeight, 2);

            results[activity.Id] = new ScheduleProgressResult(
                activity.Id, rate, ScheduleProgressSource.Children,
                SourceName(ScheduleProgressSource.Children), null);
        }

        return results;
    }

    private static ScheduleProgressResult? ResolveDirect(
        ScheduleProgressInput activity,
        IReadOnlyDictionary<Guid, decimal> sectionFieldRates,
        IReadOnlyDictionary<Guid, decimal> sectionEmployerRates,
        IReadOnlyDictionary<Guid, decimal> boqItemFieldRates,
        IReadOnlyDictionary<Guid, decimal> boqItemEmployerRates)
    {
        if (activity.BoqItemId is Guid itemId &&
            boqItemFieldRates.TryGetValue(itemId, out var itemRate))
        {
            return new ScheduleProgressResult(
                activity.Id,
                Clamp(itemRate),
                ScheduleProgressSource.BoqItem,
                SourceName(ScheduleProgressSource.BoqItem),
                boqItemEmployerRates.TryGetValue(itemId, out var itemEmployer)
                    ? Clamp(itemEmployer)
                    : null);
        }

        if (activity.SectionId is Guid sectionId &&
            sectionFieldRates.TryGetValue(sectionId, out var sectionRate))
        {
            return new ScheduleProgressResult(
                activity.Id,
                Clamp(sectionRate),
                ScheduleProgressSource.Section,
                SourceName(ScheduleProgressSource.Section),
                sectionEmployerRates.TryGetValue(sectionId, out var sectionEmployer)
                    ? Clamp(sectionEmployer)
                    : null);
        }

        if (activity.ManualRate is decimal manual)
        {
            return new ScheduleProgressResult(
                activity.Id,
                Clamp(manual),
                ScheduleProgressSource.Manual,
                SourceName(ScheduleProgressSource.Manual),
                null);
        }

        return null;
    }

    public static string SourceName(ScheduleProgressSource source) => source switch
    {
        ScheduleProgressSource.BoqItem => "Saha raporu (icmal satırı)",
        ScheduleProgressSource.Section => "Saha raporu (icmal kısmı)",
        ScheduleProgressSource.Children => "Alt aktivitelerden",
        ScheduleProgressSource.Manual => "Elle girildi",
        _ => "Ölçülemiyor"
    };

    /// <summary>
    /// Bütünün yüzdesi. Kalem bazında oran 100'ü aşabilir (sözleşme
    /// üstü imalat) ama "işin %130'u bitti" anlamsız bir cümledir.
    /// </summary>
    private static decimal Clamp(decimal rate) =>
        rate < 0m ? 0m : rate > 100m ? 100m : rate;
}
