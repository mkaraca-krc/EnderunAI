using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Engineering;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Poz yıllık birim fiyat arşivi.
///
/// İki kural korunuyor:
/// 1. Geçmiş silinmez — yeni yıl fiyatı eskisinin üstüne yazılmaz.
///    Eski bir teklif hangi kitapla hesaplandıysa o rakamla
///    açıklanabilmeli.
/// 2. Yıl atlanmaz — istenen yıla fiyat yoksa daha eskisi sessizce
///    kullanılmaz. 2025 keşfine 2019 fiyatı koymak fark edilmesi en zor
///    hatalardan biridir.
/// </summary>
[Collection("Integration")]
public sealed class PositionUnitPriceTests(DatabaseFixture fixture)
{
    private static async Task<EngineeringPosition> CreatePositionAsync(
        AppDbContext db, string suffix)
    {
        var (company, _, _) = await TestDataFactory.CreateCompanyStackAsync(db, suffix);

        var position = new EngineeringPosition
        {
            CompanyId = company.Id,
            Code = $"POZ-{suffix}",
            Name = "NYY kablo çekilmesi",
            Unit = "MTR",
            Source = EngineeringPositionSource.Official,
            Discipline = EngineeringPositionDiscipline.Electrical,
            Status = EngineeringPositionStatus.Active,
            OfficialInstitution = "ÇŞB",
            OfficialCode = $"35.{suffix[..4]}"
        };

        db.EngineeringPositions.Add(position);
        await db.SaveChangesAsync();

        return position;
    }

    [Fact]
    public async Task Upsert_NewYear_KeepsPreviousYears()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new PositionPriceService(db);

        var position = await CreatePositionAsync(db, Guid.NewGuid().ToString("N")[..8]);

        await service.UpsertAsync(position.Id, new UpsertPositionPriceInput(
            2024, PositionPriceInstitution.Csb, 120.50m, "TRY", null, "ÇŞB 2024"));

        await service.UpsertAsync(position.Id, new UpsertPositionPriceInput(
            2025, PositionPriceInstitution.Csb, 168.75m, "TRY", null, "ÇŞB 2025"));

        var history = await service.GetHistoryAsync(position.Id);

