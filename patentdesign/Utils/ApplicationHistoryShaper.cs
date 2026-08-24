using patentdesign.Dtos.Response;
using patentdesign.Models;

namespace patentdesign.Utils
{
    /// <summary>
    /// Shapes an <see cref="ApplicationInfo"/> into the SuperAdmin-friendly
    /// <see cref="ApplicationHistoryResponseDto"/> (<c>hist</c>) payload.
    /// The UI reads <c>assignment.*</c> for assignment entries (applicationType = 5)
    /// and <c>newValue.*</c> for recordal types 7, 8, 9 and 10; <c>oldValue</c>/<c>newValue</c>
    /// are always included as fallbacks.
    /// </summary>
    public static class ApplicationHistoryShaper
    {
        public static ApplicationHistoryResponseDto Shape(ApplicationInfo app, string? fileNumber = null)
        {
            var hist = new ApplicationHistoryResponseDto
            {
                Id = app.id,
                ApplicationType = app.ApplicationType,
                ApplicationDate = app.ApplicationDate,
                CurrentStatus = app.CurrentStatus,
                PaymentId = app.PaymentId,
                FileNumber = fileNumber,
                OldValue = app.OldValue,
                NewValue = app.NewValue
            };

            if (app.Assignment != null)
            {
                var a = app.Assignment;
                hist.Assignment = new AssignmentPayloadDto
                {
                    AssignorName = a.assignorName,
                    AssignorAddress = a.assignorAddress,
                    AssignorCountry = a.assignorCountry,
                    AssigneeName = a.assigneeName,
                    AssigneeAddress = a.assigneeAddress,
                    AssigneeCountry = a.assigneeCountry,
                    DateOfAssignment = a.dateOfAssignment == default ? null : a.dateOfAssignment,
                    AssignmentDeedUrl = a.deedOfAgreementUrl,
                    AuthorizationLetterUrl = a.authorizationLetterUrl,

                    // Optional fields not stored on AssignmentType — fall back to old/new value.
                    AssignorEmail = TryGetPayloadString(app.OldValue, "assignorEmail", "email", "Email"),
                    AssignorPhone = TryGetPayloadString(app.OldValue, "assignorPhone", "phone", "Phone"),
                    AssignorNationality = TryGetPayloadString(app.OldValue, "assignorNationality", "nationality", "Nationality"),
                    AssigneeEmail = TryGetPayloadString(app.NewValue, "assigneeEmail", "email", "Email"),
                    AssigneePhone = TryGetPayloadString(app.NewValue, "assigneePhone", "phone", "Phone"),
                    AssigneeNationality = TryGetPayloadString(app.NewValue, "assigneeNationality", "nationality", "Nationality"),
                };
            }
            else if (app.ApplicationType == FormApplicationTypes.Assignment)
            {
                // No AssignmentType stored — synthesise from old/new value so the UI still pre-fills.
                hist.Assignment = new AssignmentPayloadDto
                {
                    AssignorName = TryGetPayloadString(app.OldValue, "assignorName", "name", "Name"),
                    AssignorEmail = TryGetPayloadString(app.OldValue, "assignorEmail", "email", "Email"),
                    AssignorPhone = TryGetPayloadString(app.OldValue, "assignorPhone", "phone", "Phone"),
                    AssignorNationality = TryGetPayloadString(app.OldValue, "assignorNationality", "nationality", "Nationality"),
                    AssignorAddress = TryGetPayloadString(app.OldValue, "assignorAddress", "address", "Address"),
                    AssignorCountry = TryGetPayloadString(app.OldValue, "assignorCountry", "country", "Country"),
                    AssigneeName = TryGetPayloadString(app.NewValue, "assigneeName", "name", "Name"),
                    AssigneeEmail = TryGetPayloadString(app.NewValue, "assigneeEmail", "email", "Email"),
                    AssigneePhone = TryGetPayloadString(app.NewValue, "assigneePhone", "phone", "Phone"),
                    AssigneeNationality = TryGetPayloadString(app.NewValue, "assigneeNationality", "nationality", "Nationality"),
                    AssigneeAddress = TryGetPayloadString(app.NewValue, "assigneeAddress", "address", "Address"),
                    AssigneeCountry = TryGetPayloadString(app.NewValue, "assigneeCountry", "country", "Country"),
                    AssignmentDeedUrl = TryGetPayloadString(app.NewValue, "assignmentDeedUrl", "deedOfAgreementUrl"),
                    AuthorizationLetterUrl = TryGetPayloadString(app.NewValue, "authorizationLetterUrl"),
                };
                if (DateTime.TryParse(TryGetPayloadString(app.NewValue, "dateOfAssignment"), out var d))
                    hist.Assignment.DateOfAssignment = d;
            }

            return hist;
        }

        /// <summary>
        /// Case-insensitive string lookup across a payload that may be a
        /// <see cref="System.Text.Json.JsonElement"/>, a <see cref="IDictionary{TKey,TValue}"/>,
        /// or a MongoDB <see cref="MongoDB.Bson.BsonDocument"/>.
        /// Returns the first non-empty match across <paramref name="names"/>.
        /// </summary>
        public static string? TryGetPayloadString(object? payload, params string[] names)
        {
            if (payload == null || names == null || names.Length == 0) return null;

            if (payload is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var name in names)
                {
                    foreach (var p in je.EnumerateObject())
                    {
                        if (!string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                        if (p.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            var s = p.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) return s;
                        }
                        else if (p.Value.ValueKind != System.Text.Json.JsonValueKind.Null
                                 && p.Value.ValueKind != System.Text.Json.JsonValueKind.Undefined)
                        {
                            return p.Value.ToString();
                        }
                    }
                }
                return null;
            }

            if (payload is IDictionary<string, object?> dict)
            {
                foreach (var name in names)
                {
                    foreach (var kv in dict)
                    {
                        if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)
                            && kv.Value != null
                            && !string.IsNullOrWhiteSpace(kv.Value.ToString()))
                            return kv.Value.ToString();
                    }
                }
                return null;
            }

            if (payload is MongoDB.Bson.BsonDocument bson)
            {
                foreach (var name in names)
                {
                    foreach (var el in bson.Elements)
                    {
                        if (string.Equals(el.Name, name, StringComparison.OrdinalIgnoreCase)
                            && el.Value != null
                            && !el.Value.IsBsonNull)
                        {
                            var s = el.Value.ToString();
                            if (!string.IsNullOrWhiteSpace(s)) return s;
                        }
                    }
                }
            }

            return null;
        }
    }
}
