using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Models;
using ProposalStudio.Services;

namespace ProposalStudio.Controllers
{
    [ApiController]
    [Route("api/governance")]
    [Authorize]
    public class GovernanceController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly PricingGovernance _pricing;

        public GovernanceController(AppDbContext context, PricingGovernance pricing)
        {
            _context = context;
            _pricing = pricing;
        }

        // GET: api/governance
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] Guid? businessId = null)
        {
            var access = await BusinessScope.ResolveAsync(_context, User);
            var houseId = access.TargetId(businessId);
            if (houseId is not Guid id)
                return BadRequest(BusinessScope.MissingMessage);

            var settings = await _pricing.GetForBusinessAsync(id);

            return Ok(new
            {
                settings.Id,
                settings.BusinessId,
                settings.DiscountFloorPercent,
                settings.HighValueThreshold,
                settings.VatDefaultMode,
                settings.AllowPublicPrices
            });
        }

        // PATCH: api/governance (Admin only)
        [HttpPatch]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(
            [FromBody] UpdateGovernanceRequest request,
            [FromQuery] Guid? businessId = null)
        {
            var access = await BusinessScope.ResolveAsync(_context, User);
            var houseId = access.TargetId(businessId);
            if (houseId is not Guid id)
                return BadRequest(BusinessScope.MissingMessage);

            var settings = await _context.GovernanceSettings
                .FirstOrDefaultAsync(g => g.BusinessId == id);

            var now = DateTimeOffset.UtcNow;

            if (settings == null)
            {
                settings = new GovernanceSettings
                {
                    Id = Guid.NewGuid(),
                    BusinessId = id,
                    DiscountFloorPercent = 8m,
                    HighValueThreshold = 1_000_000m,
                    VatDefaultMode = "line",
                    AllowPublicPrices = false,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _context.GovernanceSettings.Add(settings);
            }

            if (request.DiscountFloorPercent.HasValue)
            {
                settings.DiscountFloorPercent = request.DiscountFloorPercent.Value;
            }

            if (request.HighValueThreshold.HasValue)
            {
                settings.HighValueThreshold = request.HighValueThreshold.Value;
            }

            if (request.VatDefaultMode != null && !string.IsNullOrWhiteSpace(request.VatDefaultMode))
            {
                settings.VatDefaultMode = request.VatDefaultMode.Trim();
            }

            if (request.AllowPublicPrices.HasValue)
            {
                settings.AllowPublicPrices = request.AllowPublicPrices.Value;
            }

            settings.UpdatedAt = now;
            await _context.SaveChangesAsync();

            return Ok(settings);
        }
    }

    public class UpdateGovernanceRequest
    {
        public decimal? DiscountFloorPercent { get; set; }
        public decimal? HighValueThreshold { get; set; }
        public string? VatDefaultMode { get; set; }
        public bool? AllowPublicPrices { get; set; }
    }
}
