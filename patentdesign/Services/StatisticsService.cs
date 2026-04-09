using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using patentdesign.Dtos.Request;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Models;
//using patentdesign.Services.Interface;
using System.Security.Authentication;

namespace patentdesign.Services;

public class StatisticsService
{
    private readonly IMongoCollection<StaffPerformance> _workflowCollection;
    private readonly IMongoCollection<AppUser> _userCollection;
    private readonly IMongoCollection<PaymentRecord> _paymentCollection;
    private readonly IMongoCollection<Filling> _fillingCollection;
    private readonly ILogger<StatisticsService> _log;

    public StatisticsService(IOptions<PatentDesignDBSettings> patentDesignDbSettings, ILogger<StatisticsService> log)
    {
        var useSandbox = patentDesignDbSettings.Value.UseSandbox;
        var digitalOcean = useSandbox != "Y" ? patentDesignDbSettings.Value.ConnectionStringUp : patentDesignDbSettings.Value.ConnectionString;

        var settings = MongoClientSettings.FromUrl(new MongoUrl(digitalOcean));
        settings.SslSettings = new SslSettings { EnabledSslProtocols = SslProtocols.Tls12 };
        var mongoClient = new MongoClient(settings);
        var db = mongoClient.GetDatabase(patentDesignDbSettings.Value.DatabaseName);
        _workflowCollection = db.GetCollection<StaffPerformance>("staffPerformance");
        _userCollection = db.GetCollection<AppUser>("appUsers");
        _paymentCollection = db.GetCollection<PaymentRecord>("payments");
        _fillingCollection = db.GetCollection<Filling>(patentDesignDbSettings.Value.FilesCollectionName);
        _log = log;
    }

    #region Public API

    public IReadOnlyList<UnitInfoDto> GetUnits(string registryType)
    {
        _log.LogInformation("Fetching units for RegistryType {RegistryType}", registryType);
        var unitMappings = GetUnitMappings(registryType);
        var result = unitMappings.Select(unit => new UnitInfoDto
        {
            UnitId = unit.UnitId,
            UnitName = unit.UnitName,
            RegistryType = registryType
        }).ToList();
        _log.LogInformation("Fetched {UnitCount} units for RegistryType {RegistryType}", result.Count, registryType);
        return result;
    }

    public async Task<IReadOnlyList<StaffInfoDto>> GetStaffAsync(string registryType, int unitId)
    {
        _log.LogInformation("Fetching staff for RegistryType {RegistryType}, UnitId {UnitId}", registryType, unitId);
        var unitMapping = GetUnitMapping(registryType, unitId);
        var fileType = ParseRegistryType(registryType);

        var filter = Builders<StaffPerformance>.Filter.And(
            Builders<StaffPerformance>.Filter.Eq(x => x.FileType, fileType),
            Builders<StaffPerformance>.Filter.Eq(x => x.OfficeUnit, unitMapping.Role)
        );

        var staffIds = await _workflowCollection.DistinctAsync(x => x.AppUserId, filter);
        var staffIdList = await staffIds.ToListAsync();

        if (staffIdList.Count == 0)
        {
            _log.LogInformation("No staff found for RegistryType {RegistryType}, UnitId {UnitId}", registryType, unitId);
            return [];
        }

        // Only include officer accounts when resolving staff names/emails.
        var users = await _userCollection.Find(Builders<AppUser>.Filter.And(
            Builders<AppUser>.Filter.Eq(x => x.AccountType, AccountType.Officer),
            Builders<AppUser>.Filter.Or(
                Builders<AppUser>.Filter.In(x => x.Id, staffIdList),
                Builders<AppUser>.Filter.In(x => x.CreatorId, staffIdList)
            ))).ToListAsync();
        var userLookup = BuildUserLookup(users);

        var result = staffIdList.Distinct().Select(id =>
        {
            userLookup.TryGetValue(id, out var user);
            return new StaffInfoDto
            {
                StaffId = id ?? string.Empty,
                StaffName = user == null ? string.Empty : (!string.IsNullOrWhiteSpace(user.Name)
                    ? user.Name.Trim()
                    : $"{user.FirstName} {user.LastName}".Trim()),
                StaffEmail = user?.Email ?? string.Empty,
                UnitId = unitMapping.UnitId,
                UnitName = unitMapping.UnitName
            };
        }).ToList();

        _log.LogInformation("Fetched {StaffCount} staff entries for RegistryType {RegistryType}, UnitId {UnitId}", result.Count, registryType, unitId);
        return result;
    }

