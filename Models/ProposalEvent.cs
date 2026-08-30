namespace ProposalStudio.Models
{
    /// <summary>
    /// Public share-link event (opened / download). Spec §5.8 / §12 hashes the IP.
    /// </summary>
    public class ProposalEvent
    {
        public Guid Id { get; set; }

        public Guid ProposalId { get; set; }

        /// <summary>opened | download</summary>
        public string Type { get; set; } = "opened";

        public DateTimeOffset CreatedAt { get; set; }

        public string? IpHash { get; set; }
    }
}
