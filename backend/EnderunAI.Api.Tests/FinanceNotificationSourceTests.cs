using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.FinancialInstruments;
using EnderunAI.Api.Models.Notifications;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Notifications;
using EnderunAI.Api.Services.Notifications.Sources;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Finans tetikleyicileri: çek, fatura, kredi taksiti, kart ekstresi
/// ve harcırah mahsubu.
///
/// İKİ ORTAK KURAL her kaynakta ayrı ayrı sınanıyor:
/// - KAPANAN KAYNAK ADAY ÜRETMEZ (ödenen çek, iptal kredi, kapanan
///   mahsup) — üretseydi bildirim merkezi çözülmüş işlerle dolardı.
/// - TUTAR AYRI ALANDA: güvenli metin tutarsız, tutarlı metin ayrı
///   izinde.
/// </summary>
[Collection("Integration")]
public sealed class FinanceNotificationSourceTests(DatabaseFixture fixture)
{
    private static readonly DateTime Today = DateTime.UtcNow.Date;

    private sealed record Context(Guid CompanyId, Guid BranchId, Guid AccountId);

    private async Task<Context> CreateContextAsync(string suffix)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await TestDataFactory.CreateProjectAsync(db, suffix);

        return new Context(
            project.CompanyId, project.BranchId,
            project.EmployerCurrentAccountId!.Value);
    }

    private async Task<IReadOnlyList<NotificationCandidate>> BuildAsync<TSource>(
        Guid companyId)
        where TSource : INotificationSource
    {
        using var scope = fixture.Factory.Services.CreateScope();

        var source = scope.ServiceProvider
            .GetRequiredService<IEnumerable<INotificationSource>>()
            .OfType<TSource>()
            .Single();

        return await source.BuildAsync(
            new NotificationScanContext(companyId, Today), CancellationToken.None);
    }

    // ---------------- Çek ----------------

    private async Task<Guid> AddChequeAsync(
        Context context, ChequeDirection direction, ChequeStatus status,
        decimal amount, int dueInDays)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cheque = new Cheque
        {
            CompanyId = context.CompanyId,
            Direction = direction,
            Status = status,
            InternalNumber = $"I{Guid.NewGuid():N}"[..12],
            ChequeNumber = $"C{Guid.NewGuid():N}"[..10],
            BankName = "Test Bankası",
            CurrentAccountId = context.AccountId,
            Amount = amount,
            AmountTry = amount,
            ExchangeRate = 1m,
            CurrencyCode = "TRY",
            IssueDate = Today,
            DueDate = Today.AddDays(dueInDays)
        };

        db.Cheques.Add(cheque);
        await db.SaveChangesAsync();

        return cheque.Id;
    }

    /// <summary>
    /// Vadesi yaklaşan çek aday üretiyor; ufkun dışındaki üretmiyor.
    /// Her çek AYRI bildirim — kullanıcı tek tek kapatabilmeli.
    /// </summary>
    [Fact]
    public async Task ChequeSource_ProducesOneCandidatePerUpcomingCheque()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddChequeAsync(context, ChequeDirection.Issued, ChequeStatus.Issued,
            100_000m, 3);

        await AddChequeAsync(context, ChequeDirection.Received, ChequeStatus.Portfolio,
            50_000m, 5);

        // Ufkun dışında: 7 günü aşıyor.
        await AddChequeAsync(context, ChequeDirection.Issued, ChequeStatus.Issued,
            9_000m, 30);

        var candidates = await BuildAsync<ChequeDueNotificationSource>(context.CompanyId);

        Assert.Equal(2, candidates.Count);
        Assert.All(candidates,
            x => Assert.Equal(ChequeDueNotificationSource.TypeKey, x.Type));
    }

    /// <summary>
    /// ÖDENMİŞ ÇEK ADAY ÜRETMEZ; tarama onu görmeyince bildirim
    /// kendiliğinden kapanır.
    /// </summary>
    [Fact]
    public async Task PaidCheque_ProducesNoCandidate()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddChequeAsync(context, ChequeDirection.Issued, ChequeStatus.Paid,
            100_000m, 2);

        var candidates = await BuildAsync<ChequeDueNotificationSource>(context.CompanyId);

        Assert.Empty(candidates);
    }

    /// <summary>
    /// VADESİ GEÇMİŞ ÇEK KRİTİK: geciken bir ödeme, yaklaşan bir
    /// ödemeden daha acildir.
    /// </summary>
    [Fact]
    public async Task OverdueCheque_IsCritical()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddChequeAsync(context, ChequeDirection.Issued, ChequeStatus.Issued,
            10_000m, -4);

        var candidate = (await BuildAsync<ChequeDueNotificationSource>(
            context.CompanyId)).Single();

        Assert.Equal(NotificationSeverity.Critical, candidate.Severity);
        Assert.Contains("gecikti", candidate.Title);
    }

    /// <summary>
    /// TUTAR AYRI: güvenli metinde tutar yok, tutarlı metinde var ve
    /// finans iznine bağlı.
    /// </summary>
    [Fact]
    public async Task ChequeCandidate_KeepsTheAmountOutOfTheSafeText()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddChequeAsync(context, ChequeDirection.Issued, ChequeStatus.Issued,
            123_456m, 2);

        var candidate = (await BuildAsync<ChequeDueNotificationSource>(
            context.CompanyId)).Single();

        Assert.DoesNotContain("123", candidate.Detail ?? "");
        Assert.Contains("123", candidate.AmountDetail ?? "");
        Assert.Equal(PermissionCatalog.Keys.FinanceView, candidate.AmountPermission);
        Assert.Equal(PermissionCatalog.Keys.FinanceView, candidate.RequiredPermission);
    }

    /// <summary>
    /// BAŞKA ŞİRKETİN ÇEKİ SAYILMAZ. Mevcut brifing kaynağı şirket
    /// filtresi taşımıyordu; yeni kaynaklar baştan şirket bazlı.
    /// </summary>
    [Fact]
    public async Task ChequeSource_IsScopedToTheCompany()
    {
        var mine = Guid.NewGuid().ToString("N")[..8];
        var other = Guid.NewGuid().ToString("N")[..8];

        var context = await CreateContextAsync(mine);
        var foreign = await CreateContextAsync(other);

        await AddChequeAsync(foreign, ChequeDirection.Issued, ChequeStatus.Issued,
            77_000m, 2);

        var candidates = await BuildAsync<ChequeDueNotificationSource>(context.CompanyId);

        Assert.Empty(candidates);
    }

    // ---------------- Kredi taksiti ----------------

    private async Task<Guid> AddLoanAsync(
        Context context, BankLoanStatus status, int dueInDays, bool isPaid)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var loan = new BankLoan
        {
            CompanyId = context.CompanyId,
            Name = $"Kredi {Guid.NewGuid():N}"[..12],
            Status = status,
            PrincipalAmount = 120_000m,
            MonthlyInterestRate = 3m,
            InstallmentCount = 12,
            DrawdownDate = Today,
            FirstInstallmentDate = Today.AddDays(dueInDays)
        };

        db.BankLoans.Add(loan);
        await db.SaveChangesAsync();

        db.BankLoanInstallments.Add(new BankLoanInstallment
        {
            BankLoanId = loan.Id,
            Number = 1,
            DueDate = Today.AddDays(dueInDays),
            PrincipalAmount = 8_000m,
            InterestAmount = 2_500m,
            IsPaid = isPaid
        });

        await db.SaveChangesAsync();

        return loan.Id;
    }

    [Fact]
    public async Task LoanSource_ProducesCandidateForAnUnpaidInstallment()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddLoanAsync(context, BankLoanStatus.Active, 3, isPaid: false);

        var candidate = (await BuildAsync<LoanInstallmentNotificationSource>(
            context.CompanyId)).Single();

        Assert.Equal(LoanInstallmentNotificationSource.TypeKey, candidate.Type);

        // Anapara + faiz tek tutarda: kullanıcının hesaptan çıkacak
        // parası bu.
        Assert.Contains("10.500", candidate.AmountDetail ?? "");
    }

    /// <summary>
    /// ÖDENMİŞ TAKSİT ve İPTAL KREDİ aday üretmez — kapatılan bir
    /// kaydın hatırlatması da kalkmalı.
    /// </summary>
    [Fact]
    public async Task PaidInstallmentAndCancelledLoan_ProduceNoCandidates()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        await AddLoanAsync(context, BankLoanStatus.Active, 2, isPaid: true);
        await AddLoanAsync(context, BankLoanStatus.Cancelled, 2, isPaid: false);

        var candidates = await BuildAsync<LoanInstallmentNotificationSource>(
            context.CompanyId);

        Assert.Empty(candidates);
    }

    // ---------------- Kart ekstresi ----------------

    private async Task<Guid> AddCardWithExpenseAsync(
        Context context, CreditCardOwnership ownership, int dueDay, decimal amount)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await Services.Expenses.ExpenseCategoryProvisioner.EnsureAsync(
            db, context.CompanyId, CancellationToken.None);

        var categoryId = await db.ExpenseCategories
            .Where(x => x.CompanyId == context.CompanyId &&
                        x.Code == Services.Expenses.ExpenseCategoryCatalog.Supplies)
            .Select(x => x.Id)
            .SingleAsync();

        Guid? partnerId = null;

        if (ownership == CreditCardOwnership.Personal)
        {
            var partner = new Models.Expenses.PartnerAccount
            {
                CompanyId = context.CompanyId,
                FullName = "Kart Sahibi"
            };

            db.PartnerAccounts.Add(partner);
            await db.SaveChangesAsync();

            partnerId = partner.Id;
        }

        var card = new CreditCard
        {
            CompanyId = context.CompanyId,
            Name = $"Kart {Guid.NewGuid():N}"[..10],
            Ownership = ownership,
            PartnerAccountId = partnerId,
            StatementDay = 1,
            DueDay = dueDay,
            IsActive = true
        };

        db.CreditCards.Add(card);
        await db.SaveChangesAsync();

        db.ExpenseEntries.Add(new Models.Expenses.ExpenseEntry
        {
            CompanyId = context.CompanyId,
            CenterType = Models.Expenses.ExpenseCenterType.Branch,
            BranchId = context.BranchId,
            ExpenseCategoryId = categoryId,
            ExpenseDate = Today,
            Amount = amount,
            Description = "Kart harcaması",
            PaymentMethod = Models.Expenses.ExpensePaymentMethod.CreditCard,
            DocumentType = Models.Expenses.ExpenseDocumentType.Invoice,
            CreditCardId = card.Id,
            PartnerAccountId = partnerId
        });

        await db.SaveChangesAsync();

        return card.Id;
    }

    /// <summary>
    /// ŞAHIS KARTI HATIRLATMA ÜRETMEZ: ekstreyi kişi ödüyor, şirketin
    /// nakdi çıkmıyor. Hatırlatma şirketin yapacağı iş için.
    /// </summary>
    [Fact]
    public async Task PersonalCardStatement_ProducesNoCandidate()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        // Son ödeme günü ufkun içine düşsün.
        var dueDay = Math.Min(28, Today.AddDays(3).Day);

        await AddCardWithExpenseAsync(
            context, CreditCardOwnership.Personal, dueDay, 5_000m);

        var candidates = await BuildAsync<CreditCardStatementNotificationSource>(
            context.CompanyId);

        Assert.Empty(candidates);
    }

    // ---------------- Harcırah mahsubu ----------------

    /// <summary>
    /// Mahsup bekleyen görev aday üretiyor ve TUTAR ELDEN
    /// MASKESİNDE: harcırah tutarını saha personeli görmemeli.
    /// </summary>
    [Fact]
    public async Task DutySettlementCandidate_MasksTheAmountBehindExtraPaymentView()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var personnel = await TestDataFactory.CreatePersonnelAsync(
                db, context.CompanyId, suffix);

            db.PersonnelDuties.Add(new PersonnelDuty
            {
                PersonnelId = personnel.Id,
                TargetProjectId = await db.Projects
                    .Where(x => x.CompanyId == context.CompanyId)
                    .Select(x => x.Id)
                    .FirstAsync(),
                DutyType = PersonnelDutyType.Work,
                Status = PersonnelDutyStatus.Approved,
                StartDate = Today.AddDays(-10),
                EndDate = Today.AddDays(-8),
                DailyAllowance = 1_500m,
                ReceiptAmount = 900m
            });

            await db.SaveChangesAsync();
        }

        var candidate = (await BuildAsync<DutySettlementNotificationSource>(
            context.CompanyId)).Single();

        Assert.Equal(DutySettlementNotificationSource.TypeKey, candidate.Type);

        // Güvenli metinde tutar yok.
        Assert.DoesNotContain("3.600", candidate.Detail ?? "");

        // Fark = 3 gün x 1.500 − 900 = 3.600
        Assert.Contains("3.600", candidate.AmountDetail ?? "");
        Assert.Equal(
            PermissionCatalog.Keys.ExtraPaymentView, candidate.AmountPermission);
    }

    /// <summary>
    /// Fişi harcırahı karşılayan görev aday ÜRETMEZ: mahsup bekleyen
    /// bir fark yok.
    /// </summary>
    [Fact]
    public async Task DutyWithNoSettlementGap_ProducesNoCandidate()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var context = await CreateContextAsync(suffix);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var personnel = await TestDataFactory.CreatePersonnelAsync(
                db, context.CompanyId, suffix);

            db.PersonnelDuties.Add(new PersonnelDuty
            {
                PersonnelId = personnel.Id,
                TargetProjectId = await db.Projects
                    .Where(x => x.CompanyId == context.CompanyId)
                    .Select(x => x.Id)
                    .FirstAsync(),
                DutyType = PersonnelDutyType.Work,
                Status = PersonnelDutyStatus.Approved,
                StartDate = Today.AddDays(-5),
                EndDate = Today.AddDays(-5),
                DailyAllowance = 1_000m,
                ReceiptAmount = 1_000m
            });

            await db.SaveChangesAsync();
        }

        var candidates = await BuildAsync<DutySettlementNotificationSource>(
            context.CompanyId);

        Assert.Empty(candidates);
    }
}
