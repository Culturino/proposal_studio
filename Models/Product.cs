using System.Text.Json;

namespace ProposalStudio.Models
{
    public class Product
    {
        public Guid Id { get; set; }

        public Guid BrandId { get; set; }

        public Guid CategoryId { get; set; }

        public string Model { get; set; } = string.Empty;

        public JsonDocument Dimensions { get; set; } = JsonDocument.Parse("{}");

        public string[] Features { get; set; } = Array.Empty<string>();

        public string? Blurb { get; set; }

        public string? Tagline { get; set; }

        public string[] Finishes { get; set; } = Array.Empty<string>();

        public Guid? HeroImageId { get; set; }

        public Guid[] GalleryImageIds { get; set; } = Array.Empty<Guid>();

        public string[] DefaultIncludes { get; set; } = Array.Empty<string>();

        public string[] DefaultExcludes { get; set; } = Array.Empty<string>();

        public string? AvailabilityNote { get; set; }

        public string Status { get; set; } = "active";

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }
}