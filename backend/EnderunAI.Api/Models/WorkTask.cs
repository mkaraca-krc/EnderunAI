using EnderunAI.Api.Models.Expenses;

namespace EnderunAI.Api.Models;

public enum WorkTaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

/// <summary>
/// GÖREV DURUMLARI.
///
/// AKIŞ:
///   Açık -> Devam ediyor -> Tamamlandı (yapan) -> Onaylandı (gönderen)
///                                              -> İade edildi (gönderen) -> Açık
///
/// ÇİFT ADIMLI KAPANIŞ: yapanın "bitti" demesi görevi kapatmaz;
/// gönderen onaylayınca kapanır. Gönderen kendine görev açtıysa tek
/// adımda kapanır — kendini onaylatmak anlamsız bir tören olurdu.
///
/// KALDIRILAN İKİ DURUM (2026-08-23):
///
///   `Draft = 0` — kodda hiç kullanılmıyordu. M1'de görev ya açılır ya
///   açılmaz; taslak görev gelen kutusunu bulandırırdı ("bana atandı"
///   dediğin şey henüz gönderilmemiş olabilirdi).
///
///   `Waiting = 3` — tek kullanımı "açık sayılan durumlar" listesiydi.
///   Cazip görünüyor ama tehlikeli: "bekliyor" KİMİN İŞİ olduğunu
///   belirsizleştirir. Bekleme zaten `Completed` (top göndarende) ve
///   `Returned` (top yapanda) ile temsil ediliyor; üçüncü bir bekleme
///   hangi tarafın topu tuttuğunu gizlerdi.
///
/// SAYILAR KAYMIYOR: kaldırılan iki değerin yerine yeni durum
/// konmadı. Veritabanında `0` ya da `3` yazan bir satır kalsaydı
/// sessizce başka bir duruma dönüşürdü.
/// </summary>
public enum WorkTaskStatus
{
    Open = 1,
    InProgress = 2,

    /// <summary>Yapan bitirdi; GÖNDERENİN onayı bekleniyor.</summary>
    Completed = 4,

    Cancelled = 5,

    /// <summary>Gönderen onayladı — görev kapandı.</summary>
    Approved = 6,

    /// <summary>
    /// Gönderen iade etti, GEREKÇESİYLE. Görev yapana geri döner.
    /// Bu durum kalıcı değil: iade edilen görev `Open`'a çevrilir ve
    /// iade sayısı bir artar. Ayrı bir durum olarak DURMASI, "iade
    /// edildi ama henüz görülmedi" anını temsil ediyor.
    /// </summary>
    Returned = 7
}

public sealed class WorkTask : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid? ProjectId { get; set; }

    public string TaskNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public WorkTaskPriority Priority { get; set; } = WorkTaskPriority.Normal;
    public WorkTaskStatus Status { get; set; } = WorkTaskStatus.Open;

    public Guid? AssignedToUserId { get; set; }
    public Guid? AssignedByUserId { get; set; }

    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public string? CompletionNote { get; set; }

    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }

    /// <summary>
    /// KAYDA BAĞLI GÖREV: hangi modülün hangi kaydı.
    /// Boşsa görev SERBEST'tir ve o zaman masraf merkezi zorunlu olur.
    /// </summary>
    public string? SourceModule { get; set; }
    public Guid? SourceEntityId { get; set; }
    public string? SourceEventCode { get; set; }
    public string? Tags { get; set; }

    // ---------------------------------------------------------------
    // ÇİFT ADIMLI KAPANIŞ
    // ---------------------------------------------------------------

    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }

    public DateTime? ReturnedAtUtc { get; set; }
    public Guid? ReturnedByUserId { get; set; }

    /// <summary>İade gerekçesi — ZORUNLU. Gerekçesiz iade, sessiz bir
    /// "beğenmedim"dir ve yapan neyi düzelteceğini bilemez.</summary>
    public string? ReturnReason { get; set; }

    /// <summary>
    /// Kaç kez iade edildi. GÖREVDE GÖRÜNÜR: üçüncü kez iade edilen
    /// bir iş, tek seferde biten bir işle aynı satırda görünmemeli.
    /// </summary>
    public int ReturnCount { get; set; }

    // ---------------------------------------------------------------
    // DEVRETME İZİ
    // ---------------------------------------------------------------

    /// <summary>
    /// Görev devredildiyse ÖNCEKİ sorumlu. Devretme denetime yazılır;
    /// bu alanlar ekranda "kimden geldi" sorusunu cevaplıyor.
    ///
    /// Yalnız SON devretme tutuluyor: tam zincir denetim kaydında.
    /// Kayıt üzerinde zincir tutmak, tabloyu bir olay defterine
    /// çevirirdi — o iş audit_logs'un.
    /// </summary>
    public Guid? DelegatedFromUserId { get; set; }
    public DateTime? DelegatedAtUtc { get; set; }
    public int DelegationCount { get; set; }

    // ---------------------------------------------------------------
    // MASRAF MERKEZİ — SERBEST GÖREVDE ZORUNLU
    // ---------------------------------------------------------------

    /*
     * NEDEN ZORUNLU: masraf merkezi olmayan serbest görev, faaliyet
     * raporunda karşılığı olmayan iş demektir. Ay sonunda "bu emek
     * nereye gitti" sorusunun cevabı olmaz.
     *
     * NEDEN AYNI DESEN: gider kaydı (ExpenseEntry) zaten
     * CenterType + Branch/Project/ProjectSite üçlüsünü kullanıyor.
     * Göreve ikinci bir "masraf merkezi" kavramı uydurmak, aynı
     * soruyu iki ayrı biçimde cevaplayan iki tablo yaratırdı.
     *
     * KAYDA BAĞLI GÖREVDE ZORUNLU DEĞİL: merkez kaydın kendisinden
     * türetilebiliyor (hakediş -> proje, mal kabul -> depo/şube).
     */
    public ExpenseCenterType? CenterType { get; set; }

    public Guid? BranchId { get; set; }
    public Guid? ProjectSiteId { get; set; }
}