    public async Task<StaffPerformanceDataDto> GetStaffPerformanceAsync(string registryType, int unitId, string periodType, string periodValue, int year)
    {
        _log.LogInformation("Fetching staff performance for RegistryType {RegistryType}, UnitId {UnitId}, PeriodType {PeriodType}, PeriodValue {PeriodValue}, Year {Year}", registryType, unitId, periodType, periodValue, year);
        var fileType = ParseRegistryType(registryType);
        var unitMapping = GetUnitMapping(registryType, unitId);
        var dateRange = GetDateRange(periodType, periodValue, year);

        var baseFilter = Builders<StaffPerformance>.Filter.And(
            Builders<StaffPerformance>.Filter.Eq(x => x.FileType, fileType),
            Builders<StaffPerformance>.Filter.Eq(x => x.OfficeUnit, unitMapping.Role),
            Builders<StaffPerformance>.Filter.Gte(x => x.Date, dateRange.StartDate),
            Builders<StaffPerformance>.Filter.Lte(x => x.Date, dateRange.EndDate)
        );

        var assignedFilter = Builders<StaffPerformance>.Filter.And(baseFilter, BuildAssignedFilter(unitMapping.RuleType));
        var treatedFilter = Builders<StaffPerformance>.Filter.And(baseFilter, BuildTreatedFilter(fileType, unitMapping.RuleType));

        var assignedCounts = await _workflowCollection.Aggregate()
            .Match(assignedFilter)
            .Group(x => x.AppUserId, g => new { StaffId = g.Key, Count = g.Count() })
            .ToListAsync();

        var treatedCounts = await _workflowCollection.Aggregate()
            .Match(treatedFilter)
            .Group(x => x.AppUserId, g => new { StaffId = g.Key, Count = g.Count() })
            .ToListAsync();

        var totalAssigned = assignedCounts.Sum(x => x.Count);
        var totalTreated = treatedCounts.Sum(x => x.Count);

        var staffIds = assignedCounts.Select(x => x.StaffId)
            .Concat(treatedCounts.Select(x => x.StaffId))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        // Only include officer accounts when resolving staff names/emails.
        var users = staffIds.Count == 0
            ? []
            : await _userCollection.Find(Builders<AppUser>.Filter.And(
                Builders<AppUser>.Filter.Eq(x => x.AccountType, AccountType.Officer),
                Builders<AppUser>.Filter.Or(
                    Builders<AppUser>.Filter.In(x => x.Id, staffIds),
                    Builders<AppUser>.Filter.In(x => x.CreatorId, staffIds)
                ))).ToListAsync();

        var userLookup = BuildUserLookup(users);
        var officerIds = userLookup.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assignedLookup = assignedCounts.ToDictionary(x => x.StaffId ?? string.Empty, x => x.Count);
        var treatedLookup = treatedCounts.ToDictionary(x => x.StaffId ?? string.Empty, x => x.Count);

        var staffPerformance = staffIds
        .Where(id => officerIds.Contains(id))
        .Select(id =>
        {
            assignedLookup.TryGetValue(id, out var assigned);
            treatedLookup.TryGetValue(id, out var treated);
            userLookup.TryGetValue(id, out var user);

            var percentage = totalAssigned == 0 ? 0 : Math.Round(treated * 100d / totalAssigned, 1);
            var contribution = totalTreated == 0 ? 0 : Math.Round(treated * 100d / totalTreated, 1);

            return new StaffPerformanceEntryDto
            {
                StaffId = id,
                StaffName = user == null ? string.Empty : (!string.IsNullOrWhiteSpace(user.Name)
                    ? user.Name.Trim()
                    : $"{user.FirstName} {user.LastName}".Trim()),
                StaffEmail = user?.Email ?? string.Empty,
                TotalAssigned = totalAssigned,
                TotalTreated = treated,
                Percentage = percentage,
                ContributionToUnit = contribution
            };
        })
        .OrderByDescending(x => x.TotalTreated)
        .ToList();

        var summary = new StaffPerformanceSummaryDto
        {
            TotalAssigned = totalAssigned,
            TotalTreated = totalTreated,
            TreatmentRate = totalAssigned == 0 ? 0 : Math.Round(totalTreated * 100d / totalAssigned, 1)
        };

        var result = new StaffPerformanceDataDto
        {
            UnitId = unitMapping.UnitId,
            UnitName = unitMapping.UnitName,
            RegistryType = registryType,
            Period = new PeriodDto
            {
                Type = periodType,
                Value = periodValue,
                Year = year
            },
            Summary = summary,
            StaffPerformance = staffPerformance
        };

        _log.LogInformation("Fetched staff performance for RegistryType {RegistryType}, UnitId {UnitId}", registryType, unitId);
        return result;
    }

