using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Engineering;

/// <summary>Bir satırın aktarımda ne yapacağı.</summary>
public enum PositionImportAction
{
    /// <summary>Hatalı — aktarılmayacak.</summary>
    Skip = 0,

    /// <summary>Yeni poz açılacak, fiyatı yazılacak.</summary>
    CreatePosition = 1,

    /// <summary>Poz zaten var; yalnızca bu yılın fiyatı eklenecek/güncellenecek.</summary>
    AddPrice = 2,

    /// <summary>Poz var ama tanımı/birimi dosyadakinden farklı; ikisi de güncellenecek.</summary>
    UpdatePositionAndPrice = 3
}

public sealed record PositionImportPreviewRow(
    int RowNumber,
    string? Code,
    string? Name,
    string? Unit,
    decimal? UnitPrice,
    PositionImportAction Action,
    string ActionName,
    string? Error,
    /// <summary>Tanım değişecekse eski hâli — sessiz değişiklik olmasın.</summary>
    string? ExistingName);

public sealed record PositionImportPreview(
    int TotalRows,
    int ValidRows,
    int InvalidRows,
    int NewPositions,
    int PriceUpdates,
    int DescriptionChanges,
    IReadOnlyList<string> FileWarnings,
    IReadOnlyList<PositionImportPreviewRow> Rows);

public sealed record PositionImportCommitResult(
    int CreatedPositions,
    int UpdatedPositions,
    int UpsertedPrices,
    int SkippedRows,
    string Message);

public sealed record PositionImportOptions(
    Guid CompanyId,
    int Year,
    PositionPriceInstitution Institution,
    EngineeringPositionDiscipline Discipline,
    string? SourceNote);

public interface IPositionImportService
{
    Task<PositionImportPreview> PreviewAsync(
        PositionImportParseResult parsed,
        PositionImportOptions options,
        CancellationToken cancellationToken = default);

