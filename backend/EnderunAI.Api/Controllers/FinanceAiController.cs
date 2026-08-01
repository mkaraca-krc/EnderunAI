using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/ai")]
public sealed class FinanceAiController : ControllerBase
{
    [HttpGet("finance-analysis")]
    public IActionResult FinanceAnalysis()
    {
        return Ok(new
        {
            summary = "Finans verileri sisteme işlendiğinde Hızır burada nakit akışı, alacak-borç dengesi ve proje finans risklerini yorumlayacak.",
            warnings = Array.Empty<string>()
        });
    }

    [HttpGet("dashboard")]
    public IActionResult Dashboard()
    {
        return Ok(new
        {
            generatedAtUtc = DateTime.UtcNow,
            items = Array.Empty<object>()
        });
    }
}
