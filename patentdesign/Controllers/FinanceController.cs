using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using patentdesign.Enums;
using patentdesign.Services;

namespace patentdesign.Controllers;

[Authorize]
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