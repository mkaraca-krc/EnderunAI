using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace EnderunAI.Api.Services.Procurement;

public sealed record GeneratedPdfDocument(byte[] Content, string FileName, string VerificationCode);

public interface IProcurementPdfService
{
    Task<GeneratedPdfDocument> GeneratePurchaseOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
}

public sealed class ProcurementPdfService(AppDbContext db) : IProcurementPdfService
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public async Task<GeneratedPdfDocument> GeneratePurchaseOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await db.PurchaseOrders
            .AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.Project)
            .Include(x => x.SupplierCurrentAccount)
            .Include(x => x.Items)
                .ThenInclude(x => x.Material)
            .FirstOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Satın alma siparişi bulunamadı.");

        var verificationCode = CreateVerificationCode(order.Id, order.OrderNumber, order.UpdatedAtUtc ?? order.CreatedAtUtc);
        var document = new PdfDocument();
        document.Info.Title = $"Satın Alma Siparişi {order.OrderNumber}";
        document.Info.Author = "Enderun AI";
        document.Info.Subject = "Satın Alma Siparişi";

        var page = document.AddPage();
        page.Size = PdfSharpCore.PageSize.A4;
        var gfx = XGraphics.FromPdfPage(page);

        var titleFont = new XFont("Arial", 16, XFontStyle.Bold);
        var headerFont = new XFont("Arial", 10, XFontStyle.Bold);
        var normalFont = new XFont("Arial", 9, XFontStyle.Regular);
        var smallFont = new XFont("Arial", 7, XFontStyle.Regular);
        var linePen = new XPen(XColors.Black, 0.5);

        double y = 30;
        gfx.DrawString(order.Company.TradeName ?? order.Company.Name, titleFont, XBrushes.Black, new XRect(35, y, 525, 24), XStringFormats.TopLeft);
        y += 24;
        gfx.DrawString("SATIN ALMA SİPARİŞİ", new XFont("Arial", 14, XFontStyle.Bold), XBrushes.Black, new XRect(35, y, 525, 22), XStringFormats.TopCenter);
        y += 28;

        DrawInfoRow(gfx, headerFont, normalFont, "Sipariş No", order.OrderNumber, "Sipariş Tarihi", order.OrderDateUtc.ToString("dd.MM.yyyy", Tr), y);
        y += 18;
        DrawInfoRow(gfx, headerFont, normalFont, "Proje", $"{order.Project.Code} - {order.Project.Name}", "Durum", order.Status.ToString(), y);
        y += 18;
        DrawInfoRow(gfx, headerFont, normalFont, "Tedarikçi", order.SupplierCurrentAccount.Title, "Para Birimi", order.CurrencyCode, y);
        y += 18;
        DrawInfoRow(gfx, headerFont, normalFont, "Vergi No", order.SupplierCurrentAccount.TaxNumber ?? "-", "Teslim Tarihi", order.DeliveryDateUtc?.ToString("dd.MM.yyyy", Tr) ?? "-", y);
        y += 28;

        var columns = new[] { 30d, 80d, 255d, 305d, 365d, 435d, 520d };
        gfx.DrawRectangle(linePen, 35, y, 525, 22);
        DrawCell(gfx, headerFont, "No", 35, y, 30, 22, XStringFormats.Center);
        DrawCell(gfx, headerFont, "Malzeme", 65, y, 175, 22, XStringFormats.CenterLeft);
        DrawCell(gfx, headerFont, "Miktar", 240, y, 65, 22, XStringFormats.CenterRight);
        DrawCell(gfx, headerFont, "Birim", 305, y, 60, 22, XStringFormats.Center);
        DrawCell(gfx, headerFont, "Birim Fiyat", 365, y, 70, 22, XStringFormats.CenterRight);
        DrawCell(gfx, headerFont, "İskonto", 435, y, 55, 22, XStringFormats.CenterRight);
        DrawCell(gfx, headerFont, "Tutar", 490, y, 70, 22, XStringFormats.CenterRight);
        y += 22;

        decimal subtotal = 0;
        var index = 1;
        foreach (var item in order.Items.OrderBy(x => x.CreatedAtUtc))
        {
            if (y > 720)
            {
                page = document.AddPage();
                page.Size = PdfSharpCore.PageSize.A4;
                gfx = XGraphics.FromPdfPage(page);
                y = 35;
            }

            var gross = item.Quantity * item.UnitPrice;
            var net = gross * (1m - item.DiscountRate / 100m);
            subtotal += net;
            gfx.DrawRectangle(linePen, 35, y, 525, 22);
            DrawCell(gfx, normalFont, index.ToString(Tr), 35, y, 30, 22, XStringFormats.Center);
            DrawCell(gfx, normalFont, $"{item.Material.Code} - {item.Material.Name}", 65, y, 175, 22, XStringFormats.CenterLeft);
            DrawCell(gfx, normalFont, item.Quantity.ToString("N4", Tr), 240, y, 65, 22, XStringFormats.CenterRight);
            DrawCell(gfx, normalFont, item.Unit, 305, y, 60, 22, XStringFormats.Center);
            DrawCell(gfx, normalFont, item.UnitPrice.ToString("N2", Tr), 365, y, 70, 22, XStringFormats.CenterRight);
            DrawCell(gfx, normalFont, $"%{item.DiscountRate.ToString("N2", Tr)}", 435, y, 55, 22, XStringFormats.CenterRight);
            DrawCell(gfx, normalFont, net.ToString("N2", Tr), 490, y, 70, 22, XStringFormats.CenterRight);
            y += 22;
            index++;
        }

        var vat = subtotal * order.VatRate / 100m;
        var total = subtotal + vat;
        y += 10;
        DrawTotal(gfx, headerFont, normalFont, "Ara Toplam", subtotal, order.CurrencyCode, y); y += 18;
        DrawTotal(gfx, headerFont, normalFont, $"KDV %{order.VatRate.ToString("N2", Tr)}", vat, order.CurrencyCode, y); y += 18;
        DrawTotal(gfx, headerFont, normalFont, "Genel Toplam", total, order.CurrencyCode, y); y += 28;

        if (!string.IsNullOrWhiteSpace(order.Description))
        {
            gfx.DrawString("Açıklama:", headerFont, XBrushes.Black, new XRect(35, y, 75, 16), XStringFormats.TopLeft);
            gfx.DrawString(order.Description, normalFont, XBrushes.Black, new XRect(110, y, 450, 40), XStringFormats.TopLeft);
            y += 42;
        }

        var signatureY = Math.Max(y + 10, 650);
        DrawSignature(gfx, headerFont, "Hazırlayan", 35, signatureY);
        DrawSignature(gfx, headerFont, "Kontrol Eden", 170, signatureY);
        DrawSignature(gfx, headerFont, "Onaylayan", 305, signatureY);
        DrawSignature(gfx, headerFont, "Tedarikçi Onayı", 440, signatureY);

        gfx.DrawString($"Doğrulama Kodu: {verificationCode}", smallFont, XBrushes.Black, new XRect(35, 800, 300, 12), XStringFormats.TopLeft);
        gfx.DrawString($"Enderun AI · {DateTime.UtcNow:dd.MM.yyyy HH:mm} UTC", smallFont, XBrushes.Black, new XRect(300, 800, 260, 12), XStringFormats.TopRight);

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return new GeneratedPdfDocument(stream.ToArray(), $"Satin_Alma_Siparisi_{Sanitize(order.OrderNumber)}.pdf", verificationCode);
    }

    private static void DrawInfoRow(XGraphics gfx, XFont header, XFont normal, string leftLabel, string leftValue, string rightLabel, string rightValue, double y)
    {
        gfx.DrawString(leftLabel + ":", header, XBrushes.Black, new XRect(35, y, 75, 16), XStringFormats.TopLeft);
        gfx.DrawString(leftValue, normal, XBrushes.Black, new XRect(110, y, 245, 16), XStringFormats.TopLeft);
        gfx.DrawString(rightLabel + ":", header, XBrushes.Black, new XRect(360, y, 85, 16), XStringFormats.TopLeft);
        gfx.DrawString(rightValue, normal, XBrushes.Black, new XRect(445, y, 115, 16), XStringFormats.TopLeft);
    }

    private static void DrawCell(XGraphics gfx, XFont font, string text, double x, double y, double width, double height, XStringFormat format) =>
        gfx.DrawString(text, font, XBrushes.Black, new XRect(x + 3, y + 3, width - 6, height - 6), format);

    private static void DrawTotal(XGraphics gfx, XFont header, XFont normal, string label, decimal amount, string currency, double y)
    {
        gfx.DrawString(label + ":", header, XBrushes.Black, new XRect(365, y, 95, 16), XStringFormats.TopRight);
        gfx.DrawString($"{amount.ToString("N2", Tr)} {currency}", normal, XBrushes.Black, new XRect(465, y, 95, 16), XStringFormats.TopRight);
    }

    private static void DrawSignature(XGraphics gfx, XFont font, string title, double x, double y)
    {
        gfx.DrawString(title, font, XBrushes.Black, new XRect(x, y, 110, 16), XStringFormats.TopCenter);
        gfx.DrawLine(new XPen(XColors.Black, 0.5), x, y + 55, x + 110, y + 55);
    }

    private static string CreateVerificationCode(Guid id, string number, DateTime stamp)
    {
        var payload = Encoding.UTF8.GetBytes($"{id:N}|{number}|{stamp:O}");
        return Convert.ToHexString(SHA256.HashData(payload))[..16];
    }

    private static string Sanitize(string value) => string.Concat(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
}