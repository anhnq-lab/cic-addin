using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace CIC.BIM.Addin.Bridge;

/// <summary>
/// Minimal JSON serializer compatible with both .NET Framework 4.8 and .NET 8.
/// No NuGet dependencies. Handles DTOs, lists, primitives, and nulls.
/// </summary>
public static class SimpleJson
{
    public static string Serialize(object? obj)
    {
        if (obj == null) return "null";

        var sb = new StringBuilder();
        WriteValue(sb, obj);
        return sb.ToString();
    }

    private static void WriteValue(StringBuilder sb, object? value)
    {
        if (value == null)
        {
            sb.Append("null");
            return;
        }

        var type = value.GetType();

        if (value is string s)
        {
            WriteString(sb, s);
        }
        else if (value is bool b)
        {
            sb.Append(b ? "true" : "false");
        }
        else if (value is int or long or short or byte)
        {
            sb.Append(value);
        }
        else if (value is double d)
        {
            sb.Append(d.ToString("G", CultureInfo.InvariantCulture));
        }
        else if (value is float f)
        {
            sb.Append(f.ToString("G", CultureInfo.InvariantCulture));
        }
        else if (value is decimal dec)
        {
            sb.Append(dec.ToString("G", CultureInfo.InvariantCulture));
        }
        else if (value is IList list)
        {
            WriteArray(sb, list);
        }
        else if (type.IsClass || type.IsValueType && !type.IsPrimitive)
        {
            WriteObject(sb, value, type);
        }
        else
        {
            WriteString(sb, value.ToString() ?? "");
        }
    }

    private static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        sb.Append($"\\u{(int)c:X4}");
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    private static void WriteArray(StringBuilder sb, IList list)
    {
        sb.Append('[');
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0) sb.Append(',');
            WriteValue(sb, list[i]);
        }
        sb.Append(']');
    }

    private static void WriteObject(StringBuilder sb, object obj, Type type)
    {
        sb.Append('{');
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        bool first = true;

        foreach (var prop in props)
        {
            if (!prop.CanRead) continue;

            try
            {
                var val = prop.GetValue(obj);

                if (!first) sb.Append(',');
                first = false;

                // camelCase the property name
                var name = ToCamelCase(prop.Name);
                WriteString(sb, name);
                sb.Append(':');
                WriteValue(sb, val);
            }
            catch
            {
                // Skip properties that throw
            }
        }
        sb.Append('}');
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        if (char.IsLower(name[0])) return name;
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    /// <summary>
    /// Simple JSON deserialization — parses a JSON string into a dictionary/list structure.
    /// Used for reading POST body payloads.
    /// </summary>
    public static object? Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        int index = 0;
        return ParseValue(json, ref index);
    }

    private static object? ParseValue(string json, ref int index)
    {
        SkipWhitespace(json, ref index);
        if (index >= json.Length) return null;

        char c = json[index];
        if (c == '{') return ParseObject(json, ref index);
        if (c == '[') return ParseArray(json, ref index);
        if (c == '"') return ParseString(json, ref index);
        if (c == 't' || c == 'f') return ParseBool(json, ref index);
        if (c == 'n') { index += 4; return null; }
        return ParseNumber(json, ref index);
    }

    private static Dictionary<string, object?> ParseObject(string json, ref int index)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        index++; // skip {
        SkipWhitespace(json, ref index);

        while (index < json.Length && json[index] != '}')
        {
            SkipWhitespace(json, ref index);
            string key = ParseString(json, ref index);
            SkipWhitespace(json, ref index);
            index++; // skip :
            var val = ParseValue(json, ref index);
            dict[key] = val;
            SkipWhitespace(json, ref index);
            if (index < json.Length && json[index] == ',') index++;
        }
        if (index < json.Length) index++; // skip }
        return dict;
    }

    private static List<object?> ParseArray(string json, ref int index)
    {
        var list = new List<object?>();
        index++; // skip [
        SkipWhitespace(json, ref index);

        while (index < json.Length && json[index] != ']')
        {
            list.Add(ParseValue(json, ref index));
            SkipWhitespace(json, ref index);
            if (index < json.Length && json[index] == ',') index++;
        }
        if (index < json.Length) index++; // skip ]
        return list;
    }

    private static string ParseString(string json, ref int index)
    {
        index++; // skip opening "
        var sb = new StringBuilder();
        while (index < json.Length && json[index] != '"')
        {
            if (json[index] == '\\')
            {
                index++;
                if (index < json.Length)
                {
                    switch (json[index])
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(json[index]); break;
                    }
                }
            }
            else
            {
                sb.Append(json[index]);
            }
            index++;
        }
        if (index < json.Length) index++; // skip closing "
        return sb.ToString();
    }

    private static bool ParseBool(string json, ref int index)
    {
        if (json[index] == 't') { index += 4; return true; }
        index += 5; return false;
    }

    private static double ParseNumber(string json, ref int index)
    {
        int start = index;
        while (index < json.Length && (char.IsDigit(json[index]) || json[index] == '.' || json[index] == '-' || json[index] == 'e' || json[index] == 'E' || json[index] == '+'))
            index++;
        var numStr = json.Substring(start, index - start);
        double.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double result);
        return result;
    }

    private static void SkipWhitespace(string json, ref int index)
    {
        while (index < json.Length && char.IsWhiteSpace(json[index])) index++;
    }
}
