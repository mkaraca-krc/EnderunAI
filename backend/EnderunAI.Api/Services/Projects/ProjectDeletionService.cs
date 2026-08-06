using System.Text.Json;
using EnderunAI.Api.Contracts.Projects;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Projects;

public sealed record ProjectDeletionOutcome(bool Success, string Message);

public interface IProjectDeletionService
{
    /// <summary>Silme öncesi bağlı kayıt özeti ve kalıcı silme izni.</summary>
    Task<ProjectDeletionImpact?> GetImpactAsync(
        Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Arşive alır (yumuşak silme): veriler durur, proje aktif listelerden
    /// düşer. Kesinleşmiş kaydı olan projeler için tek güvenli yol budur.
    /// </summary>
    Task<ProjectDeletionOutcome> ArchiveAsync(
        Guid projectId,
        string? reason,
        Guid? actorUserId,
        string? actorUsername,
        CancellationToken cancellationToken = default);

    Task<ProjectDeletionOutcome> UnarchiveAsync(
        Guid projectId,
        Guid? actorUserId,
        string? actorUsername,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kalıcı siler. Yalnızca hiçbir kesinleşmiş kayıt yoksa çalışır;
    /// onay için projenin kodunun birebir yazılması gerekir.
    /// </summary>
    Task<ProjectDeletionOutcome> HardDeleteAsync(
        Guid projectId,
        string confirmationCode,
        Guid? actorUserId,
        string? actorUsername,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// İki kademeli proje silme.
///
/// Kademe 1 — kesinleşmiş kayıt VARSA: kalıcı silme yasak, yalnızca arşiv.
/// Kesinleşmiş sayılanlar: kesinleşmiş muhasebe fişi satırı, taslak dışı
/// hakediş, onaylı alış faturası, kesinleşmiş satış faturası, çek (ve çek
/// dağılımı), kasa hareketi, stok hareketi, faktoring, barter ve ödeme
/// talebi kayıtları. Puantaj ve bordro maliyeti de bilinçli olarak
/// engelleyiciye alındı: bunlar gerçek personelin ücret geçmişi, projeyle
/// birlikte silinmeleri kabul edilemez.
///
/// Kademe 2 — kesinleşmiş kayıt YOKSA: bağlı kayıtlar tek transaction
/// içinde, yabancı anahtar sırasına göre temizlenip proje silinir.
///
/// Fiziksel temizlik ham SQL ile yapılır; EF'in global yumuşak silme
/// filtresi gizli satırları atlar ve artık haritalanmayan eski tablolar
/// (PaymentRequests, ProjectDailyReports, ProjectLaborEntries,
/// progress_payment_deduction_rules) EF üzerinden hiç görünmez — oysa
/// veritabanında yabancı anahtarları duruyor ve silmeyi bloke ediyorlar.
/// </summary>
public sealed class ProjectDeletionService(
    AppDbContext db,
    IProjectFileCleaner fileCleaner,
    ILogger<ProjectDeletionService> logger) : IProjectDeletionService
{
    public async Task<ProjectDeletionImpact?> GetImpactAsync(
        Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await db.Projects
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == projectId, cancellationToken);

        if (project is null)
            return null;

        var blockers = await CollectBlockersAsync(projectId, cancellationToken);
        var dependencies = await CollectDependenciesAsync(projectId, cancellationToken);

        var documents = await db.ProjectDocuments
            .IgnoreQueryFilters()
            .Where(x => x.ProjectId == projectId)
            .Select(x => x.SizeBytes)
            .ToListAsync(cancellationToken);

        return new ProjectDeletionImpact(
            project.Id,
            project.Code,
            project.Name,
            project.IsArchived,
            CanHardDelete: blockers.Count == 0,
            blockers,
            dependencies,
            TotalBlockingRecords: blockers.Sum(x => x.Count),
            TotalDependentRecords: dependencies.Sum(x => x.Count),
            DocumentCount: documents.Count,
            DocumentSizeBytes: documents.Sum());
    }

    public async Task<ProjectDeletionOutcome> ArchiveAsync(
        Guid projectId,
        string? reason,
        Guid? actorUserId,
        string? actorUsername,
        CancellationToken cancellationToken = default)
    {
        var project = await db.Projects
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == projectId, cancellationToken);

        if (project is null)
            return new ProjectDeletionOutcome(false, "Proje bulunamadı.");

        if (project.IsArchived)
            return new ProjectDeletionOutcome(false, "Proje zaten arşivde.");

        project.IsArchived = true;
        project.ArchivedAtUtc = DateTime.UtcNow;
        project.ArchiveReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        project.Status = ProjectStatus.Cancelled;

        WriteAudit(actorUserId, actorUsername, "Project.Archive", project, new
        {
            project.Code,
            project.Name,
            reason = project.ArchiveReason
        });

        await db.SaveChangesAsync(cancellationToken);

        return new ProjectDeletionOutcome(
            true, $"{project.Code} projesi arşive alındı. Kayıtları raporlarda durmaya devam ediyor.");
    }

    public async Task<ProjectDeletionOutcome> UnarchiveAsync(
        Guid projectId,
        Guid? actorUserId,
        string? actorUsername,
        CancellationToken cancellationToken = default)
    {
        var project = await db.Projects
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == projectId, cancellationToken);

        if (project is null)
            return new ProjectDeletionOutcome(false, "Proje bulunamadı.");

        if (!project.IsArchived)
            return new ProjectDeletionOutcome(false, "Proje zaten arşivde değil.");

        project.IsArchived = false;
        project.ArchivedAtUtc = null;
        project.ArchiveReason = null;
        project.Status = ProjectStatus.Active;

        WriteAudit(actorUserId, actorUsername, "Project.Unarchive", project, new
        {
            project.Code,
            project.Name
        });

        await db.SaveChangesAsync(cancellationToken);

        return new ProjectDeletionOutcome(true, $"{project.Code} projesi arşivden çıkarıldı.");
    }

    public async Task<ProjectDeletionOutcome> HardDeleteAsync(
        Guid projectId,
        string confirmationCode,
        Guid? actorUserId,
        string? actorUsername,
        CancellationToken cancellationToken = default)
    {
        var project = await db.Projects
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == projectId, cancellationToken);

        if (project is null)
            return new ProjectDeletionOutcome(false, "Proje bulunamadı.");

        if (!string.Equals(
                confirmationCode?.Trim(), project.Code, StringComparison.OrdinalIgnoreCase))
        {
            return new ProjectDeletionOutcome(
                false, $"Onay için proje kodunu birebir yazmalısınız: {project.Code}");
        }

        var blockers = await CollectBlockersAsync(projectId, cancellationToken);
        if (blockers.Count > 0)
        {
            var detail = string.Join(", ", blockers.Select(x => $"{x.Label} ({x.Count})"));
            return new ProjectDeletionOutcome(
                false,
                "Projeye bağlı kesinleşmiş kayıtlar olduğu için kalıcı silme yapılamaz: " +
                $"{detail}. Bunun yerine projeyi arşive alabilirsiniz.");
        }

        var dependencies = await CollectDependenciesAsync(projectId, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await PurgeDependentsAsync(projectId, cancellationToken);

            var deleted = await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM projects WHERE \"Id\" = {0}", [projectId], cancellationToken);

            if (deleted == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ProjectDeletionOutcome(false, "Proje silinemedi.");
            }

            db.SecurityAuditEvents.Add(new SecurityAuditEvent
            {
                ActorUserId = actorUserId,
                ActorUsername = actorUsername,
                Action = "Project.HardDelete",
                EntityType = "Project",
                EntityId = projectId,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    project.Code,
                    project.Name,
                    project.CompanyId,
                    dependencies = dependencies
                        .Select(x => new { x.Key, x.Label, x.Count })
                        .ToList()
                }),
                OccurredAtUtc = DateTime.UtcNow
            });

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(ex, "Proje kalıcı silme başarısız: {ProjectId}", projectId);

            return new ProjectDeletionOutcome(
                false,
                "Projeye bağlı beklenmeyen bir kayıt silmeyi engelledi, hiçbir şey silinmedi. " +
                $"Teknik ayrıntı: {ex.GetBaseException().Message}");
        }

        // Dosyalar veritabanı işlemi başarıyla tamamlandıktan sonra silinir;
        // tersi sırada transaction geri alınsa bile dosyalar gitmiş olurdu.
        var removedFiles = fileCleaner.DeleteProjectFiles(projectId);

        return new ProjectDeletionOutcome(
            true,
            $"{project.Code} projesi kalıcı olarak silindi." +
            (removedFiles ? " Yüklenen dosyaları da diskten kaldırıldı." : string.Empty));
    }

    private async Task<List<ProjectDeletionBlocker>> CollectBlockersAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        var blockers = new List<ProjectDeletionBlocker>();

        void Add(string key, string label, int count, string reason)
        {
            if (count > 0)
                blockers.Add(new ProjectDeletionBlocker(key, label, count, reason));
        }

        Add("postedVoucherLines", "Kesinleşmiş muhasebe fişi satırı",
            await db.AccountingVoucherLines
                .IgnoreQueryFilters()
                .CountAsync(
                    x => x.ProjectId == projectId
                         && x.AccountingVoucher.Status == AccountingVoucherStatus.Posted,
                    cancellationToken),
            "Kesinleşmiş fiş silinemez; yasal defter kaydıdır.");

        Add("progressPayments", "Taslak dışı hakediş",
            await db.ProgressPayments
                .IgnoreQueryFilters()
                .CountAsync(
                    x => x.ProjectId == projectId && x.Status != ProgressPaymentStatus.Draft,
                    cancellationToken),
            "Onaya çıkmış veya kesinleşmiş hakediş silinemez.");

        Add("supplierInvoices", "Onaylı alış/gider faturası",
            await db.SupplierInvoices
                .IgnoreQueryFilters()
                .CountAsync(
                    x => x.ProjectId == projectId && x.Status == SupplierInvoiceStatus.Approved,
                    cancellationToken),
            "Onaylı fatura cari ve muhasebe kaydı üretmiştir.");

        Add("salesInvoices", "Kesinleşmiş satış faturası",
            await db.SalesInvoices
                .IgnoreQueryFilters()
                .CountAsync(
                    x => x.ProjectId == projectId && x.Status == SalesInvoiceStatus.Posted,
                    cancellationToken),
            "Kesinleşmiş satış faturası gelir kaydı üretmiştir.");

        Add("cheques", "Çek",
            await db.Cheques
                .IgnoreQueryFilters()
                .CountAsync(x => x.ProjectId == projectId, cancellationToken),
            "Çek defteri kaydı projeyle birlikte silinemez.");

        Add("chequeAllocations", "Çek dağılımı",
            await db.ChequeAllocations
                .IgnoreQueryFilters()
                .CountAsync(x => x.ProjectId == projectId, cancellationToken),
            "Bu projeye pay verilmiş çek dağılımı var.");

        Add("cashTransactions", "Kasa/banka hareketi",
            await db.CashTransactions
                .IgnoreQueryFilters()
                .CountAsync(x => x.ProjectId == projectId, cancellationToken),
            "Gerçekleşmiş para hareketi silinemez.");

        Add("stockMovements", "Stok hareketi",
            await db.StockMovements
                .IgnoreQueryFilters()
                .CountAsync(x => x.ProjectId == projectId, cancellationToken),
            "Stok giriş/çıkış geçmişi silinemez.");

        Add("factoringTransactions", "Faktoring işlemi",
            await db.FactoringTransactions
                .IgnoreQueryFilters()
                .CountAsync(x => x.ProjectId == projectId, cancellationToken),
            "Faktoring kaydı finansal yükümlülüktür.");

        Add("barterLedgerEntries", "Barter kaydı",
            await db.BarterLedgerEntries
                .IgnoreQueryFilters()
                .CountAsync(x => x.ProjectId == projectId, cancellationToken),
            "Barter defteri kaydı silinemez.");

        Add("paymentRequests", "Ödeme talebi",
            await CountRawAsync("\"PaymentRequests\" t", projectId, cancellationToken),
            "Ödeme talebi kaydı silinemez.");

        Add("attendanceRecords", "Puantaj kaydı",
            await CountRawAsync(
                "attendance_records ar JOIN project_sites ps ON ps.\"Id\" = ar.\"ProjectSiteId\"",
                projectId, cancellationToken, "ps"),
            "Puantaj gerçek personelin ücret geçmişidir, projeyle silinemez.");

        Add("laborCosts", "Bordro işçilik maliyeti",
            await CountRawAsync(
                "hr_project_labor_costs lc JOIN project_sites ps ON ps.\"Id\" = lc.\"ProjectSiteId\"",
                projectId, cancellationToken, "ps"),
            "Bordrodan gelen işçilik maliyeti kaydı silinemez.");

        Add("assetAssignments", "Zimmet kaydı",
            await CountRawAsync(
                "hr_asset_assignments aa JOIN warehouses w ON w.\"Id\" = aa.\"WarehouseId\"",
                projectId, cancellationToken, "w"),
            "Projenin deposu üzerinden açılmış personel zimmeti var.");

        return blockers;
    }

    private async Task<List<ProjectDeletionDependency>> CollectDependenciesAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        var dependencies = new List<ProjectDeletionDependency>();

        async Task AddAsync(string key, string label, string fromClause, string alias = "t")
        {
            var count = await CountRawAsync(fromClause, projectId, cancellationToken, alias);
            if (count > 0)
                dependencies.Add(new ProjectDeletionDependency(key, label, count));
        }

        await AddAsync("draftVoucherLines", "Taslak muhasebe fişi satırı", "accounting_voucher_lines t");
        await AddAsync("progressPaymentDrafts", "Taslak hakediş", "progress_payments t");
        await AddAsync("supplierInvoiceDrafts", "Taslak/iptal alış faturası", "supplier_invoices t");
        await AddAsync("salesInvoiceDrafts", "Taslak/iptal satış faturası", "sales_invoices t");
        await AddAsync("boqs", "Keşif/icmal", "project_boqs t");
        await AddAsync("measurements", "Metraj", "project_measurements t");
        await AddAsync("extraWorks", "İlave iş", "project_extra_works t");
        await AddAsync("hakedisSections", "Hakediş bölümü", "project_hakedis_sections t");
        await AddAsync("sites", "Şantiye", "project_sites t");
        await AddAsync("warehouses", "Depo", "warehouses t");
        await AddAsync("documents", "Proje dosyası", "project_documents t");
        await AddAsync("costTransactions", "Maliyet hareketi", "\"ProjectCostTransactions\" t");
        await AddAsync("purchaseRequests", "Satın alma talebi", "purchase_requests t");
        await AddAsync("purchaseOrders", "Satın alma siparişi", "purchase_orders t");
        await AddAsync("offers", "Teklif", "offers t");
        await AddAsync("stockReservations", "Stok rezervasyonu", "stock_reservations t");
        await AddAsync("personnelAssignments", "Personel ataması", "personnel_assignments t");
        await AddAsync("isgSiteDocuments", "İSG saha dokümanı", "isg_site_documents t");
        await AddAsync("isgIncidents", "İSG olayı", "isg_incidents t");
        await AddAsync("employerPortalLinks", "İşveren portal bağlantısı", "employer_portal_links t");
        await AddAsync("priceDifferenceProfiles", "Fiyat farkı profili", "price_difference_profiles t");
        await AddAsync("deductionRules", "Hakediş kesinti kuralı", "progress_payment_deduction_rules t");
        await AddAsync("userDataScopes", "Kullanıcı veri kapsamı", "user_data_scopes t");
        await AddAsync("dailyReports", "Günlük rapor (eski tablo)", "\"ProjectDailyReports\" t");
        await AddAsync("laborEntries", "İşçilik girişi (eski tablo)", "\"ProjectLaborEntries\" t");

        return dependencies;
    }

    private async Task<int> CountRawAsync(
        string fromClause,
        Guid projectId,
        CancellationToken cancellationToken,
        string alias = "t")
    {
        // Tablo adları kod içinde sabit; parametre olarak yalnızca proje kimliği geçer.
        var sql = $"SELECT count(*)::int AS \"Value\" FROM {fromClause} WHERE {alias}.\"ProjectId\" = {{0}}";

        return await db.Database
            .SqlQueryRaw<int>(sql, projectId)
            .SingleAsync(cancellationToken);
    }

    /// <summary>
    /// Bağlı kayıtları yabancı anahtar sırasına göre siler: torunlar,
    /// çocuklar, sonra projenin kendisi (çağıran tarafından).
    /// </summary>
    private async Task PurgeDependentsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        // Kesinleşmiş kayıt olmadığı doğrulandı; buradaki her şey taslak,
        // tanım veya planlama kaydıdır.
        var statements = new[]
        {
            // Muhasebe: yalnızca taslak fiş satırları kalmış olabilir.
            "DELETE FROM accounting_voucher_lines WHERE \"ProjectId\" = {0}",

            // Hakediş ve keşif zinciri (çocukları CASCADE ile gider).
            "DELETE FROM progress_payment_advance_material_offsets o USING progress_payments p" +
            " WHERE o.\"ProgressPaymentId\" = p.\"Id\" AND p.\"ProjectId\" = {0}",
            "DELETE FROM progress_payments WHERE \"ProjectId\" = {0}",
            "DELETE FROM project_extra_works WHERE \"ProjectId\" = {0}",
            "DELETE FROM project_measurements WHERE \"ProjectId\" = {0}",
            "DELETE FROM project_boqs WHERE \"ProjectId\" = {0}",
            "DELETE FROM project_hakedis_sections WHERE \"ProjectId\" = {0}",
            "DELETE FROM progress_payment_deduction_rules WHERE \"ProjectId\" = {0}",
            "DELETE FROM price_difference_profiles WHERE \"ProjectId\" = {0}",

            // Faturalar: taslak/iptal olanlar kaldı. Depolardan önce silinmeli,
            // çünkü fatura ve fatura satırı depoya bağlanabiliyor.
            "DELETE FROM supplier_invoice_items i USING supplier_invoices s" +
            " WHERE i.\"SupplierInvoiceId\" = s.\"Id\" AND s.\"ProjectId\" = {0}",
            "DELETE FROM supplier_invoice_items i USING warehouses w" +
            " WHERE i.\"WarehouseId\" = w.\"Id\" AND w.\"ProjectId\" = {0}",
            "DELETE FROM supplier_invoices WHERE \"ProjectId\" = {0}",
            "UPDATE supplier_invoices SET \"WarehouseId\" = NULL FROM warehouses w" +
            " WHERE supplier_invoices.\"WarehouseId\" = w.\"Id\" AND w.\"ProjectId\" = {0}",
            "UPDATE sales_invoices SET \"ProjectId\" = NULL WHERE \"ProjectId\" = {0}",

            // Stok rezervasyonları: hem satın alma talebine hem depoya bağlı,
            // ikisinden de önce gitmeli.
            "DELETE FROM stock_reservations WHERE \"ProjectId\" = {0}",
            "DELETE FROM stock_reservations sr USING warehouses w" +
            " WHERE sr.\"WarehouseId\" = w.\"Id\" AND w.\"ProjectId\" = {0}",
            "DELETE FROM stock_reservations sr USING purchase_requests pr" +
            " WHERE sr.\"PurchaseRequestId\" = pr.\"Id\" AND pr.\"ProjectId\" = {0}",

            // Satın alma zinciri: mal kabul → sipariş → RFQ → talep.
            "DELETE FROM goods_receipt_items gi USING goods_receipts g, purchase_orders po" +
            " WHERE gi.\"GoodsReceiptId\" = g.\"Id\" AND g.\"PurchaseOrderId\" = po.\"Id\"" +
            " AND po.\"ProjectId\" = {0}",
            "DELETE FROM goods_receipts g USING purchase_orders po" +
            " WHERE g.\"PurchaseOrderId\" = po.\"Id\" AND po.\"ProjectId\" = {0}",
            "DELETE FROM goods_receipt_items gi USING goods_receipts g, warehouses w" +
            " WHERE gi.\"GoodsReceiptId\" = g.\"Id\" AND g.\"WarehouseId\" = w.\"Id\"" +
            " AND w.\"ProjectId\" = {0}",
            "DELETE FROM goods_receipts g USING warehouses w" +
            " WHERE g.\"WarehouseId\" = w.\"Id\" AND w.\"ProjectId\" = {0}",
            "DELETE FROM purchase_orders WHERE \"ProjectId\" = {0}",
            "DELETE FROM rfq_supplier_quotation_items qi USING rfq_supplier_quotations q," +
            " rfq_suppliers rs, rfqs r, purchase_requests pr" +
            " WHERE qi.\"RfqSupplierQuotationId\" = q.\"Id\" AND q.\"RfqSupplierId\" = rs.\"Id\"" +
            " AND rs.\"RfqId\" = r.\"Id\" AND r.\"PurchaseRequestId\" = pr.\"Id\"" +
            " AND pr.\"ProjectId\" = {0}",
            "DELETE FROM rfq_supplier_quotations q USING rfq_suppliers rs, rfqs r," +
            " purchase_requests pr" +
            " WHERE q.\"RfqSupplierId\" = rs.\"Id\" AND rs.\"RfqId\" = r.\"Id\"" +
            " AND r.\"PurchaseRequestId\" = pr.\"Id\" AND pr.\"ProjectId\" = {0}",
            "DELETE FROM rfq_suppliers rs USING rfqs r, purchase_requests pr" +
            " WHERE rs.\"RfqId\" = r.\"Id\" AND r.\"PurchaseRequestId\" = pr.\"Id\"" +
            " AND pr.\"ProjectId\" = {0}",
            "DELETE FROM rfq_items ri USING rfqs r, purchase_requests pr" +
            " WHERE ri.\"RfqId\" = r.\"Id\" AND r.\"PurchaseRequestId\" = pr.\"Id\"" +
            " AND pr.\"ProjectId\" = {0}",
            "DELETE FROM rfqs r USING purchase_requests pr" +
            " WHERE r.\"PurchaseRequestId\" = pr.\"Id\" AND pr.\"ProjectId\" = {0}",
            "DELETE FROM purchase_requests WHERE \"ProjectId\" = {0}",
            "DELETE FROM offers WHERE \"ProjectId\" = {0}",

            // Depo.
            "DELETE FROM warehouse_stocks ws USING warehouses w" +
            " WHERE ws.\"WarehouseId\" = w.\"Id\" AND w.\"ProjectId\" = {0}",
            "DELETE FROM warehouses WHERE \"ProjectId\" = {0}",

            // Maliyet ve saha kayıtları.
            "DELETE FROM \"ProjectCostTransactions\" WHERE \"ProjectId\" = {0}",
            "DELETE FROM \"ProjectDailyReports\" WHERE \"ProjectId\" = {0}",
            "DELETE FROM \"ProjectLaborEntries\" WHERE \"ProjectId\" = {0}",
            "DELETE FROM \"PaymentRequests\" WHERE \"ProjectId\" = {0}",

            // Şantiye ve altındaki kayıtlar.
            "DELETE FROM project_site_daily_report_photos p USING project_site_daily_reports d," +
            " project_sites s WHERE p.\"DailyReportId\" = d.\"Id\"" +
            " AND d.\"ProjectSiteId\" = s.\"Id\" AND s.\"ProjectId\" = {0}",
            "DELETE FROM project_site_daily_report_work_items wi USING project_site_daily_reports d," +
            " project_sites s WHERE wi.\"DailyReportId\" = d.\"Id\"" +
            " AND d.\"ProjectSiteId\" = s.\"Id\" AND s.\"ProjectId\" = {0}",
            "DELETE FROM project_site_daily_reports d USING project_sites s" +
            " WHERE d.\"ProjectSiteId\" = s.\"Id\" AND s.\"ProjectId\" = {0}",
            "DELETE FROM project_site_assignments a USING project_sites s" +
            " WHERE a.\"ProjectSiteId\" = s.\"Id\" AND s.\"ProjectId\" = {0}",
            "DELETE FROM project_documents WHERE \"ProjectId\" = {0}",
            "DELETE FROM isg_site_documents WHERE \"ProjectId\" = {0}",
            "UPDATE isg_incidents SET \"ProjectId\" = NULL, \"ProjectSiteId\" = NULL" +
            " WHERE \"ProjectId\" = {0}",
            "DELETE FROM project_sites WHERE \"ProjectId\" = {0}",

            // Kalanlar.
            "DELETE FROM personnel_assignments WHERE \"ProjectId\" = {0}",
            "DELETE FROM employer_portal_links WHERE \"ProjectId\" = {0}",
            "DELETE FROM user_data_scopes WHERE \"ProjectId\" = {0}"
        };

        foreach (var sql in statements)
            await db.Database.ExecuteSqlRawAsync(sql, [projectId], cancellationToken);
    }

    private void WriteAudit(
        Guid? actorUserId, string? actorUsername, string action, Project project, object details)
    {
        db.SecurityAuditEvents.Add(new SecurityAuditEvent
        {
            ActorUserId = actorUserId,
            ActorUsername = actorUsername,
            Action = action,
            EntityType = "Project",
            EntityId = project.Id,
            DetailsJson = JsonSerializer.Serialize(details),
            OccurredAtUtc = DateTime.UtcNow
        });
    }
}
