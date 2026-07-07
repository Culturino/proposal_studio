using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;

namespace ProposalStudio.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
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
    }
}