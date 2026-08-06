using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Engineering;

/// <summary>
/// Hazır eşleme profili. Kullanıcı dosyayı yükleyip profili seçince
/// sütun eşlemesiyle uğraşmaz; gelecek yıl sürümleri de aynı profille
/// aktarılır.
/// </summary>
public sealed record BookImportProfile(
    string Key,
    string Name,
    string Description,
    PositionPriceInstitution Institution,
    EngineeringPositionDiscipline DefaultDiscipline,
    string FileKind);

public sealed record BookImportSummary(
    string ProfileKey,
    string ProfileName,
    int ParsedRows,
    int GroupHeaders,
    int SuspiciousRows,
    int CreatedPositions,
    int UpdatedPositions,
    int UpsertedPrices,
    int InheritedUnits,
    IReadOnlyList<string> SuspiciousLines,
    IReadOnlyList<string> Warnings,
    string Message);

public interface IBookImportService
{
    IReadOnlyList<BookImportProfile> GetProfiles();

    /// <summary>Ayrıştırır ama YAZMAZ — önizleme.</summary>
    Task<BookImportSummary> PreviewAsync(
        string profileKey,
        Stream file,
        Guid companyId,
        int year,
        string? sourceNote,
        string? codePrefixFilter,
        CancellationToken cancellationToken = default);

