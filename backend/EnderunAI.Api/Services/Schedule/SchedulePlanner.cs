namespace EnderunAI.Api.Services.Schedule;

/// <summary>
/// Bağımlılık türü. Dördü de destekleniyor; sahada en yaygın olanı
/// <see cref="FinishToStart"/> olduğu için varsayılan odur.
/// </summary>
public enum ScheduleDependencyType
{
    /// <summary>Bitir-Başla: öncül bitmeden ardıl başlamaz.</summary>
    FinishToStart = 0,

    /// <summary>Başla-Başla: öncül başlamadan ardıl başlamaz.</summary>
    StartToStart = 1,

    /// <summary>Bitir-Bitir: öncül bitmeden ardıl bitemez.</summary>
    FinishToFinish = 2,

    /// <summary>Başla-Bitir: öncül başlamadan ardıl bitemez.</summary>
    StartToFinish = 3
}

/// <summary>Motora verilen aktivite: kullanıcının girdiği plan tarihleri.</summary>
/// <param name="Start">Kullanıcının girdiği planlanan başlangıç. Bağımlılık
/// bunu yalnızca İLERİ iter, asla öne çekmez — elle konmuş bir tarihin
/// sistem tarafından öne alınması, bilinçli bırakılmış boşlukları
/// (malzeme bekleme, işveren onayı) sessizce yok ederdi.</param>
public sealed record ScheduleActivityInput(
    Guid Id,
    string Name,
    DateOnly Start,
    DateOnly Finish);

/// <param name="LagWorkDays">Gecikme payı, çalışma günü. Negatif olabilir
/// (örtüşme): "duvar bitmeden 3 gün önce kablo çekimine başlanır".</param>
public sealed record ScheduleDependencyInput(
    Guid PredecessorId,
    Guid SuccessorId,
    ScheduleDependencyType Type = ScheduleDependencyType.FinishToStart,
    int LagWorkDays = 0);

/// <param name="TotalFloatWorkDays">Bolluk: proje bitişini ötelemeden
/// kaç çalışma günü gecikilebilir. Sıfır ve altı = kritik yol.</param>
/// <param name="ShiftedWorkDays">Bağımlılığın girilen tarihi kaç gün
/// ilerlettiği. Sıfırdan büyükse plan tarihi artık kullanıcının
/// yazdığı tarih değildir; ekran bunu göstermeli.</param>
public sealed record ScheduledActivity(
    Guid Id,
    string Name,
    DateOnly Start,
    DateOnly Finish,
    DateOnly LateStart,
    DateOnly LateFinish,
    int DurationWorkDays,
    int TotalFloatWorkDays,
    bool IsCritical,
    int ShiftedWorkDays);

