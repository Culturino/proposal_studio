using System.Text.Json;

namespace ProposalStudio.Models
{
    public class Proposal
    {
        public Guid Id { get; set; }

        public Guid BusinessId { get; set; }

        public int ReferenceNumber { get; set; }

        public string Reference { get; set; } = string.Empty;

        public Guid TemplateId { get; set; }

        public int TemplateVersion { get; set; }

        public Guid ClientId { get; set; }

        public Guid AdvisorId { get; set; }

        public string Currency { get; set; } = "AED";

        public string VatMode { get; set; } = "line";

        public int ValidityDays { get; set; }

        public string Status { get; set; } = "draft";

        public decimal? PriceTotal { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public DateTimeOffset? SentAt { get; set; }

        public DateTimeOffset? ExpiresAt { get; set; }

        public JsonDocument? Snapshot { get; set; }

        public string? PdfUrl { get; set; }
    }
}