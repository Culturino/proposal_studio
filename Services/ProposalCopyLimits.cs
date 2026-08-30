namespace ProposalStudio.Services
{
    /// <summary>
    /// Copy lengths that fit the Piano Luxury instrument page without colliding
    /// with the feature row (tagline at 148pt, blurb from 180pt, features at 360pt).
    /// </summary>
    public static class ProposalCopyLimits
    {
        /// <summary>
        /// ~180pt of copy at 13.7pt × 1.38 leading on a 442pt measure ≈ 8 lines / 520 characters.
        /// </summary>
        public const int BlurbMaxChars = 520;

        /// <summary>One italic line at 15.1pt on a 445pt measure.</summary>
        public const int TaglineMaxChars = 72;
    }
}
