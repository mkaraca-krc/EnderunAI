using EnderunAI.Api.Models;
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
    ///   1. İŞ EMRİ bir merkez taşımalı. Hatırlatma taşımak zorunda
    ///      değil — bkz. <paramref name="kind"/>.
    ///   2. <c>CenterType</c> hangi alanın dolu olduğuyla ÇELİŞMEMELİ —
    ///      tür seçimden türer, ayrıca elle girilmez.
    ///   3. Şantiye seçildiyse projesi de gelmeli ve şantiye o projeye
    ///      ait olmalı. (Aidiyet kontrolü veritabanı gerektirdiği için
    ///      <paramref name="santiyeninProjesi"/> ile dışarıdan verilir.)
    /// </summary>
    /// <returns>Hata mesajı; kural sağlanıyorsa <c>null</c>.</returns>
    /// <param name="kind">
    /// KAYDIN TÜRÜ — MERKEZ ZORUNLULUĞUNU BU BELİRLER.
    ///
    /// Masraf merkezi, muhasebenin masrafı YAZACAĞI yeri söyler.
    /// Kişisel bir hatırlatmanın masrafı yoktur; ondan merkez
    /// istemek, var olmayan bir soruya cevap zorlamaktır.
    ///
    /// ── BU KURAL BİR KEZ YANLIŞ KONDU, ÖLÇÜM DÜZELTTİ ──
    ///
    /// Önce "merkez çağıranın şubesinden türesin, şubesi yoksa Hızır
    /// hatırlatma açamasın" denmişti. ÖLÇÜLDÜ (2026-09-04, canlı):
    /// aktif şube 1, `user_data_scopes` içinde `BranchId` dolu satır
    /// SIFIR, yani şube kapsamı olan kullanıcı 0/13. O kural
    /// özelliği 13 kullanıcının 13'ünde de öldürürdü.
    ///
    /// Karar değişti (Mehmet Karacabey, 2026-09-04): zorunluluk
    /// VERİ DURUMUNA değil kaydın TÜRÜNE bağlanır. Veri durumuna
    /// bağlanan bir kural, veri değiştiği gün sessizce başka bir
    /// kural olur.
    /// </param>
    public static string? Dogrula(
        WorkTaskKind kind,
        Guid? projectId,
        Guid? branchId,
        Guid? projectSiteId,
        ExpenseCenterType? centerType,
        Guid? santiyeninProjesi)
    {
        /*
         * ═══ KAÇIŞ KAPATILDI — KURAL-KATMAN/1 (2026-09-04) ═══
         *
         * Burada şu vardı:
         *
         *     if (!string.IsNullOrWhiteSpace(sourceModule))
         *         return null;
         *
         * Yani DOLU herhangi bir dizge, merkez kuralının tamamını
         * atlıyordu. Kuralın kendi yorumu bunu zaten söylüyordu:
         * "dizgeye bakan kural kural değildir".
         *
         * ── NEDEN ŞİMDİ VE NEDEN GÜVENLE ──
         *
         * Muafiyetin gerekçesi "hakediş ya da mal kabul üzerinden
         * doğan görevin merkezi kaynak kaydından türetilebilir"di.
         * ÖLÇÜLDÜ (2026-09-04, canlı): o vaka HİÇ GERÇEKLEŞMEMİŞ.
         * Üç görevin `SourceModule` dağılımı `MANUAL × 2`, `(boş) × 1`.
         *
         * Yani kaçış, kurulduğu sebep için bir kez bile kullanılmadı;
         * kullanan tek şey ön yüzün kendi işareti olan `MANUAL` oldu —
         * ki o, tam olarak MUAF OLMAMASI gereken durum. Kaçış,
         * korumayı kaldırdığı yerde koruma gerekiyordu.
         *
         * ── GÖREV AÇAN İKİ YER VAR, İKİSİ DE ETKİLENMİYOR ──
         *
         * `WorkTasksController` (ön yüz her zaman merkez gönderiyor)
         * ve `HizirActionTools` (denetleyiciyi hiç görmüyor, bu kural
         * ona zaten uygulanmıyor). Hiçbir arka uç servisi gerçek bir
         * modül adıyla görev açmıyor — muhasebe fişlerindeki
         * `SourceModule` alanları BAŞKA bir varlığa ait.
         *
         * ── GELECEKTE BİR MODÜL GÖREV AÇARSA ──
         *
         * Merkezi KENDİSİ verecek. Kaynak kaydını okuyan taraf, o
         * kaydın projesini/şubesini de biliyor; bilmiyorsa merkez
         * gerçekten belirsiz demektir ve o zaman muafiyet değil, bir
         * karar gerekir.
         *
         * `sourceModule` parametresi de KALDIRILDI: dursaydı biri
         * günün birinde yeniden bir dizge kontrolü yazardı. Kaldırılan
         * bir kaçış, kullanılmayan bir kaçıştan güvenlidir.
         */

        var secilenler = new[] { projectId, branchId, projectSiteId }
            .Count(x => x.HasValue);

        if (secilenler == 0)
        {
            /*
             * HATIRLATMADA MERKEZ ARANMAZ — VE BU, KURALIN GERİ
             * KALANINI KAPSAM DIŞI BIRAKMAZ.
             *
             * Yalnız ZORUNLULUK türe bağlı. Hatırlatma bir merkez
             * GÖNDERİRSE aşağıdaki çelişki ve aidiyet denetimleri
             * ona da uygulanır: "zorunlu değil" ile "denetlenmez"
             * aynı şey olsaydı, tür alanı kuralın tamamını atlatan
             * yeni bir kaçış olurdu — kapattığımız `sourceModule`
             * kaçışının aynısı.
             */
            if (kind == WorkTaskKind.Hatirlatma)
                return null;

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
