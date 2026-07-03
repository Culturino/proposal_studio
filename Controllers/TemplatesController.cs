using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Models;

namespace ProposalStudio.Controllers
{
    [ApiController]
    [Route("api/templates")]
    public class TemplateController : ControllerBase
    {
        private readonly AppDbContext _context;

        public TemplateController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/templates
        [HttpGet]
        public async Task<IActionResult> GetTemplates()
        {
            var templates = await _context.Templates
                .Select(t => new
                {
                    t.Id,
                    t.Name,
                    t.Key,
                    t.Version,
                    t.Active,
                    t.BusinessId,
                    t.CreatedAt
                })
                .ToListAsync();

            return Ok(templates);
        }

        // GET: api/templates/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetTemplate(Guid id)
        {
            var template = await _context.Templates
                .FirstOrDefaultAsync(t => t.Id == id);

            if (template == null)
                return NotFound();

            return Ok(template);
        }

        // POST: api/templates
        [HttpPost]
        public async Task<IActionResult> CreateTemplate(Template template)
        {
            template.Id = Guid.NewGuid();
            template.CreatedAt = DateTimeOffset.UtcNow;
            template.Active = true;

            _context.Templates.Add(template);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetTemplate), new { id = template.Id }, template);
        }

        // PUT: api/templates/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTemplate(Guid id, Template request)
        {
            var template = await _context.Templates.FirstOrDefaultAsync(t => t.Id == id);

            if (template == null)
                return NotFound();

            template.Name = request.Name;
            template.Key = request.Key;
            template.Version = request.Version;
            template.Active = request.Active;

            await _context.SaveChangesAsync();

            return Ok(template);
        }

        // DELETE: api/templates/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTemplate(Guid id)
        {
            var template = await _context.Templates.FirstOrDefaultAsync(t => t.Id == id);

            if (template == null)
                return NotFound();

            _context.Templates.Remove(template);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}