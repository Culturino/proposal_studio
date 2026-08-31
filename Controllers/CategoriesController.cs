using System.Text;
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
    public class CategoriesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly AuditService _audit;

        public CategoriesController(AppDbContext context, AuditService audit)
        {
            _context = context;
            _audit = audit;
        }

        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            var access = await BusinessScope.ResolveAsync(_context, User);
            if (!access.Ok)
                return BadRequest(BusinessScope.MissingMessage);

            var categories = await BusinessScope.Filter(_context.ProductCategories, access, c => c.BusinessId)
                .OrderBy(c => c.CreatedAt)
                .ThenBy(c => c.Name)
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.Slug,
                    c.Active,
                    ProductCount = _context.Products.Count(p =>
                        p.CategoryId == c.Id && p.Status != "archived" && p.Status != "deleted"),
                    c.CreatedAt,
                    c.UpdatedAt
                })
                .ToListAsync();

            return Ok(categories);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetCategory(Guid id)
        {
            var access = await BusinessScope.ResolveAsync(_context, User);
            if (!access.Ok)
                return BadRequest(BusinessScope.MissingMessage);

            var category = await BusinessScope.Filter(_context.ProductCategories, access, c => c.BusinessId)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (category == null)
            {
                return NotFound("Category not found");
            }

            return Ok(category);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest("Category name is required.");
            }

            var access = await BusinessScope.ResolveAsync(_context, User);
            var houseId = access.TargetId();
            if (houseId is not Guid businessId)
                return BadRequest(BusinessScope.MissingMessage);

            var name = request.Name.Trim();
            var slug = string.IsNullOrWhiteSpace(request.Slug)
                ? Slugify(name)
                : Slugify(request.Slug);
            if (string.IsNullOrWhiteSpace(slug))
            {
                return BadRequest("Category name must include letters or numbers.");
            }

            var duplicate = await _context.ProductCategories
                .AnyAsync(c => c.BusinessId == businessId && c.Slug == slug);
            if (duplicate)
            {
                return Conflict($"A category with slug \"{slug}\" already exists.");
            }

            var category = new ProductCategory
            {
                Id = Guid.NewGuid(),
                BusinessId = businessId,
                Name = name,
                Slug = slug,
                Active = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _context.ProductCategories.Add(category);
            await _context.SaveChangesAsync();
            await _audit.LogAsync(User, "create", "category", category.Id, null, AuditService.CategorySnapshot(category));

            return CreatedAtAction(nameof(GetCategory), new { id = category.Id }, category);
        }

        [HttpPatch("{id:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateCategory(Guid id, [FromBody] UpdateCategoryRequest request)
        {
            var access = await BusinessScope.ResolveAsync(_context, User);
            if (!access.Ok)
                return BadRequest(BusinessScope.MissingMessage);

            var category = await BusinessScope.Filter(_context.ProductCategories, access, c => c.BusinessId)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (category == null)
            {
                return NotFound("Category not found");
            }

            var before = AuditService.CategorySnapshot(category);

            if (request.Name != null && !string.IsNullOrWhiteSpace(request.Name))
            {
                category.Name = request.Name.Trim();
            }

            if (request.Slug != null)
            {
                var slug = Slugify(request.Slug);
                if (string.IsNullOrWhiteSpace(slug))
                    return BadRequest("Slug must include letters or numbers.");
                category.Slug = slug;
            }

            if (request.Active.HasValue)
            {
                category.Active = request.Active.Value;
            }

            var collides = await _context.ProductCategories
                .AnyAsync(c => c.Id != category.Id
                    && c.BusinessId == category.BusinessId
                    && c.Slug == category.Slug);
            if (collides)
            {
                return Conflict($"A category with slug \"{category.Slug}\" already exists.");
            }

            category.UpdatedAt = DateTimeOffset.UtcNow;
            await _context.SaveChangesAsync();
            await _audit.LogAsync(User, "update", "category", category.Id, before, AuditService.CategorySnapshot(category));

            return Ok(category);
        }

        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteCategory(Guid id)
        {
            var access = await BusinessScope.ResolveAsync(_context, User);
            if (!access.Ok)
                return BadRequest(BusinessScope.MissingMessage);

            var category = await BusinessScope.Filter(_context.ProductCategories, access, c => c.BusinessId)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (category == null)
            {
                return NotFound("Category not found");
            }

            var inUse = await _context.Products.CountAsync(p =>
                p.CategoryId == id && p.Status != "deleted");
            if (inUse > 0)
            {
                return Conflict(
                    $"\"{category.Name}\" still has {inUse} product{(inUse == 1 ? "" : "s")}. Move or delete those first.");
            }

            var before = AuditService.CategorySnapshot(category);
            _context.ProductCategories.Remove(category);
            await _context.SaveChangesAsync();
            await _audit.LogAsync(User, "delete", "category", category.Id, before, null);

            return NoContent();
        }

        private static string Slugify(string value)
        {
            var s = value.Trim().ToLowerInvariant();
            var sb = new StringBuilder();
            foreach (var ch in s)
            {
                if (char.IsLetterOrDigit(ch))
                    sb.Append(ch);
                else if (ch is ' ' or '-' or '_')
                {
                    if (sb.Length > 0 && sb[^1] != '-')
                        sb.Append('-');
                }
            }
            return sb.ToString().Trim('-');
        }
    }

    public class CreateCategoryRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Slug { get; set; }
    }

    public class UpdateCategoryRequest
    {
        public string? Name { get; set; }
        public string? Slug { get; set; }
        public bool? Active { get; set; }
    }
}
