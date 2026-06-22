using patentdesign.Models;

namespace patentdesign.Utils
{
    /// <summary>
    /// Centralised mapping from enum values to the user-visible status / application-type labels.
    /// Keep this as the single source of truth: any UI / PDF / API surface that needs to render a
    /// human-readable name for these enums should call <see cref="ToDisplayName(ApplicationStatuses)"/>
    /// or <see cref="ToDisplayName(FormApplicationTypes)"/> instead of <c>enum.ToString()</c>.
    /// </summary>
    public static class StatusDisplay
    {
        /// <summary>
        /// Returns the user-visible label for an <see cref="ApplicationStatuses"/> value.
        /// <see cref="ApplicationStatuses.NewOpposition"/> (code 30) is rendered as "Opposed".
        /// All other values fall back to the enum's default string form.
        /// </summary>
        public static string ToDisplayName(this ApplicationStatuses status) => status switch
        {
            ApplicationStatuses.NewOpposition => "Opposed",
            _ => status.ToString()
        };

        /// <summary>
        /// Nullable overload mirroring <see cref="ToDisplayName(ApplicationStatuses)"/>.
        /// Returns an empty string when the value is <c>null</c>.
        /// </summary>
        public static string ToDisplayName(this ApplicationStatuses? status)
            => status?.ToDisplayName() ?? string.Empty;

        /// <summary>
        /// Returns the user-visible label for a <see cref="FormApplicationTypes"/> value.
        /// <see cref="FormApplicationTypes.NewOpposition"/> (index 16) is rendered as "Opposed".
        /// All other values fall back to the enum's default string form.
        /// </summary>
        public static string ToDisplayName(this FormApplicationTypes type) => type switch
        {
            FormApplicationTypes.NewOpposition => "Opposed",
            _ => type.ToString()
        };

        /// <summary>
        /// Nullable overload mirroring <see cref="ToDisplayName(FormApplicationTypes)"/>.
        /// Returns an empty string when the value is <c>null</c>.
        /// </summary>
        public static string ToDisplayName(this FormApplicationTypes? type)
            => type?.ToDisplayName() ?? string.Empty;
    }
}
