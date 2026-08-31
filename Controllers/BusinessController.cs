using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Models;
using ProposalStudio.Services;

namespace ProposalStudio.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class BusinessesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public BusinessesController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("current")]
        [AllowAnonymous]
        public async Task<IActionResult> GetCurrent()
        {
            Business? row = null;
            if (User.Identity?.IsAuthenticated == true)
                row = await BusinessScope.ForUserAsync(_context, User);

            row ??= await _context.Businesses
                .Where(b => b.Active)
                .OrderBy(b => b.CreatedAt)
                .FirstOrDefaultAsync();

            if (row == null)
            {
                return NotFound("No business is configured.");
            }

            return Ok(new
            {
                row.Id,
                row.Name,
                row.Slug,
                row.ReferencePrefix,
                row.Blurb,
                row.Phone,
                row.Website,
                row.Instagram,
                row.Address
            });
        }

        // GET: api/businesses
        [HttpGet]
        public async Task<ActionResult<List<Business>>> GetBusinesses()
        {
            if (BusinessScope.IsPlatformAdmin(User))
            {
                var all = await _context.Businesses
                    .Where(b => b.Active)
                    .OrderBy(b => b.Name)
                    .ToListAsync();
                return Ok(all);
            }

            var access = await BusinessScope.ResolveAsync(_context, User);
            if (!access.Ok)
                return BadRequest(BusinessScope.MissingMessage);

            var businesses = await _context.Businesses
                .Where(b => b.Id == access.HomeId)
                .OrderBy(b => b.Name)
                .ToListAsync();

            return Ok(businesses);
        }

        // GET: api/businesses/{id}
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<Business>> GetBusiness(Guid id)
        {
            var access = await BusinessScope.ResolveAsync(_context, User);
            if (!access.Ok)
                return BadRequest(BusinessScope.MissingMessage);

            if (!access.Unrestricted && access.HomeId != id)
                return NotFound();

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
        [Authorize(Roles = "Admin")]
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
                Blurb = string.IsNullOrWhiteSpace(request.Blurb) ? null : request.Blurb.Trim(),
                BrandKitId = request.BrandKitId,
                Active = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _context.Businesses.Add(business);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetBusiness), new { id = business.Id }, business);
        }

        // PATCH: api/businesses/{id}
        [HttpPatch("{id:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateBusiness(Guid id, [FromBody] UpdateBusinessRequest request)
        {
            var access = await BusinessScope.ResolveAsync(_context, User);
            if (!access.Ok)
                return BadRequest(BusinessScope.MissingMessage);

            if (!access.Unrestricted && access.HomeId != id)
                return NotFound();

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

            if (request.Blurb != null)
            {
                business.Blurb = string.IsNullOrWhiteSpace(request.Blurb) ? null : request.Blurb.Trim();
            }

            if (request.Phone != null)
            {
                business.Phone = string.IsNullOrWhiteSpace(request.Phone) ? "" : request.Phone.Trim();
            }

            if (request.Website != null)
            {
                business.Website = string.IsNullOrWhiteSpace(request.Website) ? "" : request.Website.Trim();
            }

            if (request.Instagram != null)
            {
                business.Instagram = string.IsNullOrWhiteSpace(request.Instagram) ? "" : request.Instagram.Trim();
            }

            if (request.Address != null)
            {
                business.Address = string.IsNullOrWhiteSpace(request.Address) ? "" : request.Address.Trim();
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
        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteBusiness(Guid id)
        {
            var access = await BusinessScope.ResolveAsync(_context, User);
            if (!access.Ok)
                return BadRequest(BusinessScope.MissingMessage);

            if (!access.Unrestricted && access.HomeId != id)
                return NotFound();

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
        public string? Blurb { get; set; }
        public Guid? BrandKitId { get; set; }
    }

    public class UpdateBusinessRequest
    {
        public string? Name { get; set; }
        public string? Slug { get; set; }
        public string? ReferencePrefix { get; set; }
        public string? Blurb { get; set; }
        public string? Phone { get; set; }
        public string? Website { get; set; }
        public string? Instagram { get; set; }
        public string? Address { get; set; }
        public Guid? BrandKitId { get; set; }
        public bool? Active { get; set; }
    }
}
