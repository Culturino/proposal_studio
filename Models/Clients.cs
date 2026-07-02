namespace ProposalStudio.Models
{
    public class Client
    {
        public Guid Id { get; set; }

        public Guid BusinessId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Email { get; set; }

        public string? Phone { get; set; }

        public string? Notes { get; set; }

        public Guid? CreatedBy { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }
}