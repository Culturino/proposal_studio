using System.Text.Json;
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
    public class ProductsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ProductsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/products?brand=&category=&q=
        [HttpGet]
        public async Task<IActionResult> GetProducts(
            [FromQuery] string? brand = null,
            [FromQuery] string? category = null,
            [FromQuery] string? q = null)
        {
            var query =
                from product in _context.Products
                join b in _context.Brands on product.BrandId equals b.Id
                join cat in _context.ProductCategories on product.CategoryId equals cat.Id
                where product.Status != "archived" && product.Status != "deleted"
                select new { product, b, cat };

            if (!string.IsNullOrWhiteSpace(brand))
            {
                var term = brand.Trim().ToLower();
                query = query.Where(x => x.b.Name.ToLower().Contains(term));
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                var term = category.Trim().ToLower();
                query = query.Where(x => x.cat.Name.ToLower().Contains(term) || x.cat.Slug.ToLower() == term);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(x =>
                    x.product.Model.ToLower().Contains(term) ||
                    (x.product.Tagline != null && x.product.Tagline.ToLower().Contains(term)) ||
                    (x.product.Blurb != null && x.product.Blurb.ToLower().Contains(term)));
            }

            var products = await query
                .OrderBy(x => x.b.Name)
                .ThenBy(x => x.product.Model)
                .Select(x => new
                {
                    x.product.Id,
                    x.product.BrandId,
                    x.product.CategoryId,
                    Brand = x.b.Name,
                    Category = x.cat.Name,
                    x.product.Model,
                    x.product.Dimensions,
                    x.product.Features,
                    x.product.Blurb,
                    x.product.Tagline,
                    x.product.Finishes,
                    x.product.DefaultIncludes,
                    x.product.DefaultExcludes,
                    x.product.AvailabilityNote,
                    x.product.Status,
                    Price = _context.Prices
                        .Where(p => p.ProductId == x.product.Id)
                        .OrderByDescending(p => p.ValidFrom)
                        .Select(p => new
                        {
                            p.Currency,
                            p.Finish,
                            p.Amount,
                            p.ValidFrom,
                            p.Source
                        })
                        .FirstOrDefault()
                })
                .ToListAsync();

            return Ok(products);
        }

        // GET: api/products/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetProduct(Guid id)
        {
            var product = await (
                from p in _context.Products
                join brand in _context.Brands
                    on p.BrandId equals brand.Id
                join category in _context.ProductCategories
                    on p.CategoryId equals category.Id
                where p.Id == id
                select new
                {
                    p.Id,
                    p.BrandId,
                    p.CategoryId,
                    Brand = brand.Name,
                    Category = category.Name,
                    p.Model,
                    p.Dimensions,
                    p.Features,
                    p.Blurb,
                    p.Tagline,
                    p.Finishes,
                    p.DefaultIncludes,
                    p.DefaultExcludes,
                    p.AvailabilityNote,
                    p.Status,
                    Price = _context.Prices
                        .Where(price => price.ProductId == p.Id)
                        .OrderByDescending(price => price.ValidFrom)
                        .Select(price => new
                        {
                            price.Currency,
                            price.Finish,
                            price.Amount,
                            price.ValidFrom,
                            price.Source
                        })
                        .FirstOrDefault()
                }
            ).FirstOrDefaultAsync();

            if (product == null)
            {
                return NotFound();
            }

            return Ok(product);
        }

        // GET: api/products/{id}/finishes
        [HttpGet("{id}/finishes")]
        public async Task<IActionResult> GetFinishes(Guid id)
        {
            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound("Product not found");
            }

            return Ok(product.Finishes ?? Array.Empty<string>());
        }

        // GET: api/products/{id}/image (public)
        [HttpGet("{id}/image")]
        [AllowAnonymous]
        public IActionResult GetProductImage(Guid id)
        {
            var path = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot/images/products",
                $"{id}.png"
            );

            if (!System.IO.File.Exists(path))
            {
                return NotFound("Image not found");
            }

            // Cache for 1 hour to speed up catalog loading
            Response.Headers.Append("Cache-Control", "public, max-age=3600");

            return PhysicalFile(path, "image/png");
        }

        // POST: api/products (Admin)
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateProduct([FromBody] CreateProductRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Model))
            {
                return BadRequest("Product model is required.");
            }

            if (request.BrandId == Guid.Empty || request.CategoryId == Guid.Empty)
            {
                return BadRequest("BrandId and CategoryId are required.");
            }

            var brandExists = await _context.Brands.AnyAsync(b => b.Id == request.BrandId);
            if (!brandExists)
            {
                return BadRequest("Brand was not found.");
            }

            var categoryExists = await _context.ProductCategories.AnyAsync(c => c.Id == request.CategoryId);
            if (!categoryExists)
            {
                return BadRequest("Category was not found.");
            }

            var now = DateTimeOffset.UtcNow;
            var product = new Product
            {
                Id = Guid.NewGuid(),
                BrandId = request.BrandId,
                CategoryId = request.CategoryId,
                Model = request.Model.Trim(),
                Dimensions = ParseDimensions(request.Dimensions),
                Features = request.Features ?? Array.Empty<string>(),
                Blurb = string.IsNullOrWhiteSpace(request.Blurb) ? null : request.Blurb.Trim(),
                Tagline = string.IsNullOrWhiteSpace(request.Tagline) ? null : request.Tagline.Trim(),
                Finishes = request.Finishes ?? Array.Empty<string>(),
                DefaultIncludes = request.DefaultIncludes ?? Array.Empty<string>(),
                DefaultExcludes = request.DefaultExcludes ?? Array.Empty<string>(),
                AvailabilityNote = string.IsNullOrWhiteSpace(request.AvailabilityNote)
                    ? null
                    : request.AvailabilityNote.Trim(),
                Status = string.IsNullOrWhiteSpace(request.Status) ? "active" : request.Status.Trim(),
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.Products.Add(product);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, product);
        }

        // PATCH: api/products/{id} (Admin)
        [HttpPatch("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateProduct(Guid id, [FromBody] UpdateProductRequest request)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound();
            }

            if (request.BrandId.HasValue)
            {
                var brandExists = await _context.Brands.AnyAsync(b => b.Id == request.BrandId.Value);
                if (!brandExists)
                {
                    return BadRequest("Brand was not found.");
                }

                product.BrandId = request.BrandId.Value;
            }

            if (request.CategoryId.HasValue)
            {
                var categoryExists = await _context.ProductCategories
                    .AnyAsync(c => c.Id == request.CategoryId.Value);
                if (!categoryExists)
                {
                    return BadRequest("Category was not found.");
                }

                product.CategoryId = request.CategoryId.Value;
            }

            if (request.Model != null && !string.IsNullOrWhiteSpace(request.Model))
            {
                product.Model = request.Model.Trim();
            }

            if (request.Dimensions != null)
            {
                product.Dimensions = ParseDimensions(request.Dimensions);
            }

            if (request.Features != null)
            {
                product.Features = request.Features;
            }

            if (request.Blurb != null)
            {
                product.Blurb = string.IsNullOrWhiteSpace(request.Blurb) ? null : request.Blurb.Trim();
            }

            if (request.Tagline != null)
            {
                product.Tagline = string.IsNullOrWhiteSpace(request.Tagline) ? null : request.Tagline.Trim();
            }

            if (request.Finishes != null)
            {
                product.Finishes = request.Finishes;
            }

            if (request.DefaultIncludes != null)
            {
                product.DefaultIncludes = request.DefaultIncludes;
            }

            if (request.DefaultExcludes != null)
            {
                product.DefaultExcludes = request.DefaultExcludes;
            }

            if (request.AvailabilityNote != null)
            {
                product.AvailabilityNote = string.IsNullOrWhiteSpace(request.AvailabilityNote)
                    ? null
                    : request.AvailabilityNote.Trim();
            }

            if (request.Status != null && !string.IsNullOrWhiteSpace(request.Status))
            {
                product.Status = request.Status.Trim();
            }

            product.UpdatedAt = DateTimeOffset.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(product);
        }

        // DELETE: api/products/{id} (Admin) — always soft-archive (survives restarts; seeder will not resurrect)
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteProduct(Guid id)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound();
            }

            product.Status = "archived";
            product.UpdatedAt = DateTimeOffset.UtcNow;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // Parse dimensions JSON from a string, object, or null (works with Newtonsoft + STJ).
        private static JsonDocument ParseDimensions(object? dimensions)
        {
            if (dimensions == null)
            {
                return JsonDocument.Parse("{}");
            }

            if (dimensions is string raw)
            {
                return JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
            }

            if (dimensions is JsonElement element)
            {
                if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                {
                    return JsonDocument.Parse("{}");
                }

                if (element.ValueKind == JsonValueKind.String)
                {
                    var text = element.GetString();
                    return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
                }

                return JsonDocument.Parse(element.GetRawText());
            }

            // Newtonsoft typically deserializes anonymous JSON objects as JObject/Dictionary
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(dimensions);
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(json) || json == "null" ? "{}" : json);
        }
    }

    public class CreateProductRequest
    {
        public Guid BrandId { get; set; }

        public Guid CategoryId { get; set; }

        public string Model { get; set; } = string.Empty;

        public object? Dimensions { get; set; }

        public string[]? Features { get; set; }

        public string? Blurb { get; set; }

        public string? Tagline { get; set; }

        public string[]? Finishes { get; set; }

        public string[]? DefaultIncludes { get; set; }

        public string[]? DefaultExcludes { get; set; }

        public string? AvailabilityNote { get; set; }

        public string? Status { get; set; }
    }

    public class UpdateProductRequest
    {
        public Guid? BrandId { get; set; }

        public Guid? CategoryId { get; set; }

        public string? Model { get; set; }

        public object? Dimensions { get; set; }

        public string[]? Features { get; set; }

        public string? Blurb { get; set; }

        public string? Tagline { get; set; }

        public string[]? Finishes { get; set; }

        public string[]? DefaultIncludes { get; set; }

        public string[]? DefaultExcludes { get; set; }

        public string? AvailabilityNote { get; set; }

        public string? Status { get; set; }
    }
}
