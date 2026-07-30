using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Models;

namespace ProposalStudio.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BusinessesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public BusinessesController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/businesses
        [HttpGet]
        public async Task<ActionResult<List<Business>>> GetBusinesses()
        {
            var businesses = await _context.Businesses
                .OrderBy(b => b.Name)
                .ToListAsync();

            return Ok(businesses);
        }

        // GET: api/businesses/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<Business>> GetBusiness(Guid id)
        {
            var business = await _context.Businesses
                .FirstOrDefaultAsync(b => b.Id == id);

            if (business == null)
            {
                return NotFound();
            }

            return Ok(business);
        }

        // POST: api/businesses
        [HttpPost]
        public async Task<IActionResult> CreateBusiness([FromBody] CreateBusinessRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Slug))
            {
                return BadRequest("Business name and slug are required.");
            }

            var business = new Business
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                Slug = request.Slug.Trim().ToLower(),
                ReferencePrefix = string.IsNullOrWhiteSpace(request.ReferencePrefix) ? "" : request.ReferencePrefix.Trim(),
                BrandKitId = request.BrandKitId,
                Active = true, // Default to active on creation
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _context.Businesses.Add(business);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetBusiness), new { id = business.Id }, business);
        }

        // PATCH: api/businesses/{id}
        [HttpPatch("{id}")]
        public async Task<IActionResult> UpdateBusiness(Guid id, [FromBody] UpdateBusinessRequest request)
        {
            var business = await _context.Businesses.FirstOrDefaultAsync(b => b.Id == id);

            if (business == null)
            {
                return NotFound();
            }

            if (request.Name != null && !string.IsNullOrWhiteSpace(request.Name))
            {
                business.Name = request.Name.Trim();
            }

            if (request.Slug != null && !string.IsNullOrWhiteSpace(request.Slug))
            {
                business.Slug = request.Slug.Trim().ToLower();
            }

            if (request.ReferencePrefix != null)
            {
                business.ReferencePrefix = request.ReferencePrefix.Trim();
            }

            if (request.BrandKitId.HasValue)
            {
                business.BrandKitId = request.BrandKitId.Value;
            }

            if (request.Active.HasValue)
            {
                business.Active = request.Active.Value;
            }

            business.UpdatedAt = DateTimeOffset.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(business);
        }

        // DELETE: api/businesses/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteBusiness(Guid id)
        {
            var business = await _context.Businesses.FirstOrDefaultAsync(b => b.Id == id);

            if (business == null)
            {
                return NotFound();
            }

            _context.Businesses.Remove(business);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }

    public class CreateBusinessRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? ReferencePrefix { get; set; }
        public Guid? BrandKitId { get; set; }
    }

    public class UpdateBusinessRequest
    {
        public string? Name { get; set; }
        public string? Slug { get; set; }
        public string? ReferencePrefix { get; set; }
        public Guid? BrandKitId { get; set; }
        public bool? Active { get; set; }
    }
}