    public async Task<UnitPerformanceDataDto> GetUnitPerformanceAsync(string registryType, string periodType, string periodValue, int year)
    {
        _log.LogInformation("Fetching unit performance for RegistryType {RegistryType}, PeriodType {PeriodType}, PeriodValue {PeriodValue}, Year {Year}", registryType, periodType, periodValue, year);
        var fileType = ParseRegistryType(registryType);
        var unitMappings = GetUnitMappings(registryType);
        var dateRange = GetDateRange(periodType, periodValue, year);

        var unitResults = new List<UnitPerformanceEntryDto>();
        var totalAssigned = 0;
        var totalTreated = 0;

        foreach (var unit in unitMappings)
        {
            var baseFilter = Builders<StaffPerformance>.Filter.And(
                Builders<StaffPerformance>.Filter.Eq(x => x.FileType, fileType),
                Builders<StaffPerformance>.Filter.Eq(x => x.OfficeUnit, unit.Role),
                Builders<StaffPerformance>.Filter.Gte(x => x.Date, dateRange.StartDate),
                Builders<StaffPerformance>.Filter.Lte(x => x.Date, dateRange.EndDate)
            );

            var assignedFilter = Builders<StaffPerformance>.Filter.And(baseFilter, BuildAssignedFilter(unit.RuleType));
            var treatedFilter = Builders<StaffPerformance>.Filter.And(baseFilter, BuildTreatedFilter(fileType, unit.RuleType));

            var assignedCount = await _workflowCollection.CountDocumentsAsync(assignedFilter);
            var treatedCount = await _workflowCollection.CountDocumentsAsync(treatedFilter);
            var staffCount = await _workflowCollection.DistinctAsync(x => x.AppUserId, baseFilter);
            var staffCountValue = (await staffCount.ToListAsync()).Count;

            totalAssigned += (int)assignedCount;
            totalTreated += (int)treatedCount;

            unitResults.Add(new UnitPerformanceEntryDto
            {
                UnitId = unit.UnitId,
                UnitName = unit.UnitName,
                TotalAssigned = (int)assignedCount,
                TotalTreated = (int)treatedCount,
                TreatmentRate = assignedCount == 0 ? 0 : Math.Round(treatedCount * 100d / assignedCount, 1),
                StaffCount = staffCountValue,
                AvgPerStaff = staffCountValue == 0 ? 0 : Math.Round(treatedCount / (double)staffCountValue, 1)
            });
        }

        var result = new UnitPerformanceDataDto
        {
            RegistryType = registryType,
            Period = new PeriodDto
            {
                Type = periodType,
                Value = periodValue,
                Year = year
            },
            Overview = new UnitPerformanceOverviewDto
            {
                TotalUnits = unitResults.Count,
                TotalAssigned = totalAssigned,
                TotalTreated = totalTreated,
                OverallRate = totalAssigned == 0 ? 0 : Math.Round(totalTreated * 100d / totalAssigned, 1)
            },
            Units = unitResults.OrderBy(x => x.UnitId).ToList()
        };

        _log.LogInformation("Fetched unit performance for RegistryType {RegistryType}", registryType);
        return result;
    }

    #endregion

    #region Finance Statistics

