namespace EnderunAI.Api.Services.Fleet;

/// <summary>
/// Aracın dönem içinde bulunduğu tek bir aralık.
/// <see cref="ProjectId"/> boşsa araç o günlerde MERKEZ HAVUZUNDAYDI.
/// </summary>
public sealed record VehicleAllocationSegment(
    Guid? ProjectId,
    DateTime Start,
    /// <summary>Dahil — bu gün de araç oradaydı.</summary>
    DateTime End)
{
    public int Days => (int)(End.Date - Start.Date).TotalDays + 1;
}

public sealed record VehicleCostAllocationLine(
    Guid? ProjectId,
    int Days,
    /// <summary>Gün payı — yüzde (bilgi amaçlı, tutar bundan türetilmez).</summary>
    decimal SharePercent,
    decimal Amount);

/// <summary>
/// DÖNEMSEL ARAÇ MASRAFININ GÜN ORANIYLA BÖLÜŞTÜRÜLMESİ — kira,
/// sigorta, kasko, MTV.
///
/// Saf ve statik: veritabanı yok. Bölüştürme SUNUCUDA yapılır;
/// istemcide yapılsaydı iki ekran iki farklı dağıtım üretebilirdi.
///
/// TOPLAM DAİMA %100 KAPANIR. Yüzdeler yuvarlandığı için payların
/// toplamı tutardan sapabilir; fark KAYBOLMAZ, en büyük paya yazılır.
/// Kuruş farkı sessizce düşseydi gider merkezi raporu ile ödenen tutar
/// birbirini tutmazdı ve fark her dönem büyürdü.
///
/// BOŞ GÜNLER MERKEZE YAZILIR: araç dönemin bir kısmında hiçbir
/// projeye atanmamışsa o günler merkez havuzunun payıdır. Atlanılsaydı
/// gün toplamı dönemi kapatmaz ve tutarın bir kısmı hiçbir merkeze
/// düşmezdi.
/// </summary>
public static class VehicleCostAllocationCalculator
{
    public static IReadOnlyList<VehicleCostAllocationLine> Allocate(
        IReadOnlyList<VehicleAllocationSegment> segments,
        decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Bölüştürülecek tutar sıfırdan büyük olmalıdır.");

        if (segments.Count == 0)
            throw new ArgumentException("Bölüştürme için gün aralığı yok.");

        // Aynı projeye ait aralıklar TEK SATIRDA toplanır: araç aynı
        // projeye dönem içinde iki kez uğradıysa iki ayrı satır değil,
        // toplam gün üzerinden tek pay çıkar.
        var byProject = segments
            .GroupBy(x => x.ProjectId)
            .Select(x => new { ProjectId = x.Key, Days = x.Sum(y => y.Days) })
            .Where(x => x.Days > 0)
            .OrderByDescending(x => x.Days)
            .ThenBy(x => x.ProjectId)
            .ToList();

        var totalDays = byProject.Sum(x => x.Days);

        if (totalDays <= 0)
            throw new ArgumentException("Bölüştürme için gün aralığı yok.");

        var lines = byProject
            .Select(x => new VehicleCostAllocationLine(
                x.ProjectId,
                x.Days,
                decimal.Round(100m * x.Days / totalDays, 4),
                decimal.Round(amount * x.Days / totalDays, 2)))
            .ToList();

        var difference = amount - lines.Sum(x => x.Amount);

        if (difference != 0m)
        {
            // Fark EN BÜYÜK PAYA yazılır: oransal olarak en az sapmayı
            // orası yaratır ve fark hep aynı yerde toplanır, dağılmaz.
            lines[0] = lines[0] with { Amount = lines[0].Amount + difference };
        }

        return lines;
    }

    /// <summary>
    /// Atamalardan dönem içindeki aralıkları çıkarır. Atamanın dışında
    /// kalan günler MERKEZ (ProjectId = null) sayılır.
    ///
    /// GÜN GÜN İLERLER: dönem en fazla birkaç yüz gündür ve aralık
    /// sınırlarını kapalı formülle çıkarmak (atama başlangıçları,
    /// bitişleri, dönem sınırları) kolayca yanlış yazılabilen bir
    /// sınır aritmetiği gerektiriyordu. Basit döngü hem doğru hem
    /// okunur.
    /// </summary>
    public static IReadOnlyList<VehicleAllocationSegment> BuildSegments(
        IReadOnlyList<(Guid? ProjectId, DateTime Start, DateTime? End)> assignments,
        DateTime periodStart,
        DateTime periodEnd)
    {
        var first = periodStart.Date;
        var last = periodEnd.Date;

        if (last < first)
            throw new ArgumentException("Dönem bitişi başlangıcından önce olamaz.");

        // Atamanın BİTİŞ GÜNÜ dahil değil: o gün araç artık yeni
        // yerdedir (V1'deki tarih sorgusunun aynı sınırı).
        Guid? ProjectOn(DateTime day) => assignments
            .Where(x => x.Start.Date <= day && (x.End is null || x.End.Value.Date > day))
            .OrderByDescending(x => x.Start)
            .Select(x => x.ProjectId)
            .FirstOrDefault();

        var segments = new List<VehicleAllocationSegment>();

        var segmentProject = ProjectOn(first);
        var segmentStart = first;

        for (var day = first.AddDays(1); day <= last; day = day.AddDays(1))
        {
            var project = ProjectOn(day);

            if (project == segmentProject)
                continue;

            segments.Add(new VehicleAllocationSegment(
                segmentProject, segmentStart, day.AddDays(-1)));

            segmentProject = project;
            segmentStart = day;
        }

        segments.Add(new VehicleAllocationSegment(segmentProject, segmentStart, last));

        return segments;
    }
}
