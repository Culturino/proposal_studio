using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalStudio.Data;
using ProposalStudio.Models;
using ProposalStudio.Services;

namespace ProposalStudio.Controllers
{
    [ApiController]
    [Route("api/templates")]
    [Authorize]
    public class TemplateController : ControllerBase
    {
        private readonly AppDbContext _context;

        public TemplateController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/templates/renderers
        [HttpGet("renderers")]
        public IActionResult GetRenderers()
        {
            return Ok(ProposalRendererCatalog.All.Select(r => new
            {
                r.Key,
                r.Name,
                r.Description,
                r.Pages
            }));
        }

        // GET: api/templates
        [HttpGet]
        public async Task<IActionResult> GetTemplates()
        {
            var templates = await _context.Templates
                .Where(t => t.Active)
                .OrderByDescending(t => t.Version)
                .ToListAsync();

            return Ok(templates.Select(ToDto));
        }

        // GET: api/templates/{id}
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetTemplate(Guid id)
        {
            var template = await _context.Templates
                .FirstOrDefaultAsync(t => t.Id == id);

            if (template == null)
                return NotFound();

            return Ok(ToDto(template));
        }

        // POST: api/templates (Admin)
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateTemplate([FromBody] CreateTemplateRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { message = "Name is required." });

            if (!string.IsNullOrWhiteSpace(request.Key) && !ProposalRendererCatalog.IsKnown(request.Key))
                return BadRequest(new { message = "Unknown renderer. Choose one from the renderer list." });

            var renderer = ProposalRendererCatalog.Require(request.Key);

            var business = await _context.Businesses
                .FirstOrDefaultAsync(b => b.Slug == "house-of-pianos");
            if (business == null)
                return BadRequest(new { message = "Business was not found." });

            var kit = await _context.BrandKits
                .FirstOrDefaultAsync(k => k.BusinessId == business.Id);
            var palette = kit == null ? PdfPalette.Defaults : PdfPalette.FromJson(kit.Colors);

            var now = DateTimeOffset.UtcNow;
            var template = new Template
            {
                Id = Guid.NewGuid(),
                BusinessId = business.Id,
                Key = renderer.Key,
                Name = request.Name.Trim(),
                Version = request.Version is > 0 ? request.Version.Value : 1,
                PageSchema = BrandStyleService.MergeSchema(null, palette, kit?.LogoFile, renderer.Key),
                StylingLocked = true,
                Active = request.Active ?? true,
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.Templates.Add(template);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetTemplate), new { id = template.Id }, ToDto(template));
        }

        // PATCH: api/templates/{id} (Admin)
        [HttpPatch("{id:guid}")]
        [Authorize(Roles = "Admin")]
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
                if (!ProposalRendererCatalog.IsKnown(request.Key))
                    return BadRequest(new { message = "Unknown renderer. Choose one from the renderer list." });

                template.Key = ProposalRendererCatalog.Normalize(request.Key);
                template.PageSchema = BrandStyleService.MergeSchema(
                    template.PageSchema,
                    PdfPalette.FromSchema(template.PageSchema),
                    BrandStyleService.LogoFromSchema(template.PageSchema),
                    template.Key);
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

            return Ok(ToDto(template));
        }

        // DELETE: api/templates/{id} (Admin)
        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteTemplate(Guid id)
        {
            var template = await _context.Templates.FirstOrDefaultAsync(t => t.Id == id);

            if (template == null)
                return NotFound();

            template.Active = false;
            template.UpdatedAt = DateTimeOffset.UtcNow;
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private static object ToDto(Template t)
        {
            var renderer = ProposalRendererCatalog.Require(t.Key);
            return new
            {
                t.Id,
                t.Name,
                t.Key,
                t.Version,
                t.Active,
                t.BusinessId,
                t.CreatedAt,
                rendererKey = renderer.Key,
                rendererName = renderer.Name,
                rendererDescription = renderer.Description,
                colors = PdfPalette.FromSchema(t.PageSchema).ToMap()
            };
        }
    }

    public class CreateTemplateRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Key { get; set; }
        public int? Version { get; set; }
        public bool? Active { get; set; }
    }

    public class UpdateTemplateRequest
    {
        public string? Name { get; set; }
        public string? Key { get; set; }
        public int? Version { get; set; }
        public bool? Active { get; set; }
    }
}
