namespace ProposalStudio.Models
{
    public class User
    {
        public Guid Id { get; set; }

        public Guid BusinessId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string? Phone { get; set; }

        public string? PasswordHash { get; set; }

        public string Role { get; set; } = "advisor";

        public bool Active { get; set; }

        // Later plans.
        public bool TwoFactorEnabled { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }
}