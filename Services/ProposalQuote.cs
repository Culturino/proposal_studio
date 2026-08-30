using System.Globalization;
using System.Text.Json;

namespace ProposalStudio.Services
{
    public record ProposalAddonLine(string Name, decimal Amount);

    /// <summary>Quoted totals: catalog/override × qty, plus selected add-ons.</summary>
    public static class ProposalQuote
    {
        public const decimal VatRate = 0.05m;

        public static IReadOnlyList<ProposalAddonLine> ParseAddons(JsonDocument? doc)
        {
            var list = new List<ProposalAddonLine>();
            if (doc == null) return list;

            try
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array) return list;

                foreach (var el in root.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    var name = ReadString(el, "name", "Name");
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var amount = ReadMoney(el, "price", "Price", "amount", "Amount");
                    list.Add(new ProposalAddonLine(name.Trim(), amount));
                }
            }
            catch (JsonException)
            {
                // Stored JSON is decorative if it cannot be read; do not fail the save.
            }

            return list;
        }

        public static decimal SumAddons(JsonDocument? doc) =>
            ParseAddons(doc).Sum(a => a.Amount);

        /// <summary>
        /// Instrument × qty plus add-ons. Null when the instrument itself is on request.
        /// </summary>
        public static decimal? Total(decimal? unitPrice, int qty, JsonDocument? addons)
        {
            if (unitPrice is not > 0)
                return null;

            var extra = SumAddons(addons);
            return decimal.Round(unitPrice.Value * Math.Max(qty, 1) + extra, 2);
        }

        public static decimal VatAmount(decimal net) =>
            decimal.Round(net * VatRate, 2);

        public static string FormatMoney(decimal value)
        {
            var culture = CultureInfo.GetCultureInfo("en-US");
            return value == decimal.Truncate(value)
                ? value.ToString("N0", culture)
                : value.ToString("N2", culture);
        }

        private static string? ReadString(JsonElement el, params string[] names)
        {
            foreach (var name in names)
            {
                if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                    return prop.GetString();
            }
            return null;
        }

        private static decimal ReadMoney(JsonElement el, params string[] names)
        {
            foreach (var name in names)
            {
                if (!el.TryGetProperty(name, out var prop)) continue;
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var n))
                    return n;
                if (prop.ValueKind == JsonValueKind.String
                    && decimal.TryParse(prop.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p))
                    return p;
            }
            return 0;
        }
    }
}
