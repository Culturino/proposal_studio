namespace ProposalStudio.Models
{
    /// <summary>
    /// Marker rows the seeder writes so a first boot can be told apart from every later one.
    /// </summary>
    public class SeedMeta
    {
        public string Key { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;

        public DateTimeOffset UpdatedAt { get; set; }
    }
}
