using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using patentdesign.Dtos.Request;
using patentdesign.Services;

namespace patentdesign.Controllers;

//[Authorize]
[ApiController]
[Route("api")]
public class StatisticsController(StatisticsService statisticsService) : ControllerBase
{
    [HttpGet("statistics/performance/staff")]
    public async Task<IActionResult> GetStaffPerformance(
        [FromQuery] string? registryType,
        [FromQuery] int? unitId,
        [FromQuery] string? periodType,
        [FromQuery] string? periodValue,
        [FromQuery] int? year)
    {
        if (string.IsNullOrWhiteSpace(registryType))
        {
            return BadRequest(new { success = false, error = "Missing required parameter: registryType" });
        }

        if (!unitId.HasValue)
        {
            return BadRequest(new { success = false, error = "Missing required parameter: unitId" });
        }

        if (string.IsNullOrWhiteSpace(periodType))
        {
            return BadRequest(new { success = false, error = "Missing required parameter: periodType" });
        }

        if (string.IsNullOrWhiteSpace(periodValue))
        {
            return BadRequest(new { success = false, error = "Missing required parameter: periodValue" });
        }

        if (!year.HasValue)
        {
            return BadRequest(new { success = false, error = "Missing required parameter: year" });
        }

        try
        {
            var data = await statisticsService.GetStaffPerformanceAsync(registryType, unitId.Value, periodType, periodValue, year.Value);
            return Ok(new { success = true, data });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    [HttpGet("statistics/performance/units")]
    public async Task<IActionResult> GetUnitPerformance(
        [FromQuery] string? registryType,
        [FromQuery] string? periodType,
        [FromQuery] string? periodValue,
        [FromQuery] int? year)
    {
        if (string.IsNullOrWhiteSpace(registryType))
        {
            return BadRequest(new { success = false, error = "Missing required parameter: registryType" });
        }

        if (string.IsNullOrWhiteSpace(periodType))
        {
            return BadRequest(new { success = false, error = "Missing required parameter: periodType" });
        }

        if (string.IsNullOrWhiteSpace(periodValue))
        {
            return BadRequest(new { success = false, error = "Missing required parameter: periodValue" });
        }

        if (!year.HasValue)
        {
            return BadRequest(new { success = false, error = "Missing required parameter: year" });
        }

        try
        {
            var data = await statisticsService.GetUnitPerformanceAsync(registryType, periodType, periodValue, year.Value);
            return Ok(new { success = true, data });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    [HttpGet("units")]
    public IActionResult GetUnits([FromQuery] string? registryType)
    {
        if (string.IsNullOrWhiteSpace(registryType))
        {
            return BadRequest(new { success = false, error = "Missing required parameter: registryType" });
        }

        try
        {
            var data = statisticsService.GetUnits(registryType);
            return Ok(new { success = true, data });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    [HttpGet("staff")]
    public async Task<IActionResult> GetStaff([FromQuery] int? unitId, [FromQuery] string? registryType)
    {
        if (string.IsNullOrWhiteSpace(registryType))
        {
            return BadRequest(new { success = false, error = "Missing required parameter: registryType" });
        }

        if (!unitId.HasValue)
        {
            return BadRequest(new { success = false, error = "Missing required parameter: unitId" });
        }

        try
        {
            var data = await statisticsService.GetStaffAsync(registryType, unitId.Value);
            return Ok(new { success = true, data });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    [HttpPost("statistics/finance/compare")]
    public async Task<IActionResult> GetFinanceComparison([FromBody] FinanceComparisonRequestDto? request)
    {
        Console.WriteLine("Finance statistics search has started");
        if (string.IsNullOrWhiteSpace(request?.RegistryType))
        {
            return BadRequest(new { success = false, error = "Missing required parameter: registryType" });
        }

        if (request?.Periods == null || request.Periods.Count == 0)
        {
            return BadRequest(new { success = false, error = "Missing required parameter: periods" });
        }

        try
        {
            var data = await statisticsService.GetFinanceComparisonAsync(request);
            return Ok(new { success = true, data });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
    }
}
