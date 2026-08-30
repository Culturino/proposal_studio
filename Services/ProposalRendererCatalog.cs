namespace ProposalStudio.Services
{
    /// <summary>
    /// Named PDF layouts. A template's <c>Key</c> (and schema <c>renderer</c>)
    /// picks one of these. Add a new layout by registering it here and handling
    /// the key in <see cref="ProposalPdfService.GenerateFor"/>.
    /// </summary>
    public static class ProposalRendererCatalog
    {
        public const string PianoLuxury = "piano_luxury";

        public static IReadOnlyList<ProposalRendererInfo> All { get; } =
        [
            new(
                PianoLuxury,
                "Piano — Luxury",
                "Six-page 16:9 private proposal: cover, instrument, gallery, specifications, investment, closing.",
                ["cover", "instrument", "gallery", "specs", "investment", "closing"])
        ];

        public static ProposalRendererInfo? Find(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            var normalized = NormalizeToken(key);
            return All.FirstOrDefault(r =>
                string.Equals(NormalizeToken(r.Key), normalized, StringComparison.Ordinal));
        }

        public static bool IsKnown(string? key) => Find(key) != null;

        /// <summary>Unknown or empty keys fall back to the luxury deck.</summary>
        public static string Normalize(string? key) => Find(key)?.Key ?? PianoLuxury;

        public static ProposalRendererInfo Require(string? key) =>
            Find(key) ?? All[0];

        private static string NormalizeToken(string key) =>
            key.Trim().Replace('-', '_').ToLowerInvariant();
    }

    public sealed record ProposalRendererInfo(
        string Key,
        string Name,
        string Description,
        IReadOnlyList<string> Pages);
}
