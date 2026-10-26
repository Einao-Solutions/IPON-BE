using System.Globalization;

namespace patentdesign.Utils;

/// <summary>
/// Central time helpers. Persist instants as UTC (DateTime.UtcNow); use LagosNow only for
/// human-facing text and calendar-day calculations; parse external date strings with ParseToUtc.
/// </summary>
public static class AppTime
{
    public static readonly TimeZoneInfo LagosZone = ResolveLagosZone();

    public static DateTime LagosNow => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, LagosZone);

    /// <summary>
    /// Parses an external date string into a UTC DateTime.
    /// - Strings with an explicit offset/Z are converted to UTC.
    /// - Date-only strings (no time component) are kept on the same calendar day at 00:00 UTC.
    /// - Strings with a wall-clock time and no offset are treated as Lagos (WAT) time.
    /// </summary>
    public static bool TryParseToUtc(string? value, out DateTime result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        var hasOffset = text.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
                        System.Text.RegularExpressions.Regex.IsMatch(text, @"T.*[+-]\d{2}:?\d{2}$");

        if (hasOffset && DateTimeOffset.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dto))
        {
            result = dto.UtcDateTime;
            return true;
        }

        if (!DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed) &&
            !DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
        {
            return false;
        }

        result = parsed.TimeOfDay == TimeSpan.Zero
            ? DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc)
            : TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), LagosZone);
        return true;
    }

    public static DateTime ParseToUtc(string? value)
    {
        if (TryParseToUtc(value, out var result))
        {
            return result;
        }

        Serilog.Log.Warning("AppTime.ParseToUtc could not parse date value {Value}", value);
        throw new FormatException($"Invalid date value '{value}'.");
    }

    private static TimeZoneInfo ResolveLagosZone()
    {
        foreach (var id in new[] { "Africa/Lagos", "W. Central Africa Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        Serilog.Log.Warning("Lagos time zone not found on host; falling back to fixed UTC+1");
        return TimeZoneInfo.CreateCustomTimeZone("WAT", TimeSpan.FromHours(1), "West Africa Time", "West Africa Time");
    }
}