        Assert.Equal(2, history.Count);
        Assert.Equal(2025, history[0].Year);
        Assert.Equal(168.75m, history[0].UnitPrice);
        Assert.Equal(2024, history[1].Year);
        Assert.Equal(120.50m, history[1].UnitPrice);
    }

    [Fact]
    public async Task Upsert_SameYearAndInstitution_UpdatesInsteadOfDuplicating()
    {
        // Aynı fiyat kitabının iki kez yüklenmesi satır çoğaltmamalı.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new PositionPriceService(db);

        var position = await CreatePositionAsync(db, Guid.NewGuid().ToString("N")[..8]);

        await service.UpsertAsync(position.Id, new UpsertPositionPriceInput(
            2025, PositionPriceInstitution.Csb, 100m, "TRY", null, "ilk yükleme"));

        await service.UpsertAsync(position.Id, new UpsertPositionPriceInput(
            2025, PositionPriceInstitution.Csb, 115m, "TRY", null, "düzeltilmiş kitap"));

        var history = await service.GetHistoryAsync(position.Id);

        Assert.Single(history);
        Assert.Equal(115m, history[0].UnitPrice);
        Assert.Equal("düzeltilmiş kitap", history[0].SourceNote);
    }

    [Fact]
    public async Task Upsert_SameYearDifferentInstitutions_Coexist()
    {
        // Aynı poz numarası iki kurumun kitabında farklı fiyatla geçebilir.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new PositionPriceService(db);

        var position = await CreatePositionAsync(db, Guid.NewGuid().ToString("N")[..8]);

        await service.UpsertAsync(position.Id, new UpsertPositionPriceInput(
            2025, PositionPriceInstitution.Csb, 168.75m, "TRY", null, null));

        await service.UpsertAsync(position.Id, new UpsertPositionPriceInput(
            2025, PositionPriceInstitution.Tedas, 181.20m, "TRY", null, null));

        var history = await service.GetHistoryAsync(position.Id);

        Assert.Equal(2, history.Count);
        Assert.All(history, x => Assert.Equal(2025, x.Year));

        var csb = history.Single(x => x.Institution == PositionPriceInstitution.Csb);
        var tedas = history.Single(x => x.Institution == PositionPriceInstitution.Tedas);

        Assert.Equal(168.75m, csb.UnitPrice);
        Assert.Equal(181.20m, tedas.UnitPrice);
        Assert.Equal("ÇŞB", csb.InstitutionName);
        Assert.Equal("TEDAŞ", tedas.InstitutionName);
    }

    [Fact]
    public async Task Resolve_RequestedYearMissing_DoesNotFallBackToOlderYear()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new PositionPriceService(db);

        var position = await CreatePositionAsync(db, Guid.NewGuid().ToString("N")[..8]);

        await service.UpsertAsync(position.Id, new UpsertPositionPriceInput(
            2019, PositionPriceInstitution.Csb, 42m, "TRY", null, null));

        var resolution = await service.ResolveAsync(position.Id, year: 2025);

        Assert.False(resolution.Found);
        Assert.Null(resolution.UnitPrice);
        Assert.Contains("2025", resolution.Explanation);
    }

    [Fact]
    public async Task Resolve_WithoutYear_UsesNewest()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new PositionPriceService(db);

        var position = await CreatePositionAsync(db, Guid.NewGuid().ToString("N")[..8]);

        await service.UpsertAsync(position.Id, new UpsertPositionPriceInput(
            2023, PositionPriceInstitution.Csb, 90m, "TRY", null, null));
        await service.UpsertAsync(position.Id, new UpsertPositionPriceInput(
            2025, PositionPriceInstitution.Csb, 168.75m, "TRY", null, null));

        var resolution = await service.ResolveAsync(position.Id);

        Assert.True(resolution.Found);
        Assert.Equal(168.75m, resolution.UnitPrice);
        Assert.Equal(2025, resolution.Year);
        Assert.Contains("2025", resolution.Explanation);
    }

    [Fact]
    public async Task Resolve_FilteredByInstitution_IgnoresOtherInstitutions()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new PositionPriceService(db);

        var position = await CreatePositionAsync(db, Guid.NewGuid().ToString("N")[..8]);

        await service.UpsertAsync(position.Id, new UpsertPositionPriceInput(
            2025, PositionPriceInstitution.Csb, 168.75m, "TRY", null, null));

        var tedas = await service.ResolveAsync(
            position.Id, 2025, PositionPriceInstitution.Tedas);

        Assert.False(tedas.Found);
        Assert.Contains("TEDAŞ", tedas.Explanation);
    }

    [Fact]
    public async Task Resolve_MultipleEffectiveDatesInSameYear_UsesLatest()
    {
        // Yıl içinde ek fiyat kitabı yayımlanabiliyor; en son yürürlüğe
        // giren geçerli olmalı.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new PositionPriceService(db);

        var position = await CreatePositionAsync(db, Guid.NewGuid().ToString("N")[..8]);

        await service.UpsertAsync(position.Id, new UpsertPositionPriceInput(
            2025, PositionPriceInstitution.Csb, 168.75m, "TRY",
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), "ana kitap"));

        await service.UpsertAsync(position.Id, new UpsertPositionPriceInput(
            2025, PositionPriceInstitution.Tedas, 190m, "TRY",
            new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc), "ek kitap"));

        var resolution = await service.ResolveAsync(position.Id, 2025);

        Assert.True(resolution.Found);
        Assert.Equal(190m, resolution.UnitPrice);
        Assert.Equal(PositionPriceInstitution.Tedas, resolution.Institution);
    }

    [Fact]
    public async Task Resolve_PositionWithoutAnyPrice_ExplainsInsteadOfReturningZero()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new PositionPriceService(db);

        var position = await CreatePositionAsync(db, Guid.NewGuid().ToString("N")[..8]);

        var resolution = await service.ResolveAsync(position.Id);

        Assert.False(resolution.Found);
        Assert.Null(resolution.UnitPrice);
        Assert.Contains("fiyat", resolution.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2101)]
    public async Task Upsert_UnreasonableYear_IsRejected(int year)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new PositionPriceService(db);

        var position = await CreatePositionAsync(db, Guid.NewGuid().ToString("N")[..8]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpsertAsync(position.Id, new UpsertPositionPriceInput(
                year, PositionPriceInstitution.Csb, 100m, "TRY", null, null)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Upsert_NonPositivePrice_IsRejected(decimal price)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new PositionPriceService(db);

        var position = await CreatePositionAsync(db, Guid.NewGuid().ToString("N")[..8]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpsertAsync(position.Id, new UpsertPositionPriceInput(
                2025, PositionPriceInstitution.Csb, price, "TRY", null, null)));
    }

    [Fact]
    public async Task Delete_RemovesOnlyThatYear()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new PositionPriceService(db);

        var position = await CreatePositionAsync(db, Guid.NewGuid().ToString("N")[..8]);

        var older = await service.UpsertAsync(position.Id, new UpsertPositionPriceInput(
            2024, PositionPriceInstitution.Csb, 120m, "TRY", null, null));
        await service.UpsertAsync(position.Id, new UpsertPositionPriceInput(
            2025, PositionPriceInstitution.Csb, 168m, "TRY", null, null));

        Assert.True(await service.DeleteAsync(older.Id));

        var history = await service.GetHistoryAsync(position.Id);

        Assert.Single(history);
        Assert.Equal(2025, history[0].Year);
    }
}
