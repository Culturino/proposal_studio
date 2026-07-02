using System.Text.Json;

namespace ProposalStudio.Models
{
    public class ProposalItem
    {
        public Guid Id { get; set; }

        public Guid ProposalId { get; set; }

        public Guid ProductId { get; set; }

        public string? Finish { get; set; }

        public int Qty { get; set; }

        public decimal? UnitPrice { get; set; }

        public decimal? PriceOverride { get; set; }

        public string[] Included { get; set; } = Array.Empty<string>();

        public string[] Excluded { get; set; } = Array.Empty<string>();

        public JsonDocument Addons { get; set; } = JsonDocument.Parse("[]");

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }
}