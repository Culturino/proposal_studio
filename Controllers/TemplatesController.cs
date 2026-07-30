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
            template.UpdatedAt = DateTimeOffset.UtcNow; // Ensure this exists on model
            template.Active = true;

            _context.Templates.Add(template);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetTemplate), new { id = template.Id }, template);
        }

        // PATCH: api/templates/{id}
        [HttpPatch("{id}")]
        public async Task<IActionResult> UpdateTemplate(Guid id, [FromBody] UpdateTemplateRequest request)
        {
            var template = await _context.Templates.FirstOrDefaultAsync(t => t.Id == id);

            if (template == null)
                return NotFound();

            if (request.Name != null && !string.IsNullOrWhiteSpace(request.Name))
            {
                template.Name = request.Name.Trim();
            }

            if (request.Key != null && !string.IsNullOrWhiteSpace(request.Key))
            {
                template.Key = request.Key.Trim();
            }

            if (request.Version.HasValue)
            {
                template.Version = request.Version.Value;
            }

            if (request.Active.HasValue)
            {
                template.Active = request.Active.Value;
            }

            template.UpdatedAt = DateTimeOffset.UtcNow;

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

    public class UpdateTemplateRequest
    {
        public string? Name { get; set; }
        public string? Key { get; set; }
        public int? Version { get; set; }
        public bool? Active { get; set; }
    }
}