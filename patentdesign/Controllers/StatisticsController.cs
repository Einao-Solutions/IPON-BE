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
    /// <summary>
    /// Gets staff performance statistics for a specific unit and period.
    /// </summary>
    /// <param name="registryType">Registry type (TradeMark, Patent, or Design).</param>
    /// <param name="unitId">Unit identifier.</param>
    /// <param name="periodType">Period type (month, quarter, or year).</param>
    /// <param name="periodValue">Period value (month name/number or quarter label).</param>
    /// <param name="year">Calendar year for the period.</param>
    /// <returns>Performance summary and staff breakdown for the requested unit.</returns>
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

    /// <summary>
    /// Compares finance tech fee statistics across one or more periods.
    /// </summary>
    /// <param name="request">Comparison request containing registry type and period filters.</param>
    /// <returns>Tech fee totals and payment type breakdown per period.</returns>
    [HttpPost("statistics/finance/techfee/compare")]
    public async Task<IActionResult> GetFinanceTechFeeComparison([FromBody] FinanceComparisonRequestDto? request)
    {
        Console.WriteLine("Finance tech fee statistics search has started");
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
            var data = await statisticsService.GetFinanceTechFeeComparisonAsync(request);
            return Ok(new { success = true, data });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Gets unit performance statistics for the requested period.
    /// </summary>
    /// <param name="registryType">Registry type (TradeMark, Patent, or Design).</param>
    /// <param name="periodType">Period type (month, quarter, or year).</param>
    /// <param name="periodValue">Period value (month name/number or quarter label).</param>
    /// <param name="year">Calendar year for the period.</param>
    /// <returns>Performance overview and unit breakdown for the requested period.</returns>
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

    /// <summary>
    /// Compares staff performance statistics across one or more periods for a specific unit.
    /// </summary>
    /// <param name="request">Comparison request containing registry type, unit id, and period filters.</param>
    /// <returns>Staff performance summaries and staff breakdown per period.</returns>
    [HttpPost("statistics/performance/staff/compare")]
    public async Task<IActionResult> GetStaffPerformanceComparison([FromBody] StaffPerformanceComparisonRequestDto? request)
    {
        if (string.IsNullOrWhiteSpace(request?.RegistryType))
        {
            return BadRequest(new { success = false, error = "Missing required parameter: registryType" });
        }

        if (!request?.UnitId.HasValue ?? true)
        {
            return BadRequest(new { success = false, error = "Missing required parameter: unitId" });
        }

        if (request?.Periods == null || request.Periods.Count == 0)
        {
            return BadRequest(new { success = false, error = "Missing required parameter: periods" });
        }

        try
        {
            var data = await statisticsService.GetStaffPerformanceComparisonAsync(request);
            return Ok(new { success = true, data });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Compares unit performance statistics across one or more periods.
    /// </summary>
    /// <param name="request">Comparison request containing registry type and period filters.</param>
    /// <returns>Unit performance overviews and unit breakdown per period.</returns>
    [HttpPost("statistics/performance/units/compare")]
    public async Task<IActionResult> GetUnitPerformanceComparison([FromBody] UnitPerformanceComparisonRequestDto? request)
    {
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
            var data = await statisticsService.GetUnitPerformanceComparisonAsync(request);
            return Ok(new { success = true, data });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Gets the list of processing units for a registry type.
    /// </summary>
    /// <param name="registryType">Registry type (TradeMark, Patent, or Design).</param>
    /// <returns>List of units for the registry.</returns>
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

    /// <summary>
    /// Gets staff assigned to a specific unit and registry type.
    /// </summary>
    /// <param name="unitId">Unit identifier.</param>
    /// <param name="registryType">Registry type (TradeMark, Patent, or Design).</param>
    /// <returns>List of staff accounts for the unit.</returns>
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

    /// <summary>
    /// Compares finance statistics across one or more periods.
    /// </summary>
    /// <param name="request">Comparison request containing registry type and period filters.</param>
    /// <returns>Finance totals and payment type breakdown per period.</returns>
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

    /// <summary>
    /// Compares operational filing statistics across one or more periods.
    /// </summary>
    /// <param name="request">Comparison request containing registry type and period filters.</param>
    /// <returns>Operational breakdowns per period for the selected registry type.</returns>
    [HttpPost("statistics/operational/compare")]
    public async Task<IActionResult> GetOperationalComparison([FromBody] OperationalComparisonRequestDto? request)
    {
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
            var data = await statisticsService.GetOperationalComparisonAsync(request);
            return Ok(new { success = true, data });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Compares support officer performance across one or more periods.
    /// </summary>
    /// <param name="request">Support performance request containing scope and periods.</param>
    /// <returns>Response-first weighted support performance metrics per officer and period.</returns>
    [HttpPost("statistics/support/performance/compare")]
    public async Task<IActionResult> GetSupportPerformanceComparison([FromBody] SupportPerformanceRequestDto? request)
    {
        if (string.IsNullOrWhiteSpace(request?.Scope))
        {
            return BadRequest(new { success = false, error = "Missing required parameter: scope" });
        }

        if (request?.Periods == null || request.Periods.Count == 0)
        {
            return BadRequest(new { success = false, error = "Missing required parameter: periods" });
        }

        try
        {
            var data = await statisticsService.GetSupportPerformanceComparisonAsync(request);
            return Ok(new { success = true, data });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Invalidates cached statistics responses.
    /// </summary>
    /// <returns>New cache version identifier.</returns>
    [HttpPost("statistics/cache/invalidate")]
    public async Task<IActionResult> InvalidateStatisticsCache()
    {
        var version = await statisticsService.InvalidateStatisticsCacheAsync();
        return Ok(new { success = true, cacheVersion = version });
    }
}
