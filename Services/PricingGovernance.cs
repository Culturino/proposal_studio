using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Models;

namespace ProposalStudio.Services
{
    public class PricingGovernance
    {
        private readonly AppDbContext _context;

        public PricingGovernance(AppDbContext context)
        {
            _context = context;
        }

        public async Task<GovernanceSettings> GetForBusinessAsync(Guid businessId)
        {
            var settings = await _context.GovernanceSettings
                .FirstOrDefaultAsync(g => g.BusinessId == businessId);

            if (settings != null)
            {
                return settings;
            }

            // Sensible defaults when row is missing
            return new GovernanceSettings
            {
                BusinessId = businessId,
                DiscountFloorPercent = 8m,
                HighValueThreshold = 1_000_000m,
                VatDefaultMode = "line",
                AllowPublicPrices = false
            };
        }

        /// <summary>
        /// Lowest unit price an advisor may use without approval.
        /// Null catalog price means "On request" — no floor applies.
        /// </summary>
        public decimal? FloorUnitPrice(decimal? catalogAmount, decimal floorPercent)
        {
            if (catalogAmount == null || catalogAmount <= 0)
            {
                return null;
            }

            var factor = 1m - (floorPercent / 100m);
            return Math.Round(catalogAmount.Value * factor, 2);
        }

        public bool IsBelowFloor(decimal? offeredUnitPrice, decimal? catalogAmount, decimal floorPercent)
        {
            var floor = FloorUnitPrice(catalogAmount, floorPercent);
            if (floor == null || offeredUnitPrice == null)
            {
                return false;
            }

            return offeredUnitPrice < floor;
        }

        public bool IsHighValue(decimal? total, decimal threshold)
        {
            return total.HasValue && total.Value >= threshold;
        }
    }
}
