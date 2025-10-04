using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gravel;

/// <summary>
///     JSON converter for LexKey (hex string).
/// </summary>
public class LexKeyJsonConverter : JsonConverter<LexKey>
{
    public override LexKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return LexKey.Empty;

        var hex = reader.GetString() ?? "";
        return LexKey.FromHexString(hex);
    }

    public override void Write(Utf8JsonWriter writer, LexKey value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToHexString());
    }
}
