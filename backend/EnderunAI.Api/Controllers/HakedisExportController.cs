using ClosedXML.Excel;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Hakedis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EnderunAI.Api.Formatting;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// Hakediş Excel çıktısı — NATURA formatına uygun sayfalar: imalat
/// icmali (bölüm bazlı), poz detayı, ihzarat, kesinti icmali (alt
/// kalemli), üst hesap ve ödeme dağılımı.
///
/// PDF için ayrı bir kütüphane eklenmedi: çıktı arayüzdeki yazdırma
/// sayfasından (logo antetli) alınıyor. Buradaki Excel, üzerinde
/// çalışılabilir hâli.
/// </summary>
[ApiController]
[Authorize]
[Route("api/hakedis-export")]
public sealed class HakedisExportController(AppDbContext db) : ControllerBase
{
    private const string MoneyFormat = "#,##0.00";

    [HttpGet("{id:guid}/excel")]
    [RequirePermission(PermissionCatalog.Keys.HakedisView)]
    public async Task<IActionResult> Excel(Guid id, CancellationToken cancellationToken)
    {
        var payment = await db.ProgressPayments
            .AsNoTracking()
            .Include(x => x.Sections)
            .Include(x => x.Items)
            .Include(x => x.Deductions).ThenInclude(x => x.Lines)
            .Include(x => x.AdvanceMaterials)
            .Include(x => x.PaymentPlans)
            .Include(x => x.Project)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (payment is null)
            return NotFound(new { message = "Hakediş bulunamadı." });

        var company = await db.Companies
            .AsNoTracking()
            .Where(x => x.Id == payment.CompanyId)
            .Select(x => new { x.Name })
            .SingleOrDefaultAsync(cancellationToken);

        using var workbook = new XLWorkbook();

        BuildSummarySheet(workbook, payment, company?.Name);
        BuildItemsSheet(workbook, payment);

        if (payment.AdvanceMaterials.Count > 0)
            BuildAdvanceMaterialsSheet(workbook, payment);

        BuildDeductionsSheet(workbook, payment);

        if (payment.PaymentPlans.Count > 0)
            BuildPaymentSheet(workbook, payment);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Hakedis-{payment.ProgressPaymentNumber}.xlsx");
    }

    private static void BuildSummarySheet(
        XLWorkbook workbook, ProgressPayment payment, string? companyName)
    {
        var sheet = workbook.Worksheets.Add("Üst Hesap");
        var row = 1;

        sheet.Cell(row, 1).Value = companyName ?? "";
        sheet.Cell(row, 1).Style.Font.Bold = true;
        sheet.Cell(row, 1).Style.Font.FontSize = 14;
        row += 2;

        void Info(string label, string value)
        {
            sheet.Cell(row, 1).Value = label;
            sheet.Cell(row, 1).Style.Font.Bold = true;
            sheet.Cell(row, 2).Value = value;
            row++;
        }

        Info("Proje", $"{payment.Project.Code} — {payment.Project.Name}");
        Info("Hakediş No", payment.ProgressPaymentNumber);
        Info("Dönem", payment.PeriodNumber.ToString());
        Info("Tanzim Tarihi", payment.ProgressPaymentDate.ToString("dd.MM.yyyy"));

        if (payment.PeriodStartDate is DateTime start && payment.PeriodEndDate is DateTime end)
            Info("Dönem Aralığı", $"{start:dd.MM.yyyy} - {end:dd.MM.yyyy}");

        row++;

        void Money(string label, decimal value, bool bold = false)
        {
            sheet.Cell(row, 1).Value = label;
            sheet.Cell(row, 2).Value = value;
            sheet.Cell(row, 2).Style.NumberFormat.Format = MoneyFormat;

            if (bold)
            {
                sheet.Cell(row, 1).Style.Font.Bold = true;
                sheet.Cell(row, 2).Style.Font.Bold = true;
            }

            row++;
        }

        Money("Kümülatif İmalat", payment.CumulativeWorkAmount);
        Money("Açık İhzarat", payment.CumulativeAdvanceMaterialAmount);
        Money("Kümülatif Toplam", payment.CumulativeAmount, bold: true);
        Money("Önceki Hakedişler (Minha)", -payment.PreviousAmount);
        Money("Bu Hakediş", payment.CurrentAmount, bold: true);

        if (payment.PriceDifferenceAmount != 0m)
            Money("Fiyat Farkı", payment.PriceDifferenceAmount);

        Money($"KDV (%{TurkishFormat.Whole(payment.VatRate)})", payment.VatAmount);
        Money("Brüt Tutar", payment.GrossPayableAmount, bold: true);

        if (payment.WithholdingAmount > 0m)
        {
            Money(
                $"KDV Tevkifatı ({payment.WithholdingNumerator}/{payment.WithholdingDenominator})",
                -payment.WithholdingAmount);
        }

        if (payment.IncomeTaxWithholdingAmount > 0m)
            Money($"Stopaj (%{TurkishFormat.Rate(payment.IncomeTaxWithholdingRate)})", -payment.IncomeTaxWithholdingAmount);

        Money("Kesintiler Toplamı", -payment.TotalDeductionAmount);
        Money("TAHSİL EDİLECEK", payment.NetPayableAmount, bold: true);

        row++;
        sheet.Cell(row, 1).Value = "Yazı ile";
        sheet.Cell(row, 1).Style.Font.Bold = true;
        sheet.Cell(row, 2).Value = TurkishNumberToWords.Convert(payment.NetPayableAmount);

        sheet.Column(1).Width = 32;
        sheet.Column(2).Width = 44;
    }

