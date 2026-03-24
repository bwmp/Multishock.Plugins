using System.Globalization;
using System.Text;
using System.Text.Json;

namespace VRChatOSC.Services;

public static class OscArgumentParser
{
    public static bool TryParse(string? rawArguments, out object?[] arguments, out string error)
    {
        var value = rawArguments?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            arguments = [];
            error = string.Empty;
            return true;
        }

        if (value.StartsWith('['))
        {
            return TryParseJsonArray(value, out arguments, out error);
        }

        try
        {
            var parsed = SplitCsv(value)
                .Select(ParseToken)
                .ToArray();

            arguments = parsed;
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            arguments = [];
            error = $"Unable to parse OSC arguments: {ex.Message}";
            return false;
        }
    }

    private static bool TryParseJsonArray(string rawArguments, out object?[] arguments, out string error)
    {
        try
        {
            using var document = JsonDocument.Parse(rawArguments);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                arguments = [];
                error = "JSON arguments must be an array";
                return false;
            }

            var parsed = new List<object?>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                parsed.Add(ParseElement(element));
            }

            arguments = [.. parsed];
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            arguments = [];
            error = $"Unable to parse OSC argument JSON: {ex.Message}";
            return false;
        }
    }

    private static object? ParseElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number when element.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(ParseElement).ToArray(),
            _ => element.GetRawText(),
        };
    }

    private static object? ParseToken(string token)
    {
        var value = token.Trim();
        if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (bool.TryParse(value, out var boolValue))
        {
            return boolValue;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            return intValue;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
        {
            return longValue;
        }

        if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleValue))
        {
            return doubleValue;
        }

        if (value.StartsWith('"') && value.EndsWith('"') && value.Length >= 2)
        {
            return value[1..^1].Replace("\\\"", "\"");
        }

        return value;
    }

    private static IEnumerable<string> SplitCsv(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        var values = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
                builder.Append(c);
                continue;
            }

            if (c == ',' && !inQuotes)
            {
                values.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(c);
        }

        values.Add(builder.ToString());
        return values;
    }
}
