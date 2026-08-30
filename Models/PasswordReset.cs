namespace ProposalStudio.Models
{
    /// <summary>
    /// One-time admin-issued link so a user can set a new password.
    /// </summary>
    public class PasswordReset
    {
        public Guid Id { get; set; }

        public string Token { get; set; } = string.Empty;

        public Guid UserId { get; set; }

        public Guid CreatedBy { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset ExpiresAt { get; set; }

        public DateTimeOffset? UsedAt { get; set; }

        public bool Revoked { get; set; }
    }
}
