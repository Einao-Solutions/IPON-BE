using patentdesign.Enums;
using patentdesign.Models;

namespace patentdesign.Dtos.Request
{
    public class PerformanceDto
    {
        public string? ApplicationId { get; set; }
        public FormApplicationTypes? ApplicationType { get; set; }
        public ApplicationStatuses? BeforeStatus { get; set; }
        public ApplicationStatuses? AfterStatus { get; set; }
        public string? Reason { get; set; }
        public DateTime? Date { get; set; }
        public string? AppUserId { get; set; }
        public Roles? OfficeUnit { get; set; }
        public string? FileNumber { get; set; }
        public FileTypes? FileType { get; set; }
    }

    public class FinanceComparisonRequestDto
    {
        public string? RegistryType { get; set; }
        public List<FinancePeriodRequestDto> Periods { get; set; } = [];
    }

    public class OperationalComparisonRequestDto
    {
        public string? RegistryType { get; set; }
        public List<FinancePeriodRequestDto> Periods { get; set; } = [];
    }

    public class StaffPerformanceComparisonRequestDto
    {
        public string? RegistryType { get; set; }
        public int? UnitId { get; set; }
        public List<FinancePeriodRequestDto> Periods { get; set; } = [];
    }

    public class UnitPerformanceComparisonRequestDto
    {
        public string? RegistryType { get; set; }
        public List<FinancePeriodRequestDto> Periods { get; set; } = [];
    }

    public class FinancePeriodRequestDto
    {
        public string Type { get; set; } = string.Empty;
        public string? Value { get; set; }
        public int? Year { get; set; }
        public int? StartYear { get; set; }
        public int? EndYear { get; set; }
        public int? StartMonth { get; set; }
        public int? EndMonth { get; set; }
        public int? StartOffset { get; set; }
        public int? EndOffset { get; set; }
        public string? OffsetUnit { get; set; }
        public string? Label { get; set; }

    }
}
