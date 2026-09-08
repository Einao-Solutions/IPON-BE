using System.Reflection;
using System.Text.Json;
using patentdesign.Dtos.Request;
using patentdesign.Services;

namespace PatentDesign.Tests.Services;

public class StatisticsDateRangeTests
{
    private static readonly Func<FinancePeriodRequestDto, (DateTime StartDate, DateTime EndDate, string Label)> ResolvePeriod =
        typeof(StatisticsService)
            .GetMethod("ResolveFinancePeriod", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Func<FinancePeriodRequestDto, (DateTime StartDate, DateTime EndDate, string Label)>>();

    [Theory]
    [InlineData("2026-03-10", "2026-03-10")]
    [InlineData("2024-02-28", "2024-02-29")]
    [InlineData("2025-12-20", "2026-01-15")]
    public void DateRangeIncludesBothUtcCalendarDays(string startDate, string endDate)
    {
        var period = new FinancePeriodRequestDto
        {
            Type = "date-range",
            StartDate = DateOnly.ParseExact(startDate, "yyyy-MM-dd"),
            EndDate = DateOnly.ParseExact(endDate, "yyyy-MM-dd")
        };

        var range = ResolvePeriod(period);

        Assert.Equal(period.StartDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), range.StartDate);
        Assert.Equal(period.EndDate.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc), range.EndDate);
        Assert.Equal(DateTimeKind.Utc, range.StartDate.Kind);
        Assert.Equal(DateTimeKind.Utc, range.EndDate.Kind);
        Assert.Equal($"{startDate} - {endDate}", range.Label);
        Assert.True(range.EndDate >= range.StartDate);
    }

    [Fact]
    public void DateRangePreservesCustomLabelAndAcceptsNormalizedType()
    {
        var range = ResolvePeriod(new FinancePeriodRequestDto
        {
            Type = " DATE-RANGE ",
            StartDate = new DateOnly(2026, 3, 1),
            EndDate = new DateOnly(2026, 3, 15),
            Label = " First half of March "
        });

        Assert.Equal("First half of March", range.Label);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DateRangeRequiresBothDates(bool hasStart, bool hasEnd)
    {
        var period = new FinancePeriodRequestDto
        {
            Type = "date-range",
            StartDate = hasStart ? new DateOnly(2026, 3, 1) : null,
            EndDate = hasEnd ? new DateOnly(2026, 3, 15) : null
        };

        var exception = Assert.Throws<ArgumentException>(() => ResolvePeriod(period));

        Assert.Equal("Missing required parameter: startDate/endDate", exception.Message);
    }

    [Fact]
    public void DateRangeRejectsReversedDates()
    {
        var period = new FinancePeriodRequestDto
        {
            Type = "date-range",
            StartDate = new DateOnly(2026, 3, 15),
            EndDate = new DateOnly(2026, 3, 1)
        };

        var exception = Assert.Throws<ArgumentException>(() => ResolvePeriod(period));

        Assert.Equal("Invalid date range: startDate must be on or before endDate", exception.Message);
    }

    [Fact]
    public void ComparisonRequestBindsIsoDatesAndIncludesDatesInSerializedPeriods()
    {
        const string json = """
            {"registryType":"Patent","periods":[{"type":"date-range","startDate":"2026-03-01","endDate":"2026-03-15"}]}
            """;
        var request = JsonSerializer.Deserialize<FinanceComparisonRequestDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var period = Assert.Single(request.Periods);

        Assert.Equal("Patent", request.RegistryType);
        Assert.Equal(new DateOnly(2026, 3, 1), period.StartDate);
        Assert.Equal(new DateOnly(2026, 3, 15), period.EndDate);
        var originalKey = JsonSerializer.Serialize(request.Periods);
        period.EndDate = new DateOnly(2026, 3, 16);
        Assert.NotEqual(originalKey, JsonSerializer.Serialize(request.Periods));
    }

    [Theory]
    [InlineData("month", "2", 2, 2)]
    [InlineData("quarter", "Q1", 1, 3)]
    [InlineData("year", null, 1, 12)]
    public void ExistingPeriodsKeepTheirCalendarBoundaries(string type, string? value, int startMonth, int endMonth)
    {
        var range = ResolvePeriod(new FinancePeriodRequestDto
        {
            Type = type,
            Value = value,
            Year = 2024
        });

        Assert.Equal(new DateTime(2024, startMonth, 1, 0, 0, 0, DateTimeKind.Utc), range.StartDate);
        Assert.Equal(new DateOnly(2024, endMonth, DateTime.DaysInMonth(2024, endMonth)), DateOnly.FromDateTime(range.EndDate));
        Assert.Equal(23, range.EndDate.Hour);
        Assert.Equal(59, range.EndDate.Minute);
        Assert.Equal(59, range.EndDate.Second);
    }
}
