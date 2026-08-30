using System.Text.Json;

namespace ProposalStudio.Services
{
    /// <summary>
    /// Colors a proposal PDF is drawn with. Taken from the proposal's template version,
    /// which is a snapshot of the brand kit at the time that version was created.
    /// </summary>
    public sealed class PdfPalette
    {
        public string Ink { get; init; } = "#141312";
        public string Ink2 { get; init; } = "#1E1C1A";
        public string Paper { get; init; } = "#FFFFFF";
        public string Cream { get; init; } = "#E6E0D4";
        public string Gold { get; init; } = "#9A8154";
        public string GoldBright { get; init; } = "#B59A6A";
        public string Text { get; init; } = "#1A1815";
        public string Muted { get; init; } = "#6E6860";
        public string Line { get; init; } = "#DCD6CB";

        public string GoldLight => GoldBright;
        public string Charcoal => Text;
        public string BodyInk => Text;
        /// <summary>Captions on ink pages — lighter than muted, quieter than cream headings.</summary>
        public string MutedDark { get; init; } = "#BDB6A9";
        public string Faint => Muted;
        public string ExcludeInk => Muted;
        public string CardLine => Line;
        public string RowLine => Ink2;

        public static PdfPalette Defaults { get; } = new();

        public static Dictionary<string, string> DefaultMap() => new()
        {
            ["ink"] = Defaults.Ink,
            ["ink2"] = Defaults.Ink2,
            ["paper"] = Defaults.Paper,
            ["cream"] = Defaults.Cream,
            ["gold"] = Defaults.Gold,
            ["goldBright"] = Defaults.GoldBright,
            ["text"] = Defaults.Text,
            ["muted"] = Defaults.Muted,
            ["line"] = Defaults.Line
        };

        public Dictionary<string, string> ToMap() => new()
        {
            ["ink"] = Ink,
            ["ink2"] = Ink2,
            ["paper"] = Paper,
            ["cream"] = Cream,
            ["gold"] = Gold,
            ["goldBright"] = GoldBright,
            ["text"] = Text,
            ["muted"] = Muted,
            ["line"] = Line
        };

        public static PdfPalette FromMap(IReadOnlyDictionary<string, string>? map)
        {
            if (map == null || map.Count == 0)
                return Defaults;

            string Pick(string key, string fallback) =>
                map.TryGetValue(key, out var v) && IsHex(v) ? Normalize(v) : fallback;

            var d = Defaults;
            return new PdfPalette
            {
                Ink = Pick("ink", d.Ink),
                Ink2 = Pick("ink2", d.Ink2),
                Paper = Pick("paper", d.Paper),
                Cream = Pick("cream", d.Cream),
                Gold = Pick("gold", d.Gold),
                GoldBright = Pick("goldBright", d.GoldBright),
                Text = Pick("text", d.Text),
                Muted = Pick("muted", d.Muted),
                Line = Pick("line", d.Line),
                MutedDark = Pick("mutedOnDark", d.MutedDark)
            };
        }

        public static PdfPalette FromJson(JsonDocument? document)
        {
            if (document == null)
                return Defaults;
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("colors", out var nested) &&
                nested.ValueKind == JsonValueKind.Object)
                return FromElement(nested);
            return FromElement(document.RootElement);
        }

        public static PdfPalette FromSchema(JsonDocument? schema) => FromJson(schema);

        private static PdfPalette FromElement(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return Defaults;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                    map[prop.Name] = prop.Value.GetString() ?? "";
            }

            return FromMap(map);
        }

        public static bool IsHex(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var v = value.Trim();
            if (v.StartsWith('#')) v = v[1..];
            return (v.Length == 6 || v.Length == 3 || v.Length == 8) &&
                   v.All(c => Uri.IsHexDigit(c));
        }

        public static string Normalize(string value)
        {
            var v = value.Trim();
            if (!v.StartsWith('#')) v = "#" + v;
            if (v.Length == 4)
                v = $"#{v[1]}{v[1]}{v[2]}{v[2]}{v[3]}{v[3]}";
            return v.ToUpperInvariant();
        }
    }
}
