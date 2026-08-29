namespace ProposalStudio.Models
{
    public class GovernanceSettings
    {
        public Guid Id { get; set; }

        public Guid BusinessId { get; set; }

        /// <summary>Max discount off retail before send is blocked (Phase 1).</summary>
        public decimal DiscountFloorPercent { get; set; } = 8m;

        /// <summary>Spec §5.6: proposals at or above this total need manager approval before send/share.</summary>
        public decimal HighValueThreshold { get; set; } = 1_000_000m;

        public string VatDefaultMode { get; set; } = "line";

        /// <summary>Brand rule: never expose prices on public exports.</summary>
        public bool AllowPublicPrices { get; set; } = false;

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }
}
