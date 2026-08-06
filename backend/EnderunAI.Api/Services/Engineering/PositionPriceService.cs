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
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new PositionPriceRow(
                x.Id,
                x.Year,
                x.Institution,
                InstitutionNameOf(x.Institution),
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

        // Yıl içinde birden çok yürürlük varsa en son yürürlüğe girmiş olan.
        var match = await query
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.EffectiveFrom ?? DateTime.MinValue)
            .ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (match is null)
        {
            var scope = institution.HasValue
                ? $"{InstitutionNameOf(institution.Value)} kurumunda"
                : "hiçbir kurumda";

            var period = year.HasValue ? $"{year} yılı için" : "";

            return new PositionPriceResolution(
                false, null, null, null, null, null, null,
                $"Bu poza {scope} {period} birim fiyat tanımlı değil. " +
                "Fiyat girilmeden ya da reçete analizi kullanılmadan kalem fiyatlanamaz.");
        }

        return new PositionPriceResolution(
            true,
            match.UnitPrice,
            match.CurrencyCode,
            match.Year,
            match.Institution,
            InstitutionNameOf(match.Institution),
            match.SourceNote,
            $"{match.Year} {InstitutionNameOf(match.Institution)} birim fiyatı");
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
                     && x.Institution == input.Institution,
                cancellationToken);

        if (existing is null)
        {
            existing = new PositionUnitPrice
            {
                EngineeringPositionId = positionId,
                Year = input.Year,
                Institution = input.Institution
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