    private static void BuildItemsSheet(XLWorkbook workbook, ProgressPayment payment)
    {
        var sheet = workbook.Worksheets.Add("İmalat İcmali");

        var headers = new[]
        {
            "Bölüm", "Poz", "Açıklama", "Birim", "Sözleşme Mik.",
            "Önceki Mik.", "Bu Dönem Mik.", "Genel Toplam Mik.",
            "Malzeme BF", "Montaj BF", "GG&K BF", "Birim Fiyat",
            "Malzeme", "Montaj", "GG&K",
            "Önceki Tutar", "Bu Dönem", "Genel Toplam", "Pursantaj %"
        };

        WriteHeader(sheet, headers);

        var sectionsById = payment.Sections.ToDictionary(x => x.Id);
        var row = 2;

        foreach (var item in payment.Items.OrderBy(x => x.LineNumber))
        {
            var sectionName = item.ProgressPaymentSectionId is Guid sectionId &&
                              sectionsById.TryGetValue(sectionId, out var section)
                ? section.Name
                : "";

            sheet.Cell(row, 1).Value = sectionName;
            sheet.Cell(row, 2).Value = item.PositionCode;
            sheet.Cell(row, 3).Value = item.Description;
            sheet.Cell(row, 4).Value = item.Unit;
            sheet.Cell(row, 5).Value = item.ContractQuantity;
            sheet.Cell(row, 6).Value = item.PreviousQuantity;
            sheet.Cell(row, 7).Value = item.CurrentQuantity;
            sheet.Cell(row, 8).Value = item.CumulativeQuantity;
            sheet.Cell(row, 9).Value = item.MaterialUnitPrice;
            sheet.Cell(row, 10).Value = item.LaborUnitPrice;
            sheet.Cell(row, 11).Value = item.OverheadUnitPrice;
            sheet.Cell(row, 12).Value = item.UnitPrice;
            sheet.Cell(row, 13).Value = item.MaterialAmount;
            sheet.Cell(row, 14).Value = item.LaborAmount;
            sheet.Cell(row, 15).Value = item.OverheadAmount;
            sheet.Cell(row, 16).Value = item.PreviousAmount;
            sheet.Cell(row, 17).Value = item.CurrentAmount;
            sheet.Cell(row, 18).Value = item.CumulativeAmount;
            sheet.Cell(row, 19).Value = item.CompletionRate;

            sheet.Range(row, 5, row, 19).Style.NumberFormat.Format = MoneyFormat;
            row++;
        }

        // Bölüm icmali ayrı bir blok olarak altta.
        if (payment.Sections.Count > 0)
        {
            row += 2;
            sheet.Cell(row, 1).Value = "BÖLÜM İCMALİ";
            sheet.Cell(row, 1).Style.Font.Bold = true;
            row++;

            sheet.Cell(row, 1).Value = "Bölüm";
            sheet.Cell(row, 2).Value = "Malzeme";
            sheet.Cell(row, 3).Value = "Montaj";
            sheet.Cell(row, 4).Value = "GG&K";
            sheet.Cell(row, 5).Value = "Bu Dönem";
            sheet.Cell(row, 6).Value = "Genel Toplam";
            sheet.Range(row, 1, row, 6).Style.Font.Bold = true;
            row++;

            foreach (var section in payment.Sections.OrderBy(x => x.Order))
            {
                sheet.Cell(row, 1).Value = section.Name;
                sheet.Cell(row, 2).Value = section.MaterialAmount;
                sheet.Cell(row, 3).Value = section.LaborAmount;
                sheet.Cell(row, 4).Value = section.OverheadAmount;
                sheet.Cell(row, 5).Value = section.CurrentAmount;
                sheet.Cell(row, 6).Value = section.CumulativeAmount;
                sheet.Range(row, 2, row, 6).Style.NumberFormat.Format = MoneyFormat;
                row++;
            }
        }

        sheet.Columns().AdjustToContents();
    }

