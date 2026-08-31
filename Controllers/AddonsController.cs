using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Models;
using ProposalStudio.Services;

namespace ProposalStudio.Controllers
{
    [ApiController]
    [Route("api/addons")]
    [Authorize]
    public class AddonsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AddonsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAddons()
        {
            var access = await BusinessScope.ResolveAsync(_context, User);
            if (!access.Ok)
                return BadRequest(BusinessScope.MissingMessage);

            var addons = await BusinessScope.Filter(_context.Addons, access, a => a.BusinessId)
                .Where(a => a.Active)
                .Select(a => new
                {
                    a.Id,
                    a.BusinessId,
                    a.Name,
                    a.Amount,
                    a.Currency,
                    a.Active,
                    a.CreatedAt,
                    a.UpdatedAt
                })
                .ToListAsync();

            return Ok(addons);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetAddon(Guid id)
        {
            var access = await BusinessScope.ResolveAsync(_context, User);
            if (!access.Ok)
                return BadRequest(BusinessScope.MissingMessage);

            var addon = await BusinessScope.Filter(_context.Addons, access, a => a.BusinessId)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (addon == null)
                return NotFound();

            return Ok(addon);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateAddon(Addon addon)
        {
            var access = await BusinessScope.ResolveAsync(_context, User);
            var houseId = access.TargetId(addon.BusinessId == Guid.Empty ? null : addon.BusinessId);
            if (houseId is not Guid businessId)
                return BadRequest(BusinessScope.MissingMessage);

            addon.Id = Guid.NewGuid();
            addon.BusinessId = businessId;
            addon.CreatedAt = DateTimeOffset.UtcNow;
            addon.UpdatedAt = DateTimeOffset.UtcNow;
            addon.Active = true;

            _context.Addons.Add(addon);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetAddon), new { id = addon.Id }, addon);
        }

        [HttpPatch("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateAddon(Guid id, [FromBody] UpdateAddonRequest request)
        {
            var access = await BusinessScope.ResolveAsync(_context, User);
            if (!access.Ok)
                return BadRequest(BusinessScope.MissingMessage);

            var addon = await BusinessScope.Filter(_context.Addons, access, a => a.BusinessId)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (addon == null)
                return NotFound();

            if (request.Name != null)
            {
                if (!string.IsNullOrWhiteSpace(request.Name))
                {
                    addon.Name = request.Name.Trim();
                }
            }

            if (request.Amount.HasValue)
            {
                addon.Amount = request.Amount.Value;
            }

            if (request.Currency != null)
            {
                if (!string.IsNullOrWhiteSpace(request.Currency))
                {
                    addon.Currency = request.Currency.Trim();
                }
            }

            if (request.Active.HasValue)
            {
                addon.Active = request.Active.Value;
            }

            addon.UpdatedAt = DateTimeOffset.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(addon);
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteAddon(Guid id)
        {
            var access = await BusinessScope.ResolveAsync(_context, User);
            if (!access.Ok)
                return BadRequest(BusinessScope.MissingMessage);

            var addon = await BusinessScope.Filter(_context.Addons, access, a => a.BusinessId)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (addon == null)
                return NotFound();

            // Soft-delete — hard remove would be re-seeded on older builds; marker + inactive keeps it gone
            addon.Active = false;
            addon.UpdatedAt = DateTimeOffset.UtcNow;
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }

    public class UpdateAddonRequest
    {
        public string? Name { get; set; }
        public decimal? Amount { get; set; }
        public string? Currency { get; set; }
        public bool? Active { get; set; }
    }
}