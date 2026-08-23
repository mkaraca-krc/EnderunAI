using System.Globalization;
using System.Text;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Hizir;

/// <summary>
/// Hızır'ın eylem araçları — Katman 2.
///
/// İKİ KADEME VAR, ÜÇÜNCÜSÜ YOK:
///
/// 1) Güvenli (<see cref="HizirToolTier.Safe"/>): doğrudan çalışır.
///    Ya hiçbir şey kaydetmez (taslak, öneri, liste) ya da yalnızca
///    çağıran kullanıcıyı etkiler (kendine hatırlatma).
///
/// 2) Onaylı (<see cref="HizirToolTier.RequiresApproval"/>): bu
///    araçların çalıştırıcısı iş servisini HİÇ ÇAĞIRMAZ. Yalnızca
///    bekleyen eylem kaydı üretir; yürütme, kullanıcının kendi
///    oturumuyla gelen ayrı bir HTTP ucundadır.
///
/// 3) Tehlikeli işlemler için kademe yoktur: para transferi/ödeme, çek
///    durum değişikliği, kasa/banka hareketi, herhangi bir silme,
///    bordro/muhasebe fişi kesinleştirme, yetki-rol-kullanıcı yönetimi
///    araç olarak TANIMLANMAZ. Modelin bu işlemlere giden bir kod yolu
///    bulunmaz. Bkz. HizirForbiddenActionTests.
/// </summary>
public sealed class HizirActionTools(
    AppDbContext db,
    IHizirPendingActionStore pendingActions,
    EnderunAI.Api.Services.DocumentNumbers.IDocumentNumberService documentNumbers)
{
    private const int RowLimit = 25;
    private static readonly CultureInfo Tr = new("tr-TR");

    public IReadOnlyList<HizirTool> Build() =>
    [
        // ---------- GÜVENLİ KADEME ----------

        new HizirTool(
            "taslak_hazirla",
            "İstenen belge için taslak metin hazırlar (teklif isteği, " +
            "fatura açıklaması, rapor özeti vb.). Hiçbir şey kaydetmez, " +
            "yalnızca kullanıcının kopyalayabileceği metni döndürür.",
            Schema(
                ("belge_turu", "string", "rfq | fatura | rapor | eposta"),
                ("konu", "string", "Taslağın konusu"),
                ("notlar", "string", "Eklenmesi istenen ayrıntılar")),
            null,
            PrepareDraftAsync),

        new HizirTool(
            "hatirlatma_olustur",
            "Kullanıcının KENDİSİNE bir hatırlatma/görev oluşturur. " +
            "Başkasına görev atayamaz.",
            Schema(
                ("baslik", "string", "Hatırlatma başlığı"),
                ("aciklama", "string", "Ayrıntı"),
                ("son_tarih", "string", "YYYY-AA-GG biçiminde son tarih")),
            null,
            CreateReminderAsync),

        new HizirTool(
            "personel_atama_onerisi",
            "Bir şantiye için uygun personel ÖNERİSİ üretir. Atama " +
            "YAPMAZ; yalnızca gerekçeli bir liste döndürür, atamayı " +
            "kullanıcı ilgili ekrandan kendisi yapar.",
            Schema(
                ("santiye", "string", "Şantiye adı veya kodu"),
                ("ihtiyac", "string", "Aranan nitelik/meslek")),
            PermissionCatalog.Keys.PersonnelView,
            SuggestAssignmentAsync),

        // ---------- ONAY GEREKTİREN KADEME ----------
        // Bu üçünün çalıştırıcısı yalnızca bekleyen eylem kaydı üretir.

        new HizirTool(
            "rfq_ac",
            "Bir satın alma talebinden teklif isteme (RFQ) süreci açar. " +
            "ONAY GEREKTİRİR: hazırlanır, kullanıcı onaylayınca çalışır.",
            Schema(
                ("satinalma_talep_no", "string", "Talep numarası"),
                ("baslik", "string", "RFQ başlığı"),
                ("son_teklif_tarihi", "string", "YYYY-AA-GG")),
            PermissionCatalog.Keys.PurchasingRfqCreate,
            (ctx, args, ct) => PrepareApprovalAsync(
                ctx, args, ct, "rfq_ac",
                PermissionCatalog.Keys.PurchasingRfqCreate,
                BuildRfqSummary),
            HizirToolTier.RequiresApproval),

        new HizirTool(
            "fatura_onaya_gonder",
            "Taslak durumdaki bir tedarikçi faturasını onaya gönderir. " +
            "ONAY GEREKTİRİR.",
            Schema(("fatura_no", "string", "Fatura numarası veya sistem numarası")),
            PermissionCatalog.Keys.AccountingEdit,
            (ctx, args, ct) => PrepareApprovalAsync(
                ctx, args, ct, "fatura_onaya_gonder",
                PermissionCatalog.Keys.AccountingEdit,
                BuildInvoiceSummary),
            HizirToolTier.RequiresApproval),

        new HizirTool(
            "eposta_gonder",
            "Sistemde KAYITLI bir adrese e-posta gönderir (cari, personel " +
            "veya kullanıcı e-postaları). Serbest adres kabul edilmez. " +
            "ONAY GEREKTİRİR.",
            Schema(
                ("alici", "string", "Sistemde kayıtlı kişi/cari adı veya e-postası"),
                ("konu", "string", "E-posta konusu"),
                ("mesaj", "string", "Gönderilecek metin")),
            PermissionCatalog.Keys.SecretariatView,
            (ctx, args, ct) => PrepareApprovalAsync(
                ctx, args, ct, "eposta_gonder",
                PermissionCatalog.Keys.SecretariatView,
                BuildEmailSummary),
            HizirToolTier.RequiresApproval)
    ];

    // ---------- Güvenli kademe uygulamaları ----------

    private Task<HizirToolOutcome> PrepareDraftAsync(
        HizirToolContext context,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken cancellationToken)
    {
        var kind = Text(args, "belge_turu") ?? "belge";
        var topic = Text(args, "konu") ?? "(konu belirtilmedi)";
        var notes = Text(args, "notlar");

        return Task.FromResult(new HizirToolOutcome(
            $"TASLAK ({kind}) — kaydedilmedi, yalnızca metin:\n" +
            $"Konu: {topic}\n" +
            (string.IsNullOrWhiteSpace(notes) ? "" : $"Notlar: {notes}\n") +
            "Bu metni ilgili ekrana kopyalayabilirsin. Taslağı sen yaz, " +
            "kullanıcının konusuna ve rolüne uygun, resmi bir dille."));
    }

    private async Task<HizirToolOutcome> CreateReminderAsync(
        HizirToolContext context,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken cancellationToken)
    {
        var title = Text(args, "baslik");
        if (string.IsNullOrWhiteSpace(title))
            return new HizirToolOutcome("HATA: Hatırlatma başlığı gerekli.", IsError: true);

        var companyId = await db.Companies
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (companyId == Guid.Empty)
            return new HizirToolOutcome("KAYIT YOK: Şirket bulunamadı.", IsError: true);

        DateTime? dueDate = null;
        if (DateTime.TryParse(Text(args, "son_tarih"), out var parsed))
            dueDate = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);

        /*
         * NUMARA MERKEZÎ ÜRETEÇTEN — YARIŞ HATASI KAPATILDI.
         *
         * Eskiden `db.WorkTasks.CountAsync() + 1` idi: iki eşzamanlı
         * istek aynı sayıyı okur ve AYNI görev numarasını alırdı.
         * Sayım ayrıca SİLİNMİŞ kayıtları saymadığı için numara
         * geriye de gidebiliyordu.
         *
         * `DocumentNumberService` numarayı tek bir SQL ifadesinde
         * üretiyor (INSERT ... ON CONFLICT DO UPDATE ... RETURNING);
         * artırım veritabanında atomik, kilit ya da yeniden deneme
         * gerekmiyor. MHS, VCK, TKL sıralarının hepsi aynı yerden
         * geçiyor.
         */
        var taskNumber = await documentNumbers.GenerateAsync(
            companyId, "WORK_TASK", "GRV", cancellationToken);

        db.WorkTasks.Add(new WorkTask
        {
            CompanyId = companyId,
            TaskNumber = taskNumber,
            Title = title.Trim(),
            Description = Text(args, "aciklama"),
            Priority = WorkTaskPriority.Normal,
            Status = WorkTaskStatus.Open,
            // Hatırlatma yalnızca çağıran kullanıcıya atanır; Hızır
            // üzerinden başkasına görev yüklenemez.
            AssignedToUserId = context.UserId,
            AssignedByUserId = context.UserId,
            DueDate = dueDate,
            CreatedByUserId = context.UserId
        });

        await db.SaveChangesAsync(cancellationToken);

        return new HizirToolOutcome(
            $"Hatırlatma oluşturuldu: \"{title.Trim()}\"" +
            (dueDate is null ? "" : $", son tarih {dueDate:dd.MM.yyyy}") +
            ". Görevler ekranından görebilirsin.");
    }

    private async Task<HizirToolOutcome> SuggestAssignmentAsync(
        HizirToolContext context,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken cancellationToken)
    {
        var need = Text(args, "ihtiyac");

        var query = db.Personnel
            .AsNoTracking()
            .Where(x => x.Status == PersonnelStatus.Active);

        if (!string.IsNullOrWhiteSpace(need))
        {
            var term = need.Trim().ToLowerInvariant();
            query = query.Where(x =>
                (x.JobTitle != null && x.JobTitle.ToLower().Contains(term)) ||
                (x.Profession != null && x.Profession.ToLower().Contains(term)));
        }

        var rows = await query
            .OrderBy(x => x.FirstName)
            .Take(RowLimit)
            .Select(x => new
            {
                x.EmployeeNumber,
                FullName = x.FirstName + " " + x.LastName,
                x.JobTitle,
                x.Profession,
                ActiveSites = x.SiteAssignments.Count(a => a.IsActive && !a.IsDeleted)
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return new HizirToolOutcome("KAYIT YOK: Ölçüte uyan aktif personel bulunamadı.");

        var builder = new StringBuilder(
            "ÖNERİ (atama YAPILMADI, yalnızca liste):\n");

        foreach (var row in rows)
        {
            builder.AppendLine(
                $"- {row.FullName} ({row.EmployeeNumber})" +
                $"{(row.JobTitle is null ? "" : $" | {row.JobTitle}")}" +
                $"{(row.Profession is null ? "" : $" | {row.Profession}")}" +
                $" | aktif şantiye ataması: {row.ActiveSites}");
        }

        builder.AppendLine(
            "Kullanıcıya bunun bir öneri olduğunu, atamayı Personel " +
            "ekranından kendisinin yapması gerektiğini söyle.");

        return new HizirToolOutcome(builder.ToString());
    }

    // ---------- Onaylı kademe: yalnızca kayıt üretir ----------

    /// <summary>
    /// Onay gerektiren eylemi HAZIRLAR. Dikkat: burada hiçbir iş servisi
    /// çağrılmaz, hiçbir kayıt değişmez. Yalnızca bekleyen eylem satırı
    /// yazılır ve özet SUNUCUDA argümanlardan üretilir.
    /// </summary>
    private async Task<HizirToolOutcome> PrepareApprovalAsync(
        HizirToolContext context,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken cancellationToken,
        string actionName,
        string requiredPermission,
        Func<IReadOnlyDictionary<string, object?>, string> summaryBuilder)
    {
        if (!context.Has(requiredPermission))
        {
            return new HizirToolOutcome(
                "YETKİSİZ: Kullanıcının bu eylemi yapma izni yok.", Denied: true);
        }

        var summary = summaryBuilder(args);

        var pending = await pendingActions.CreateAsync(
            context.UserId, actionName, args, summary, requiredPermission,
            cancellationToken);

        return new HizirToolOutcome(
            $"ONAY BEKLİYOR (id: {pending.Id}). Eylem HAZIRLANDI ama " +
            "HENÜZ YAPILMADI. Kullanıcıya şu özeti göster ve onay " +
            $"düğmesine basmasını iste:\n{summary}\n" +
            "Onaylandığını varsayma, kendin onaylayamazsın.");
    }

    private static string BuildRfqSummary(IReadOnlyDictionary<string, object?> args) =>
        "Teklif isteme (RFQ) süreci açılacak.\n" +
        $"- Satın alma talebi: {Text(args, "satinalma_talep_no") ?? "(belirtilmedi)"}\n" +
        $"- Başlık: {Text(args, "baslik") ?? "(belirtilmedi)"}\n" +
        $"- Son teklif tarihi: {Text(args, "son_teklif_tarihi") ?? "(belirtilmedi)"}";

    private static string BuildInvoiceSummary(IReadOnlyDictionary<string, object?> args) =>
        "Tedarikçi faturası onaya gönderilecek.\n" +
        $"- Fatura: {Text(args, "fatura_no") ?? "(belirtilmedi)"}";

    private static string BuildEmailSummary(IReadOnlyDictionary<string, object?> args) =>
        "E-posta gönderilecek.\n" +
        $"- Alıcı: {Text(args, "alici") ?? "(belirtilmedi)"}\n" +
        $"- Konu: {Text(args, "konu") ?? "(belirtilmedi)"}\n" +
        $"- Mesaj: {Truncate(Text(args, "mesaj"), 300)}";

    // ---------- Yardımcılar ----------

    private static string? Text(IReadOnlyDictionary<string, object?> args, string key) =>
        args.TryGetValue(key, out var value) && value is not null
            ? value.ToString()
            : null;

    private static string Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value)
            ? "(boş)"
            : value.Length <= max ? value : value[..max] + "...";

    private static object Schema(
        params (string Name, string Type, string Description)[] fields)
    {
        var properties = new Dictionary<string, object>();

        foreach (var field in fields)
        {
            properties[field.Name] = new
            {
                type = field.Type,
                description = field.Description
            };
        }

        return new
        {
            type = "object",
            properties,
            required = Array.Empty<string>()
        };
    }
}
