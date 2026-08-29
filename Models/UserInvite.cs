namespace ProposalStudio.Models
{
    /// <summary>
    /// One-time onboarding link. The admin picks the role; the invitee fills in their own details.
    /// </summary>
    public class UserInvite
    {
        public Guid Id { get; set; }

        public string Token { get; set; } = string.Empty;

        public string Role { get; set; } = "advisor";

        public Guid CreatedBy { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset ExpiresAt { get; set; }

        public DateTimeOffset? UsedAt { get; set; }

        public Guid? CreatedUserId { get; set; }

        public bool Revoked { get; set; }
    }
}
