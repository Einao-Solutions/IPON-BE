using System.Globalization;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
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
  //  private readonly ILoggerService _log;

    public StatisticsService(IOptions<PatentDesignDBSettings> patentDesignDbSettings)
    {
        var useSandbox = patentDesignDbSettings.Value.UseSandbox;
        var digitalOcean = useSandbox != "Y" ? patentDesignDbSettings.Value.ConnectionStringUp : patentDesignDbSettings.Value.ConnectionString;

        var settings = MongoClientSettings.FromUrl(new MongoUrl(digitalOcean));
        settings.SslSettings = new SslSettings { EnabledSslProtocols = SslProtocols.Tls12 };
        var mongoClient = new MongoClient(settings);
        var db = mongoClient.GetDatabase(patentDesignDbSettings.Value.DatabaseName);
        _workflowCollection = db.GetCollection<StaffPerformance>("staffPerformance");
        _userCollection = db.GetCollection<AppUser>("appUsers");
     //   _log = log;
    }

    public IReadOnlyList<UnitInfoDto> GetUnits(string registryType)
    {
        var unitMappings = GetUnitMappings(registryType);
        return unitMappings.Select(unit => new UnitInfoDto
        {
            UnitId = unit.UnitId,
            UnitName = unit.UnitName,
            RegistryType = registryType
        }).ToList();
    }

    public async Task<IReadOnlyList<StaffInfoDto>> GetStaffAsync(string registryType, int unitId)
    {
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

        return staffIdList.Distinct().Select(id =>
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
    }

    public async Task<StaffPerformanceDataDto> GetStaffPerformanceAsync(string registryType, int unitId, string periodType, string periodValue, int year)
    {
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
        var treatedFilter = Builders<StaffPerformance>.Filter.And(baseFilter, BuildTreatedFilter(unitMapping.RuleType));

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

        return new StaffPerformanceDataDto
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
    }

    public async Task<UnitPerformanceDataDto> GetUnitPerformanceAsync(string registryType, string periodType, string periodValue, int year)
    {
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
            var treatedFilter = Builders<StaffPerformance>.Filter.And(baseFilter, BuildTreatedFilter(unit.RuleType));

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

        return new UnitPerformanceDataDto
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
    }

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

    private static FilterDefinition<StaffPerformance> BuildAssignedFilter(ApplicationUnits ruleType)
    {
        var filter = Builders<StaffPerformance>.Filter;
        return ruleType switch
        {
            ApplicationUnits.Search => filter.Eq(x => x.BeforeStatus, ApplicationStatuses.AwaitingSearch),
            ApplicationUnits.Examination => filter.Eq(x => x.BeforeStatus, ApplicationStatuses.AwaitingExaminer),
            ApplicationUnits.Publication => filter.Eq(x => x.ApplicationType, FormApplicationTypes.PublicationStatusUpdate),
            ApplicationUnits.Opposition => filter.Eq(x => x.ApplicationType, FormApplicationTypes.NewOpposition),
            ApplicationUnits.Acceptance => filter.In(x => x.ApplicationType, new FormApplicationTypes?[]
            {
                FormApplicationTypes.ClericalUpdate,
                FormApplicationTypes.WithdrawalRequest,
                FormApplicationTypes.AppealRequest
            }),
            ApplicationUnits.Certificate => filter.In(x => x.ApplicationType, new FormApplicationTypes?[]
            {
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

    private static FilterDefinition<StaffPerformance> BuildTreatedFilter(ApplicationUnits ruleType)
    {
        var filter = Builders<StaffPerformance>.Filter;
        return ruleType switch
        {
            ApplicationUnits.Search => filter.And(
                filter.Eq(x => x.BeforeStatus, ApplicationStatuses.AwaitingSearch),
                filter.Eq(x => x.AfterStatus, ApplicationStatuses.AwaitingExaminer)
            ),
            ApplicationUnits.Examination => filter.And(
                filter.Eq(x => x.BeforeStatus, ApplicationStatuses.AwaitingExaminer),
                filter.In(x => x.AfterStatus, new ApplicationStatuses?[] { ApplicationStatuses.Rejected, ApplicationStatuses.Publication })
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
                filter.In(x => x.ApplicationType, new FormApplicationTypes?[]
                {
                    FormApplicationTypes.ClericalUpdate,
                    FormApplicationTypes.WithdrawalRequest,
                    FormApplicationTypes.AppealRequest
                }),
                filter.Eq(x => x.AfterStatus, ApplicationStatuses.Approved)
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
                new(3, "Certificate Unit", Roles.PatentCertification, ApplicationUnits.Acceptance)
            };
        }

        if (string.Equals(registryType, "Design", StringComparison.OrdinalIgnoreCase))
        {
            return new List<UnitMapping>
            {
                new(1, "Search Unit", Roles.DesignSearch, ApplicationUnits.Search),
                new(2, "Examination Unit", Roles.DesignExaminer, ApplicationUnits.Examination),
                new(3, "Certificate Unit", Roles.DesignCertification, ApplicationUnits.Acceptance)
            };
        }

        throw new ArgumentException("Invalid registryType. Must be TradeMark, Patent, or Design");
    }

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

}
