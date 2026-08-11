using EnderunAI.Api.Contracts.Accounting;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.FinancialInstruments;

/// <summary>
/// Barter alacağının nakit akıştaki görünümü.
///
/// NAKİT DEĞİL: hakedişin barter kısmı zaten net tutardan DÜŞÜLÜYOR
/// (HakedisCalculationService: net = brüt − tevkifat − stopaj −
/// kesintiler; barter bir kesinti türü). Yani nakit sütunu bugün
/// zaten doğru. Eksik olan GÖRÜNÜRLÜKTÜ: hakedişin bir kısmının
/// nakde dönmeyeceği hiçbir yerde yazmıyordu.
///
/// Bu yüzden satır <see cref="CashFlowCertainty.NonCash"/> ile
/// geliyor: yürüyen bakiyeye girmiyor, ama "bu tutar mal/hizmet
/// olarak gelecek" diye görünüyor. Nakit sayılsaydı tablo, eline hiç
/// geçmeyecek bir parayı likidite gibi okurdu.
///
/// TESLİM ALINAN DÜŞER: bakiye = kesilen − teslim alınan. Karşılığı
/// gelmiş barter artık beklenen bir alacak değildir.
/// </summary>
public sealed class BarterInstrumentService(AppDbContext db) : IFinancialInstrumentSource
{
    public const string ReceivableKind = "BarterReceivable";

    public async Task<List<InstrumentCashLine>> GetCashLinesAsync(
        Guid companyId, DateTime from, DateTime to,
        CancellationToken cancellationToken)
    {
        // Kesintiler dönem içinde; teslim alımlar TÜM ZAMANLARDAN
        // düşülüyor: geçen ay teslim alınan bir daire, bu ayın
        // alacağını da kapatır.
        var deductions = await db.BarterLedgerEntries
            .AsNoTracking()
            .Where(x => x.Project.CompanyId == companyId &&
                        x.EntryType == BarterEntryType.Deduction &&
                        x.EntryDate >= from.Date && x.EntryDate <= to.Date)
            .Select(x => new
            {
                x.ProjectId,
                ProjectCode = x.Project.Code,
                ProjectName = x.Project.Name,
                x.EntryDate,
                x.Amount
            })
            .ToListAsync(cancellationToken);

        if (deductions.Count == 0)
            return [];

        var projectIds = deductions.Select(x => x.ProjectId).Distinct().ToList();

        var received = await db.BarterLedgerEntries
            .AsNoTracking()
            .Where(x => projectIds.Contains(x.ProjectId) &&
                        x.EntryType == BarterEntryType.Receipt)
            .GroupBy(x => x.ProjectId)
            .Select(g => new { ProjectId = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var receivedByProject = received.ToDictionary(x => x.ProjectId, x => x.Total);

        var lines = new List<InstrumentCashLine>();

        foreach (var group in deductions.GroupBy(x => x.ProjectId))
        {
            var remaining = receivedByProject.GetValueOrDefault(group.Key, 0m);

            // Teslim alınanlar en eski kesintiden başlayarak kapatılır:
            // barter defteri kalem eşleştirmesi tutmuyor, en makul
            // varsayım FIFO.
            foreach (var entry in group.OrderBy(x => x.EntryDate))
            {
                var open = entry.Amount;

                if (remaining > 0m)
                {
                    var applied = Math.Min(remaining, open);
                    open -= applied;
                    remaining -= applied;
                }

                if (open <= 0m)
                    continue;

                lines.Add(new InstrumentCashLine(
                    entry.EntryDate.Date,
                    entry.EntryDate.Date,
                    ReceivableKind,
                    "Barter alacağı",
                    $"{entry.ProjectName} — hakedişin mal/hizmet olarak alınacak kısmı",
                    decimal.Round(open, 2),
                    true,
                    CashFlowCertainty.NonCash,
                    entry.ProjectId,
                    entry.ProjectCode));
            }
        }

        return lines;
    }
}
