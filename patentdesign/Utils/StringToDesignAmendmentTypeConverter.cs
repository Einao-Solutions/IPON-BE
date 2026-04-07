using System.Text.Json;
using System.Text.Json.Serialization;
using patentdesign.Models;

namespace patentdesign.Utils
{
    public class StringToDesignAmendmentTypeConverter : JsonConverter<DesignAmendmentTypes>
    {
        public override DesignAmendmentTypes Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string stringValue = reader.GetString()!;
                
                // Try to parse as integer first
                if (int.TryParse(stringValue, out int intValue))
                {
                    return (DesignAmendmentTypes)intValue;
                }
                
                // Try to parse as enum name
                if (Enum.TryParse<DesignAmendmentTypes>(stringValue, true, out var enumValue))
                {
                    return enumValue;
                }
            }
            else if (reader.TokenType == JsonTokenType.Number)
            {
                int intValue = reader.GetInt32();
                return (DesignAmendmentTypes)intValue;
            }

            throw new JsonException($"Cannot convert '{reader.GetString()}' to {nameof(DesignAmendmentTypes)}");
        }

        public override void Write(Utf8JsonWriter writer, DesignAmendmentTypes value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue((int)value);
        }
    }
}
