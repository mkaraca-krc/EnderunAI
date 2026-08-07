using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Offers;

/// <summary>
/// Teklifin icmale aktarılma sonucu.
/// </summary>
/// <param name="ProjectBoqId">Oluşturulan icmal.</param>
/// <param name="BoqNumber">İcmal numarası.</param>
/// <param name="ItemCount">Aktarılan kalem sayısı.</param>
/// <param name="TotalAmount">İcmal toplamı.</param>
/// <param name="Warnings">Aktarımda dikkat edilmesi gerekenler.</param>
public sealed record OfferBoqTransferResult(
    Guid ProjectBoqId,
    string BoqNumber,
    int ItemCount,
    decimal TotalAmount,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Kazanılan teklifi projenin keşif icmaline (ProjectBoq) çevirir.
///
/// NEDEN AYRI KAYIT: teklif satış belgesidir, icmal ise hakedişin
/// referansıdır. Aynı tabloyu paylaşsalardı teklifte yapılan bir
/// düzeltme sözleşme metrajını sessizce değiştirirdi. Aktarım tek
/// yönlü ve tek seferliktir; sonrasında ikisi bağımsız yaşar.
///
/// FİYAT BİLEŞENLERİ birebir taşınır (malzeme/montaj/GG). Teklif
/// kaleminde bileşen girilmemişse tutarın tamamı malzemeye yazılır —
/// toplam değişmez, yalnızca dağılım varsayılana düşer ve bu uyarı
/// olarak raporlanır.
/// </summary>
public sealed class OfferBoqTransferService(AppDbContext db)
{
    /// <summary>
    /// Teklifi yeni bir icmale aktarır.
    /// </summary>
    /// <param name="offerId">Kaynak teklif.</param>
    /// <param name="projectId">Hedef proje; null ise teklifin projesi.</param>
    /// <param name="name">İcmal adı; boşsa teklif başlığı.</param>
    /// <param name="actorUserId">İşlemi yapan.</param>
    public async Task<OfferBoqTransferResult> TransferAsync(
        Guid offerId,
        Guid? projectId,
        string? name,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var offer = await db.Offers
            .Include(x => x.Items.OrderBy(item => item.LineNumber))
            .SingleOrDefaultAsync(x => x.Id == offerId, cancellationToken)
            ?? throw new KeyNotFoundException("Teklif bulunamadı.");

        if (offer.Items.Count == 0)
            throw new InvalidOperationException("Teklifte kalem yok; aktarılacak bir şey bulunmuyor.");

        var targetProjectId = projectId ?? offer.ProjectId
            ?? throw new InvalidOperationException(
                "Teklif bir projeye bağlı değil. Aktarım için proje seçin.");

        var project = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == targetProjectId)
            .Select(x => new { x.Id, x.Code, x.CompanyId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Proje bulunamadı.");

        if (project.CompanyId != offer.CompanyId)
        {
            throw new InvalidOperationException(
                "Teklif ile proje farklı şirketlere ait; aktarım yapılamaz.");
        }

        var warnings = new List<string>();

        // Aynı teklif ikinci kez aktarılırsa iki icmal doğar ve hangisinin
        // sözleşme olduğu belirsizleşir. Engellemiyoruz (revizyon meşru
        // bir ihtiyaç) ama sessiz de geçmiyoruz.
        var alreadyTransferred = await db.ProjectBoqs
            .AsNoTracking()
            .AnyAsync(x => x.SourceOfferId == offerId, cancellationToken);

        if (alreadyTransferred)
        {
            warnings.Add(
                "Bu teklif daha önce icmale aktarılmış. Yeni icmal ayrı bir " +
                "kayıt olarak açıldı; sözleşme referansının hangisi olduğunu " +
                "icmal ekranından işaretleyin.");
        }

        var boqNumber = await BuildBoqNumberAsync(
            project.CompanyId, project.Code, cancellationToken);

        var boq = new ProjectBoq
        {
            CompanyId = project.CompanyId,
            ProjectId = project.Id,
            BoqNumber = boqNumber,
            Name = string.IsNullOrWhiteSpace(name)
                ? $"{offer.Title} (teklif {offer.OfferNumber})"
                : name.Trim(),
            Status = ProjectBoqStatus.Draft,
            CurrencyCode = offer.Currency,
            IsCurrentRevision = true,
            SourceOfferId = offer.Id
        };

        var missingComponents = 0;
        var lineNumber = 0;

        foreach (var item in offer.Items.OrderBy(x => x.LineNumber))
        {
            lineNumber++;

            var componentTotal =
                item.MaterialUnitPrice + item.LaborUnitPrice + item.OverheadUnitPrice;

            decimal material, labor, overhead;

            if (componentTotal > 0m)
            {
                material = item.MaterialUnitPrice;
                labor = item.LaborUnitPrice;
                overhead = item.OverheadUnitPrice;
            }
            else
            {
                // Bileşen girilmemiş: tutarın tamamı malzemeye yazılır.
                // Toplam korunuyor, yalnızca dağılım varsayılana düşüyor.
                material = item.UnitSalesPrice;
                labor = 0m;
                overhead = 0m;
                missingComponents++;
            }

            // UnitPrice bileşenlerin TOPLAMIdır; teklifteki satış fiyatı
            // ile bileşenler çelişirse bileşenler esas alınır ve fark
            // uyarı olarak raporlanır.
            var unitPrice = material + labor + overhead;

            if (componentTotal > 0m &&
                Math.Abs(unitPrice - item.UnitSalesPrice) > 0.01m)
            {
                warnings.Add(
                    $"{lineNumber}. kalem: bileşen toplamı ({unitPrice:0.##}) " +
                    $"satış fiyatından ({item.UnitSalesPrice:0.##}) farklı; " +
                    "icmale bileşen toplamı yazıldı.");
            }

            boq.Items.Add(new ProjectBoqItem
            {
                LineNumber = lineNumber,
                EngineeringPositionId = item.EngineeringPositionId,
                PositionCode = item.PositionNumber ?? string.Empty,
                Description = item.Description,
                Unit = item.Unit,
                ContractQuantity = item.Quantity,
                MaterialUnitPrice = material,
                LaborUnitPrice = labor,
                OverheadUnitPrice = overhead,
                UnitPrice = unitPrice,
                TotalAmount = decimal.Round(unitPrice * item.Quantity, 2)
            });
        }

        boq.TotalAmount = decimal.Round(boq.Items.Sum(x => x.TotalAmount), 2);

        if (missingComponents > 0)
        {
            warnings.Add(
                $"{missingComponents} kalemde malzeme/montaj/GG ayrımı yoktu; " +
                "tutarın tamamı malzemeye yazıldı. İcmal ekranından " +
                "düzeltebilirsiniz.");
        }

        db.ProjectBoqs.Add(boq);
        await db.SaveChangesAsync(cancellationToken);

        return new OfferBoqTransferResult(
            boq.Id, boq.BoqNumber, boq.Items.Count, boq.TotalAmount, warnings);
    }

    /// <summary>
    /// Proje kodundan sıralı icmal numarası üretir.
    /// </summary>
    private async Task<string> BuildBoqNumberAsync(
        Guid companyId, string projectCode, CancellationToken cancellationToken)
    {
        var count = await db.ProjectBoqs
            .AsNoTracking()
            .CountAsync(x => x.CompanyId == companyId, cancellationToken);

        var prefix = string.IsNullOrWhiteSpace(projectCode) ? "ICMAL" : projectCode;

        for (var attempt = 1; attempt <= 50; attempt++)
        {
            var candidate = $"{prefix}-ICM-{count + attempt:D3}";

            var taken = await db.ProjectBoqs
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.BoqNumber == candidate,
                    cancellationToken);

            if (!taken)
                return candidate;
        }

        // Ardışık numara bulunamayacak kadar çakışma varsa benzersizliği
        // garanti eden bir son çare kullanılır; numara üretmeden kaydı
        // reddetmek kullanıcıyı çıkışsız bırakırdı.
        return $"{prefix}-ICM-{Guid.NewGuid():N}"[..24];
    }
}
