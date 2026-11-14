using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaperTrading.Api.Infrastructure;

/// <summary>
/// Serializes <see cref="decimal"/> as a JSON <b>string</b> ("50010.5") instead of a number.
/// JavaScript parses JSON numbers into IEEE-754 doubles, which silently loses precision on money
/// (e.g. 0.1 + 0.2). Strings force clients to use a decimal library. Reading accepts both forms.
/// </summary>
public sealed class DecimalStringJsonConverter : JsonConverter<decimal>
{
    private const NumberStyles Style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number) return reader.GetDecimal();

        if (reader.TokenType == JsonTokenType.String &&
            decimal.TryParse(reader.GetString(), Style, CultureInfo.InvariantCulture, out var value))
            return value;

        throw new JsonException("Expected a decimal number or numeric string.");
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteStringValue(Normalize(value).ToString(CultureInfo.InvariantCulture));

    /// <summary>Strips trailing zeros (1.50000000 -> 1.5) without changing the value.</summary>
    public static decimal Normalize(decimal value) => value / 1.0000000000000000000000000000m;
}
