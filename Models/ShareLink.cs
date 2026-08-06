namespace ProposalStudio.Models
{
    public class ShareLink
    {
        public Guid Id { get; set; }

        public Guid ProposalId { get; set; }

        /// <summary>Unguessable public token used in /p/{token}.</summary>
        public string Token { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset? ExpiresAt { get; set; }

        public bool Revoked { get; set; }
    }
}
