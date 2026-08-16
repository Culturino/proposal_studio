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
    public class BrandsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly AuditService _audit;

        public BrandsController(AppDbContext context, AuditService audit)
        {
            _context = context;
            _audit = audit;
        }

        // GET: api/brands
        [HttpGet]
        public async Task<IActionResult> GetBrands()
        {
            var brands = await _context.Brands
                .OrderBy(b => b.Name)
                .Select(b => new
                {
                    b.Id,
                    b.Name,
                    b.Blurb,
                    b.LogoAssetId
                })
                .ToListAsync();

            return Ok(brands);
        }

        // GET: api/brands/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetBrand(Guid id)
        {
            var brand = await _context.Brands
                .Where(b => b.Id == id)
                .Select(b => new
                {
                    b.Id,
                    b.Name,
                    b.Blurb,
                    b.LogoAssetId
                })
                .FirstOrDefaultAsync();

            if (brand == null)
            {
                return NotFound("Brand not found");
            }

            return Ok(brand);
        }

        // POST: api/brands
        [HttpPost]
        public async Task<IActionResult> CreateBrand([FromBody] CreateBrandRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest("Brand name is required.");
            }

            var brand = new Brand
            {
                Id = Guid.NewGuid(),
                BusinessId = request.BusinessId,
                Name = request.Name.Trim(),
                Blurb = string.IsNullOrWhiteSpace(request.Blurb) ? null : request.Blurb.Trim(),
                LogoAssetId = request.LogoAssetId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _context.Brands.Add(brand);
            await _context.SaveChangesAsync();
            await _audit.LogAsync(User, "create", "brand", brand.Id, null, AuditService.BrandSnapshot(brand));

            return CreatedAtAction(nameof(GetBrand), new { id = brand.Id }, brand);
        }

        // PATCH: api/brands/{id}
        [HttpPatch("{id}")]
        public async Task<IActionResult> UpdateBrand(Guid id, [FromBody] UpdateBrandRequest request)
        {
            var brand = await _context.Brands.FirstOrDefaultAsync(b => b.Id == id);

            if (brand == null)
            {
                return NotFound("Brand not found");
            }

            var before = AuditService.BrandSnapshot(brand);

            if (request.Name != null)
            {
                if (!string.IsNullOrWhiteSpace(request.Name))
                {
                    brand.Name = request.Name.Trim();
                }
            }

            if (request.Blurb != null)
            {
                brand.Blurb = string.IsNullOrWhiteSpace(request.Blurb) ? null : request.Blurb.Trim();
            }

            // Update logo if a new one was provided
            if (request.LogoAssetId.HasValue)
            {
                brand.LogoAssetId = request.LogoAssetId.Value;
            }

            brand.UpdatedAt = DateTimeOffset.UtcNow;

            await _context.SaveChangesAsync();
            await _audit.LogAsync(User, "update", "brand", brand.Id, before, AuditService.BrandSnapshot(brand));

            return Ok(brand);
        }

        // DELETE: api/brands/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteBrand(Guid id)
        {
            var brand = await _context.Brands.FirstOrDefaultAsync(b => b.Id == id);

            if (brand == null)
            {
                return NotFound("Brand not found");
            }

            var before = AuditService.BrandSnapshot(brand);
            _context.Brands.Remove(brand);
            await _context.SaveChangesAsync();
            await _audit.LogAsync(User, "delete", "brand", brand.Id, before, null);

            return NoContent();
        }
    }

    public class CreateBrandRequest
    {
        public Guid BusinessId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Blurb { get; set; }
        public Guid? LogoAssetId { get; set; }
    }

    public class UpdateBrandRequest
    {
        public string? Name { get; set; }
        public string? Blurb { get; set; }
        public Guid? LogoAssetId { get; set; }
    }
}