using System.Text.Json;

namespace ProposalStudio.Models
{
    public class Addon
    {
        public Guid Id { get; set; }

        public Guid BusinessId { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal? Amount { get; set; }
        public string Currency { get; set; } = "AED";

        public bool Active { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }
}