using EnderunAI.Api.Data;
using EnderunAI.Api.Formatting;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.FinancialInstruments;
using EnderunAI.Api.Models.Notifications;
using EnderunAI.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Notifications.Sources;

/// <summary>
/// Finans bildirimlerinin ortak biçimlendirmesi.
///
/// TUTAR AYRI: <c>Detail</c> tutarsız, <c>AmountDetail</c> tutarlı.
/// Tek metin üretip sonra tutarı ayıklamaya çalışmak kırılgan olurdu.
/// </summary>
internal static class FinanceNotificationText
{
    public static string Money(decimal value) => $"{TurkishFormat.Amount(value)} TL";
}

/// <summary>
/// Vadesi yaklaşan ve vadesi geçmiş ÇEKLER.
///
/// Her çek AYRI bildirim: brifingdeki gibi "5 çekin vadesi geliyor"
/// diye toplamak özet için iyi ama hatırlatma için değil — kullanıcı
/// tek tek okuyup kapatabilmeli, biri halledilince o satır kapanmalı.
/// </summary>
public sealed class ChequeDueNotificationSource(AppDbContext db) : INotificationSource
{
    public const string TypeKey = "cheque.due";

    private static readonly ChequeStatus[] OpenStatuses =
    [
        ChequeStatus.Portfolio, ChequeStatus.AtBank,
        ChequeStatus.AtFactoring, ChequeStatus.Issued
    ];

    public string Key => "cek_vadesi";

    public IReadOnlyCollection<string> OwnedTypes => [TypeKey];

    public async Task<IReadOnlyList<NotificationCandidate>> BuildAsync(
        NotificationScanContext context, CancellationToken cancellationToken)
    {
        var until = context.Today.AddDays(NotificationWindow.DueEarlyDays);

        var rows = await db.Cheques
            .AsNoTracking()
            .Where(x => x.CompanyId == context.CompanyId &&
                        OpenStatuses.Contains(x.Status) &&
                        x.DueDate <= until)
            .Select(x => new
            {
                x.Id,
                x.Direction,
                x.ChequeNumber,
                x.BankName,
                x.Amount,
                x.DueDate
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row =>
        {
            var days = (row.DueDate.Date - context.Today).Days;

            var verb = row.Direction == ChequeDirection.Issued
                ? "Verilen çek ödenecek"
                : "Alınan çek tahsil edilecek";

            return new NotificationCandidate(
                TypeKey,
                row.Id,
                row.DueDate.ToString("yyyy-MM-dd"),
                $"{verb} — {NotificationWindow.DueLabel(days)}",
                $"{row.BankName} · çek no {row.ChequeNumber}",
                NotificationWindow.SeverityForDue(days),
                "/finans/cekler",
                row.DueDate,
                $"{row.BankName} · çek no {row.ChequeNumber} · " +
                FinanceNotificationText.Money(row.Amount),
                PermissionCatalog.Keys.FinanceView,
                PermissionCatalog.Keys.FinanceView);
        }).ToList();
    }
}

/// <summary>Vadesi yaklaşan tedarikçi ve satış faturaları.</summary>
public sealed class InvoiceDueNotificationSource(AppDbContext db) : INotificationSource
{
    public const string SupplierTypeKey = "invoice.supplier.due";
    public const string SalesTypeKey = "invoice.sales.due";

    public string Key => "fatura_vadesi";

    public IReadOnlyCollection<string> OwnedTypes => [SupplierTypeKey, SalesTypeKey];