    private static void BuildAdvanceMaterialsSheet(
        XLWorkbook workbook, ProgressPayment payment)
    {
        var sheet = workbook.Worksheets.Add("İhzarat");

        WriteHeader(sheet,
        [
            "Poz", "Açıklama", "Birim", "Miktar", "Birim Fiyat",
            "Bedellendirme %", "Tutar", "Mahsup Edilen", "Açık Bakiye"
        ]);

        var row = 2;

        foreach (var item in payment.AdvanceMaterials.OrderBy(x => x.LineNumber))
        {
            sheet.Cell(row, 1).Value = item.PositionCode;
            sheet.Cell(row, 2).Value = item.Description;
            sheet.Cell(row, 3).Value = item.Unit;
            sheet.Cell(row, 4).Value = item.Quantity;
            sheet.Cell(row, 5).Value = item.UnitPrice;
            sheet.Cell(row, 6).Value = item.ValuationRate;
            sheet.Cell(row, 7).Value = item.Amount;
            sheet.Cell(row, 8).Value = item.OffsetAmount;
            sheet.Cell(row, 9).Value = item.Amount - item.OffsetAmount;

            sheet.Range(row, 4, row, 9).Style.NumberFormat.Format = MoneyFormat;
            row++;
        }

        sheet.Columns().AdjustToContents();
    }

    private static void BuildDeductionsSheet(XLWorkbook workbook, ProgressPayment payment)
    {
        var sheet = workbook.Worksheets.Add("Kesinti İcmali");

        WriteHeader(sheet,
        [
            "Kesinti", "Oran %", "Kümülatif Taban",
            "Önceden Kesilen", "Bu Hakediş", "Kümülatif"
        ]);

        var row = 2;

        foreach (var deduction in payment.Deductions.OrderBy(x => x.LineNumber))
        {
            sheet.Cell(row, 1).Value = deduction.Description;
            sheet.Cell(row, 2).Value = deduction.Rate;
            sheet.Cell(row, 3).Value = deduction.CumulativeBaseAmount;
            sheet.Cell(row, 4).Value = deduction.PreviousAmount;
            sheet.Cell(row, 5).Value = deduction.Amount;
            sheet.Cell(row, 6).Value = deduction.CumulativeAmount;
            sheet.Range(row, 2, row, 6).Style.NumberFormat.Format = MoneyFormat;
            sheet.Cell(row, 1).Style.Font.Bold = true;
            row++;

            // Alt kalemler ana kesintinin altında girintili.
            foreach (var line in deduction.Lines.OrderBy(x => x.LineNumber))
            {
                sheet.Cell(row, 1).Value = $"    {line.Name} " +
                    $"({TurkishFormat.Whole(line.Quantity)} × {TurkishFormat.Amount(line.UnitPrice)}, KDV %{TurkishFormat.Whole(line.VatRate)})";
                sheet.Cell(row, 5).Value = line.GrossAmount;
                sheet.Cell(row, 5).Style.NumberFormat.Format = MoneyFormat;
                row++;
            }
        }

        row++;
        sheet.Cell(row, 1).Value = "TOPLAM KESİNTİ";
        sheet.Cell(row, 5).Value = payment.TotalDeductionAmount;
        sheet.Cell(row, 5).Style.NumberFormat.Format = MoneyFormat;
        sheet.Range(row, 1, row, 6).Style.Font.Bold = true;

        sheet.Columns().AdjustToContents();
    }

    private static void BuildPaymentSheet(XLWorkbook workbook, ProgressPayment payment)
    {
        var sheet = workbook.Worksheets.Add("Ödeme Dağılımı");

        WriteHeader(sheet, ["Ödeme Şekli", "Oran %", "Tutar", "Vade Günü", "Vade Tarihi"]);

        var row = 2;

        foreach (var plan in payment.PaymentPlans.OrderBy(x => x.LineNumber))
        {
            sheet.Cell(row, 1).Value = plan.PaymentType == ProgressPaymentPaymentType.Cash
                ? "Nakit"
                : "Vadeli Çek";
            sheet.Cell(row, 2).Value = plan.Rate;
            sheet.Cell(row, 3).Value = plan.Amount;
            sheet.Cell(row, 4).Value = plan.MaturityDays ?? 0;
            sheet.Cell(row, 5).Value = plan.DueDate?.ToString("dd.MM.yyyy") ?? "";
            sheet.Range(row, 2, row, 3).Style.NumberFormat.Format = MoneyFormat;
            row++;
        }

        row++;
        sheet.Cell(row, 1).Value = "TOPLAM";
        sheet.Cell(row, 3).Value = payment.PaymentPlans.Sum(x => x.Amount);
        sheet.Cell(row, 3).Style.NumberFormat.Format = MoneyFormat;
        sheet.Range(row, 1, row, 5).Style.Font.Bold = true;

        sheet.Columns().AdjustToContents();
    }

    private static void WriteHeader(IXLWorksheet sheet, string[] headers)
    {
        for (var index = 0; index < headers.Length; index++)
            sheet.Cell(1, index + 1).Value = headers[index];

        var range = sheet.Range(1, 1, 1, headers.Length);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.LightGray;
        sheet.SheetView.FreezeRows(1);
    }
}
