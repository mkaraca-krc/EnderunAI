using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.DocumentNumbers;

public sealed class DocumentNumberService(AppDbContext db)
    : IDocumentNumberService
{
    public async Task<string> GenerateAsync(
        Guid companyId,
        string documentType,
        string prefix,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("Şirket bilgisi zorunludur.", nameof(companyId));

        if (string.IsNullOrWhiteSpace(documentType))
            throw new ArgumentException("Belge tipi zorunludur.", nameof(documentType));

        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("Belge ön eki zorunludur.", nameof(prefix));

        var normalizedType = documentType.Trim().ToUpperInvariant();
        var normalizedPrefix = prefix.Trim().ToUpperInvariant();
        var year = DateTime.UtcNow.Year;

        // Çağıran zaten bir transaction açtıysa (ör. fatura onayı: fiş
        // üretimi + maliyet kaydı tek atomik blokta) yenisini açmak
        // "connection is already in a transaction" hatası verir. Böyle
        // durumlarda mevcut transaction'a katılıp commit'i çağırana
        // bırakıyoruz.
        var ownsTransaction = db.Database.CurrentTransaction is null;
        var transaction = ownsTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var sequence = await db.DocumentNumberSequences
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId &&
                     x.DocumentType == normalizedType &&
                     x.Year == year,
                cancellationToken);

        if (sequence is null)
        {
            sequence = new DocumentNumberSequence
            {
                CompanyId = companyId,
                DocumentType = normalizedType,
                Prefix = normalizedPrefix,
                Year = year,
                LastNumber = 1,
                NumberLength = 6
            };

            db.DocumentNumberSequences.Add(sequence);
        }
        else
        {
            sequence.LastNumber++;
            sequence.Prefix = normalizedPrefix;
            sequence.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            await transaction.DisposeAsync();
        }

        return $"{sequence.Prefix}-{sequence.Year}-{sequence.LastNumber.ToString().PadLeft(sequence.NumberLength, '0')}";
    }
}
