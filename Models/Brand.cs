namespace ProposalStudio.Models
{
    public class Brand
    {
        public Guid Id { get; set; }

        public Guid BusinessId { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>Brand story shown in catalog / proposal copy.</summary>
        public string? Blurb { get; set; }

        public Guid? LogoAssetId { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }
}