namespace ProposalStudio.Services
{
    public static class ProposalStatuses
    {
        public const string Draft = "draft";
        public const string Sent = "sent";
        public const string Viewed = "viewed";
        public const string Accepted = "accepted";
        public const string Expired = "expired";

        public static readonly string[] All =
            [Draft, Sent, Viewed, Accepted, Expired];

        public static string? Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var key = value.Trim().ToLowerInvariant();
            return All.Contains(key) ? key : null;
        }
    }
}
