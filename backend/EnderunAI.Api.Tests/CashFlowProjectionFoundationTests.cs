using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Nakit akış projeksiyonunun veri temeli ve yetki kapısı.
///
/// AYRI İZİN: tablo bordro çıkışını ELDEN DAHİL tam tutarla
/// gösterecek. finance.view'e bırakılamazdı — o izin Teknik Ofis ve
/// Teknik Koordinatör'de de var ve ikisinde ek ödeme yetkisi yok;
/// elden toplamı nakit akış tablosundan sızardı.
/// </summary>
[Collection("Integration")]
public sealed class CashFlowProjectionFoundationTests(DatabaseFixture fixture)
{
    /// <summary>
    /// Yeni izin kataloğa girdi ve kod tarafında bir anahtarı var.
    /// </summary>
    [Fact]
    public void CashFlowPermission_IsRegisteredInTheCatalog()
    {
        Assert.Equal("cashflow.view", PermissionCatalog.Keys.CashFlowView);

        Assert.Contains(
            PermissionCatalog.Permissions,
            x => x.Key == PermissionCatalog.Keys.CashFlowView);
    }

    /// <summary>
    /// KAPI DAR: elden görmeyen roller nakit akış iznini almıyor.
    /// Teknik Ofis ve Teknik Koordinatör finance.view taşıyor ama
    /// extra_payment.view taşımıyor — ikisi de bu izni almamalı.
    /// </summary>
    [Fact]
    public void RolesWithoutExtraPaymentAccess_DoNotGetCashFlow()
    {
        foreach (var roleName in new[] { "Teknik Ofis", "Teknik Koordinatör" })
        {
            var role = RoleCatalog.Roles.Single(x => x.Name == roleName);

            Assert.Contains(PermissionCatalog.Keys.FinanceView, role.PermissionKeys);

            Assert.DoesNotContain(
                PermissionCatalog.Keys.ExtraPaymentView, role.PermissionKeys);

            Assert.DoesNotContain(
                PermissionCatalog.Keys.CashFlowView, role.PermissionKeys);
        }
    }

    /// <summary>
    /// Finans Sorumlusu, Admin ve Genel Müdür izni alıyor: tabloyu
    /// okuması gereken roller kapının içinde.
    /// </summary>
    [Fact]
    public void FinanceAndManagementRoles_GetCashFlow()
    {
        foreach (var roleName in new[] { "Finans Sorumlusu", "Admin", "Genel Müdür" })
        {
            var role = RoleCatalog.Roles.Single(x => x.Name == roleName);

            Assert.Contains(PermissionCatalog.Keys.CashFlowView, role.PermissionKeys);
        }
    }

    // ---------------- Veri temeli ----------------

    /// <summary>
    /// Projede tahsilat vadesi ve hakedişte ezme alanı yazılıp
    /// okunabiliyor.
    /// </summary>
    [Fact]
    public async Task CollectionTermFields_ArePersisted()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        project.CollectionTermDays = 45;
        await db.SaveChangesAsync();

        var stored = await db.Projects.AsNoTracking()
            .SingleAsync(x => x.Id == project.Id);

        Assert.Equal(45, stored.CollectionTermDays);
    }

    /// <summary>
    /// Tahmini gider satırı kayıtlı: tekrar sayısı, başlangıç ayı ve
    /// ödeme günü birlikte tutuluyor.
    /// </summary>
    [Fact]
    public async Task EstimatedExpense_IsPersisted()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        var expense = new CashFlowEstimatedExpense
        {
            CompanyId = project.CompanyId,
            Description = $"Ofis kirası {suffix}",
            Amount = 85_000m,
            StartYear = 2026,
            StartMonth = 9,
            RecurrenceCount = 6,
            PaymentDay = 10
        };

        db.CashFlowEstimatedExpenses.Add(expense);
        await db.SaveChangesAsync();

        var stored = await db.CashFlowEstimatedExpenses.AsNoTracking()
            .SingleAsync(x => x.Id == expense.Id);

        Assert.Equal(85_000m, stored.Amount);
        Assert.Equal(6, stored.RecurrenceCount);
        Assert.Equal(10, stored.PaymentDay);

        // Şirket geneli gider: projeye bağlı olmak zorunda değil.
        Assert.Null(stored.ProjectId);
    }
}
