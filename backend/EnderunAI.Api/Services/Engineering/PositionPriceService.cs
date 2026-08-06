using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Engineering;

/// <summary>
/// Poz birim fiyatlarının yıl/kurum bazlı arşivi.
///
/// İki kural taşır:
/// 1. <b>Geçmiş silinmez.</b> Yeni yıl fiyatı eski satırın üstüne
///    yazılmaz, yanına eklenir. Eski bir teklif ya da hakediş hangi
///    kitapla hesaplandıysa o rakamla açıklanabilmeli.
/// 2. <b>Yıl atlanmaz.</b> İstenen yıla fiyat yoksa daha eski bir yılın
///    fiyatı sessizce kullanılmaz; "o yıla fiyat yok" denir. 2025
///    keşfine 2019 fiyatı koymak, fark edilmesi en zor hatalardan.
/// </summary>
public sealed class PositionPriceService(AppDbContext db) : IPositionPriceService
{
    public async Task<IReadOnlyList<PositionPriceRow>> GetHistoryAsync(
        Guid positionId, CancellationToken cancellationToken = default)
    {
        // Kurum adı sunucu tarafında hesaplanıyor; SQL'e çevrilemeyeceği
        // için satırlar önce çekilip sonra eşleniyor.
        var rows = await db.PositionUnitPrices
            .AsNoTracking()
            .Where(x => x.EngineeringPositionId == positionId)
            .OrderByDescending(x => x.Year)
            .ThenBy(x => x.Institution)
            .ThenBy(x => x.Component)
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new PositionPriceRow(
                x.Id,
                x.Year,
                x.Institution,
                InstitutionNameOf(x.Institution),
                x.Component,
                IPositionPriceService.ComponentNameOf(x.Component),
                x.UnitPrice,
                x.CurrencyCode,
                x.EffectiveFrom,
                x.SourceNote,
                x.CreatedAtUtc))
            .ToList();
    }

    public async Task<PositionPriceResolution> ResolveAsync(
        Guid positionId,
        int? year = null,
        PositionPriceInstitution? institution = null,
        CancellationToken cancellationToken = default)
    {
        var query = db.PositionUnitPrices
            .AsNoTracking()
            .Where(x => x.EngineeringPositionId == positionId);

        if (institution.HasValue)
            query = query.Where(x => x.Institution == institution.Value);

        if (year.HasValue)
            query = query.Where(x => x.Year == year.Value);

        var candidates = await query.ToListAsync(cancellationToken);

        return Resolve(candidates, year, institution);
    }

    /// <summary>
    /// Çok sayıda poz için fiyat çözümü — TEK sorguyla.
    ///
    /// Toplu eşleştirmede poz başına ayrı sorgu, yüzlerce satırlık bir
    /// icmalde binlerce gidiş dönüş demekti. Çözüm kuralları tek tek
    /// çözümle aynı; yalnızca satırlar önceden toplu çekiliyor.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, PositionPriceResolution>> ResolveManyAsync(
        IReadOnlyList<Guid> positionIds,
        int? year = null,
        PositionPriceInstitution? institution = null,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<Guid, PositionPriceResolution>();

        if (positionIds.Count == 0)
            return result;

        var ids = positionIds.Distinct().ToList();

        var query = db.PositionUnitPrices
            .AsNoTracking()
            .Where(x => ids.Contains(x.EngineeringPositionId));

        if (institution.HasValue)
            query = query.Where(x => x.Institution == institution.Value);

        if (year.HasValue)
            query = query.Where(x => x.Year == year.Value);

        var rows = await query.ToListAsync(cancellationToken);

        var byPosition = rows
            .GroupBy(x => x.EngineeringPositionId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PositionUnitPrice>)g.ToList());

        foreach (var id in ids)
        {
            result[id] = Resolve(
                byPosition.TryGetValue(id, out var found) ? found : [],
                year,
                institution);
        }

        return result;
    }

    /// <summary>
    /// Çözüm kuralları — veritabanından bağımsız. Tekli ve toplu çözüm
    /// aynı kodu kullanır ki iki ayrı "doğru" fiyat oluşmasın.
    /// </summary>
    private static PositionPriceResolution Resolve(
        IReadOnlyList<PositionUnitPrice> candidates,
        int? year,
        PositionPriceInstitution? institution)
    {
        if (candidates.Count == 0)
        {
            var scope = institution.HasValue
                ? $"{InstitutionNameOf(institution.Value)} kurumunda"
                : "hiçbir kurumda";

            var period = year.HasValue ? $"{year} yılı için" : "";

            return new PositionPriceResolution(
                false, null, null, null, null, null, null, null, null,
                $"Bu poza {scope} {period} birim fiyat tanımlı değil. " +
                "Fiyat girilmeden ya da reçete analizi kullanılmadan kalem fiyatlanamaz.");
        }

        // En yeni yıl; yıl içinde en son yürürlüğe giren kitap.
        var selectedYear = candidates.Max(x => x.Year);

        var group = candidates
            .Where(x => x.Year == selectedYear)
            .GroupBy(x => x.Institution)
            .OrderByDescending(g => g.Max(x => x.EffectiveFrom ?? DateTime.MinValue))
            .ThenByDescending(g => g.Max(x => x.CreatedAtUtc))
            .First()
            .ToList();

        var total = group.FirstOrDefault(x => x.Component == PositionPriceComponent.Total);
        var material = group.FirstOrDefault(x => x.Component == PositionPriceComponent.Material);
        var labor = group.FirstOrDefault(x => x.Component == PositionPriceComponent.Labor);

        var reference = total ?? material ?? labor ?? group[0];

        decimal? applied;
        string explanation;

        if (total is not null)
        {
            applied = total.UnitPrice;
            explanation = $"{reference.Year} {InstitutionNameOf(reference.Institution)} " +
                          "birim fiyatı (toplam)";
        }
        else if (material is not null || labor is not null)
        {
            // Kitap toplam vermemiş, bileşen vermiş. Malzeme + montaj
            // toplanır ve bu AÇIKÇA söylenir; demontaj bedelleri farklı
            // bir iş olduğu için toplama girmez.
            applied = (material?.UnitPrice ?? 0m) + (labor?.UnitPrice ?? 0m);

            var parts = new List<string>();
            if (material is not null) parts.Add($"malzeme {material.UnitPrice:N2}");
            if (labor is not null) parts.Add($"montaj {labor.UnitPrice:N2}");

            explanation =
                $"{reference.Year} {InstitutionNameOf(reference.Institution)}: " +
                string.Join(" + ", parts) +
                " (demontaj bedelleri toplama dahil edilmedi)";
        }
        else
        {
            // Elde yalnızca demontaj türü bir bileşen var; bunu keşif
            // fiyatı diye sunmak yanlış olur.
            applied = null;
            explanation =
                $"{reference.Year} {InstitutionNameOf(reference.Institution)} kaydında yalnızca " +
                $"{IPositionPriceService.ComponentNameOf(reference.Component).ToLowerInvariant()} " +
                "bedeli var; keşif birim fiyatı olarak kullanılamaz.";
        }

        return new PositionPriceResolution(
            applied is not null,
            applied,
            material?.UnitPrice,
            labor?.UnitPrice,
            reference.CurrencyCode,
            reference.Year,
            reference.Institution,
            InstitutionNameOf(reference.Institution),
            reference.SourceNote,
            explanation);
    }

    public async Task<PositionPriceRow> UpsertAsync(
        Guid positionId,
        UpsertPositionPriceInput input,
        CancellationToken cancellationToken = default)
    {
        var positionExists = await db.EngineeringPositions
            .AnyAsync(x => x.Id == positionId, cancellationToken);

        if (!positionExists)
            throw new ArgumentException("Poz bulunamadı.");

        if (input.Year is < 2000 or > 2100)
            throw new ArgumentException("Fiyat yılı 2000-2100 aralığında olmalıdır.");

        if (input.UnitPrice <= 0)
            throw new ArgumentException("Birim fiyat sıfırdan büyük olmalıdır.");

        var currency = string.IsNullOrWhiteSpace(input.CurrencyCode)
            ? "TRY"
            : input.CurrencyCode.Trim().ToUpperInvariant();

        var existing = await db.PositionUnitPrices
            .FirstOrDefaultAsync(
                x => x.EngineeringPositionId == positionId
                     && x.Year == input.Year
                     && x.Institution == input.Institution
                     && x.Component == input.Component,
                cancellationToken);

        if (existing is null)
        {
            existing = new PositionUnitPrice
            {
                EngineeringPositionId = positionId,
                Year = input.Year,
                Institution = input.Institution,
                Component = input.Component
            };

            db.PositionUnitPrices.Add(existing);
        }

        existing.UnitPrice = input.UnitPrice;
        existing.CurrencyCode = currency;
        existing.EffectiveFrom = input.EffectiveFrom.HasValue
            ? DateTime.SpecifyKind(input.EffectiveFrom.Value.Date, DateTimeKind.Utc)
            : null;
        existing.SourceNote = string.IsNullOrWhiteSpace(input.SourceNote)
            ? null
            : input.SourceNote.Trim();
        existing.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return new PositionPriceRow(
            existing.Id,
            existing.Year,
            existing.Institution,
            InstitutionNameOf(existing.Institution),
            existing.Component,
            IPositionPriceService.ComponentNameOf(existing.Component),
            existing.UnitPrice,
            existing.CurrencyCode,
            existing.EffectiveFrom,
            existing.SourceNote,
            existing.CreatedAtUtc);
    }

    public async Task<bool> DeleteAsync(
        Guid priceId, CancellationToken cancellationToken = default)
    {
        var price = await db.PositionUnitPrices
            .FirstOrDefaultAsync(x => x.Id == priceId, cancellationToken);

        if (price is null)
            return false;

        db.PositionUnitPrices.Remove(price);
        await db.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// EF sorgusu içinde de kullanılabilsin diye ifade gövdeli ve
    /// yan etkisiz tutuldu.
    /// </summary>
    public static string InstitutionNameOf(PositionPriceInstitution institution) =>
        institution switch
        {
            PositionPriceInstitution.Csb => "ÇŞB",
            PositionPriceInstitution.Tedas => "TEDAŞ",
            PositionPriceInstitution.Company => "Şirket",
            _ => "Diğer"
        };
}