    Task<PositionImportCommitResult> CommitAsync(
        PositionImportParseResult parsed,
        PositionImportOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Poz kitabını veritabanına aktarır.
///
/// Eşleştirme resmi poz numarası + kurum üzerinden yapılır; poz kodu
/// değil. Aynı kitap ikinci kez yüklendiğinde yeni poz AÇILMAZ, o yılın
/// fiyatı güncellenir — yani aktarım tekrar çalıştırılabilir.
///
/// Tanım değişikliği gizlenmez: mevcut pozun adı dosyadakinden
/// farklıysa önizlemede eski ve yeni hâli yan yana gösterilir. Resmi
/// kitap kendi pozunun tanımında yetkilidir, ama kullanıcı neyin
/// değişeceğini onaylamadan yazılmaz.
/// </summary>
public sealed class PositionImportService(AppDbContext db) : IPositionImportService
{
    /// <summary>Birim boş gelen satırlar için varsayılan.</summary>
    private const string FallbackUnit = "AD";

    public async Task<PositionImportPreview> PreviewAsync(
        PositionImportParseResult parsed,
        PositionImportOptions options,
        CancellationToken cancellationToken = default)
    {
        var existing = await LoadExistingAsync(parsed, options, cancellationToken);

        var rows = new List<PositionImportPreviewRow>(parsed.Rows.Count);

        foreach (var row in parsed.Rows)
        {
            if (!row.IsValid)
            {
                rows.Add(new PositionImportPreviewRow(
                    row.RowNumber, row.Code, row.Name, row.Unit, row.UnitPrice,
                    PositionImportAction.Skip, "Atlanacak", row.Error, null));

                continue;
            }

            var key = NormalizeCode(row.Code!);

            if (!existing.TryGetValue(key, out var match))
            {
                rows.Add(new PositionImportPreviewRow(
                    row.RowNumber, row.Code, row.Name, row.Unit, row.UnitPrice,
                    PositionImportAction.CreatePosition, "Yeni poz", null, null));

                continue;
            }

            var nameChanged = !string.Equals(
                match.Name?.Trim(), row.Name?.Trim(), StringComparison.OrdinalIgnoreCase);

            var unitChanged = !string.Equals(
                match.Unit?.Trim(),
                (row.Unit ?? FallbackUnit).Trim(),
                StringComparison.OrdinalIgnoreCase);

            if (nameChanged || unitChanged)
            {
                rows.Add(new PositionImportPreviewRow(
                    row.RowNumber, row.Code, row.Name, row.Unit, row.UnitPrice,
                    PositionImportAction.UpdatePositionAndPrice,
                    "Tanım/birim güncellenecek", null, match.Name));

                continue;
            }

            rows.Add(new PositionImportPreviewRow(
                row.RowNumber, row.Code, row.Name, row.Unit, row.UnitPrice,
                PositionImportAction.AddPrice, "Fiyat eklenecek", null, null));
        }

        return new PositionImportPreview(
            parsed.Rows.Count,
            rows.Count(x => x.Action != PositionImportAction.Skip),
            rows.Count(x => x.Action == PositionImportAction.Skip),
            rows.Count(x => x.Action == PositionImportAction.CreatePosition),
            rows.Count(x => x.Action is PositionImportAction.AddPrice
                                or PositionImportAction.UpdatePositionAndPrice),
            rows.Count(x => x.Action == PositionImportAction.UpdatePositionAndPrice),
            parsed.FileWarnings,
            rows);
    }

    public async Task<PositionImportCommitResult> CommitAsync(
        PositionImportParseResult parsed,
        PositionImportOptions options,
        CancellationToken cancellationToken = default)
    {
        var existing = await LoadExistingAsync(parsed, options, cancellationToken);

        var created = 0;
        var updated = 0;
        var prices = 0;
        var skipped = parsed.Rows.Count(x => !x.IsValid);

        // Şirketteki mevcut poz kodları: yeni açılan pozlara çakışmayan
        // iç kod üretmek için gerekiyor.
        var usedCodes = await db.EngineeringPositions
            .AsNoTracking()
            .Where(x => x.CompanyId == options.CompanyId)
            .Select(x => x.Code)
            .ToListAsync(cancellationToken);

        var codeSet = usedCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var row in parsed.Rows)
        {
            if (!row.IsValid)
                continue;

            var key = NormalizeCode(row.Code!);
            var unit = string.IsNullOrWhiteSpace(row.Unit) ? FallbackUnit : row.Unit.Trim();

            EngineeringPosition position;

            if (existing.TryGetValue(key, out var match))
            {
                position = match;

                if (!string.Equals(position.Name, row.Name, StringComparison.Ordinal)
                    || !string.Equals(position.Unit, unit, StringComparison.Ordinal))
                {
                    position.Name = row.Name!;
                    position.Unit = unit;
                    position.UpdatedAtUtc = DateTime.UtcNow;
                    updated++;
                }
            }
            else
            {
                var internalCode = BuildInternalCode(row.Code!, codeSet);

                position = new EngineeringPosition
                {
                    CompanyId = options.CompanyId,
                    Code = internalCode,
                    Name = row.Name!,
                    Unit = unit,
                    Source = options.Institution == PositionPriceInstitution.Company
                        ? EngineeringPositionSource.Enderun
                        : EngineeringPositionSource.Official,
                    Discipline = options.Discipline,
                    Status = EngineeringPositionStatus.Active,
                    OfficialInstitution = InstitutionLabel(options.Institution),
                    OfficialCode = row.Code!.Trim(),
                    Category = row.Category,
                    Description = row.Description,
                    SearchKeywords = BuildKeywords(row.Code!, row.Name!, row.Category)
                };

                db.EngineeringPositions.Add(position);
                existing[key] = position;
                codeSet.Add(internalCode);
                created++;
            }

            // Poz henüz kaydedilmemiş olabilir; fiyat aynı izleme
            // bağlamında navigasyonla bağlanıyor.
            var price = await db.PositionUnitPrices
                .FirstOrDefaultAsync(
                    x => x.EngineeringPositionId == position.Id
                         && x.Year == options.Year
                         && x.Institution == options.Institution,
                    cancellationToken);

            if (price is null)
            {
                price = new PositionUnitPrice
                {
                    EngineeringPosition = position,
                    Year = options.Year,
                    Institution = options.Institution
                };

                db.PositionUnitPrices.Add(price);
            }

            price.UnitPrice = row.UnitPrice!.Value;
            price.CurrencyCode = "TRY";
            price.SourceNote = options.SourceNote;
            price.UpdatedAtUtc = DateTime.UtcNow;

            prices++;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new PositionImportCommitResult(
            created, updated, prices, skipped,
            $"{created} yeni poz, {updated} güncellenen poz, {prices} fiyat kaydı. " +
            (skipped > 0 ? $"{skipped} hatalı satır aktarılmadı." : "Hatalı satır yok."));
    }

    /// <summary>
    /// Dosyadaki poz numaralarına karşılık gelen mevcut kayıtlar.
    /// Eşleşme resmi poz numarası + kurum üzerinden; iç kod değil.
    /// </summary>
    private async Task<Dictionary<string, EngineeringPosition>> LoadExistingAsync(
        PositionImportParseResult parsed,
        PositionImportOptions options,
        CancellationToken cancellationToken)
    {
        var codes = parsed.Rows
            .Where(x => x.IsValid && x.Code is not null)
            .Select(x => x.Code!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (codes.Count == 0)
            return new Dictionary<string, EngineeringPosition>(StringComparer.OrdinalIgnoreCase);

        var institution = InstitutionLabel(options.Institution);

        var matches = await db.EngineeringPositions
            .Where(x => x.CompanyId == options.CompanyId
                        && x.OfficialInstitution == institution
                        && x.OfficialCode != null
                        && codes.Contains(x.OfficialCode))
            .ToListAsync(cancellationToken);

        var result = new Dictionary<string, EngineeringPosition>(StringComparer.OrdinalIgnoreCase);

        foreach (var match in matches)
            result[NormalizeCode(match.OfficialCode!)] = match;

        return result;
    }

    /// <summary>
    /// Resmi poz numarasından şirket içi kod üretir. Çakışma olursa
    /// sonuna sayaç eklenir; kod şirket içinde tekil olmak zorunda.
    /// </summary>
    private static string BuildInternalCode(string officialCode, HashSet<string> used)
    {
        var candidate = officialCode.Trim().ToUpperInvariant();

        if (candidate.Length > 40)
            candidate = candidate[..40];

        if (!used.Contains(candidate))
            return candidate;

        for (var i = 2; i < 1000; i++)
        {
            var suffix = $"-{i}";
            var trimmed = candidate.Length + suffix.Length > 40
                ? candidate[..(40 - suffix.Length)]
                : candidate;

            var next = trimmed + suffix;

            if (!used.Contains(next))
                return next;
        }

        return $"POZ-{Guid.NewGuid():N}"[..40];
    }

    private static string BuildKeywords(string code, string name, string? category)
    {
        var parts = new[] { code, name, category }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim());

        var keywords = string.Join(" ", parts);

        return keywords.Length > 1000 ? keywords[..1000] : keywords;
    }

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    private static string InstitutionLabel(PositionPriceInstitution institution) =>
        PositionPriceService.InstitutionNameOf(institution);
}
