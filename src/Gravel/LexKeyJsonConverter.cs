using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gravel;

/// <summary>
///     JSON converter for <see cref="LexKey" /> that serializes to and from hex string representation.
/// </summary>
public class LexKeyJsonConverter : JsonConverter<LexKey>
{
    /// <summary>
    ///     Reads a <see cref="LexKey" /> from a JSON hex string or null value.
    /// </summary>
    /// <param name="reader">The JSON reader.</param>
    /// <param name="typeToConvert">The type to convert.</param>
    /// <param name="options">Serializer options.</param>
    /// <returns>The deserialized <see cref="LexKey" />.</returns>
    public override LexKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return LexKey.Empty;

        var hex = reader.GetString() ?? "";
        return LexKey.FromHexString(hex);
    }

    /// <summary>
    ///     Writes a <see cref="LexKey" /> as a hex string to JSON.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The <see cref="LexKey" /> value to write.</param>
    /// <param name="options">Serializer options.</param>
    public override void Write(Utf8JsonWriter writer, LexKey value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToHexString());
    }
}
