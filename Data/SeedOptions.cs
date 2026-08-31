namespace ProposalStudio.Data
{
    /// <summary>
    /// Bound from the "Seed" configuration section. Controls which seed tiers run and how the
    /// first administrator is created on an otherwise empty production database.
    /// </summary>
    public class SeedOptions
    {
        public const string SectionName = "Seed";

        /// <summary>
        /// Creates the four shared-password demo accounts. Development only — these are
        /// well-known credentials and must never be enabled on a public deployment.
        /// </summary>
        public bool DemoUsers { get; set; }

        /// <summary>
        /// Also create Layla / Omar / Sara when <see cref="DemoUsers"/> is on.
        /// Off on Host so only the admin account exists.
        /// </summary>
        public bool DemoTeam { get; set; } = true;

        /// <summary>
        /// Seed Steinway catalog products and addons. Off on Host —
        /// those rows get new ids that do not match photos on disk.
        /// </summary>
        public bool SeedCatalog { get; set; } = true;

        /// <summary>
        /// Sign-in address for the bootstrap administrator, used only when the users table is
        /// empty and <see cref="DemoUsers"/> is off. Supply via secrets, not appsettings.
        /// </summary>
        public string? AdminEmail { get; set; }

        /// <summary>
        /// Initial password for the bootstrap administrator. Supply via secrets and change it
        /// after the first sign-in.
        /// </summary>
        public string? AdminPassword { get; set; }

        public string? AdminName { get; set; }
    }
}
