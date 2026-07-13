using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Models;

namespace ProposalStudio.Controllers
{
    [ApiController]
    [Route("api/addons")]
    public class AddonsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AddonsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/templates
        [HttpGet]
        public async Task<IActionResult> GetAddons()
        {
            var addons = await _context.Addons
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

        // GET: api/addon/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetAddon(Guid id)
        {
            var addon = await _context.Addons
                .FirstOrDefaultAsync(a => a.Id == id);

            if (addon == null)
                return NotFound();

            return Ok(addon);
        }

        // POST: api/templates
        [HttpPost]
        public async Task<IActionResult> CreateAddon(Addon addon)
        {
            addon.Id = Guid.NewGuid();
            addon.CreatedAt = DateTimeOffset.UtcNow;
            addon.Active = true;

            _context.Addons.Add(addon);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetAddon), new { id = addon.Id }, addon);
        }

        // PUT: api/addon/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateAddon(Guid id, Addon request)
        {
            var addon = await _context.Addons.FirstOrDefaultAsync(a => a.Id == id);

            if (addon == null)
                return NotFound();

            addon.Name = request.Name;
            addon.Amount = request.Amount;

            addon.Active = request.Active;

            await _context.SaveChangesAsync();

            return Ok(addon);
        }

        // DELETE: api/addon/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAddon(Guid id)
        {
            var addon = await _context.Addons.FirstOrDefaultAsync(a => a.Id == id);

            if (addon == null)
                return NotFound();

            _context.Addons.Remove(addon);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}