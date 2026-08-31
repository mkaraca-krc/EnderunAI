using EnderunAI.Api.Models.Expenses;

namespace EnderunAI.Api.Services.Common;

/// <summary>
/// MASRAF MERKEZİ KURALI — TEK KAYNAK.
///
/// NEDEN AYRI SINIF: kural önce yalnız `WorkTasksController`'ın POST
/// gövdesinde yaşıyordu. Ölçüldü — Hızır (`HizirActionTools`) doğrudan
/// `db.WorkTasks.Add(...)` yazıyor ve denetleyiciyi HİÇ görmüyor; PUT ise
/// merkez alanlarına hiç dokunmuyordu. Yani ortada iki kapı değil,
/// bir kapı ve bir açık duvar vardı.
///
/// Bu sınıf kuralı tek yere topluyor. Bu pakette POST ve PUT çağırıyor;
/// Hızır yolu KURAL-KATMAN/1'de bağlanacak (bilinçli kapsam kararı,
/// aşağıdaki nota bakınız).
///
/// AÇIK KALAN KAPI — BU PAKETTE KAPANMIYOR:
/// `SourceModule` dolu olan istekler kuralın dışında kalmaya devam
/// ediyor. Ön yüz artık her zaman merkez gönderdiği için bu kaçış
/// fiilen kullanılmıyor, ama KAPI KAPANMIŞ DEĞİL. Kapanması, kuralın
/// dizgeye değil kaydın TÜRÜNE bakmasıyla olacak (KURAL-KATMAN/1).
/// </summary>
public static class MasrafMerkeziKurali
{
    /// <summary>
    /// Merkez seçiminin kendi içinde tutarlı olduğunu doğrular.
    ///
    /// ÜÇ İDDİA:
    ///   1. Kayda bağlı olmayan iş emri bir merkez taşımalı.
    ///   2. <c>CenterType</c> hangi alanın dolu olduğuyla ÇELİŞMEMELİ —
    ///      tür seçimden türer, ayrıca elle girilmez.
    ///   3. Şantiye seçildiyse projesi de gelmeli ve şantiye o projeye
    ///      ait olmalı. (Aidiyet kontrolü veritabanı gerektirdiği için
    ///      <paramref name="santiyeninProjesi"/> ile dışarıdan verilir.)
    /// </summary>
    /// <returns>Hata mesajı; kural sağlanıyorsa <c>null</c>.</returns>
    public static string? Dogrula(
        Guid? projectId,
        Guid? branchId,
        Guid? projectSiteId,
        ExpenseCenterType? centerType,
        string? sourceModule,
        Guid? santiyeninProjesi)
    {
        /*
         * KAYDA BAĞLI GÖREV MUAF — ŞİMDİLİK.
         *
         * Hakediş ya da mal kabul üzerinden doğan bir görevin merkezi
         * kaynak kaydından türetilebiliyor. Ama bu muafiyet bir DİZGEYE
         * bakıyor ve dizgeye bakan kural kural değildir: yarın eklenen
         * her modül adı aynı kaçışı yeniden açar. Kaydın türüne bağlanması
         * KURAL-KATMAN/1'in işi.
         */
        if (!string.IsNullOrWhiteSpace(sourceModule))
            return null;

        var secilenler = new[] { projectId, branchId, projectSiteId }
            .Count(x => x.HasValue);

        if (secilenler == 0)
        {
            return "Masraf merkezi zorunludur: proje, şube ya da şantiye seçin.";
        }

        if (secilenler > 1 && !(projectSiteId.HasValue && projectId.HasValue && branchId is null))
        {
            /*
             * ŞANTİYE + PROJE BİRLİKTE MEŞRUDUR: şantiye zaten bir
             * projenin altında yaşıyor ve ikisi birlikte saklanıyor.
             * Diğer her çoklu seçim çelişkidir.
             */
            return "Tek bir masraf merkezi seçilebilir: proje, şube ya da şantiye.";
        }

        var beklenenTur = projectSiteId.HasValue
            ? ExpenseCenterType.ProjectSite
            : branchId.HasValue
                ? ExpenseCenterType.Branch
                : ExpenseCenterType.Project;

        if (centerType.HasValue && centerType.Value != beklenenTur)
        {
            /*
             * TÜR SEÇİMDEN TÜRER.
             *
             * Bugün `CenterType` istekten olduğu gibi alınıyordu ve hangi
             * alanın dolu olduğuyla karşılaştırılmıyordu: `Project` yazıp
             * `BranchId` doldurmak mümkündü ve kimse itiraz etmezdi.
             * İki kaynak birbiriyle çelişirse hangisinin doğru olduğunu
             * kimse bilemez — o yüzden çelişki REDDEDİLİR.
             */
            return "Masraf merkezi türü seçilen merkezle uyuşmuyor.";
        }

        if (projectSiteId.HasValue)
        {
            if (!projectId.HasValue)
                return "Şantiye seçildiğinde projesi de gönderilmelidir.";

            if (santiyeninProjesi is null)
                return "Seçilen şantiye bulunamadı.";

            if (santiyeninProjesi.Value != projectId.Value)
                return "Seçilen şantiye, seçilen projeye ait değil.";
        }

        return null;
    }

    /// <summary>
    /// Seçimden türeyen tür. Kayıt yazılırken bu kullanılır; istekten
    /// gelen değer yalnızca ÇELİŞKİ KONTROLÜ için okunur, saklanmaz.
    /// </summary>
    public static ExpenseCenterType? TuruTuret(
        Guid? projectId,
        Guid? branchId,
        Guid? projectSiteId)
    {
        if (projectSiteId.HasValue) return ExpenseCenterType.ProjectSite;
        if (branchId.HasValue) return ExpenseCenterType.Branch;
        if (projectId.HasValue) return ExpenseCenterType.Project;
        return null;
    }
}
