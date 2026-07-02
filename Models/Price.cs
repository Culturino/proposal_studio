namespace ProposalStudio.Models
{
    public class Price
    {
        public Guid Id { get; set; }

        public Guid ProductId { get; set; }

        public string Currency { get; set; } = "AED";

        public string? Finish { get; set; }

        public decimal? Amount { get; set; }

        public DateOnly? ValidFrom { get; set; }

        public string? Source { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }
}