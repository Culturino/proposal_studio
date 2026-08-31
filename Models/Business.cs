namespace ProposalStudio.Models
{
    public class Business
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Slug { get; set; } = string.Empty;

        public string ReferencePrefix { get; set; } = string.Empty;

        /// <summary>Short house narrative for proposals / brand surfaces.</summary>
        public string? Blurb { get; set; }

        public string? Phone { get; set; }

        public string? Website { get; set; }

        public string? Instagram { get; set; }

        public string? Address { get; set; }

        public Guid? BrandKitId { get; set; }

        public bool Active { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }
}