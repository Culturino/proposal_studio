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

        [HttpGet]
        public async Task<ActionResult<List<Business>>> GetBusinesses()
        {
            var businesses = await _context.Businesses
                .OrderBy(b => b.Name)
                .ToListAsync();

            return Ok(businesses);
        }

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
    }
}