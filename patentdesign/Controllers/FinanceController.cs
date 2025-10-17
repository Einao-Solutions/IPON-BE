using Microsoft.AspNetCore.Mvc;
using patentdesign.Models;
using patentdesign.Services;

namespace patentdesign.Controllers;

[ApiController]
[Route("api/finance")]

public class FinanceController(FinanceService financeService) : ControllerBase
{
    [HttpPost("GetFinanceSummary")]
    public async Task<ActionResult<List<FinanceSummaryType>>> GetFinanceSummary([FromBody] FinanceQueryType data)
    {
        var res = await financeService.GetFinanceSummary(data);
        return Ok(res);
    }
}