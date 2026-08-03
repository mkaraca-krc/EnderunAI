using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Accounting;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

[Collection("Integration")]
public sealed class AccountingAccountSeedTests(DatabaseFixture fixture)
{
    /// <summary>
    /// Regresyon testi: hesap planı seed dosyası (Data/Seeds/
    /// enderun-accounting-accounts.json) uzun süre repoda yoktu ve
    /// csproj'da publish çıktısına kopyalama kuralı bulunmadığı için
    /// canlıda seed ucu FileNotFoundException veriyordu. Bu test hem
    /// dosyanın bulunabildiğini hem de gerçekten hesap ürettiğini
    /// doğruluyor.
    /// </summary>
    [Fact]
    public async Task SeedAsync_CreatesUniformChartOfAccounts()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IAccountingAccountSeedService>();

        var company = new Company
        {
            Code = $"SEED-{suffix}",
            Name = $"Hesap Planı Seed Testi {suffix}"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var result = await service.SeedAsync(company.Id, CancellationToken.None);

        Assert.True(result.CreatedCount > 500,
            $"Beklenenden az hesap oluştu: {result.CreatedCount}");
        Assert.Equal(0, result.ExistingCount);

        // Faz A otomatik fiş motorunun aradığı hesaplar mutlaka bulunmalı
        // ve fiş kesilebilir olmalı — aksi halde tedarikçi faturası
        // onayı hesap bulunamadı hatası verir.
        var required = new[] { "320", "120", "740", "191.01.03", "391.09", "600.03", "780.01.01" };
        var found = await db.AccountingAccounts
            .Where(x => x.CompanyId == company.Id &&
                        required.Contains(x.Code) &&
                        x.IsPostingAllowed)
            .Select(x => x.Code)
            .ToListAsync();

        Assert.Equal(required.Length, found.Count);

        // Hiyerarşi tutarlı kurulmuş olmalı: kök dışındaki her hesabın
        // üst hesabı bağlanmış olmalı.
        var orphanCount = await db.AccountingAccounts
            .CountAsync(x => x.CompanyId == company.Id &&
                             x.Level > 1 &&
                             x.ParentAccountId == null);
        Assert.Equal(0, orphanCount);
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IAccountingAccountSeedService>();

        var company = new Company
        {
            Code = $"SEED2-{suffix}",
            Name = $"Hesap Planı Tekrar Seed {suffix}"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var first = await service.SeedAsync(company.Id, CancellationToken.None);
        var second = await service.SeedAsync(company.Id, CancellationToken.None);

        Assert.Equal(0, second.CreatedCount);
        Assert.Equal(first.CreatedCount, second.ExistingCount);
    }

    /// <summary>
    /// Seed dosyası müşteriye özel cari alt defterini (320.x tedarikçi,
    /// 120.x müşteri kartları vb.) İÇERMEMELİ — bunlar canlı iş verisi,
    /// veritabanında ve gecelik yedekte durur, repoya girmez.
    /// </summary>
    [Fact]
    public async Task SeedAsync_DoesNotContainCustomerSubLedger()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IAccountingAccountSeedService>();

        var company = new Company
        {
            Code = $"SEED3-{suffix}",
            Name = $"Hesap Planı Gizlilik {suffix}"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        await service.SeedAsync(company.Id, CancellationToken.None);

        var subLedgerCount = await db.AccountingAccounts
            .CountAsync(x => x.CompanyId == company.Id &&
                             (x.Code.StartsWith("320.") || x.Code.StartsWith("120.")));

        Assert.Equal(0, subLedgerCount);
    }
}
