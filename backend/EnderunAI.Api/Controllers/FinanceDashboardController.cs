using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/finance")]
public sealed class FinanceDashboardController : ControllerBase
{
    [HttpGet("dashboard")]
    public IActionResult Dashboard()
    {
        return Ok(new
        {
            totalContractAmount = 0m,
            totalProgressPaymentAmount = 0m,
            totalPriceDifferenceAmount = 0m,
            totalDeductionAmount = 0m,
            totalNetPayableAmount = 0m,
            activeProjectCount = 0,
            progressPaymentCount = 0
        });
    }

    [HttpGet("cari-summary")]
    public IActionResult CurrentAccountSummary()
    {
        return Ok(new
        {
            totalReceivable = 0m,
            totalPayable = 0m,
            netBalance = 0m,
            accountCount = 0
        });
    }

    [HttpGet("projects-summary")]
    public IActionResult ProjectsSummary()
    {
        return Ok(Array.Empty<object>());
    }

    [HttpGet("cash-flow")]
    public IActionResult CashFlow()
    {
        return Ok(new
        {
            totalIncome = 0m,
            totalExpense = 0m,
            netCash = 0m
        });
    }

    [HttpGet("suppliers-summary")]
    public IActionResult SuppliersSummary()
    {
        return Ok(Array.Empty<object>());
    }
}
