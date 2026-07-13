using patentdesign.Models;

namespace patentdesign.Dtos.Response;

public class PeriodDto
{
    public string Type { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int Year { get; set; }
}

public class StaffPerformanceSummaryDto
{
    public int TotalAssigned { get; set; }
    public int TotalTreated { get; set; }
    public double TreatmentRate { get; set; }
}

public class StaffPerformanceEntryDto
{
    public string StaffId { get; set; } = string.Empty;
    public string StaffName { get; set; } = string.Empty;
    public string StaffEmail { get; set; } = string.Empty;
    public int TotalAssigned { get; set; }
    public int TotalTreated { get; set; }
    public double Percentage { get; set; }
    public double ContributionToUnit { get; set; }
}

public class StaffPerformanceDataDto
{
    public int UnitId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public string RegistryType { get; set; } = string.Empty;
    public PeriodDto Period { get; set; } = new();
    public StaffPerformanceSummaryDto Summary { get; set; } = new();
    public List<StaffPerformanceEntryDto> StaffPerformance { get; set; } = [];
}

public class UnitPerformanceOverviewDto
{
    public int TotalUnits { get; set; }
    public int TotalAssigned { get; set; }
    public int TotalTreated { get; set; }
    public double OverallRate { get; set; }
}

public class UnitPerformanceEntryDto
{
    public int UnitId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public int TotalAssigned { get; set; }
    public int TotalTreated { get; set; }
    public double TreatmentRate { get; set; }
    public int StaffCount { get; set; }
    public double AvgPerStaff { get; set; }
}

public class UnitPerformanceDataDto
{
    public string RegistryType { get; set; } = string.Empty;
    public PeriodDto Period { get; set; } = new();
    public UnitPerformanceOverviewDto Overview { get; set; } = new();
    public List<UnitPerformanceEntryDto> Units { get; set; } = [];
}

public class UnitInfoDto
{
    public int UnitId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public string RegistryType { get; set; } = string.Empty;
}

public class StaffInfoDto
{
    public string StaffId { get; set; } = string.Empty;
    public string StaffName { get; set; } = string.Empty;
    public string StaffEmail { get; set; } = string.Empty;
    public int UnitId { get; set; }
    public string UnitName { get; set; } = string.Empty;
}

public class FinancePaymentTypeResultDto
{
    public string PaymentType { get; set; } = string.Empty;
    public double TotalGovernmentFee { get; set; }
    public int Count { get; set; }
}

public class FinancePeriodResultDto
{
    public string Label { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public double TotalGovernmentFee { get; set; }
    public int TotalPayments { get; set; }
    public List<FinancePaymentTypeResultDto> PaymentTypes { get; set; } = [];
    public List<FinanceMonthlyBreakdownDto> MonthlyBreakdown { get; set; } = [];
}

public class FinanceMonthlyBreakdownDto
{
    public string Label { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public double TotalGovernmentFee { get; set; }
    public int TotalPayments { get; set; }
}

public class FinanceComparisonDataDto
{
    public List<FinancePeriodResultDto> Periods { get; set; } = [];
}

public class OperationalBreakdownItemDto
{
    public string Key { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class OperationalPeriodResultDto
{
    public string Label { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int TotalFiles { get; set; }
    public List<OperationalBreakdownItemDto> ApplicationTypes { get; set; } = [];
    public List<OperationalBreakdownItemDto> TrademarkClasses { get; set; } = [];
    public List<OperationalBreakdownItemDto> TradeMarkTypes { get; set; } = [];
    public List<OperationalBreakdownItemDto> DesignTypes { get; set; } = [];
    public List<OperationalBreakdownItemDto> PatentTypes { get; set; } = [];
    public List<OperationalBreakdownItemDto> PatentApplicationTypes { get; set; } = [];
    public List<OperationalBreakdownItemDto> FileOrigins { get; set; } = [];
    public List<OperationalBreakdownItemDto> FilingCountries { get; set; } = [];
    public List<OperationalBreakdownItemDto> Nationalities { get; set; } = [];
}

public class OperationalComparisonDataDto
{
    public string RegistryType { get; set; } = string.Empty;
    public List<OperationalPeriodResultDto> Periods { get; set; } = [];
}