    public async Task<FinanceComparisonDataDto> GetFinanceComparisonAsync(FinanceComparisonRequestDto request)
    {
        _log.LogInformation("Fetching finance comparison for RegistryType {RegistryType}", request?.RegistryType);
        if (request?.Periods == null || request.Periods.Count == 0)
        {
            throw new ArgumentException("Missing required parameter: periods");
        }

        if (string.IsNullOrWhiteSpace(request.RegistryType))
        {
            throw new ArgumentException("Missing required parameter: registryType");
        }

        var fileType = ParseRegistryType(request.RegistryType);
        var fileIds = await _fillingCollection
            .Find(Builders<Filling>.Filter.Eq(x => x.Type, fileType))
            .Project(x => x.FileId)
            .ToListAsync();

        var results = new List<FinancePeriodResultDto>();

        foreach (var period in request.Periods)
        {
            var range = ResolveFinancePeriod(period);

            var filter = Builders<PaymentRecord>.Filter.And(
                Builders<PaymentRecord>.Filter.Gte(x => x.Date, range.StartDate),
                Builders<PaymentRecord>.Filter.Lte(x => x.Date, range.EndDate),
                Builders<PaymentRecord>.Filter.In(x => x.FileId, fileIds)
            );

            var payments = fileIds.Count == 0
                ? []
                : await _paymentCollection.Find(filter).ToListAsync();

            var paymentTypes = payments
                .GroupBy(x => x.PaymentType ?? string.Empty)
                .Select(group => new FinancePaymentTypeResultDto
                {
                    PaymentType = group.Key,
                    TotalGovernmentFee = group.Sum(GetGovernmentFee),
                    Count = group.Count()
                })
                .OrderByDescending(x => x.TotalGovernmentFee)
                .ToList();

            results.Add(new FinancePeriodResultDto
            {
                Label = range.Label,
                StartDate = range.StartDate,
                EndDate = range.EndDate,
                TotalGovernmentFee = paymentTypes.Sum(x => x.TotalGovernmentFee),
                TotalPayments = payments.Count,
                PaymentTypes = paymentTypes
            });
        }

        var result = new FinanceComparisonDataDto
        {
            Periods = results
        };

        _log.LogInformation("Fetched finance comparison for RegistryType {RegistryType}", request.RegistryType);
        return result;
    }

    public async Task<OperationalComparisonDataDto> GetOperationalComparisonAsync(OperationalComparisonRequestDto request)
    {
        _log.LogInformation("Fetching operational comparison for RegistryType {RegistryType}", request?.RegistryType);
        if (request?.Periods == null || request.Periods.Count == 0)
        {
            throw new ArgumentException("Missing required parameter: periods");
        }

        if (string.IsNullOrWhiteSpace(request.RegistryType))
        {
            throw new ArgumentException("Missing required parameter: registryType");
        }

        var fileType = ParseRegistryType(request.RegistryType);
        var results = new List<OperationalPeriodResultDto>();

        foreach (var period in request.Periods)
        {
            var range = ResolveFinancePeriod(period);

            var filter = Builders<Filling>.Filter.And(
                Builders<Filling>.Filter.Eq(x => x.Type, fileType),
                Builders<Filling>.Filter.Gte(x => x.DateCreated, range.StartDate),
                Builders<Filling>.Filter.Lte(x => x.DateCreated, range.EndDate)
            );

            var files = await _fillingCollection.Find(filter).ToListAsync();

            var periodResult = new OperationalPeriodResultDto
            {
                Label = range.Label,
                StartDate = range.StartDate,
                EndDate = range.EndDate,
                TotalFiles = files.Count
            };

            switch (fileType)
            {
                case FileTypes.TradeMark:
                    periodResult.TrademarkClasses = BuildBreakdown(files.Select(file => file.TrademarkClass?.ToString()));
                    periodResult.TradeMarkTypes = BuildBreakdown(files.Select(file => file.TrademarkType?.ToString()));
                    periodResult.Nationalities = BuildBreakdown(files.Select(GetFirstApplicantNationality));
                    break;
                case FileTypes.Patent:
                    periodResult.FileOrigins = BuildBreakdown(files.Select(file => file.FileOrigin));
                    periodResult.FilingCountries = BuildBreakdown(files.Select(GetFilingCountryOrNationality));
                    periodResult.Nationalities = BuildBreakdown(files.Select(GetFirstApplicantNationality));
                    periodResult.PatentTypes = BuildBreakdown(files.Select(file => file.PatentType?.ToString()));
                    periodResult.PatentApplicationTypes = BuildBreakdown(files.Select(file => file.PatentApplicationType?.ToString()));
                    break;
                case FileTypes.Design:
                    periodResult.FileOrigins = BuildBreakdown(files.Select(file => file.FileOrigin));
                    periodResult.FilingCountries = BuildBreakdown(files.Select(GetFilingCountryOrNationality));
                    periodResult.Nationalities = BuildBreakdown(files.Select(GetFirstApplicantNationality));
                    periodResult.DesignTypes = BuildBreakdown(files.Select(file => file.DesignType?.ToString()));
                    break;
            }

            results.Add(periodResult);
        }

        var result = new OperationalComparisonDataDto
        {
            RegistryType = request.RegistryType,
            Periods = results
        };

        _log.LogInformation("Fetched operational comparison for RegistryType {RegistryType}", request.RegistryType);
        return result;
    }

