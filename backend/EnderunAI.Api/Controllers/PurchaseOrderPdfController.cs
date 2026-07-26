using EnderunAI.Api.Services.Procurement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/procurement-documents/purchase-orders")]
public sealed class PurchaseOrderPdfController(IProcurementPdfService pdfService) : ControllerBase
{
    [HttpGet("{orderId:guid}/pdf")]
    public async Task<IActionResult> Download(Guid orderId, CancellationToken cancellationToken)
    {
        try
        {
            var document = await pdfService.GeneratePurchaseOrderAsync(orderId, cancellationToken);
            Response.Headers.Append("X-Document-Verification-Code", document.VerificationCode);
            return File(document.Content, "application/pdf", document.FileName);
        }
        catch (InvalidOperationException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }

    [HttpGet("{orderId:guid}/preview")]
    public async Task<IActionResult> Preview(Guid orderId, CancellationToken cancellationToken)
    {
        try
        {
            var document = await pdfService.GeneratePurchaseOrderAsync(orderId, cancellationToken);
            Response.Headers.Append("Content-Disposition", $"inline; filename=\"{document.FileName}\"");
            Response.Headers.Append("X-Document-Verification-Code", document.VerificationCode);
            return File(document.Content, "application/pdf");
        }
        catch (InvalidOperationException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }
}