namespace ProposalStudio.Models
{
    public class ApprovalRequest
    {
        public Guid Id { get; set; }

        public Guid ProposalId { get; set; }

        /// <summary>e.g. below_floor</summary>
        public string Kind { get; set; } = "below_floor";

        public Guid RequestedBy { get; set; }

        public Guid? ApproverId { get; set; }

        /// <summary>pending | approved | rejected</summary>
        public string Status { get; set; } = "pending";

        public decimal? OfferedPrice { get; set; }

        public decimal? FloorPrice { get; set; }

        public decimal? CatalogPrice { get; set; }

        public string? Message { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset? DecidedAt { get; set; }
    }

    public class AppNotification
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;

        public string? Kind { get; set; }

        public Guid? RelatedId { get; set; }

        public bool Read { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
    }
}
