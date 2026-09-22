#if NET6_0
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

// Keep the persistence/API surface shared with the .NET Framework build. In
// particular, writing Microsoft JSON dates lets either edition read settings
// saved by the other edition without migrating or duplicating user data.
namespace System.Web.Script.Serialization
{
    public sealed class JavaScriptSerializer
    {
        private int maximumLength = 2097152;
        private int recursionLimit = 100;
        public int MaxJsonLength
        {
            get { return maximumLength; }
            set { if (value < 1) throw new ArgumentOutOfRangeException("value"); maximumLength = value; }
        }
        public int RecursionLimit
        {
            get { return recursionLimit; }
            set { if (value < 1) throw new ArgumentOutOfRangeException("value"); recursionLimit = value; }
        }
        public string Serialize(object value)
        {
            string result = JsonSerializer.Serialize(value, value == null ? typeof(object) : value.GetType(), Options());
            CheckLength(result);
            return result;
        }
        public string Serialize<T>(T value) { return Serialize((object)value); }
        public T Deserialize<T>(string input)
        {
            if (input == null) throw new ArgumentNullException("input");
            CheckLength(input);
            return JsonSerializer.Deserialize<T>(input, Options());
        }
        private void CheckLength(string input)
        {
            if (input.Length > maximumLength) throw new ArgumentException("JSON exceeds MaxJsonLength.");
        }
        private JsonSerializerOptions Options()
        {
            var options = new JsonSerializerOptions
            {
                MaxDepth = recursionLimit,
                PropertyNameCaseInsensitive = true,
                IncludeFields = true
            };
            options.Converters.Add(new MicrosoftDateConverter());
            options.Converters.Add(new UntypedValueConverter());
            return options;
        }

        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly Regex MicrosoftDate = new Regex(@"^/Date\((-?[0-9]+)(?:([+-])([0-9]{2})([0-9]{2}))?\)/$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        private static bool TryReadMicrosoftDate(string text, out DateTime value)
        {
            value = default(DateTime);
            Match match = MicrosoftDate.Match(text);
            if (!match.Success) return false;
            // The number is already UTC milliseconds. The optional offset is
            // metadata, not another adjustment to the represented instant.
            if (match.Groups[2].Success)
            {
                int hours = Int32.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                int minutes = Int32.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
                if (hours > 14 || minutes > 59 || (hours == 14 && minutes != 0)) throw new JsonException("Invalid Microsoft JSON date offset.");
            }
            long milliseconds;
            if (!Int64.TryParse(match.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out milliseconds))
                throw new JsonException("Microsoft JSON date is outside the supported range.");
            try { value = new DateTime(checked(Epoch.Ticks + checked(milliseconds * TimeSpan.TicksPerMillisecond)), DateTimeKind.Utc); }
            catch (ArgumentOutOfRangeException ex) { throw new JsonException("Microsoft JSON date is outside the supported range.", ex); }
            catch (OverflowException ex) { throw new JsonException("Microsoft JSON date is outside the supported range.", ex); }
            return true;
        }

        private sealed class MicrosoftDateConverter : JsonConverter<DateTime>
        {
            public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.String) throw new JsonException("Expected a JSON date string.");
                DateTime value;
                if (TryReadMicrosoftDate(reader.GetString(), out value)) return value;
                if (!reader.TryGetDateTime(out value)) throw new JsonException("Invalid JSON date string.");
                return value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
            }
            public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
            {
                long milliseconds = (value.ToUniversalTime().Ticks - Epoch.Ticks) / TimeSpan.TicksPerMillisecond;
                // JavaScriptSerializer's legacy date parser requires escaped
                // slashes, even though plain slashes are also valid JSON.
                writer.WriteRawValue("\"\\/Date(" + milliseconds.ToString(CultureInfo.InvariantCulture) + ")\\/\"");
            }
        }

        private sealed class UntypedValueConverter : JsonConverter<object>
        {
            public override object Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            {
                using (JsonDocument document = JsonDocument.ParseValue(ref reader)) return Materialize(document.RootElement);
            }
            private static object Materialize(JsonElement element)
            {
                switch (element.ValueKind)
                {
                    case JsonValueKind.Object:
                        var values = new Dictionary<string, object>(StringComparer.Ordinal);
                        foreach (JsonProperty property in element.EnumerateObject()) values[property.Name] = Materialize(property.Value);
                        return values;
                    case JsonValueKind.Array:
                        var items = new List<object>();
                        foreach (JsonElement item in element.EnumerateArray()) items.Add(Materialize(item));
                        return items.ToArray();
                    case JsonValueKind.String:
                        string text = element.GetString();
                        DateTime date;
                        return TryReadMicrosoftDate(text, out date) ? (object)date : text;
                    case JsonValueKind.Number:
                        int integer; long wideInteger; decimal precise; double approximate;
                        if (element.TryGetInt32(out integer)) return integer;
                        if (element.TryGetInt64(out wideInteger)) return wideInteger;
                        if (element.TryGetDecimal(out precise)) return precise;
                        if (element.TryGetDouble(out approximate) && !Double.IsNaN(approximate) && !Double.IsInfinity(approximate)) return approximate;
                        throw new JsonException("JSON number is outside the supported range.");
                    case JsonValueKind.True: return true;
                    case JsonValueKind.False: return false;
                    case JsonValueKind.Null: return null;
                    default: throw new JsonException("Unsupported JSON value.");
                }
            }
            public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
            {
                if (value == null) { writer.WriteNullValue(); return; }
                if (value.GetType() == typeof(object)) { writer.WriteStartObject(); writer.WriteEndObject(); return; }
                JsonSerializer.Serialize(writer, value, value.GetType(), options);
            }
        }
    }
}
#endif