    public async Task<IReadOnlyList<NotificationCandidate>> BuildAsync(
        NotificationScanContext context, CancellationToken cancellationToken)
    {
        var until = context.Today.AddDays(NotificationWindow.DueEarlyDays);

        var items = new List<NotificationCandidate>();

        // ÖDENMİŞ FATURA UYARI ÜRETMEZ: onaylanmamış ya da kapanmış
        // faturanın vadesi hatırlatma değildir.
        var supplier = await db.SupplierInvoices
            .AsNoTracking()
            .Where(x => x.CompanyId == context.CompanyId &&
                        x.Status == SupplierInvoiceStatus.Approved &&
                        x.DueDate != null && x.DueDate <= until)
            .Select(x => new
            {
                x.Id,
                x.InvoiceNumber,
                x.GrandTotal,
                x.DueDate,
                Supplier = x.SupplierCurrentAccount.Title
            })
            .ToListAsync(cancellationToken);

        foreach (var row in supplier)
        {
            var due = row.DueDate!.Value;
            var days = (due.Date - context.Today).Days;

            items.Add(new NotificationCandidate(
                SupplierTypeKey,
                row.Id,
                due.ToString("yyyy-MM-dd"),
                $"Tedarikçi faturası ödenecek — {NotificationWindow.DueLabel(days)}",
                $"{row.Supplier} · fatura {row.InvoiceNumber}",
                NotificationWindow.SeverityForDue(days),
                "/muhasebe/faturalar",
                due,
                $"{row.Supplier} · fatura {row.InvoiceNumber} · " +
                FinanceNotificationText.Money(row.GrandTotal),
                PermissionCatalog.Keys.FinanceView,
                PermissionCatalog.Keys.AccountingView));
        }

        var sales = await db.SalesInvoices
            .AsNoTracking()
            // İPTAL FATURA HATIRLATMA ÜRETMEZ; taslak da henüz
            // tahsil edilecek bir alacak değil.
            .Where(x => x.CompanyId == context.CompanyId &&
                        x.Status == SalesInvoiceStatus.Posted &&
                        x.DueDate != null && x.DueDate <= until)
            .Select(x => new
            {
                x.Id,
                Number = x.OfficialInvoiceNumber ?? x.InternalNumber,
                x.GrandTotal,
                x.DueDate,
                Customer = x.CustomerCurrentAccount.Title
            })
            .ToListAsync(cancellationToken);

        foreach (var row in sales)
        {
            var due = row.DueDate!.Value;
            var days = (due.Date - context.Today).Days;

            items.Add(new NotificationCandidate(
                SalesTypeKey,
                row.Id,
                due.ToString("yyyy-MM-dd"),
                $"Satış faturası tahsil edilecek — {NotificationWindow.DueLabel(days)}",
                $"{row.Customer} · fatura {row.Number}",
                NotificationWindow.SeverityForDue(days),
                "/muhasebe/satis-faturalari",
                due,
                $"{row.Customer} · fatura {row.Number} · " +
                FinanceNotificationText.Money(row.GrandTotal),
                PermissionCatalog.Keys.FinanceView,
                PermissionCatalog.Keys.AccountingView));
        }

        return items;
    }
}

/// <summary>
/// Ödenmemiş KREDİ TAKSİTLERİ.
///
/// Ödenmiş taksit ve iptal kredi sayılmaz — kapatılan bir kaydın
/// hatırlatması da kalkmalı.
/// </summary>
public sealed class LoanInstallmentNotificationSource(AppDbContext db)
    : INotificationSource
{
    public const string TypeKey = "loan.installment.due";

    public string Key => "kredi_taksiti";

    public IReadOnlyCollection<string> OwnedTypes => [TypeKey];

    public async Task<IReadOnlyList<NotificationCandidate>> BuildAsync(
        NotificationScanContext context, CancellationToken cancellationToken)
    {
        var until = context.Today.AddDays(NotificationWindow.DueEarlyDays);

        var rows = await db.BankLoanInstallments
            .AsNoTracking()
            .Where(x => x.BankLoan.CompanyId == context.CompanyId &&
                        x.BankLoan.Status != BankLoanStatus.Cancelled &&
                        !x.IsPaid &&
                        x.DueDate <= until)
            .Select(x => new
            {
                x.Id,
                x.Number,
                x.DueDate,
                x.PrincipalAmount,
                x.InterestAmount,
                LoanName = x.BankLoan.Name
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row =>
        {
            var days = (row.DueDate.Date - context.Today).Days;
            var total = decimal.Round(row.PrincipalAmount + row.InterestAmount, 2);

            return new NotificationCandidate(
                TypeKey,
                row.Id,
                row.DueDate.ToString("yyyy-MM-dd"),
                $"Kredi taksiti ödenecek — {NotificationWindow.DueLabel(days)}",
                $"{row.LoanName} · {row.Number}. taksit",
                NotificationWindow.SeverityForDue(days),
                "/finans/finansal-araclar",
                row.DueDate,
                $"{row.LoanName} · {row.Number}. taksit · " +
                FinanceNotificationText.Money(total),
                PermissionCatalog.Keys.FinanceView,
                PermissionCatalog.Keys.FinanceView);
        }).ToList();
    }
}

/// <summary>
/// KREDİ KARTI EKSTRESİ son ödeme günü.
///
/// Ekstre ayrı tabloda değil, harcamalardan türüyor
/// (<see cref="FinancialInstruments.CreditCardService"/>). Bildirim
/// de aynı kaynaktan besleniyor; ikinci bir hesap yazılsaydı ekstre
/// tutarı iki yerde iki türlü çıkardı.
///
/// ŞAHIS KARTI SAYILMAZ: ekstreyi kişi ödüyor, şirketin nakdi
/// çıkmıyor. Hatırlatma şirketin yapacağı iş için.
/// </summary>
public sealed class CreditCardStatementNotificationSource(
    FinancialInstruments.CreditCardService cards) : INotificationSource
{
    public const string TypeKey = "creditcard.statement.due";

    public string Key => "kart_ekstresi";

    public IReadOnlyCollection<string> OwnedTypes => [TypeKey];

    public async Task<IReadOnlyList<NotificationCandidate>> BuildAsync(
        NotificationScanContext context, CancellationToken cancellationToken)
    {
        var until = context.Today.AddDays(NotificationWindow.DueEarlyDays);

        // Harcamalar geçmiş aylarda; son ödeme günü ileride olabilir.
        var statements = await cards.GetStatementsAsync(
            context.CompanyId,
            context.Today.AddMonths(-3),
            until,
            includePersonal: false,
            cancellationToken);

        return statements
            .Where(x => x.DueDate <= until && x.Amount > 0m)
            .Select(x =>
            {
                var days = (x.DueDate.Date - context.Today).Days;

                return new NotificationCandidate(
                    TypeKey,
                    x.CreditCardId,
                    x.DueDate.ToString("yyyy-MM-dd"),
                    $"Kart ekstresi ödenecek — {NotificationWindow.DueLabel(days)}",
                    $"{x.CardName} · {x.ItemCount} harcama",
                    NotificationWindow.SeverityForDue(days),
                    "/finans/finansal-araclar",
                    x.DueDate,
                    $"{x.CardName} · {x.ItemCount} harcama · " +
                    FinanceNotificationText.Money(x.Amount),
                    PermissionCatalog.Keys.FinanceView,
                    PermissionCatalog.Keys.FinanceView);
            })
            .ToList();
    }
}

/// <summary>
/// MAHSUP BEKLEYEN HARCIRAH.
///
/// Vadesi yok, bu yüzden gün eşiği de yok: mahsup bekleyen bir görev
/// masrafı kapanana kadar açık kalmalı.
///
/// TUTAR ELDEN MASKESİNDE: harcırah tutarları extra_payment.view'a
/// tabi — saha personeli görevi görür, tutarı görmez.
/// </summary>
public sealed class DutySettlementNotificationSource(AppDbContext db)
    : INotificationSource
{
    public const string TypeKey = "duty.settlement.pending";

    public string Key => "harcirah_mahsup";

    public IReadOnlyCollection<string> OwnedTypes => [TypeKey];

    public async Task<IReadOnlyList<NotificationCandidate>> BuildAsync(
        NotificationScanContext context, CancellationToken cancellationToken)
    {
        // SettlementPending hesaplanan bir özellik; SQL'e çevrilemez.
        // Aday kümesi sorguda daraltılıp karar bellekte veriliyor.
        var rows = await db.PersonnelDuties
            .AsNoTracking()
            .Where(x => x.Personnel.CompanyId == context.CompanyId &&
                        x.Status == PersonnelDutyStatus.Approved &&
                        x.SettlementDecision == null)
            .Select(x => new
            {
                x.Id,
                x.StartDate,
                x.EndDate,
                x.DailyAllowance,
                x.ReceiptAmount,
                PersonnelName = x.Personnel.FirstName + " " + x.Personnel.LastName
            })
            .ToListAsync(cancellationToken);

        var items = new List<NotificationCandidate>();

        foreach (var row in rows)
        {
            var dayCount = Math.Max(1, (row.EndDate.Date - row.StartDate.Date).Days + 1);
            var total = row.DailyAllowance * dayCount;
            var gap = total - row.ReceiptAmount;

            if (gap <= 0m)
                continue;

            items.Add(new NotificationCandidate(
                TypeKey,
                row.Id,
                row.StartDate.ToString("yyyy-MM-dd"),
                "Harcırah mahsubu bekliyor",
                $"{row.PersonnelName} · " +
                $"{row.StartDate:dd.MM.yyyy}–{row.EndDate:dd.MM.yyyy}",
                NotificationSeverity.Warning,
                "/insan-kaynaklari/gorevlendirmeler",
                null,
                $"{row.PersonnelName} · " +
                $"{row.StartDate:dd.MM.yyyy}–{row.EndDate:dd.MM.yyyy} · " +
                $"fark {FinanceNotificationText.Money(gap)}",
                PermissionCatalog.Keys.ExtraPaymentView,
                PermissionCatalog.Keys.PersonnelView));
        }

        return items;
    }
}