/// <param name="DeadlineFloatWorkDays">Termine kalan bolluk. Negatifse
/// plan zaten termini aşıyor — henüz hiç gecikme olmadan.</param>
public sealed record SchedulePlan(
    IReadOnlyList<ScheduledActivity> Activities,
    DateOnly ProjectStart,
    DateOnly ProjectFinish,
    IReadOnlyList<Guid> CriticalActivityIds,
    DateOnly? Deadline,
    int? DeadlineFloatWorkDays,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Döngüsel bağımlılık. Plan tanımsız hale geldiği için hesap
/// yapılmaz; çağıran bunu 400 olarak yüzeye çıkarır.
/// </summary>
public sealed class ScheduleCycleException(string message, IReadOnlyList<Guid> cycle)
    : InvalidOperationException(message)
{
    public IReadOnlyList<Guid> Cycle { get; } = cycle;
}

/// <summary>
/// Kritik yol motoru (CPM): ileri geçiş, geri geçiş, bolluk ve kritik
/// yol.
///
/// Saf ve veritabanısız — doğruluğu elle hesaplanmış bir ağla
/// karşılaştırılarak test edilebilsin diye.
///
/// TEK YÖNLÜ KAYDIRMA: bağımlılık bir aktiviteyi yalnızca ileri iter.
/// Bu hem iş gerçeğidir hem de hesabı tekrarlı (idempotent) yapar:
/// aynı plan iki kez hesaplandığında tarihler oynamaz.
/// </summary>
public static class SchedulePlanner
{
    /// <summary>
    /// Döngü arar. Bulursa Türkçe mesaj döner, yoksa null.
    /// Bağımlılık kaydedilmeden ÖNCE çağrılır: döngü veritabanına hiç
    /// girmemeli, çünkü giren döngü bütün ekranı hesaplanamaz yapar.
    /// </summary>
    public static string? FindCycle(
        IReadOnlyCollection<ScheduleActivityInput> activities,
        IReadOnlyCollection<ScheduleDependencyInput> dependencies)
    {
        var names = activities.ToDictionary(x => x.Id, x => x.Name);
        var cycle = DetectCycle(activities, dependencies);

        if (cycle.Count == 0)
            return null;

        var chain = string.Join(" → ", cycle.Select(id =>
            names.TryGetValue(id, out var name) ? name : id.ToString()));

        return $"Döngüsel bağımlılık: {chain}. Bir aktivite dolaylı da olsa " +
               "kendisini bekleyemez.";
    }

    /// <summary>
    /// Planı hesaplar.
    /// </summary>
    /// <param name="deadline">İşverenin dayattığı termin. Verilirse geri
    /// geçiş bu tarihten başlar ve plan termini aşıyorsa bolluklar
    /// NEGATİFE düşer — yani gecikme daha yaşanmadan görünür.</param>
    /// <exception cref="ScheduleCycleException"/>
    public static SchedulePlan Build(
        ScheduleCalendar calendar,
        IReadOnlyCollection<ScheduleActivityInput> activities,
        IReadOnlyCollection<ScheduleDependencyInput> dependencies,
        DateOnly? deadline = null)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        var warnings = new List<string>();

        if (activities.Count == 0)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return new SchedulePlan([], today, today, [], deadline, null,
                ["İş programında aktivite yok."]);
        }

        var byId = activities.ToDictionary(x => x.Id);
        var links = Normalize(dependencies, byId, warnings);

        var cycle = DetectCycle(activities, links);

        if (cycle.Count > 0)
        {
            throw new ScheduleCycleException(
                FindCycle(activities, links)!, cycle);
        }

        var order = TopologicalOrder(activities, links);

        var duration = activities.ToDictionary(
            x => x.Id, x => calendar.DurationOf(x.Start, x.Finish));

        foreach (var activity in activities.Where(x => x.Finish < x.Start))
        {
            warnings.Add(
                $"\"{activity.Name}\" aktivitesinin bitişi başlangıcından önce; " +
                "süre bir gün kabul edildi.");
        }

        // --- İleri geçiş: en erken başlangıç / bitiş ---
        var incoming = links.ToLookup(x => x.SuccessorId);
        var start = new Dictionary<Guid, DateOnly>();
        var finish = new Dictionary<Guid, DateOnly>();

        foreach (var id in order)
        {
            var activity = byId[id];
            var days = duration[id];

            var earliestStart = calendar.NextWorkDay(activity.Start);
            DateOnly? finishConstraint = null;

            foreach (var link in incoming[id])
            {
                var predecessorStart = start[link.PredecessorId];
                var predecessorFinish = finish[link.PredecessorId];

                switch (link.Type)
                {
                    case ScheduleDependencyType.FinishToStart:
                        earliestStart = Max(earliestStart, calendar.AddWorkDays(
                            predecessorFinish, link.LagWorkDays + 1));
                        break;

                    case ScheduleDependencyType.StartToStart:
                        earliestStart = Max(earliestStart, calendar.AddWorkDays(
                            predecessorStart, link.LagWorkDays));
                        break;

                    case ScheduleDependencyType.FinishToFinish:
                        finishConstraint = Max(finishConstraint, calendar.AddWorkDays(
                            predecessorFinish, link.LagWorkDays));
                        break;

                    case ScheduleDependencyType.StartToFinish:
                        finishConstraint = Max(finishConstraint, calendar.AddWorkDays(
                            predecessorStart, link.LagWorkDays));
                        break;
                }
            }

            var resolvedStart = earliestStart;
            var resolvedFinish = calendar.FinishFromStart(resolvedStart, days);

            // Bitişi zorlayan bağ (FF/SF) başlangıçtan güçlüyse başlangıç
            // geri hesaplanır — süre korunur.
            if (finishConstraint is DateOnly required && required > resolvedFinish)
            {
                resolvedFinish = required;
                resolvedStart = calendar.StartFromFinish(required, days);
            }

            start[id] = resolvedStart;
            finish[id] = resolvedFinish;
        }

        var projectStart = start.Values.Min();
        var projectFinish = finish.Values.Max();

        // --- Geri geçiş: en geç başlangıç / bitiş ---
        var outgoing = links.ToLookup(x => x.PredecessorId);
        var lateFinish = new Dictionary<Guid, DateOnly>();
        var lateStart = new Dictionary<Guid, DateOnly>();

        // Termin planlanan bitişten ÖNCEYSE geri geçişin çıpası odur ve
        // bütün zincirin bolluğu negatife düşer — gecikme daha
        // yaşanmadan görünür.
        //
        // Termin sonraysa çıpa plan bitişinde kalır: aksi halde bol
        // terminli bir projede hiçbir aktivite kritik çıkmaz ve kritik
        // yol ekranı boşalırdı. Termin bolluğu ayrıca raporlanıyor.
        var anchor = deadline is DateOnly due
            ? Min(calendar.PreviousWorkDay(due), projectFinish)
            : projectFinish;

        // Enumerable.Reverse: List<T>.Reverse() yerinde çevirir ve void
        // döner; sıra bozulurdu.
        foreach (var id in Enumerable.Reverse(order))
        {
            var days = duration[id];
            DateOnly? latest = null;

            foreach (var link in outgoing[id])
            {
                var successorLateStart = lateStart[link.SuccessorId];
                var successorLateFinish = lateFinish[link.SuccessorId];

                var candidate = link.Type switch
                {
                    ScheduleDependencyType.FinishToStart =>
                        calendar.AddWorkDays(successorLateStart, -(link.LagWorkDays + 1)),

                    ScheduleDependencyType.StartToStart =>
                        calendar.FinishFromStart(
                            calendar.AddWorkDays(successorLateStart, -link.LagWorkDays),
                            days),

                    ScheduleDependencyType.FinishToFinish =>
                        calendar.AddWorkDays(successorLateFinish, -link.LagWorkDays),

                    _ => calendar.FinishFromStart(
                        calendar.AddWorkDays(successorLateFinish, -link.LagWorkDays),
                        days)
                };

                latest = Min(latest, candidate);
            }

            var resolved = latest ?? anchor;

            lateFinish[id] = resolved;
            lateStart[id] = calendar.StartFromFinish(resolved, days);
        }

        var results = new List<ScheduledActivity>(activities.Count);

        foreach (var activity in activities)
        {
            var id = activity.Id;
            var float_ = calendar.WorkDayOffset(start[id], lateStart[id]);

            results.Add(new ScheduledActivity(
                Id: id,
                Name: activity.Name,
                Start: start[id],
                Finish: finish[id],
                LateStart: lateStart[id],
                LateFinish: lateFinish[id],
                DurationWorkDays: duration[id],
                TotalFloatWorkDays: float_,
                IsCritical: float_ <= 0,
                ShiftedWorkDays: Math.Max(
                    0, calendar.WorkDayOffset(activity.Start, start[id]))));
        }

        var ordered = results
            .OrderBy(x => x.Start)
            .ThenBy(x => x.Finish)
            .ThenBy(x => x.Name, StringComparer.CurrentCulture)
            .ToList();

        var criticalIds = ordered
            .Where(x => x.IsCritical)
            .Select(x => x.Id)
            .ToList();

        int? deadlineFloat = deadline is DateOnly limit
            ? calendar.WorkDayOffset(projectFinish, limit)
            : null;

        if (deadlineFloat is < 0)
        {
            warnings.Add(
                $"Planlanan bitiş ({projectFinish:dd.MM.yyyy}) termini " +
                $"{Math.Abs(deadlineFloat.Value)} iş günü aşıyor — plan bu " +
                "haliyle bile terminde bitmiyor.");
        }

        return new SchedulePlan(
            Activities: ordered,
            ProjectStart: projectStart,
            ProjectFinish: projectFinish,
            CriticalActivityIds: criticalIds,
            Deadline: deadline,
            DeadlineFloatWorkDays: deadlineFloat,
            Warnings: warnings);
    }

    /// <summary>
    /// Kendine bağ ve tanınmayan aktivite referanslarını ayıklar.
    /// Bunlar hesabı bozar ama veriyi reddetmek yerine uyarıyla
    /// geçmek doğru: tek bozuk satır yüzünden bütün program
    /// görünmez olmamalı.
    /// </summary>
    private static List<ScheduleDependencyInput> Normalize(
        IReadOnlyCollection<ScheduleDependencyInput> dependencies,
        IReadOnlyDictionary<Guid, ScheduleActivityInput> byId,
        List<string> warnings)
    {
        var result = new List<ScheduleDependencyInput>(dependencies.Count);

        foreach (var link in dependencies)
        {
            if (!byId.ContainsKey(link.PredecessorId) ||
                !byId.ContainsKey(link.SuccessorId))
            {
                warnings.Add(
                    "Programda bulunmayan bir aktiviteye bağ var; yok sayıldı.");
                continue;
            }

            if (link.PredecessorId == link.SuccessorId)
            {
                warnings.Add(
                    $"\"{byId[link.SuccessorId].Name}\" kendisine bağlanmış; " +
                    "bu bağ yok sayıldı.");
                continue;
            }

            result.Add(link);
        }

        return result;
    }

    /// <summary>
    /// Kahn algoritması. Sıraya giremeyen düğümler döngüdedir; aralarından
    /// bir tanesi izlenerek okunabilir bir zincir çıkarılır.
    /// </summary>
    private static List<Guid> DetectCycle(
        IReadOnlyCollection<ScheduleActivityInput> activities,
        IReadOnlyCollection<ScheduleDependencyInput> dependencies)
    {
        var valid = dependencies
            .Where(x => x.PredecessorId != x.SuccessorId)
            .ToList();

        var remaining = Remaining(activities, valid);

        if (remaining.Count == 0)
            return [];

        // Kalanlar içinde bir çevrim izle: her düğümden bir ardıla git,
        // daha önce görülene varınca çevrim kapanmıştır.
        var next = valid
            .Where(x => remaining.Contains(x.PredecessorId) &&
                        remaining.Contains(x.SuccessorId))
            .GroupBy(x => x.PredecessorId)
            .ToDictionary(g => g.Key, g => g.First().SuccessorId);

        var current = remaining.First();
        var path = new List<Guid>();
        var seen = new Dictionary<Guid, int>();

        while (!seen.ContainsKey(current))
        {
            seen[current] = path.Count;
            path.Add(current);

            if (!next.TryGetValue(current, out var successor))
                return remaining.ToList();

            current = successor;
        }

        var chain = path.Skip(seen[current]).ToList();
        chain.Add(current);

        return chain;
    }

    private static HashSet<Guid> Remaining(
        IReadOnlyCollection<ScheduleActivityInput> activities,
        IReadOnlyCollection<ScheduleDependencyInput> dependencies)
    {
        var pending = activities.ToDictionary(
            x => x.Id,
            x => dependencies.Count(d => d.SuccessorId == x.Id));

        var successors = dependencies.ToLookup(x => x.PredecessorId);

        var ready = new Queue<Guid>(
            pending.Where(x => x.Value == 0).Select(x => x.Key));

        var settled = new HashSet<Guid>();

        while (ready.Count > 0)
        {
            var id = ready.Dequeue();
            settled.Add(id);

            foreach (var link in successors[id])
            {
                if (--pending[link.SuccessorId] == 0)
                    ready.Enqueue(link.SuccessorId);
            }
        }

        return activities
            .Select(x => x.Id)
            .Where(x => !settled.Contains(x))
            .ToHashSet();
    }

    private static List<Guid> TopologicalOrder(
        IReadOnlyCollection<ScheduleActivityInput> activities,
        IReadOnlyCollection<ScheduleDependencyInput> dependencies)
    {
        var pending = activities.ToDictionary(
            x => x.Id,
            x => dependencies.Count(d => d.SuccessorId == x.Id));

        var successors = dependencies.ToLookup(x => x.PredecessorId);

        // Girdi sırası korunsun diye kuyruk yerine sıralı liste: aynı
        // program iki kez hesaplandığında aynı sırayı üretmeli.
        var ready = new List<Guid>(
            activities.Where(x => pending[x.Id] == 0).Select(x => x.Id));

        var order = new List<Guid>(activities.Count);

        while (ready.Count > 0)
        {
            var id = ready[0];
            ready.RemoveAt(0);
            order.Add(id);

            foreach (var link in successors[id])
            {
                if (--pending[link.SuccessorId] == 0)
                    ready.Add(link.SuccessorId);
            }
        }

        return order;
    }

    private static DateOnly Max(DateOnly left, DateOnly right) =>
        left >= right ? left : right;

    private static DateOnly Max(DateOnly? left, DateOnly right) =>
        left is DateOnly value && value >= right ? value : right;

    private static DateOnly Min(DateOnly? left, DateOnly right) =>
        left is DateOnly value && value <= right ? value : right;
}
