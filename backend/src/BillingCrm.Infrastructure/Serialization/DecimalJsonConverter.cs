using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BillingCrm.Infrastructure.Serialization;

/// <summary>
/// Spec 012, research R2: reads a JSON number bound to a <see cref="decimal"/> from its raw token rather than
/// <see cref="Utf8JsonReader.GetDecimal"/> (which would silently round or accept forms this API never wants to store).
/// Refuses (throws <see cref="JsonException"/>, which the API's existing <c>ThrowOnBadRequest = false</c> pipeline already
/// turns into the same clean 400 any other malformed body gets) a token written in exponent notation (<c>1e5</c>, <c>1E-3</c>)
/// or with more than 28 significant digits, before a <see cref="decimal"/> is ever constructed from it. Applies to every
/// <c>decimal</c>-bound field already in the API (product price and VAT, invoice quantity and discount, payment amount) once
/// it is registered in <c>Program.cs</c>'s <c>ConfigureHttpJsonOptions</c> - no per-field code.
/// </summary>
public sealed class DecimalJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // GetRawText-equivalent for a number token, without allocating a JsonDocument: the raw span between quotes-less digits.
        var raw = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan.ToArray();
        var text = System.Text.Encoding.UTF8.GetString(raw);

        if (text.Contains('e') || text.Contains('E'))
        {
            throw new JsonException("A number cannot use exponent notation.");
        }

        var significantDigits = text.Count(char.IsAsciiDigit);
        if (significantDigits > 28)
        {
            throw new JsonException("A number cannot have more than 28 significant digits.");
        }

        return reader.GetDecimal();
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}