    #endregion

    #region Registry and Date Helpers

    private static FileTypes ParseRegistryType(string registryType)
    {
        if (Enum.TryParse<FileTypes>(registryType, true, out var fileType))
        {
            return fileType;
        }

        throw new ArgumentException("Invalid registryType. Must be TradeMark, Patent, or Design");
    }

    private static (DateTime StartDate, DateTime EndDate) GetDateRange(string periodType, string periodValue, int year)
    {
        if (string.Equals(periodType, "month", StringComparison.OrdinalIgnoreCase))
        {
            var month = DateTime.ParseExact(periodValue, new[] { "MMMM", "MMM" }, CultureInfo.InvariantCulture, DateTimeStyles.None).Month;
            var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddMonths(1).AddTicks(-1);
            return (start, end);
        }

        if (string.Equals(periodType, "quarter", StringComparison.OrdinalIgnoreCase))
        {
            var quarters = new Dictionary<string, (int StartMonth, int EndMonth)>(StringComparer.OrdinalIgnoreCase)
            {
                ["Q1"] = (1, 3),
                ["Q1: Jan-Mar"] = (1, 3),
                ["Q2"] = (4, 6),
                ["Q2: Apr-Jun"] = (4, 6),
                ["Q3"] = (7, 9),
                ["Q3: Jul-Sep"] = (7, 9),
                ["Q4"] = (10, 12),
                ["Q4: Oct-Dec"] = (10, 12)
            };

            if (!quarters.TryGetValue(periodValue, out var range))
            {
                throw new ArgumentException("Invalid periodValue for quarter");
            }

            var start = new DateTime(year, range.StartMonth, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(year, range.EndMonth, DateTime.DaysInMonth(year, range.EndMonth), 23, 59, 59, 999, DateTimeKind.Utc);
            return (start, end);
        }

        if (string.Equals(periodType, "year", StringComparison.OrdinalIgnoreCase))
        {
            var start = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(year, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc);
            return (start, end);
        }

        throw new ArgumentException("Invalid periodType. Must be month, quarter, or year");
    }

    private static (DateTime StartDate, DateTime EndDate, string Label) ResolveFinancePeriod(FinancePeriodRequestDto period)
    {
        if (period == null)
        {
            throw new ArgumentException("Invalid period");
        }

        var periodType = period.Type?.Trim();
        if (string.IsNullOrWhiteSpace(periodType))
        {
            throw new ArgumentException("Missing required parameter: type");
        }

        var label = period.Label?.Trim() ?? string.Empty;

        switch (periodType.ToLowerInvariant())
        {
            case "month":
            {
                if (!period.Year.HasValue)
                {
                    throw new ArgumentException("Missing required parameter: year");
                }

                if (string.IsNullOrWhiteSpace(period.Value))
                {
                    throw new ArgumentException("Missing required parameter: value");
                }

                var month = ParseMonth(period.Value);
                var start = new DateTime(period.Year.Value, month, 1, 0, 0, 0, DateTimeKind.Utc);
                var end = start.AddMonths(1).AddTicks(-1);

                if (string.IsNullOrWhiteSpace(label))
                {
                    label = $"{CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month)} {period.Year.Value}";
                }

                return (start, end, label);
            }
            case "quarter":
            {
                if (!period.Year.HasValue)
                {
                    throw new ArgumentException("Missing required parameter: year");
                }

                if (string.IsNullOrWhiteSpace(period.Value))
                {
                    throw new ArgumentException("Missing required parameter: value");
                }

                var range = GetQuarterRange(period.Value);
                var start = new DateTime(period.Year.Value, range.StartMonth, 1, 0, 0, 0, DateTimeKind.Utc);
                var end = new DateTime(period.Year.Value, range.EndMonth, DateTime.DaysInMonth(period.Year.Value, range.EndMonth), 23, 59, 59, 999, DateTimeKind.Utc);

                if (string.IsNullOrWhiteSpace(label))
                {
                    label = $"{period.Value} {period.Year.Value}";
                }

                return (start, end, label);
            }
            case "year":
            {
                if (!period.Year.HasValue)
                {
                    throw new ArgumentException("Missing required parameter: year");
                }

                var start = new DateTime(period.Year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var end = new DateTime(period.Year.Value, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc);

                if (string.IsNullOrWhiteSpace(label))
                {
                    label = period.Year.Value.ToString(CultureInfo.InvariantCulture);
                }

                return (start, end, label);
            }
            case "year-range":
            {
                if (!period.StartYear.HasValue || !period.EndYear.HasValue)
                {
                    throw new ArgumentException("Missing required parameter: startYear/endYear");
                }

                if (period.StartYear > period.EndYear)
                {
                    throw new ArgumentException("Invalid year range");
                }

                var start = new DateTime(period.StartYear.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var end = new DateTime(period.EndYear.Value, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc);

                if (string.IsNullOrWhiteSpace(label))
                {
                    label = $"{period.StartYear}-{period.EndYear}";
                }

                return (start, end, label);
            }
            case "month-range":
            {
                if (!period.Year.HasValue)
                {
                    throw new ArgumentException("Missing required parameter: year");
                }

                if (!period.StartMonth.HasValue || !period.EndMonth.HasValue)
                {
                    throw new ArgumentException("Missing required parameter: startMonth/endMonth");
                }

                if (period.StartMonth > period.EndMonth)
                {
                    throw new ArgumentException("Invalid month range");
                }

                var start = new DateTime(period.Year.Value, period.StartMonth.Value, 1, 0, 0, 0, DateTimeKind.Utc);
                var end = new DateTime(period.Year.Value, period.EndMonth.Value, DateTime.DaysInMonth(period.Year.Value, period.EndMonth.Value), 23, 59, 59, 999, DateTimeKind.Utc);

                if (string.IsNullOrWhiteSpace(label))
                {
                    label = $"{CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(period.StartMonth.Value)}-{CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(period.EndMonth.Value)} {period.Year.Value}";
                }

                return (start, end, label);
            }
            case "relative":
            {
                if (!period.StartOffset.HasValue || !period.EndOffset.HasValue)
                {
                    throw new ArgumentException("Missing required parameter: startOffset/endOffset");
                }

                if (period.StartOffset < period.EndOffset)
                {
                    throw new ArgumentException("Invalid relative range");
                }

                if (string.IsNullOrWhiteSpace(period.OffsetUnit))
                {
                    throw new ArgumentException("Missing required parameter: offsetUnit");
                }

                var now = DateTime.UtcNow;

                if (string.Equals(period.OffsetUnit, "year", StringComparison.OrdinalIgnoreCase))
                {
                    var startYear = now.Year - period.StartOffset.Value;
                    var endYear = now.Year - period.EndOffset.Value;

                    if (startYear > endYear)
                    {
                        throw new ArgumentException("Invalid relative range");
                    }

                    var start = new DateTime(startYear, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    var end = new DateTime(endYear, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc);

                    if (string.IsNullOrWhiteSpace(label))
                    {
                        label = $"{startYear}-{endYear}";
                    }

                    return (start, end, label);
                }

                if (string.Equals(period.OffsetUnit, "month", StringComparison.OrdinalIgnoreCase))
                {
                    var currentMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    var start = currentMonthStart.AddMonths(-period.StartOffset.Value);
                    var endMonthStart = currentMonthStart.AddMonths(-period.EndOffset.Value);
                    var end = endMonthStart.AddMonths(1).AddTicks(-1);

                    if (string.IsNullOrWhiteSpace(label))
                    {
                        label = $"{CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(start.Month)} {start.Year}-{CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(end.Month)} {end.Year}";
                    }

                    return (start, end, label);
                }

                throw new ArgumentException("Invalid offsetUnit. Must be month or year");
            }
            default:
                throw new ArgumentException("Invalid period type. Must be month, quarter, year, month-range, year-range, or relative");
        }
    }

    private static int ParseMonth(string value)
    {
        if (int.TryParse(value, out var month) && month is >= 1 and <= 12)
        {
            return month;
        }

        return DateTime.ParseExact(value, new[] { "MMMM", "MMM" }, CultureInfo.InvariantCulture, DateTimeStyles.None).Month;
    }

    private static (int StartMonth, int EndMonth) GetQuarterRange(string periodValue)
    {
        var quarters = new Dictionary<string, (int StartMonth, int EndMonth)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Q1"] = (1, 3),
            ["Q1: Jan-Mar"] = (1, 3),
            ["Q2"] = (4, 6),
            ["Q2: Apr-Jun"] = (4, 6),
            ["Q3"] = (7, 9),
            ["Q3: Jul-Sep"] = (7, 9),
            ["Q4"] = (10, 12),
            ["Q4: Oct-Dec"] = (10, 12)
        };

        if (!quarters.TryGetValue(periodValue, out var range))
        {
            throw new ArgumentException("Invalid periodValue for quarter");
        }

        return range;
    }

    private static double GetGovernmentFee(PaymentRecord payment)
    {
        return payment?.RemitaResponse?.lineItems?.FirstOrDefault()?.beneficiaryAmount ?? 0d;
    }

    private static List<OperationalBreakdownItemDto> BuildBreakdown(IEnumerable<string?> values)
    {
        return values
            .Select(value => string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim())
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Select(group => new OperationalBreakdownItemDto
            {
                Key = group.Key,
                Count = group.Count()
            })
            .OrderByDescending(item => item.Count)
            .ToList();
    }

    private static string? GetFirstApplicantNationality(Filling file)
    {
        return file.applicants?.FirstOrDefault()?.country;
    }

    private static string? GetFilingCountryOrNationality(Filling file)
    {
        if (!string.IsNullOrWhiteSpace(file.FilingCountry))
        {
            return file.FilingCountry;
        }

        return GetFirstApplicantNationality(file);
    }

    #endregion

    #region Performance Filters

    private static FilterDefinition<StaffPerformance> BuildAssignedFilter(ApplicationUnits ruleType)
    {
        var filter = Builders<StaffPerformance>.Filter;
        return ruleType switch
        {
            ApplicationUnits.Search => filter.Eq(x => x.BeforeStatus, ApplicationStatuses.AwaitingSearch),
            ApplicationUnits.Examination => filter.Eq(x => x.BeforeStatus, ApplicationStatuses.AwaitingExaminer),
            ApplicationUnits.Publication => filter.Eq(x => x.ApplicationType, FormApplicationTypes.PublicationStatusUpdate),
            ApplicationUnits.Opposition => filter.Eq(x => x.ApplicationType, FormApplicationTypes.NewOpposition),
            ApplicationUnits.Acceptance => filter.Eq(x => x.BeforeStatus, ApplicationStatuses.RequestWithdrawal),
            ApplicationUnits.Certificate => filter.In(x => x.ApplicationType, new FormApplicationTypes?[]
            {
                FormApplicationTypes.Assignment,
                FormApplicationTypes.License,
                FormApplicationTypes.Mortgage,
                FormApplicationTypes.Merger,
                FormApplicationTypes.CertifiedTrueCopy,
                FormApplicationTypes.Amendment,
                FormApplicationTypes.Assignment,
                FormApplicationTypes.Ownership,
                FormApplicationTypes.RegisteredUser,
                FormApplicationTypes.Merger,
                FormApplicationTypes.ChangeOfName,
                FormApplicationTypes.ChangeOfAddress
            }),
            _ => filter.Empty
        };
    }

    #endregion

    #region Unit Mappings

    private static FilterDefinition<StaffPerformance> BuildTreatedFilter(FileTypes fileType, ApplicationUnits ruleType)
    {
        var filter = Builders<StaffPerformance>.Filter;
        return ruleType switch
        {
            ApplicationUnits.Search => filter.And(
                filter.Eq(x => x.BeforeStatus, ApplicationStatuses.AwaitingSearch),
                filter.Eq(x => x.AfterStatus, ApplicationStatuses.AwaitingExaminer)
            ),
            ApplicationUnits.Examination => fileType == FileTypes.TradeMark
                ? filter.And(
                    filter.Eq(x => x.BeforeStatus, ApplicationStatuses.AwaitingExaminer),
                    filter.In(x => x.AfterStatus, new ApplicationStatuses?[] { ApplicationStatuses.Rejected, ApplicationStatuses.Publication })
                )
                : filter.And(
                    filter.Eq(x => x.BeforeStatus, ApplicationStatuses.AwaitingExaminer),
                    filter.In(x => x.AfterStatus, new ApplicationStatuses?[] { ApplicationStatuses.Rejected, ApplicationStatuses.AwaitingCertificateConfirmation })
                ),
            ApplicationUnits.Publication => filter.And(
                filter.Eq(x => x.ApplicationType, FormApplicationTypes.PublicationStatusUpdate),
                filter.In(x => x.AfterStatus, new ApplicationStatuses?[] { ApplicationStatuses.Approved, ApplicationStatuses.Rejected })
            ),
            ApplicationUnits.Opposition => filter.And(
                filter.Eq(x => x.ApplicationType, FormApplicationTypes.NewOpposition),
                filter.Eq(x => x.AfterStatus, ApplicationStatuses.Approved)
            ),
            ApplicationUnits.Acceptance => filter.And(
                filter.Eq(x => x.BeforeStatus, ApplicationStatuses.RequestWithdrawal),
                filter.In(x => x.AfterStatus, new ApplicationStatuses?[] { ApplicationStatuses.Approved, ApplicationStatuses.Rejected })
            ),
            ApplicationUnits.Certificate => filter.And(
                filter.Eq(x => x.BeforeStatus, ApplicationStatuses.AwaitingRecordalProcess),
                filter.In(x => x.AfterStatus, new ApplicationStatuses?[] { ApplicationStatuses.Approved, ApplicationStatuses.Rejected })
            ),
            _ => filter.Empty
        };
    }

    private static UnitMapping GetUnitMapping(string registryType, int unitId)
    {
        var mappings = GetUnitMappings(registryType);
        var mapping = mappings.FirstOrDefault(x => x.UnitId == unitId);
        if (mapping == null)
        {
            throw new ArgumentException($"Unit {unitId} does not exist for {registryType} registry. Valid units: {GetUnitRange(mappings)}");
        }

        return mapping;
    }

    private static string GetUnitRange(IEnumerable<UnitMapping> mappings)
    {
        var ids = mappings.Select(x => x.UnitId).OrderBy(x => x).ToList();
        if (ids.Count == 0)
        {
            return string.Empty;
        }

        if (ids.Count == 1)
        {
            return ids[0].ToString(CultureInfo.InvariantCulture);
        }

        return $"{ids.First()}-{ids.Last()}";
    }

    private static List<UnitMapping> GetUnitMappings(string registryType)
    {
        if (string.Equals(registryType, "TradeMark", StringComparison.OrdinalIgnoreCase))
        {
            return new List<UnitMapping>
            {
                new(1, "Search Unit", Roles.TrademarkSearch, ApplicationUnits.Search),
                new(2, "Examination Unit", Roles.TrademarkExaminer, ApplicationUnits.Examination),
                new(3, "Publication Unit", Roles.TrademarkPublication, ApplicationUnits.Publication),
                new(4, "Opposition Unit", Roles.TrademarkOpposition, ApplicationUnits.Opposition),
                new(5, "Acceptance Unit", Roles.TrademarkAcceptance, ApplicationUnits.Acceptance),
                new(6, "Certificate Unit", Roles.TrademarkCertification, ApplicationUnits.Certificate)
            };
        }

        if (string.Equals(registryType, "Patent", StringComparison.OrdinalIgnoreCase))
        {
            return new List<UnitMapping>
            {
                new(1, "Search Unit", Roles.PatentSearch, ApplicationUnits.Search),
                new(2, "Examination Unit", Roles.PatentExaminer, ApplicationUnits.Examination),
                new(3, "Certificate Unit", Roles.PatentExaminer, ApplicationUnits.Certificate)
            };
        }

        if (string.Equals(registryType, "Design", StringComparison.OrdinalIgnoreCase))
        {
            return new List<UnitMapping>
            {
                new(1, "Search Unit", Roles.DesignSearch, ApplicationUnits.Search),
                new(2, "Examination Unit", Roles.DesignExaminer, ApplicationUnits.Examination),
                new(3, "Certificate Unit", Roles.DesignExaminer, ApplicationUnits.Certificate)
            };
        }

        throw new ArgumentException("Invalid registryType. Must be TradeMark, Patent, or Design");
    }

    #endregion

    #region User Lookup

    private record UnitMapping(int UnitId, string UnitName, Roles Role, ApplicationUnits RuleType);

    private static Dictionary<string, AppUser> BuildUserLookup(IEnumerable<AppUser> users)
    {
        var lookup = new Dictionary<string, AppUser>(StringComparer.OrdinalIgnoreCase);
        foreach (var user in users)
        {
            if (!string.IsNullOrWhiteSpace(user.Id))
            {
                lookup[user.Id] = user;
            }

            if (!string.IsNullOrWhiteSpace(user.CreatorId))
            {
                lookup[user.CreatorId] = user;
            }

        }

        return lookup;
    }

    #endregion

}
