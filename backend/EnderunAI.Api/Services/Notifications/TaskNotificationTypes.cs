namespace EnderunAI.Api.Services.Notifications;

/// <summary>
/// GÖREV BİLDİRİM TİPLERİ — TEK TANIM.
///
/// Dizgi olarak dağıtılsaydı bir yerde "task.assigned", başka yerde
/// "task_assigned" yazılır ve mükerrer engeli (Type + SourceId +
/// PeriodKey) sessizce delinirdi.
/// </summary>
public static class TaskNotificationTypes
{
    // --- OLAY ANINDA YAZILANLAR (dört tanesi) ---

    /// <summary>Görev sana atandı.</summary>
    public const string Assigned = "task.assigned";

    /// <summary>Atadığın görev tamamlandı, onayını bekliyor.</summary>
    public const string Completed = "task.completed";

    /// <summary>Görevin iade edildi.</summary>
    public const string Returned = "task.returned";

    /// <summary>Bir yorumda @ ile anıldın.</summary>
    public const string Mentioned = "task.mentioned";

    // --- ZAMANA BAĞLI, TARAYICI YAZAR (iki tanesi) ---

    /*
     * BU İKİSİ OLAY DEĞİL, ZAMAN.
     *
     * Diğer dördü bir eylemin anında yazılıyor; bunlar "hiçbir şey
     * olmadı ama gün geçti" durumunu bildiriyor, o yüzden tarayıcı
     * gerekiyor.
     *
     * MÜKERRER ENGELİ: PeriodKey = TERMİN TARİHİ. Tarayıcı günde beş
     * kez koşsa da tek bildirim; termin değişirse PeriodKey değişir
     * ve yeni uyarı yazılabilir — eski uyarı kendiliğinden
     * geçersizleşir.
     */

    /// <summary>Termine bir gün kaldı.</summary>
    public const string DueSoon = "task.due_soon";

    /// <summary>Termin geçti.</summary>
    public const string Overdue = "task.overdue";
}
