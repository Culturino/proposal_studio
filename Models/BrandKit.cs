using System.Text.Json;

namespace ProposalStudio.Models
{
    /// <summary>Current House of Pianos palette. Saving it versions every active template.</summary>
    public class BrandKit
    {
        public Guid Id { get; set; }

        public Guid BusinessId { get; set; }

        public JsonDocument Colors { get; set; } = JsonDocument.Parse("{}");

        /// <summary>File name under wwwroot/brand. Null means the shipped hop-logo.png.</summary>
        public string? LogoFile { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }
}