    Task<BookImportSummary> ImportAsync(
        string profileKey,
        Stream file,
        Guid companyId,
        int year,
        string? sourceNote,
        string? codePrefixFilter,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Hazır profillerle poz kitabı aktarımı.
///
/// Genel sütun eşleme akışından farkı: bu kitapların düzeni bilindiği
/// için kullanıcı sütun seçmez. Yazma mantığı ise aynı sözleşmeye
/// uyar — poz eşleşmesi resmi poz numarası + kurum üzerinden, aynı
/// kitap ikinci kez yüklenirse poz çoğalmaz, fiyat güncellenir.
/// </summary>
public sealed class BookImportService(AppDbContext db) : IBookImportService
{
    private const string FallbackUnit = "AD";

    private static readonly BookImportProfile[] Profiles =
    [
        new(TedasBfkParser.ProfileKey,
            "TEDAŞ Birim Fiyat Kitabı (Excel)",
            "Yeni poz no B/C/D sütunlarından birleştirilir (85.105.1201). " +
            "Malzeme, montaj, demontaj ve demontajdan montaj bedelleri ayrı " +
            "bileşen olarak saklanır. Fiyatsız ve alt numarası yüzün katı olan " +
            "satırlar kategori başlığı sayılır, poz olarak aktarılmaz.",
            PositionPriceInstitution.Tedas,
            EngineeringPositionDiscipline.Electrical,
            "xlsx"),

        new(CsbBfkPdfParser.ProfileKey,
            "ÇŞB Birim Fiyat Kitabı (PDF)",
            "Kelime konumlarından tablo çıkarılır; kolon sınırları her sayfanın " +
            "kendi başlığından okunur. Elektrik bölümünde montajlı birim fiyat " +
            "toplam, montaj bedeli işçilik bileşeni olarak ayrılır. Birim, " +
            "tanımdaki (Ölçü: ...) ifadesinden alınabilir.",
            PositionPriceInstitution.Csb,
            EngineeringPositionDiscipline.Electrical,
            "pdf")
    ];

    public IReadOnlyList<BookImportProfile> GetProfiles() => Profiles;

    public async Task<BookImportSummary> PreviewAsync(
        string profileKey,
        Stream file,
        Guid companyId,
        int year,
        string? sourceNote,
        string? codePrefixFilter,
        CancellationToken cancellationToken = default)
    {
        var (profile, parsed) = ParseWithProfile(profileKey, file, codePrefixFilter);

        var existing = await LoadExistingAsync(parsed, companyId, profile, cancellationToken);

        var created = parsed.Rows.Count(x => !existing.ContainsKey(Normalize(x.Code)));

        return Summarize(
            profile, parsed,
            createdPositions: created,
            updatedPositions: 0,
            upsertedPrices: parsed.Rows.Sum(x => x.Prices.Count),
            message: "Önizleme — hiçbir kayıt yazılmadı.");
    }

    public async Task<BookImportSummary> ImportAsync(
        string profileKey,
        Stream file,
        Guid companyId,
        int year,
        string? sourceNote,
        string? codePrefixFilter,
        CancellationToken cancellationToken = default)
    {
        if (year is < 2000 or > 2100)
            throw new ArgumentException("Fiyat yılı 2000-2100 aralığında olmalıdır.");

        var (profile, parsed) = ParseWithProfile(profileKey, file, codePrefixFilter);

        var existing = await LoadExistingAsync(parsed, companyId, profile, cancellationToken);

        var usedCodes = (await db.EngineeringPositions
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .Select(x => x.Code)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var institutionLabel = PositionPriceService.InstitutionNameOf(profile.Institution);

        var created = 0;
        var updated = 0;
        var priceCount = 0;

        // Var olan fiyat satırları tek seferde çekiliyor; satır başına
        // sorgu 5.000 satırlık kitapta kabul edilemez olurdu.
        var positionIds = existing.Values.Select(x => x.Id).ToList();

        var priceIndex = (await db.PositionUnitPrices
                .Where(x => positionIds.Contains(x.EngineeringPositionId)
                            && x.Year == year
                            && x.Institution == profile.Institution)
                .ToListAsync(cancellationToken))
            .ToDictionary(x => (x.EngineeringPositionId, x.Component));

        foreach (var row in parsed.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = Normalize(row.Code);
            var unit = string.IsNullOrWhiteSpace(row.Unit) ? FallbackUnit : row.Unit.Trim();

            if (!existing.TryGetValue(key, out var position))
            {
                var internalCode = BuildInternalCode(row.Code, usedCodes);

                position = new EngineeringPosition
                {
                    CompanyId = companyId,
                    Code = internalCode,
                    Name = Trim(row.Name, 500),
                    Unit = Trim(unit, 30),
                    Source = EngineeringPositionSource.Official,
                    Discipline = profile.DefaultDiscipline,
                    Status = EngineeringPositionStatus.Active,
                    OfficialInstitution = institutionLabel,
                    OfficialCode = row.Code,
                    Category = Trim(row.Category, 200),
                    Description = Trim(row.Note, 1000),
                    SearchKeywords = Trim($"{row.Code} {row.Name} {row.Category}", 1000)
                };

                db.EngineeringPositions.Add(position);
                existing[key] = position;
                usedCodes.Add(internalCode);
                created++;
            }
            else if (position.Name != Trim(row.Name, 500) || position.Unit != Trim(unit, 30))
            {
                position.Name = Trim(row.Name, 500);
                position.Unit = Trim(unit, 30);
                position.UpdatedAtUtc = DateTime.UtcNow;
                updated++;
            }

            foreach (var price in row.Prices)
            {
                if (!priceIndex.TryGetValue((position.Id, price.Component), out var stored))
                {
                    stored = new PositionUnitPrice
                    {
                        EngineeringPosition = position,
                        Year = year,
                        Institution = profile.Institution,
                        Component = price.Component
                    };

                    db.PositionUnitPrices.Add(stored);
                    priceIndex[(position.Id, price.Component)] = stored;
                }

                stored.UnitPrice = price.UnitPrice;
                stored.CurrencyCode = "TRY";
                stored.SourceNote = Trim(sourceNote, 300);
                stored.UpdatedAtUtc = DateTime.UtcNow;

                priceCount++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return Summarize(
            profile, parsed, created, updated, priceCount,
            $"{created} yeni poz, {updated} güncellenen poz, {priceCount} fiyat kaydı " +
            $"({year} {institutionLabel}).");
    }

    private static (BookImportProfile Profile, BookParseResult Parsed) ParseWithProfile(
        string profileKey, Stream file, string? codePrefixFilter)
    {
        var profile = Profiles.FirstOrDefault(x =>
            string.Equals(x.Key, profileKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Bilinmeyen profil: {profileKey}");

        var parsed = profile.Key == TedasBfkParser.ProfileKey
            ? TedasBfkParser.Parse(file)
            : CsbBfkPdfParser.Parse(file, codePrefixFilter);

        return (profile, parsed);
    }

    private async Task<Dictionary<string, EngineeringPosition>> LoadExistingAsync(
        BookParseResult parsed,
        Guid companyId,
        BookImportProfile profile,
        CancellationToken cancellationToken)
    {
        var codes = parsed.Rows
            .Select(x => x.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new Dictionary<string, EngineeringPosition>(StringComparer.OrdinalIgnoreCase);

        if (codes.Count == 0)
            return result;

        var institution = PositionPriceService.InstitutionNameOf(profile.Institution);

        // Kod listesi büyük olabildiği için parçalara bölünüyor;
        // tek sorguda 5.000 parametre Npgsql'de sınırı zorlar.
        foreach (var chunk in codes.Chunk(500))
        {
            var matches = await db.EngineeringPositions
                .Where(x => x.CompanyId == companyId
                            && x.OfficialInstitution == institution
                            && x.OfficialCode != null
                            && chunk.Contains(x.OfficialCode))
                .ToListAsync(cancellationToken);

            foreach (var match in matches)
                result[Normalize(match.OfficialCode!)] = match;
        }

        return result;
    }

    private static BookImportSummary Summarize(
        BookImportProfile profile,
        BookParseResult parsed,
        int createdPositions,
        int updatedPositions,
        int upsertedPrices,
        string message)
        => new(
            profile.Key,
            profile.Name,
            parsed.Rows.Count,
            parsed.GroupHeaderCount,
            parsed.SuspiciousLines.Count,
            createdPositions,
            updatedPositions,
            upsertedPrices,
            parsed.Rows.Count(x => x.UnitInherited),
            // Şüpheli satırların tamamı yüzlerce olabiliyor; ilk 200'ü
            // gösteriliyor, sayısı tam veriliyor.
            parsed.SuspiciousLines.Take(200).ToList(),
            parsed.Warnings,
            message);

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

    private static string Normalize(string code) => code.Trim().ToUpperInvariant();

    private static string Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();

        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
