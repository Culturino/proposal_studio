using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Models;

namespace ProposalStudio.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class BrandsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public BrandsController(AppDbContext context)
        {
            _context = context;
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
                LogoAssetId = request.LogoAssetId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _context.Brands.Add(brand);
            await _context.SaveChangesAsync();

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

            if (request.Name != null)
            {
                if (!string.IsNullOrWhiteSpace(request.Name))
                {
                    brand.Name = request.Name.Trim();
                }
            }

            // Update logo if a new one was provided
            if (request.LogoAssetId.HasValue)
            {
                brand.LogoAssetId = request.LogoAssetId.Value;
            }

            brand.UpdatedAt = DateTimeOffset.UtcNow;

            await _context.SaveChangesAsync();

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

            _context.Brands.Remove(brand);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }

    public class CreateBrandRequest
    {
        public Guid BusinessId { get; set; }
        public string Name { get; set; } = string.Empty;
        public Guid? LogoAssetId { get; set; }
    }

    public class UpdateBrandRequest
    {
        public string? Name { get; set; }
        public Guid? LogoAssetId { get; set; }
    }
}